namespace BudgetApp.Models;

/// <summary>A bill the advisor suggests moving to another payday column.</summary>
public record SuggestedMove(int ExpenseId, string Title, decimal Amount, int? DueDay,
    string From, string To, int ToPayPeriodId, string Reason);
