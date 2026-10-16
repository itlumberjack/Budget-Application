using System.ComponentModel.DataAnnotations;

namespace BudgetApp.Models;

public enum PaymentStatus
{
    [Display(Name = "Not paid")]
    NotPaid = 0,
    Pending = 1,
    /// <summary>The money has come out of the account.</summary>
    Complete = 2
}
