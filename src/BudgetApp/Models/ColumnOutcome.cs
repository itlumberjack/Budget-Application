namespace BudgetApp.Models;

/// <summary>Left over in one column before and after the suggested moves.</summary>
public record ColumnOutcome(string Heading, decimal Before, decimal After);
