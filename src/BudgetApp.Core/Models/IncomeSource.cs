using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BudgetApp.Models;

/// <summary>A source of take-home pay, e.g. "Brad's paycheck" or "Wife's paycheck".</summary>
public class IncomeSource
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    [Display(Name = "Take-home pay per paycheck")]
    [Column(TypeName = "decimal(12,2)")]
    [Range(0, 10_000_000)]
    [DataType(DataType.Currency)]
    public decimal TakeHomeAmount { get; set; }

    [Display(Name = "Pay schedule")]
    public PayFrequency Frequency { get; set; } = PayFrequency.Monthly;

    /// <summary>Any known payday. Used for weekly and every-two-weeks schedules to find the rest.</summary>
    [Display(Name = "A recent payday")]
    [DataType(DataType.Date)]
    public DateTime? AnchorDate { get; set; }

    /// <summary>For monthly pay. Leave empty to be paid on the last day of the month.</summary>
    [Display(Name = "Day of month")]
    [Range(1, 31)]
    public int? DayOfMonth { get; set; }

    /// <summary>
    /// For end-of-month pay that covers the next month's bills: the Sept 30 paycheck
    /// shows up in October's budget instead of September's.
    /// </summary>
    [Display(Name = "Budget this paycheck in the following month")]
    public bool CountsTowardNextMonth { get; set; }

    /// <summary>
    /// Pay on the last day of the month comes on the last business day instead: when the 31st is a Sunday,
    /// payday is Friday the 29th. Applies to monthly pay on the last day and the second twice-a-month check.
    /// </summary>
    [Display(Name = "When the last day is a weekend, payday is the Friday before")]
    public bool MonthEndOnBusinessDay { get; set; } = true;

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}
