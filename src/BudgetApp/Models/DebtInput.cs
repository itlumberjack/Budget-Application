namespace BudgetApp.Models;

/// <summary>One debt going into a payoff plan.</summary>
public record DebtInput(int Id, string Name, decimal Balance, decimal Apr, decimal MinPayment, int? Priority);
