using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BudgetApp.Helpers;

namespace BudgetApp.Models;

/// <summary>The budget for one calendar month.</summary>
public class MonthlyBudget
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    /// <summary>A month that's over is archived: hidden from the main list and read-only.</summary>
    public bool IsArchived { get; set; }

    /// <summary>Set when someone unarchives a past month, so it isn't archived again automatically.</summary>
    public bool KeepOpen { get; set; }

    public List<PayPeriod> PayPeriods { get; set; } = new();
    public List<BudgetExpense> Expenses { get; set; } = new();

    [NotMapped]
    public DateTime FirstDay => new(Year, Month, 1);

    [NotMapped]
    public string Title => FirstDay.MonthTitle();
}
