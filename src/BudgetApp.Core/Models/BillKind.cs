using System.ComponentModel.DataAnnotations;

namespace BudgetApp.Models;

/// <summary>What sort of recurring item this is. Only loans have a balance and interest and show on Debt Payoff.</summary>
public enum BillKind
{
    /// <summary>Utilities, cell phone, insurance, subscriptions...</summary>
    Bill = 0,

    /// <summary>Food, gas and other spending that comes every week or two.</summary>
    [Display(Name = "Living expense")]
    LivingExpense = 1,

    /// <summary>Mortgage, car loan, credit card: something with an amount owed and interest.</summary>
    Loan = 2
}
