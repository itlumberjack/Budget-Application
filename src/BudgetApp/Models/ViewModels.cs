using BudgetApp.Services;

namespace BudgetApp.Models;

public record MonthSummary(int Year, int Month, decimal Income, decimal Expenses, decimal Complete)
{
    public string Title => new DateTime(Year, Month, 1).ToString("MMMM yyyy");
    public decimal Remaining => Income - Expenses;
}

/// <summary>One payday on the month page, shown as a column like the budget spreadsheet.</summary>
public class PayColumn
{
    /// <summary>Paydays this close together are shown as one column.</summary>
    public const int MergeWithinDays = 3;

    public List<PayPeriod> Paychecks { get; init; } = new();
    public List<BudgetExpense> Expenses { get; init; } = new();

    public bool IsUnassigned => Paychecks.Count == 0;

    /// <summary>Where new bills added to this column go.</summary>
    public int? TargetPayPeriodId => Paychecks.FirstOrDefault()?.Id;

    /// <summary>e.g. "October 9th" or "September 30th/October 2nd".</summary>
    public string Heading => IsUnassigned
        ? "Not assigned"
        : string.Join("/", Paychecks.Select(p => p.PayDate.Date).Distinct().Select(d => d.ToString("MMMM ") + d.Day + DaySuffix(d.Day)));

    public decimal Income => Paychecks.Sum(p => p.Total);
    public decimal Spending => Expenses.Sum(e => e.Amount);
    public decimal LeftOver => Income - Spending;

    private static string DaySuffix(int day) => day switch
    {
        11 or 12 or 13 => "th",
        _ when day % 10 == 1 => "st",
        _ when day % 10 == 2 => "nd",
        _ when day % 10 == 3 => "rd",
        _ => "th"
    };
}

public class MonthViewModel
{
    public int Year { get; init; }
    public int Month { get; init; }
    public MonthlyBudget? Budget { get; init; }
    public List<Category> Categories { get; init; } = new();

    public DateTime FirstDay => new(Year, Month, 1);
    public string Title => FirstDay.ToString("MMMM yyyy");
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

public class DebtPageModel
{
    public List<RecurringDebt> Loans { get; init; } = new();
    public DebtPlanSettings Settings { get; init; } = new();
    public PayoffPlan? Plan { get; set; }
    public PayoffPlan? MinimumOnly { get; set; }
    public List<PayoffPlan> Comparison { get; set; } = new();

    public decimal TotalOwed => Loans.Sum(l => l.Balance ?? 0);
    public decimal TotalMinimum => Loans.Sum(l => l.Amount);
    /// <summary>APR averaged by balance.</summary>
    public decimal AverageRate => TotalOwed == 0 ? 0 : Loans.Sum(l => (l.Balance ?? 0) * (l.InterestRate ?? 0)) / TotalOwed;
    public decimal MonthlyInterestNow => Loans.Sum(l => Math.Round((l.Balance ?? 0) * (l.InterestRate ?? 0) / 1200m, 2));
}
