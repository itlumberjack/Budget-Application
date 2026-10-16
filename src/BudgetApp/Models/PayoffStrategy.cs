using System.ComponentModel.DataAnnotations;

namespace BudgetApp.Models;

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
