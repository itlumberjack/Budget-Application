using System.ComponentModel.DataAnnotations;

namespace BudgetApp.Models;

public enum PayFrequency
{
    Weekly = 1,
    [Display(Name = "Every two weeks")]
    BiWeekly = 2,
    [Display(Name = "Twice a month (15th and last day)")]
    SemiMonthly = 3,
    Monthly = 4
}
