using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BudgetApp.Models;

/// <summary>A bill that repeats every month (power bill, mortgage, car payment...).</summary>
public class RecurringDebt
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Title { get; set; } = "";

    /// <summary>Who gets paid, e.g. "Trustmark" or "State Farm".</summary>
    [StringLength(100)]
    public string? Company { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    [Range(0, 10_000_000)]
    [DataType(DataType.Currency)]
    public decimal Amount { get; set; }

    [Display(Name = "Category")]
    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    [Display(Name = "Due day")]
    [Range(1, 31)]
    public int? DueDay { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    /// <summary>For a loan or card: what's still owed. Debts with a balance show on the Debt page.</summary>
    [Column(TypeName = "decimal(12,2)")]
    [Range(0, 100_000_000)]
    [DataType(DataType.Currency)]
    [Display(Name = "Amount owed")]
    public decimal? Balance { get; set; }

    /// <summary>Yearly interest rate (APR) as a percent, e.g. 6.5.</summary>
    [Column(TypeName = "decimal(6,3)")]
    [Range(0, 100)]
    [Display(Name = "Interest rate (APR %)")]
    public decimal? InterestRate { get; set; }

    /// <summary>Your own payoff order for the "Custom priority" strategy on the Debt page.</summary>
    [Range(0, 999)]
    [Display(Name = "Payoff priority")]
    public int? PayoffPriority { get; set; }

    /// <summary>When the amount owed was last entered.</summary>
    [DataType(DataType.Date)]
    public DateTime? BalanceAsOf { get; set; }

    [NotMapped]
    public bool IsLoan => Balance > 0;
}
