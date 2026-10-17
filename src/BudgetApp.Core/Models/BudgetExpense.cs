using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BudgetApp.Models;

/// <summary>A bill or spending line inside a month. Copied from a recurring debt, or added just for this month.</summary>
public class BudgetExpense
{
    public int Id { get; set; }
    public int MonthlyBudgetId { get; set; }
    public MonthlyBudget? MonthlyBudget { get; set; }

    [Required, StringLength(100)]
    public string Title { get; set; } = "";

    [StringLength(100)]
    public string? Company { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal Amount { get; set; }

    /// <summary>The paycheck this comes out of. Each payday is a column on the month page.</summary>
    public int? PayPeriodId { get; set; }
    public PayPeriod? PayPeriod { get; set; }

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    [Range(1, 31)]
    public int? DueDay { get; set; }

    /// <summary>Set when this line came from a recurring debt.</summary>
    public int? RecurringDebtId { get; set; }
    public RecurringDebt? RecurringDebt { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.NotPaid;

    /// <summary>Stays on its payday: Even out and the AI helper leave it where it is.</summary>
    public bool IsFixed { get; set; }

    /// <summary>The amount when the month's plan was saved. Null for bills added after that (or before any plan).</summary>
    [Column(TypeName = "decimal(12,2)")]
    public decimal? PlannedAmount { get; set; }

    /// <summary>
    /// Set on a payday's share of the Debt Payoff page's extra monthly payment, naming the loan it goes to.
    /// These are spread over the paydays automatically; see BudgetBuilder.SpreadDebtExtraAsync.
    /// </summary>
    public int? ExtraForDebtId { get; set; }
    public RecurringDebt? ExtraForDebt { get; set; }

    /// <summary>
    /// A share of the extra payment whose amount was set by hand. Respreading keeps it and lets the month's
    /// other paydays make up the difference. Removing a share sets it to $0 and keeps it, so it stays removed.
    /// </summary>
    public bool ExtraPinned { get; set; }

    /// <summary>A removed share of the extra payment, kept only so respreading leaves its payday out.</summary>
    [NotMapped]
    public bool IsRemovedExtra => ExtraForDebtId is not null && ExtraPinned && Amount == 0;
}
