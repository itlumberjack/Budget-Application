using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BudgetApp.Models;

public enum PaymentStatus
{
    [Display(Name = "Not paid")]
    NotPaid = 0,
    Pending = 1,
    /// <summary>The money has come out of the account.</summary>
    Complete = 2
}

public enum PayFrequency
{
    Weekly = 1,
    [Display(Name = "Every two weeks")]
    BiWeekly = 2,
    [Display(Name = "Twice a month (15th and last day)")]
    SemiMonthly = 3,
    Monthly = 4
}

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

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}

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

public enum PayoffStrategy
{
    [Display(Name = "Debt avalanche (highest interest first)")] Avalanche = 1,
    [Display(Name = "Debt snowball (lowest balance first)")] Snowball = 2,
    [Display(Name = "Highest balance first")] HighestBalance = 3,
    [Display(Name = "Order entered")] OrderEntered = 4,
    [Display(Name = "Custom priority (lowest number first)")] CustomLowestFirst = 5,
    [Display(Name = "Custom priority (highest number first)")] CustomHighestFirst = 6,
    [Display(Name = "No snowball (minimum payments only)")] MinimumOnly = 7
}

/// <summary>Household-wide rules for which month a paycheck is budgeted in. There is a single row.</summary>
public class BudgetRules
{
    public int Id { get; set; }

    /// <summary>Monthly paychecks paid on the last day of the month go in the next month's budget.</summary>
    [Display(Name = "End-of-month paychecks pay next month's bills")]
    public bool EndOfMonthPayGoesToNextMonth { get; set; }

    /// <summary>
    /// For weekly pay: whichever payday is closest to the 1st starts the new month, even if it lands
    /// a few days before the 1st. On a tie, the earlier payday wins.
    /// </summary>
    [Display(Name = "Weekly payday closest to the 1st starts the month")]
    public bool WeeklyClosestToFirstStartsMonth { get; set; }
}

public class Category
{
    public int Id { get; set; }

    [Required, StringLength(60)]
    public string Name { get; set; } = "";

    [Display(Name = "Sort order")]
    public int SortOrder { get; set; }
}

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

/// <summary>The budget for one calendar month.</summary>
public class MonthlyBudget
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public List<PayPeriod> PayPeriods { get; set; } = new();
    public List<BudgetExpense> Expenses { get; set; } = new();

    [NotMapped]
    public DateTime FirstDay => new(Year, Month, 1);

    [NotMapped]
    public string Title => FirstDay.ToString("MMMM yyyy");
}

/// <summary>One paycheck that lands in a given month.</summary>
public class PayPeriod
{
    public int Id { get; set; }
    public int MonthlyBudgetId { get; set; }
    public MonthlyBudget? MonthlyBudget { get; set; }

    public int? IncomeSourceId { get; set; }
    public IncomeSource? IncomeSource { get; set; }

    [Required, StringLength(100)]
    public string Label { get; set; } = "";

    [DataType(DataType.Date)]
    public DateTime PayDate { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal BaseAmount { get; set; }

    public List<PayPeriodExtra> Extras { get; set; } = new();

    [NotMapped]
    public decimal Total => BaseAmount + Extras.Sum(e => e.Amount);
}

/// <summary>Extra money added to a paycheck, such as a bonus or overtime.</summary>
public class PayPeriodExtra
{
    public int Id { get; set; }
    public int PayPeriodId { get; set; }
    public PayPeriod? PayPeriod { get; set; }

    [Required, StringLength(100)]
    public string Description { get; set; } = "";

    [Column(TypeName = "decimal(12,2)")]
    public decimal Amount { get; set; }
}

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
}
