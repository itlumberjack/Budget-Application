using BudgetApp.Helpers;

namespace BudgetApp.Models;

public class MonthViewModel
{
    public int Year { get; init; }
    public int Month { get; init; }
    public MonthlyBudget? Budget { get; init; }
    public List<Category> Categories { get; init; } = new();

    public DateTime FirstDay => new(Year, Month, 1);
    public string Title => FirstDay.MonthTitle();
    public DateTime Previous => FirstDay.AddMonths(-1);
    public DateTime Next => FirstDay.AddMonths(1);

    /// <summary>
    /// One column per payday, like the budget spreadsheet. Paydays within a few days of each other
    /// (Brad's Sept 30 check and Jessica's Oct 2 check) share a column. A "Not assigned" column is
    /// added when a bill has no paycheck.
    /// </summary>
    public List<PayColumn> Columns
    {
        get
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
                column.Expenses.AddRange(Budget.Expenses.Where(e => column.Paychecks.Any(p => p.Id == e.PayPeriodId)));

            var unassigned = Budget.Expenses.Where(e => e.PayPeriodId is null).ToList();
            if (unassigned.Count > 0 || columns.Count == 0)
                columns.Add(new PayColumn { Expenses = unassigned });
            return columns;
        }
    }

    public decimal TotalIncome => Budget?.PayPeriods.Sum(p => p.Total) ?? 0;
    public decimal TotalExpenses => Budget?.Expenses.Sum(e => e.Amount) ?? 0;
    public decimal TotalWithStatus(PaymentStatus status) =>
        Budget?.Expenses.Where(e => e.Status == status).Sum(e => e.Amount) ?? 0;
    public decimal Remaining => TotalIncome - TotalExpenses;
}
