using BudgetApp.Data;
using BudgetApp.Helpers;
using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

/// <summary>Builds a month's budget from the pay schedules and recurring debts.</summary>
public class BudgetBuilder(BudgetDbContext db)
{
    // Building months is done one request at a time. Otherwise two pages loading at once (the month list and a
    // month, or a phone and a computer) could both see a month as missing and each add the same bills to it.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private bool _holdingGate;

    private async Task<T> Exclusive<T>(Func<Task<T>> work)
    {
        if (_holdingGate) return await work(); // already inside, e.g. creating a month while moving a paycheck
        await Gate.WaitAsync();
        _holdingGate = true;
        try { return await work(); }
        finally { _holdingGate = false; Gate.Release(); }
    }

    private async Task Exclusive(Func<Task> work) => await Exclusive(async () => { await work(); return true; });

    public Task<MonthlyBudget> CreateMonthAsync(int year, int month) => Exclusive(() => CreateMonthCoreAsync(year, month));

    private async Task<MonthlyBudget> CreateMonthCoreAsync(int year, int month)
    {
        var budget = await db.MonthlyBudgets.FirstOrDefaultAsync(b => b.Year == year && b.Month == month);
        var isNew = budget is null;
        if (budget is null)
        {
            budget = new MonthlyBudget { Year = year, Month = month };
            db.MonthlyBudgets.Add(budget);
            await db.SaveChangesAsync();
        }
        await SyncAsync(budget.Id);
        if (isNew) await SpreadDebtExtraCoreAsync(budget.Id);
        return budget;
    }

    /// <summary>How many months past this one always have a budget ready.</summary>
    public const int MonthsAhead = 6;

    /// <summary>
    /// Archives months that have ended (unless someone reopened them) and builds any missing budgets from
    /// this month through <see cref="MonthsAhead"/> months ahead. Cheap when there's nothing to do.
    /// </summary>
    public Task KeepMonthsCurrentAsync() => Exclusive(() => KeepMonthsCurrentCoreAsync());

    private async Task KeepMonthsCurrentCoreAsync()
    {
        foreach (var ended in await db.MonthlyBudgets.BeforeThisMonth().Where(b => !b.IsArchived && !b.KeepOpen).ToListAsync())
            ended.IsArchived = true;
        await db.SaveChangesAsync();

        if (!await db.IncomeSources.AnyAsync(s => s.IsActive) && !await db.RecurringDebts.AnyAsync(d => d.IsActive))
            return;
        var built = (await db.MonthlyBudgets.FromThisMonth().Select(b => new { b.Year, b.Month }).ToListAsync())
            .Select(b => (b.Year, b.Month)).ToHashSet();
        for (var i = 0; i <= MonthsAhead; i++)
        {
            var month = Dates.ThisMonth.AddMonths(i);
            if (!built.Contains((month.Year, month.Month)))
                await CreateMonthAsync(month.Year, month.Month);
        }
    }

    /// <summary>
    /// Removes extra copies of a recurring bill in this month and later ones: the same bill on the same due day
    /// more than once, left behind when two pages built a month at the same moment. Copies that are pending,
    /// complete or changed by hand are kept. Returns how many were removed.
    /// </summary>
    public Task<int> RemoveDuplicateBillsAsync() => Exclusive(async () =>
    {
        var months = db.MonthlyBudgets.FromThisMonth().Select(b => b.Id);
        var copies = await db.BudgetExpenses.Include(e => e.RecurringDebt)
            .Where(e => e.RecurringDebtId != null && months.Contains(e.MonthlyBudgetId))
            .ToListAsync();
        var extras = copies
            .GroupBy(e => (e.MonthlyBudgetId, e.RecurringDebtId, e.DueDay))
            .SelectMany(g => g.OrderBy(e => e.Status == PaymentStatus.NotPaid).ThenBy(e => e.Id).Skip(1))
            .Where(e => e.Status == PaymentStatus.NotPaid && e.Amount == e.RecurringDebt!.Amount && e.Title == e.RecurringDebt.Title)
            .ToList();
        db.BudgetExpenses.RemoveRange(extras);
        await db.SaveChangesAsync();
        return extras.Count;
    });

