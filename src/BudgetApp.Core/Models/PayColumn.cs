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

}
