using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BudgetApp.Models;

/// <summary>What a line on a check stub is: pay earned, a tax, or a deduction taken before or after taxes.</summary>
public enum StubLineKind
{
    Earnings,
    Tax,
    [Display(Name = "Before-tax deduction")] PreTaxDeduction,
    [Display(Name = "After-tax deduction")] AfterTaxDeduction
}

/// <summary>One line from the paycheck's latest check stub, such as "Federal income tax $412.18".</summary>
public class PayStubLine
{
    public int Id { get; set; }
    public int IncomeSourceId { get; set; }

    public StubLineKind Kind { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    [Column(TypeName = "decimal(12,2)")]
    public decimal Amount { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal? YearToDate { get; set; }
}
