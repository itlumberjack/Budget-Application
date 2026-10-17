using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BudgetApp.Models;

/// <summary>One paycheck that lands in a given month.</summary>
public class PayPeriod
{
    public int Id { get; set; }
    public int MonthlyBudgetId { get; set; }
    public MonthlyBudget? MonthlyBudget { get; set; }

    public int? IncomeSourceId { get; set; }
    public IncomeSource? IncomeSource { get; set; }

    [Required, StringLength(100)]
    public string Label { get; set; } = "";

    [DataType(DataType.Date)]
    public DateTime PayDate { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal BaseAmount { get; set; }

    public List<PayPeriodExtra> Extras { get; set; } = new();

    /// <summary>
    /// Shifted to another month by hand. Brad's rule leaves these where they were put; moving one back
    /// to the month it falls in clears this.
    /// </summary>
    public bool ManuallyMoved { get; set; }

    /// <summary>The take-home was changed by hand on the month page, so a new amount on Pay &amp; Schedule leaves it alone.</summary>
    public bool AmountSetByHand { get; set; }

    /// <summary>This paycheck's income and spending when the month's plan was saved. Null before then.</summary>
    [Column(TypeName = "decimal(12,2)")]
    public decimal? PlannedIncome { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal? PlannedSpending { get; set; }

    [NotMapped]
    public decimal Total => BaseAmount + Extras.Sum(e => e.Amount);
}
