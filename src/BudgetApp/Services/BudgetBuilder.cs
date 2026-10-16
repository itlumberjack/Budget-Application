using BudgetApp.Data;
using BudgetApp.Helpers;
using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

/// <summary>Builds a month's budget from the pay schedules and recurring debts.</summary>
public class BudgetBuilder(BudgetDbContext db)
{
    public async Task<MonthlyBudget> CreateMonthAsync(int year, int month)
    {
        var budget = await db.MonthlyBudgets.FirstOrDefaultAsync(b => b.Year == year && b.Month == month);
        if (budget is null)
        {
            budget = new MonthlyBudget { Year = year, Month = month };
            db.MonthlyBudgets.Add(budget);
            await db.SaveChangesAsync();
        }
        await SyncAsync(budget.Id);
        return budget;
    }

    /// <summary>
    /// Adds any paychecks and recurring debts that are missing from the month, and puts any bill
    /// that isn't under a payday yet into one. Never changes amounts or moves bills that already
    /// have a payday, so manual adjustments are kept.
    /// </summary>
    public async Task<(int paychecks, int debts)> SyncAsync(int budgetId) =>
        await SyncAsync(budgetId,
            await db.IncomeSources.Where(s => s.IsActive).ToListAsync(),
            await db.RecurringDebts.Where(d => d.IsActive).ToListAsync());

    /// <summary>
    /// Adds a newly created paycheck or bill to the months already built for this month and later,
    /// so it shows up without anyone clicking "Add missing items". Past months are left alone.
    /// </summary>
    public async Task AddToCurrentAndFutureMonthsAsync(IncomeSource? source = null, RecurringDebt? debt = null)
    {
        var ids = await db.MonthlyBudgets.FromThisMonth().Select(b => b.Id).ToListAsync();
        foreach (var id in ids)
            await SyncAsync(id,
                source is { IsActive: true } ? [source] : [],
                debt is { IsActive: true } ? [debt] : []);
    }

    private async Task<(int paychecks, int debts)> SyncAsync(int budgetId, IReadOnlyList<IncomeSource> sources, IReadOnlyList<RecurringDebt> debts)
    {
        var budget = await db.MonthlyBudgets
            .Include(b => b.PayPeriods)
            .Include(b => b.Expenses)
            .FirstAsync(b => b.Id == budgetId);
        var rules = await db.GetRulesAsync();

        var paydays = sources
            .SelectMany(s => PaySchedule.PaydaysForBudget(s, budget.Year, budget.Month, rules).Select(d => (Source: s, Date: d)))
            .ToList();
        // Paychecks already budgeted, in this month or another one (moved there with "Move to next month"),
        // looked up in one query rather than one per payday.
        var sourceIds = paydays.Select(p => p.Source.Id).Distinct().ToList();
        var dates = paydays.Select(p => p.Date).Distinct().ToList();
        var existing = paydays.Count == 0 ? [] : (await db.PayPeriods
                .Where(p => p.IncomeSourceId != null && sourceIds.Contains(p.IncomeSourceId.Value) && dates.Contains(p.PayDate))
                .Select(p => new { p.IncomeSourceId, p.PayDate })
                .ToListAsync())
            .Select(p => (p.IncomeSourceId!.Value, p.PayDate.Date))
            .ToHashSet();

        var addedPay = 0;
        foreach (var (source, payday) in paydays)
        {
            if (budget.PayPeriods.Any(p => p.IncomeSourceId == source.Id && p.PayDate.Date == payday)
                || existing.Contains((source.Id, payday)))
                continue;
            budget.PayPeriods.Add(new PayPeriod
            {
                IncomeSourceId = source.Id,
                Label = source.Name,
                PayDate = payday,
                BaseAmount = source.TakeHomeAmount
            });
            addedPay++;
        }
        // Paychecks need ids before bills can point at them.
        await db.SaveChangesAsync();

        var addedDebts = 0;
        foreach (var debt in debts)
        {
            // A weekly bill gets one copy per week it's due. Copies already in the month are kept as they are,
            // so only the missing due days are added (and never more copies than the schedule has).
            var dueDays = debt.DueDaysIn(budget.Year, budget.Month);
            var copies = budget.Expenses.Where(e => e.RecurringDebtId == debt.Id).ToList();
            var missing = dueDays.Where(day => copies.All(e => e.DueDay != day)).Take(dueDays.Count - copies.Count);
            foreach (var day in missing)
            {
                budget.Expenses.Add(new BudgetExpense
                {
                    RecurringDebtId = debt.Id,
                    Title = debt.Title,
                    Company = debt.Company,
                    Amount = debt.Amount,
                    CategoryId = debt.CategoryId,
                    DueDay = day
                });
                addedDebts++;
            }
        }

        foreach (var expense in budget.Expenses.Where(e => e.PayPeriodId is null))
            expense.PayPeriodId = PickPayPeriod(budget.PayPeriods, budget.Year, budget.Month, expense.DueDay)?.Id;

        await db.SaveChangesAsync();
        return (addedPay, addedDebts);
    }

