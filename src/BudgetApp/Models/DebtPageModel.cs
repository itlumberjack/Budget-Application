namespace BudgetApp.Models;

public class DebtPageModel
{
    public List<RecurringDebt> Loans { get; init; } = new();
    public DebtPlanSettings Settings { get; init; } = new();
    public PayoffPlan? Plan { get; set; }
    public PayoffPlan? MinimumOnly { get; set; }
    public List<PayoffPlan> Comparison { get; set; } = new();

    public decimal TotalOwed => Loans.Sum(l => l.Balance ?? 0);
    public decimal TotalMinimum => Loans.Sum(l => l.MonthlyAmount);
    /// <summary>APR averaged by balance.</summary>
    public decimal AverageRate => TotalOwed == 0 ? 0 : Loans.Sum(l => (l.Balance ?? 0) * (l.InterestRate ?? 0)) / TotalOwed;
    public decimal MonthlyInterestNow => Loans.Sum(l => Math.Round((l.Balance ?? 0) * (l.InterestRate ?? 0) / 1200m, 2));
}