    /// <summary>
    /// Adds any paychecks and recurring debts that are missing from the month, and puts any bill
    /// that isn't under a payday yet into one. Never changes amounts or moves bills that already
    /// have a payday, so manual adjustments are kept.
    /// </summary>
    public Task<(int paychecks, int debts)> SyncAsync(int budgetId) => Exclusive(async () =>
        await SyncCoreAsync(budgetId,
            await db.IncomeSources.Where(s => s.IsActive).ToListAsync(),
            await db.RecurringDebts.Where(d => d.IsActive).ToListAsync()));

    /// <summary>
    /// Adds a newly created paycheck or bill to the months already built for this month and later,
    /// so it shows up without anyone clicking "Add missing items". Past months are left alone.
    /// </summary>
    public Task AddToCurrentAndFutureMonthsAsync(IncomeSource? source = null, RecurringDebt? debt = null) => Exclusive(() => AddToCurrentAndFutureMonthsCoreAsync(source, debt));

    private async Task AddToCurrentAndFutureMonthsCoreAsync(IncomeSource? source, RecurringDebt? debt)
    {
        var ids = await db.MonthlyBudgets.FromThisMonth().Select(b => b.Id).ToListAsync();
        foreach (var id in ids)
            await SyncCoreAsync(id,
                source is { IsActive: true } ? [source] : [],
                debt is { IsActive: true } ? [debt] : []);
    }

    /// <summary>
    /// After a paycheck's settings change on Pay &amp; Schedule, brings its paydays from today on up to date in this
    /// month's and later budgets: the new name, the new amount (paydays whose amount was changed by hand on the
    /// month page keep theirs), and the new schedule (paydays that no longer fall on the schedule go, missing ones are added, and
    /// bills on a removed payday move to the right one). Paydays moved by hand and archived months are left alone.
    /// Returns how many paydays changed.
    /// </summary>
    public Task<int> UpdateIncomeInFutureMonthsAsync(IncomeSource source) => Exclusive(async () =>
    {
        var rules = await db.GetRulesAsync();
        var today = DateTime.Today;
        var changed = 0;
        var ids = await db.MonthlyBudgets.FromThisMonth().Where(b => !b.IsArchived).Select(b => b.Id).ToListAsync();
        foreach (var id in ids)
        {
            var budget = await db.MonthlyBudgets.Include(b => b.PayPeriods).ThenInclude(p => p.Extras).Include(b => b.Expenses)
                .FirstAsync(b => b.Id == id);
            var expected = source.IsActive ? PaySchedule.PaydaysForBudget(source, budget.Year, budget.Month, rules).Select(d => d.Date).ToHashSet() : [];
            foreach (var pay in budget.PayPeriods.Where(p => p.IncomeSourceId == source.Id && p.PayDate.Date >= today && !p.ManuallyMoved).ToList())
            {
                if (!expected.Contains(pay.PayDate.Date))
                {
                    // Bills on it go back to "pick a payday" and are placed again below.
                    foreach (var e in budget.Expenses.Where(e => e.PayPeriodId == pay.Id)) e.PayPeriodId = null;
                    db.PayPeriods.Remove(pay);
                    budget.PayPeriods.Remove(pay);
                    changed++;
                    continue;
                }
                if (pay.Label != source.Name) pay.Label = source.Name;
                if (!pay.AmountSetByHand && pay.BaseAmount != source.TakeHomeAmount)
                {
                    pay.BaseAmount = source.TakeHomeAmount;
                    changed++;
                }
            }
            await db.SaveChangesAsync();
            var before = budget.PayPeriods.Count;
            await SyncCoreAsync(id, source.IsActive ? [source] : [], []);
            changed += budget.PayPeriods.Count - before;
        }
        return changed;
    });

