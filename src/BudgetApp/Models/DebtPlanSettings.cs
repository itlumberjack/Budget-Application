using System.ComponentModel.DataAnnotations.Schema;

namespace BudgetApp.Models;

/// <summary>How to pay debts off, from the Debt page. One row (Id = 1).</summary>
public class DebtPlanSettings
{
    public int Id { get; set; }
    public PayoffStrategy Strategy { get; set; } = PayoffStrategy.Avalanche;

    /// <summary>Paid toward debt each month on top of the minimum payments (the starting snowball).</summary>
    [Column(TypeName = "decimal(12,2)")]
    public decimal ExtraMonthly { get; set; }

    /// <summary>A one-time extra payment (a "snowflake"), such as a tax refund.</summary>
    [Column(TypeName = "decimal(12,2)")]
    public decimal OneTimeAmount { get; set; }
    public DateTime? OneTimeMonth { get; set; }
}
