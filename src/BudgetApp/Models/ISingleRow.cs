namespace BudgetApp.Models;

/// <summary>A settings table that only ever has one row, with Id 1.</summary>
public interface ISingleRow
{
    int Id { get; set; }
}
