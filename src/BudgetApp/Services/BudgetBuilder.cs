using BudgetApp.Data;
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
        var today = DateTime.Today;
        var ids = await db.MonthlyBudgets
            .Where(b => b.Year > today.Year || (b.Year == today.Year && b.Month >= today.Month))
            .Select(b => b.Id)
            .ToListAsync();
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

        var addedPay = 0;
        foreach (var source in sources)
        {
            foreach (var payday in PaySchedule.PaydaysForBudget(source, budget.Year, budget.Month))
            {
                if (budget.PayPeriods.Any(p => p.IncomeSourceId == source.Id && p.PayDate.Date == payday))
                    continue;
                // Already budgeted in another month (moved there with "Move to next month").
                if (await db.PayPeriods.AnyAsync(p => p.IncomeSourceId == source.Id && p.PayDate == payday))
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
        }
        // Paychecks need ids before bills can point at them.
        await db.SaveChangesAsync();

        var addedDebts = 0;
        foreach (var debt in debts)
        {
            if (budget.Expenses.Any(e => e.RecurringDebtId == debt.Id))
                continue;
            budget.Expenses.Add(new BudgetExpense
            {
                RecurringDebtId = debt.Id,
                Title = debt.Title,
                Company = debt.Company,
                Amount = debt.Amount,
                CategoryId = debt.CategoryId,
                DueDay = debt.DueDay
            });
            addedDebts++;
        }

        foreach (var expense in budget.Expenses.Where(e => e.PayPeriodId is null))
            expense.PayPeriodId = PickPayPeriod(budget.PayPeriods, budget.Year, budget.Month, expense.DueDay)?.Id;

        await db.SaveChangesAsync();
        return (addedPay, addedDebts);
    }

    /// <summary>
    /// Moves one paycheck into the month before or after, e.g. an October 31 check that pays November's bills.
    /// Bills it was covering in the old month go to that month's other paydays; in the new month it picks up
    /// the unpaid bills due before the next payday. Returns the month it landed in.
    /// </summary>
    public async Task<MonthlyBudget> MovePaycheckAsync(int payPeriodId, int monthOffset)
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
