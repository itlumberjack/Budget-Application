namespace BudgetApp.Models;

public record MonthSummary(int Year, int Month, decimal Income, decimal Expenses, decimal Complete)
{
    public string Title => new DateTime(Year, Month, 1).ToString("MMMM yyyy");
    public decimal Remaining => Income - Expenses;
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

    public decimal TotalIncome => Budget?.PayPeriods.Sum(p => p.Total) ?? 0;
    public decimal TotalExpenses => Budget?.Expenses.Sum(e => e.Amount) ?? 0;
    public decimal TotalWithStatus(PaymentStatus status) =>
        Budget?.Expenses.Where(e => e.Status == status).Sum(e => e.Amount) ?? 0;
    public decimal Remaining => TotalIncome - TotalExpenses;
}
