using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BudgetApp.Models;

/// <summary>Extra money added to a paycheck, such as a bonus or overtime.</summary>
public class PayPeriodExtra
{
    public int Id { get; set; }
    public int PayPeriodId { get; set; }
    public PayPeriod? PayPeriod { get; set; }

    [Required, StringLength(100)]
    public string Description { get; set; } = "";

    [Column(TypeName = "decimal(12,2)")]
    public decimal Amount { get; set; }

    /// <summary>A one-time extra from Debt Payoff (a bonus, a tax refund) counted as income on the payday it's paid from.</summary>
    public bool FromDebtExtra { get; set; }
}