    private async Task<(int paychecks, int debts)> SyncCoreAsync(int budgetId, IReadOnlyList<IncomeSource> sources, IReadOnlyList<RecurringDebt> debts)
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
                    DueDay = day,
                    IsFixed = debt.IsFixed
                });
                addedDebts++;
            }
        }

        var paidFrom = await PaidFromAsync();
        foreach (var expense in budget.Expenses.Where(e => e.PayPeriodId is null))
            expense.PayPeriodId = PickPayPeriod(expense, budget.PayPeriods, budget.Year, budget.Month, paidFrom)?.Id;

        await db.SaveChangesAsync();
        return (addedPay, addedDebts);
    }

    /// <summary>
    /// Keeps month-end paychecks in this month's and later budgets on the right day: moved back to the Friday
    /// when the last day is a weekend and the income source pays on the last business day, or out to the last
    /// day when it doesn't. Archived months and paychecks with a date changed by hand are left alone.
    /// Returns how many paychecks moved.
    /// </summary>
    public Task<int> RedateMonthEndPaychecksAsync() => Exclusive(async () =>
    {
        var sources = await db.IncomeSources
            .Where(s => s.Frequency == PayFrequency.Monthly || s.Frequency == PayFrequency.SemiMonthly)
            .ToDictionaryAsync(s => s.Id);
        if (sources.Count == 0) return 0;
        var ids = sources.Keys.ToList();
        var paychecks = await db.PayPeriods
            .Where(p => p.IncomeSourceId != null && ids.Contains(p.IncomeSourceId.Value)
                        && !p.MonthlyBudget!.IsArchived)
            .ToListAsync();
        var moved = 0;
        foreach (var pay in paychecks.Where(p => p.PayDate >= Dates.ThisMonth.AddMonths(-1)))
        {
            var source = sources[pay.IncomeSourceId!.Value];
            if (source.Frequency == PayFrequency.Monthly && source.DayOfMonth is int d && d < DateTime.DaysInMonth(pay.PayDate.Year, pay.PayDate.Month))
                continue;
            var last = new DateTime(pay.PayDate.Year, pay.PayDate.Month, DateTime.DaysInMonth(pay.PayDate.Year, pay.PayDate.Month));
            var businessDay = last.WeekdayOrFridayBefore();
            if (last == businessDay || (pay.PayDate.Date != last && pay.PayDate.Date != businessDay)) continue;
            var due = PaySchedule.MonthEndPayday(source, pay.PayDate.Year, pay.PayDate.Month);
            if (pay.PayDate.Date == due) continue;
            pay.PayDate = due;
            moved++;
        }
        await db.SaveChangesAsync();
        return moved;
    });

    /// <summary>
    /// Copies a recurring bill's name, company, amount, category and mandatory switch (and due day, for a
    /// monthly bill) to its unpaid copies in the months after the given one, so a change made on one month
    /// carries forward. A weekly or every-two-weeks bill's later copies in the same month change too (those due
    /// after fromDay). Archived months and copies already pending or complete are left alone. A new due day
    /// puts each copy back under the payday before it's due.
    /// </summary>
    public Task CarryForwardAsync(int debtId, int year, int month, int? fromDay, bool dueDayChanged) => Exclusive(async () =>
    {
        var debt = await db.RecurringDebts.FindAsync(debtId);
        if (debt is null) return;
        var paidFrom = await PaidFromAsync();
        var sameMonth = debt.Frequency != BillFrequency.Monthly;
        var months = await db.MonthlyBudgets.Where(b => b.Year > year || (b.Year == year && b.Month > month) || (sameMonth && b.Year == year && b.Month == month))
            .Where(b => !b.IsArchived)
            .Where(b => b.Expenses.Any(e => e.RecurringDebtId == debtId))
            .Include(b => b.PayPeriods).Include(b => b.Expenses).ToListAsync();
        foreach (var later in months)
            foreach (var copy in later.Expenses.Where(e => e.RecurringDebtId == debtId && e.Status == PaymentStatus.NotPaid
                         && (later.Year != year || later.Month != month || (e.DueDay ?? 0) > (fromDay ?? 0))))
            {
                copy.Title = debt.Title;
                copy.Company = debt.Company;
                copy.Amount = debt.Amount;
                copy.CategoryId = debt.CategoryId;
                copy.IsFixed = debt.IsFixed;
                if (debt.Frequency != BillFrequency.Monthly) continue;
                copy.DueDay = debt.DueDay;
                if (dueDayChanged) copy.PayPeriodId = PickPayPeriod(copy, later.PayPeriods, later.Year, later.Month, paidFrom)?.Id;
            }
        await db.SaveChangesAsync();
    });

    /// <summary>
    /// Puts the Debt Payoff page's extra payments into the budgets of this month and later ones, or just one month.
    /// The monthly extra comes out of the chosen payday (or, with no payday chosen, is spread over the paydays so
    /// they're left with about the same), and each one-time extra comes out of the payday on or before its date.
    /// Each share goes to the loan the payoff plan is paying down. Shares already pending or complete, or set by
    /// hand, are kept and count toward their payday; what they leave short (or over) moves on to the next payday.
    /// In this month, what falls on a payday that has passed moves on to the next one still to come.
    /// </summary>
    public Task SpreadDebtExtraAsync(int? budgetId = null, (int Year, int Month)? after = null) => Exclusive(async () =>
    {
        var ids = budgetId is int id ? [id]
            : await (after is var (y, m) ? db.MonthlyBudgets.After(y, m) : db.MonthlyBudgets.FromThisMonth())
                .Where(b => !b.IsArchived).Select(b => b.Id).ToListAsync();
        foreach (var month in ids) await SpreadDebtExtraCoreAsync(month);
    });

    private async Task SpreadDebtExtraCoreAsync(int budgetId)
    {
        var budget = await db.MonthlyBudgets.Include(b => b.PayPeriods).ThenInclude(p => p.Extras).Include(b => b.Expenses)
            .FirstAsync(b => b.Id == budgetId);
        if (budget.IsArchived) return;
        // Shares that are paid, pending or set by hand stay as they are and count toward their payday.
        var kept = budget.Expenses.Where(e => e.ExtraForDebtId != null && (e.Status != PaymentStatus.NotPaid || e.ExtraPinned || e.ExtraAddedByHand)).ToList();
        db.BudgetExpenses.RemoveRange(budget.Expenses.Where(e => e.ExtraForDebtId != null && !kept.Contains(e)).ToList());
        foreach (var pay in budget.PayPeriods)
        {
            db.PayPeriodExtras.RemoveRange(pay.Extras.Where(x => x.FromDebtExtra));
            pay.Extras.RemoveAll(x => x.FromDebtExtra);
        }
        await db.SaveChangesAsync();

        var settings = await db.GetDebtPlanAsync();
        var loans = await db.RecurringDebts.Loans().ToListAsync();
        var first = Dates.ThisMonth;
        if (loans.Count == 0 || settings.Strategy == PayoffStrategy.MinimumOnly || budget.FirstDay < first) return;

        var columns = new MonthViewModel { Year = budget.Year, Month = budget.Month, Budget = budget }.Columns.Where(c => !c.IsUnassigned).ToList();
        if (columns.Count == 0) return;
        var lumps = settings.LumpSums.Where(l => l.Date.Year == budget.Year && l.Date.Month == budget.Month && l.Amount > 0).ToList();
        bool Passed(PayColumn c) => budget.FirstDay == first && c.Paychecks.Max(p => p.PayDate.Date) < DateTime.Today;
        int ColumnOn(DateTime date) => Math.Max(0, columns.FindLastIndex(c => c.Paychecks.Min(p => p.PayDate.Date) <= date.Date));

        // A one-time extra is money coming in: it's added to the income of the payday it's paid from (or, once that
        // payday has passed, the next one still to come).
        if (lumps.Count > 0)
        {
            foreach (var lump in lumps)
            {
                var i = ColumnOn(lump.Date);
                while (i < columns.Count - 1 && Passed(columns[i])) i++;
                columns[i].Paychecks[0].Extras.Add(new PayPeriodExtra
                {
                    PayPeriodId = columns[i].Paychecks[0].Id, Amount = lump.Amount, FromDebtExtra = true,
                    Description = string.IsNullOrWhiteSpace(lump.Note) ? "One-time extra for debt" : lump.Note
                });
            }
            await db.SaveChangesAsync();
            columns = new MonthViewModel { Year = budget.Year, Month = budget.Month, Budget = budget }.Columns.Where(c => !c.IsUnassigned).ToList();
        }
        // A payday can take new shares once it's still to come (in this month) and nothing on it was set by hand.
        bool Open(PayColumn c) => !Passed(c) && !kept.Any(e => e.ExtraPinned && c.Paychecks.Any(p => p.Id == e.PayPeriodId));

        // What each payday is meant to give.
        var wanted = new decimal[columns.Count];
        foreach (var lump in lumps)
            wanted[ColumnOn(lump.Date)] += lump.Amount;
        var monthly = new decimal[columns.Count];
        if (settings.ExtraMonthly > 0)
        {
            var paycheck = ChosenPaycheck(budget, settings);
            if (paycheck is not null)
                monthly[columns.FindIndex(c => c.Paychecks.Any(p => p.Id == paycheck.Id))] += settings.ExtraMonthly;
            else
            {
                // Spread over the paydays that can take it, by what each would have left with no extra on it.
                var open = Enumerable.Range(0, columns.Count).Where(i => Open(columns[i])).ToList();
                if (open.Count == 0) open = [columns.Count - 1];
                var shares = DebtExtraSplitter.Spread(open.Select(i => columns[i].LeftOver + KeptOn(kept, columns[i]) - wanted[i]).ToList(), settings.ExtraMonthly);
                for (var k = 0; k < open.Count; k++) monthly[open[k]] += shares[k];
            }
        }

        for (var i = 0; i < columns.Count; i++) wanted[i] += monthly[i];
        // Monthly extra money from outside the paychecks counts as income on the payday it's paid from (added
        // below, once there's a loan to pay it to).
        var monthlyIncome = new List<PayPeriodExtra>();
        if (settings.ExtraMonthlyIsIncome)
            for (var i = 0; i < columns.Count; i++)
            {
                if (monthly[i] <= 0) continue;
                var at = i;
                while (at < columns.Count - 1 && Passed(columns[at])) at++;
                monthlyIncome.Add(new PayPeriodExtra
                {
                    PayPeriodId = columns[at].Paychecks[0].Id, Amount = monthly[i], FromDebtExtra = true, Description = "Extra for debt"
                });
            }

        // Walk the paydays in order. Kept shares count toward their payday; a payday that can't take new shares
        // passes on what it's short (or what it's over by) to the next one.
        var carry = 0m;
        var give = new List<(int PayPeriodId, decimal Amount)>();
        for (var i = 0; i < columns.Count; i++)
        {
            var want = wanted[i] + carry - KeptOn(kept, columns[i]);
            if (!Open(columns[i])) { carry = want; continue; }
            carry = Math.Min(0, want);
            if (want > 0) give.Add((columns[i].TargetPayPeriodId!.Value, want));
        }
        if (give.Count == 0) return;

        // Each loan's balance at the start of this month, as the payoff plan projects it from today's balances.
        var inputs = loans.Select(d => d.ToDebtInput()).ToList();
        var startBalances = DebtPlanner.StartBalances(inputs, settings, first, budget.Year, budget.Month);
        var keptByLoan = kept.GroupBy(e => e.ExtraForDebtId!.Value).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
        // First the loan the payoff plan sends extra to this month, up to what the plan gives it; then the plan's
        // order, up to what each loan still owes.
        var monthIndex = (budget.Year - first.Year) * 12 + budget.Month - first.Month;
        var plan = DebtPlanner.Plan(inputs, settings.Strategy, settings.ExtraMonthly, first, settings.LumpSums);
        var planned = monthIndex < plan.Schedule.Count ? plan.Schedule[monthIndex].Extra : new Dictionary<int, decimal>();
        var targets = planned.Select(x => (Id: x.Key, Capacity: Math.Max(0, x.Value - keptByLoan.GetValueOrDefault(x.Key)))).ToList();
        targets.AddRange(plan.Debts.Select(o => o.Debt).Select(d => (d.Id, Capacity: Math.Max(0,
            startBalances.GetValueOrDefault(d.Id) - d.MinPayment - keptByLoan.GetValueOrDefault(d.Id) - planned.GetValueOrDefault(d.Id)))));
        targets = targets.Where(t => t.Capacity > 0).ToList();
        if (targets.Count == 0) return;

        db.PayPeriodExtras.AddRange(monthlyIncome);
        var payments = DebtExtraSplitter.Assign(give, targets);
        var byId = loans.ToDictionary(l => l.Id);
        foreach (var p in payments)
        {
            var loan = byId[p.DebtId];
            db.BudgetExpenses.Add(new BudgetExpense
            {
                MonthlyBudgetId = budget.Id, PayPeriodId = p.PayPeriodId, ExtraForDebtId = loan.Id,
                Title = $"Extra: {loan.Title}", Company = loan.Company, CategoryId = loan.CategoryId,
                Amount = p.Amount, IsFixed = true
            });
        }
        await db.SaveChangesAsync();
    }

    // Extra payments added by hand come on top of the plan's, so they don't count toward a payday's share.
    private static decimal KeptOn(List<BudgetExpense> kept, PayColumn column) =>
        kept.Where(e => !e.ExtraAddedByHand && column.Paychecks.Any(p => p.Id == e.PayPeriodId)).Sum(e => e.Amount);

    /// <summary>
    /// The paycheck the monthly extra comes out of in a month: the chosen income's Nth paycheck that month (the
    /// last one for 0, or when there are fewer). Null when none is chosen or that income isn't paid that month.
    /// </summary>
    public static PayPeriod? ChosenPaycheck(MonthlyBudget budget, DebtPlanSettings settings)
    {
        if (settings.ExtraIncomeSourceId is not int sourceId) return null;
        var paychecks = budget.PayPeriods.Where(p => p.IncomeSourceId == sourceId).OrderBy(p => p.PayDate).ToList();
        if (paychecks.Count == 0) return null;
        var n = settings.ExtraPaydayNumber;
        return n >= 1 && n <= paychecks.Count ? paychecks[n - 1] : paychecks[^1];
    }

    /// <summary>
    /// Debt Payoff used to offer weekly or every-two-weeks loan payments. Loans still on one of those
    /// schedules go back to one monthly payment, here and in this month's and later budgets.
    /// </summary>
    public Task UndoLoanPaymentScheduleAsync() => Exclusive(async () =>
    {
        var settings = await db.GetDebtPlanAsync();
        if (settings.PaymentFrequency == BillFrequency.Monthly) return;
        var loans = await db.RecurringDebts.Where(d => d.Kind == BillKind.Loan && d.Frequency != BillFrequency.Monthly).ToListAsync();
        foreach (var loan in loans) loan.ChangePaymentSchedule(BillFrequency.Monthly, null);
        settings.PaymentFrequency = BillFrequency.Monthly;
        settings.PaymentStartDate = null;
        await db.SaveChangesAsync();
        await RescheduleAsync(loans.Select(l => l.Id).ToList());
        await SpreadDebtExtraAsync();
    });

    /// <summary>
    /// After bills change schedule (loans switched to weekly payments, say), redoes their copies in this month's
    /// and later months' budgets. A month where one of those bills is already pending or complete keeps its copies
    /// of that bill as they are, so nothing paid is lost or paid twice. Archived months are left alone.
    /// </summary>
    public Task RescheduleAsync(IReadOnlyCollection<int> debtIds) => Exclusive(async () =>
    {
        var debts = await db.RecurringDebts.Where(d => debtIds.Contains(d.Id) && d.IsActive).ToListAsync();
        var months = await db.MonthlyBudgets.FromThisMonth().Where(b => !b.IsArchived).Include(b => b.Expenses).ToListAsync();
        foreach (var month in months)
        {
            var redo = debts
                .Where(d => month.Expenses.Where(e => e.RecurringDebtId == d.Id).All(e => e.Status == PaymentStatus.NotPaid))
                .ToList();
            var ids = redo.Select(d => d.Id).ToHashSet();
            db.BudgetExpenses.RemoveRange(month.Expenses.Where(e => e.RecurringDebtId is int id && ids.Contains(id)));
            await db.SaveChangesAsync();
            await SyncCoreAsync(month.Id, [], redo);
        }
    });

    /// <summary>
    /// After the budget rules change, moves paychecks in this month's and later months' budgets to the
    /// month the rules now put them in (bringing their bills along), then fills in any paydays that now
    /// belong to each month. Older months are left as they are.
    /// </summary>
    public Task<int> ApplyRulesToCurrentAndFutureMonthsAsync() => Exclusive(() => ApplyRulesToCurrentAndFutureMonthsCoreAsync());

    private async Task<int> ApplyRulesToCurrentAndFutureMonthsCoreAsync()
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
            await SyncCoreAsync(id, activeSources, activeDebts);
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
    public Task<MonthlyBudget> MovePaycheckAsync(int payPeriodId, int monthOffset, bool manual = false) => Exclusive(() => MovePaycheckCoreAsync(payPeriodId, monthOffset, manual));

    private async Task<MonthlyBudget> MovePaycheckCoreAsync(int payPeriodId, int monthOffset, bool manual)
    {
        var pay = await db.PayPeriods.Include(p => p.MonthlyBudget).FirstAsync(p => p.Id == payPeriodId);
        var from = pay.MonthlyBudget!;
        var target = from.FirstDay.AddMonths(monthOffset);
        var to = await CreateMonthAsync(target.Year, target.Month);

        // Re-home the old month's bills that were under this paycheck.
        var oldMonth = await db.MonthlyBudgets.Include(b => b.PayPeriods).Include(b => b.Expenses).FirstAsync(b => b.Id == from.Id);
        var remaining = oldMonth.PayPeriods.Where(p => p.Id != pay.Id).ToList();
        var paidFrom = await PaidFromAsync();
        foreach (var e in oldMonth.Expenses.Where(e => e.PayPeriodId == pay.Id))
            e.PayPeriodId = PickPayPeriod(e, remaining, oldMonth.Year, oldMonth.Month, paidFrom)?.Id;

        pay.MonthlyBudgetId = to.Id;
        pay.ManuallyMoved = manual && await NaturalMonthAsync(pay) != to.FirstDay;
        await db.SaveChangesAsync();

        // In the new month, unpaid bills that now fall to this paycheck move under it.
        var newMonth = await db.MonthlyBudgets.Include(b => b.PayPeriods).Include(b => b.Expenses).FirstAsync(b => b.Id == to.Id);
        foreach (var e in newMonth.Expenses.Where(e => e.Status == PaymentStatus.NotPaid))
        {
            var pick = PickPayPeriod(e, newMonth.PayPeriods, newMonth.Year, newMonth.Month, paidFrom);
            var tiedElsewhere = e.RecurringDebtId is int debtId && paidFrom.TryGetValue(debtId, out var sourceId) && sourceId != pay.IncomeSourceId;
            if (pick is not null && pick.PayDate.Date == pay.PayDate.Date && !tiedElsewhere)
                e.PayPeriodId = pay.Id;
        }
        await db.SaveChangesAsync();
        return to;
    }

    /// <summary>Recurring bills tied to one person's paychecks: bill id to income source id.</summary>
    private async Task<Dictionary<int, int>> PaidFromAsync() =>
        await db.RecurringDebts.Where(d => d.PaidFromIncomeSourceId != null)
            .ToDictionaryAsync(d => d.Id, d => d.PaidFromIncomeSourceId!.Value);

    /// <summary>
    /// The paycheck a month's bill should come out of. A bill tied to one person's paychecks only looks at
    /// theirs (unless they have none this month); otherwise any paycheck will do.
    /// </summary>
    public static PayPeriod? PickPayPeriod(BudgetExpense bill, IEnumerable<PayPeriod> payPeriods, int year, int month, IReadOnlyDictionary<int, int> paidFrom)
    {
        var pays = payPeriods.ToList();
        if (bill.RecurringDebtId is int debtId && paidFrom.TryGetValue(debtId, out var sourceId) && pays.Any(p => p.IncomeSourceId == sourceId))
            pays = pays.Where(p => p.IncomeSourceId == sourceId).ToList();
        return PickPayPeriod(pays, year, month, bill.DueDay);
    }

    /// <summary>
    /// After a bill is tied to someone's paychecks (or untied), puts its unpaid copies in this month's and
    /// later budgets under the paycheck it now belongs to. Archived months are left alone.
    /// </summary>
    public Task PlaceCopiesAsync(int debtId) => Exclusive(async () =>
    {
        var paidFrom = await PaidFromAsync();
        var months = await db.MonthlyBudgets.FromThisMonth().Where(b => !b.IsArchived)
            .Where(b => b.Expenses.Any(e => e.RecurringDebtId == debtId))
            .Include(b => b.PayPeriods).Include(b => b.Expenses).ToListAsync();
        foreach (var month in months)
            foreach (var copy in month.Expenses.Where(e => e.RecurringDebtId == debtId && e.Status == PaymentStatus.NotPaid))
                copy.PayPeriodId = PickPayPeriod(copy, month.PayPeriods, month.Year, month.Month, paidFrom)?.Id;
        await db.SaveChangesAsync();
    });

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
