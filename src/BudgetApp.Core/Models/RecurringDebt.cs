using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BudgetApp.Helpers;

namespace BudgetApp.Models;

/// <summary>
/// Something that repeats every month, week or two weeks: a bill (power, cell phone), a living expense
/// (food, gas) or a loan (car payment, credit card). Shown on the Bills &amp; Expenses page.
/// </summary>
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

    [Display(Name = "Type")]
    public BillKind Kind { get; set; } = BillKind.Bill;

    [Display(Name = "Category")]
    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    [Display(Name = "How often")]
    public BillFrequency Frequency { get; set; } = BillFrequency.Monthly;

    /// <summary>For a monthly bill: the day of the month it's due.</summary>
    [Display(Name = "Due day")]
    [Range(1, 31)]
    public int? DueDay { get; set; }

    /// <summary>For a weekly or every-two-weeks bill: any date it's due. Later ones are counted from it.</summary>
    [Display(Name = "First due date")]
    [DataType(DataType.Date)]
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// A fixed (mandatory) item stays on the payday it falls under, like weekly groceries. Even out and the
    /// AI helper only move flexible ones.
    /// </summary>
    [Display(Name = "Mandatory: stays on its payday")]
    public bool IsFixed { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    /// <summary>For a loan or card: what's still owed. Loans with a balance show on the Debt Payoff page.</summary>
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
    public bool IsLoan => Kind == BillKind.Loan && Balance > 0;

    /// <summary>Only loans keep a balance, interest rate and payoff priority.</summary>
    public void ClearLoanDetailsUnlessLoan()
    {
        if (Kind == BillKind.Loan) return;
        Balance = null;
        InterestRate = null;
        PayoffPriority = null;
        BalanceAsOf = null;
    }

    /// <summary>What the bill costs in an average month (a weekly bill comes 52 times a year).</summary>
    [NotMapped]
    public decimal MonthlyAmount => Frequency switch
    {
        BillFrequency.Weekly => Math.Round(Amount * 52 / 12, 2),
        BillFrequency.BiWeekly => Math.Round(Amount * 26 / 12, 2),
        _ => Amount
    };

    /// <summary>
    /// The days this bill is due in a month: one for a monthly bill (or none set), and every week or
    /// every other week from the first due date for the others. Nothing before the first due date.
    /// </summary>
    public IReadOnlyList<int?> DueDaysIn(int year, int month)
    {
        if (Frequency == BillFrequency.Monthly || StartDate is not DateTime start) return [DueDay];
        var step = Frequency == BillFrequency.Weekly ? 7 : 14;
        return Dates.RepeatingInMonth(start, step, year, month)
            .Where(d => d >= start.Date)
            .Select(d => (int?)d.Day)
            .ToList();
    }
}
