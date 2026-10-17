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

    /// <summary>
    /// The payday the monthly extra comes out of: the paycheck from this income (the ExtraPaydayNumber-th one in the
    /// month, 0 for the last). Null spreads it over the month's paydays so they're left with about the same.
    /// </summary>
    public int? ExtraIncomeSourceId { get; set; }
    public int ExtraPaydayNumber { get; set; }

    /// <summary>One-time extra payments, each on its own date (DebtLumpSum rows, filled in by GetDebtPlanAsync).</summary>
    [NotMapped]
    public List<LumpSum> LumpSums { get; set; } = new();

    /// <summary>The one-time extras in a month.</summary>
    public decimal LumpSumsIn(int year, int month) => LumpSums.Where(l => l.Date.Year == year && l.Date.Month == month).Sum(l => l.Amount);

    /// <summary>
    /// No longer offered: loans used to be payable weekly or every two weeks. Kept so a schedule picked back then
    /// can be undone at startup (BudgetBuilder.UndoLoanPaymentScheduleAsync).
    /// </summary>
    public BillFrequency PaymentFrequency { get; set; } = BillFrequency.Monthly;

    public DateTime? PaymentStartDate { get; set; }
}
