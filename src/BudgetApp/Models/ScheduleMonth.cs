namespace BudgetApp.Models;

/// <summary>One month of a plan: what went to each debt (by Id) and what's left.</summary>
public record ScheduleMonth(int Number, DateTime Month, Dictionary<int, decimal> Payments, Dictionary<int, decimal> Balances,
    decimal Interest, decimal Snowball, decimal TotalBalance);
