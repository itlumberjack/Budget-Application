namespace BudgetApp.Models;

/// <summary>How one debt fares under a plan.</summary>
public record DebtOutcome(DebtInput Debt, int Order, int? PayoffMonth, DateTime? PayoffDate, decimal InterestPaid);
