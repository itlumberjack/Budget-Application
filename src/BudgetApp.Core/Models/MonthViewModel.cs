using BudgetApp.Helpers;

namespace BudgetApp.Models;

public class MonthViewModel
{
    public int Year { get; init; }
    public int Month { get; init; }
    public MonthlyBudget? Budget { get; init; }
    public List<Category> Categories { get; init; } = new();
    public HouseholdSettings Household { get; init; } = new();

    /// <summary>What's left on a loan after each of this month's loan payments, by month line id.</summary>
    public Dictionary<int, decimal> OwedAfter { get; init; } = new();

    public DateTime FirstDay => new(Year, Month, 1);
    public string Title => FirstDay.MonthTitle();
    public DateTime Previous => FirstDay.AddMonths(-1);
    public DateTime Next => FirstDay.AddMonths(1);

    /// <summary>
    /// One column per payday, like the budget spreadsheet. Paydays within a few days of each other
    /// (Brad's Sept 30 check and Jessica's Oct 2 check) share a column. A "Not assigned" column is
    /// added when a bill has no paycheck.
    /// </summary>
    public List<PayColumn> Columns => _columns ??= BuildColumns();
    private List<PayColumn>? _columns;

    private List<PayColumn> BuildColumns()
    {
        if (Budget is null) return new();
        var columns = new List<PayColumn>();
        foreach (var pay in Budget.PayPeriods.OrderBy(p => p.PayDate).ThenBy(p => p.Label))
        {
            var current = columns.LastOrDefault();
            if (current is null || (pay.PayDate.Date - current.Paychecks[^1].PayDate.Date).TotalDays > PayColumn.MergeWithinDays)
                columns.Add(current = new PayColumn());
            current.Paychecks.Add(pay);
        }
        foreach (var column in columns)
            column.Expenses.AddRange(Budget.Expenses.Where(e => !e.IsRemovedExtra && column.Paychecks.Any(p => p.Id == e.PayPeriodId)));

        var unassigned = Budget.Expenses.Where(e => e.PayPeriodId is null && !e.IsRemovedExtra).ToList();
        if (unassigned.Count > 0 || columns.Count == 0)
            columns.Add(new PayColumn { Expenses = unassigned });
        return columns;
    }

    /// <summary>The bill comes out of a payday on or before its due day (or it has no due day).</summary>
    public bool PaysOnTime(PayColumn column, BudgetExpense bill) =>
        column.IsUnassigned || bill.DueDay is not int day
        || column.Paychecks.Min(p => p.PayDate.Date) <= new DateTime(Year, Month, Math.Min(day, DateTime.DaysInMonth(Year, Month)));

    /// <summary>
    /// Where Even out and the AI helper may put a bill: a payday on or before its due day, or the month's first
    /// payday when it's due before any paycheck arrives.
    /// </summary>
    public bool CanMoveTo(PayColumn column, BudgetExpense bill) =>
        !column.IsUnassigned && (PaysOnTime(column, bill) || column == Columns.FirstOrDefault(c => !c.IsUnassigned));

    public decimal TotalIncome => Budget?.PayPeriods.Sum(p => p.Total) ?? 0;
    public decimal TotalExpenses => Budget?.Expenses.Sum(e => e.Amount) ?? 0;
    public decimal TotalWithStatus(PaymentStatus status) =>
        Budget?.Expenses.Where(e => e.Status == status).Sum(e => e.Amount) ?? 0;
    public decimal Remaining => TotalIncome - TotalExpenses;

    public bool HasPlan => Budget?.HasPlan ?? false;
    public decimal PlannedIncome => Budget?.PayPeriods.Sum(p => p.PlannedIncome ?? 0) ?? 0;
    public decimal PlannedExpenses => Budget?.PayPeriods.Sum(p => p.PlannedSpending ?? 0) ?? 0;
    public decimal PlannedRemaining => PlannedIncome - PlannedExpenses;

    /// <summary>The month's buffer and each partner's share, added up over its paydays.</summary>
    public LeftoverSplit TotalSplit => LeftoverSplit.Sum(Columns.Where(c => !c.IsUnassigned).Select(c => Household.Split(c.LeftOver)));
}
