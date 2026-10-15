using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BudgetApp.Models;

/// <summary>The budget for one calendar month.</summary>
public class MonthlyBudget
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public List<PayPeriod> PayPeriods { get; set; } = new();
    public List<BudgetExpense> Expenses { get; set; } = new();

    [NotMapped]
    public DateTime FirstDay => new(Year, Month, 1);

    [NotMapped]
    public string Title => FirstDay.ToString("MMMM yyyy");
}
