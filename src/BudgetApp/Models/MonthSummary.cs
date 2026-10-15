namespace BudgetApp.Models;

public record MonthSummary(int Year, int Month, decimal Income, decimal Expenses, decimal Complete)
{
    public string Title => new DateTime(Year, Month, 1).ToString("MMMM yyyy");
    public decimal Remaining => Income - Expenses;
}