    /// <summary>
    /// After the budget rules change, moves paychecks in this month's and later months' budgets to the
    /// month the rules now put them in (bringing their bills along), then fills in any paydays that now
    /// belong to each month. Older months are left as they are.
    /// </summary>
    public async Task<int> ApplyRulesToCurrentAndFutureMonthsAsync()
    {
        var rules = await db.GetRulesAsync();
        var thisMonth = Dates.ThisMonth;
        var sources = await db.IncomeSources.ToDictionaryAsync(s => s.Id);
        var moved = 0;

        // Last month is included so its end-of-month paycheck can move into this month (and back again).
        var lastMonth = thisMonth.AddMonths(-1);
        var paychecks = await db.PayPeriods.Include(p => p.MonthlyBudget)
            .Where(p => p.IncomeSourceId != null && !p.ManuallyMoved)
            .Where(p => p.MonthlyBudget!.Year > lastMonth.Year || (p.MonthlyBudget.Year == lastMonth.Year && p.MonthlyBudget.Month >= lastMonth.Month))
            .OrderBy(p => p.PayDate)
            .ToListAsync();
        foreach (var pay in paychecks)
        {
            if (!sources.TryGetValue(pay.IncomeSourceId!.Value, out var source)) continue;
            var belongs = PaySchedule.BudgetMonthFor(source, pay.PayDate, rules);
            var current = pay.MonthlyBudget!.FirstDay;
            if (belongs == current || (belongs < thisMonth && current < thisMonth)) continue;
            var offset = Dates.MonthsBetween(current, belongs);
            if (offset is 1 or -1)
            {
                await MovePaycheckAsync(pay.Id, offset);
                moved++;
            }
        }

        var ids = await db.MonthlyBudgets.FromThisMonth()
            .OrderBy(b => b.Year).ThenBy(b => b.Month)
            .Select(b => b.Id).ToListAsync();
        var activeSources = sources.Values.Where(s => s.IsActive).ToList();
        var activeDebts = await db.RecurringDebts.Where(d => d.IsActive).ToListAsync();
        foreach (var id in ids)
            await SyncAsync(id, activeSources, activeDebts);
        return moved;
    }

    /// <summary>The month a paycheck goes in by its schedule and Brad's rule.</summary>
    private async Task<DateTime> NaturalMonthAsync(PayPeriod pay)
    {
        var source = pay.IncomeSourceId is int id ? await db.IncomeSources.FindAsync(id) : null;
        return source is null
            ? pay.PayDate.MonthStart()
            : PaySchedule.BudgetMonthFor(source, pay.PayDate, await db.GetRulesAsync());
    }

    /// <summary>
    /// Moves one paycheck into the month before or after, e.g. an October 31 check that pays November's bills.
    /// Bills it was covering in the old month go to that month's other paydays; in the new month it picks up
    /// the unpaid bills due before the next payday. Returns the month it landed in.
    /// A move by hand (<paramref name="manual"/>) is remembered so Brad's rule won't undo it.
    /// </summary>
    public async Task<MonthlyBudget> MovePaycheckAsync(int payPeriodId, int monthOffset, bool manual = false)
    {
        var pay = await db.PayPeriods.Include(p => p.MonthlyBudget).FirstAsync(p => p.Id == payPeriodId);
        var from = pay.MonthlyBudget!;
        var target = from.FirstDay.AddMonths(monthOffset);
        var to = await CreateMonthAsync(target.Year, target.Month);

        // Re-home the old month's bills that were under this paycheck.
        var oldMonth = await db.MonthlyBudgets.Include(b => b.PayPeriods).Include(b => b.Expenses).FirstAsync(b => b.Id == from.Id);
        var remaining = oldMonth.PayPeriods.Where(p => p.Id != pay.Id).ToList();
        foreach (var e in oldMonth.Expenses.Where(e => e.PayPeriodId == pay.Id))
            e.PayPeriodId = PickPayPeriod(remaining, oldMonth.Year, oldMonth.Month, e.DueDay)?.Id;

        pay.MonthlyBudgetId = to.Id;
        pay.ManuallyMoved = manual && await NaturalMonthAsync(pay) != to.FirstDay;
        await db.SaveChangesAsync();

        // In the new month, unpaid bills that now fall to this paycheck move under it.
        var newMonth = await db.MonthlyBudgets.Include(b => b.PayPeriods).Include(b => b.Expenses).FirstAsync(b => b.Id == to.Id);
        foreach (var e in newMonth.Expenses.Where(e => e.Status == PaymentStatus.NotPaid))
        {
            var pick = PickPayPeriod(newMonth.PayPeriods, newMonth.Year, newMonth.Month, e.DueDay);
            if (pick is not null && pick.PayDate.Date == pay.PayDate.Date)
                e.PayPeriodId = pay.Id;
        }
        await db.SaveChangesAsync();
        return to;
    }

    /// <summary>
    /// The paycheck a bill should come out of: the last payday on or before its due day,
    /// or the month's first payday when it's due before any paycheck arrives (or has no due day).
    /// </summary>
    public static PayPeriod? PickPayPeriod(IEnumerable<PayPeriod> payPeriods, int year, int month, int? dueDay)
    {
        var ordered = payPeriods.OrderBy(p => p.PayDate).ThenBy(p => p.Id).ToList();
        if (ordered.Count == 0) return null;
        if (dueDay is int day)
        {
            var dueDate = new DateTime(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
            var before = ordered.LastOrDefault(p => p.PayDate.Date <= dueDate);
            if (before is not null) return ordered.First(p => p.PayDate.Date == before.PayDate.Date);
        }
        return ordered[0];
    }
}
