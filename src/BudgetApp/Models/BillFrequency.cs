using System.ComponentModel.DataAnnotations;

namespace BudgetApp.Models;

/// <summary>How often a recurring bill comes due.</summary>
public enum BillFrequency
{
    Monthly = 0,
    Weekly = 1,
    [Display(Name = "Every two weeks")]
    BiWeekly = 2
}
