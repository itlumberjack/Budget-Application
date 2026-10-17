using System.ComponentModel.DataAnnotations.Schema;

namespace BudgetApp.Models;

/// <summary>How to pay debts off, from the Debt page. One row (Id = 1).</summary>
public class DebtPlanSettings : ISingleRow
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

    /// <summary>
    /// How often loans are paid. Every two weeks pays half the monthly payment each time, and weekly a quarter,
    /// which adds up to 13 monthly payments a year. Each loan on Bills &amp; Expenses follows this schedule.
    /// </summary>
    public BillFrequency PaymentFrequency { get; set; } = BillFrequency.Monthly;

    /// <summary>The first weekly or every-two-weeks payment; later ones are counted from it.</summary>
    public DateTime? PaymentStartDate { get; set; }
}
