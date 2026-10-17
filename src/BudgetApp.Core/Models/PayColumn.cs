using BudgetApp.Helpers;

namespace BudgetApp.Models;

/// <summary>One payday on the month page, shown as a column like the budget spreadsheet.</summary>
public class PayColumn
{
    /// <summary>Paydays this close together are shown as one column.</summary>
    public const int MergeWithinDays = 3;

    public List<PayPeriod> Paychecks { get; init; } = new();
    public List<BudgetExpense> Expenses { get; init; } = new();

    public bool IsUnassigned => Paychecks.Count == 0;

    /// <summary>Where new bills added to this column go.</summary>
    public int? TargetPayPeriodId => Paychecks.FirstOrDefault()?.Id;

    /// <summary>e.g. "October 9th" or "September 30th/October 2nd".</summary>
    public string Heading => IsUnassigned
        ? "Not assigned"
        : string.Join("/", Paychecks.Select(p => p.PayDate.Date).Distinct().Select(d => d.ToString("MMMM ") + Dates.Ordinal(d.Day)));

    public decimal Income => Paychecks.Sum(p => p.Total);
    public decimal Spending => Expenses.Sum(e => e.Amount);
    public decimal LeftOver => Income - Spending;

    // Where things stand now: what's been paid, what's on its way, and what's left once paid bills are out.
    public decimal Paid => Expenses.Where(e => e.Status == PaymentStatus.Complete).Sum(e => e.Amount);
    public decimal Pending => Expenses.Where(e => e.Status == PaymentStatus.Pending).Sum(e => e.Amount);
    public decimal StillToPay => Expenses.Where(e => e.Status == PaymentStatus.NotPaid).Sum(e => e.Amount);
    public decimal LeftNow => Income - Paid - Pending;

    // The original plan (paychecks added after it was saved count as planned at zero).
    public decimal PlannedIncome => Paychecks.Sum(p => p.PlannedIncome ?? 0);
    public decimal PlannedSpending => Paychecks.Sum(p => p.PlannedSpending ?? 0);
    public decimal PlannedLeftOver => PlannedIncome - PlannedSpending;

}
