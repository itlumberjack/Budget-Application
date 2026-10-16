namespace BudgetApp.Models;

public record ArrangementSuggestion(string Summary, List<SuggestedMove> Moves, List<ColumnOutcome> Columns);
