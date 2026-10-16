namespace BudgetApp.Models;

/// <summary>Left over in one column before and after the suggested moves.</summary>
public record ColumnOutcome(string Heading, decimal Before, decimal After)
{
    /// <summary>Each column's left over before and after the moves.</summary>
    public static List<ColumnOutcome> For(IEnumerable<PayColumn> columns, IReadOnlyCollection<SuggestedMove> moves) =>
        columns.Select(c => new ColumnOutcome(c.Heading, c.LeftOver,
            c.LeftOver
            + moves.Where(m => c.Expenses.Any(e => e.Id == m.ExpenseId)).Sum(m => m.Amount)
            - moves.Where(m => c.Paychecks.Any(p => p.Id == m.ToPayPeriodId)).Sum(m => m.Amount))).ToList();
}
