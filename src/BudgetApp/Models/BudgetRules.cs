using System.ComponentModel.DataAnnotations;

namespace BudgetApp.Models;

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
