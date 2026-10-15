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

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
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

    [Column(TypeName = "decimal(12,2)")]
    public decimal Amount { get; set; }

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    [Range(1, 31)]
    public int? DueDay { get; set; }

    /// <summary>Set when this line came from a recurring debt.</summary>
    public int? RecurringDebtId { get; set; }
    public RecurringDebt? RecurringDebt { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.NotPaid;
}
