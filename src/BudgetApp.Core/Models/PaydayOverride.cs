namespace BudgetApp.Models;

/// <summary>
/// A payday that comes on a different day than usual, such as an early payday before a holiday. The paycheck
/// still belongs to the month its regular date would put it in.
/// </summary>
public class PaydayOverride
{
    public int Id { get; set; }
    public int IncomeSourceId { get; set; }

    /// <summary>The day the schedule would pay.</summary>
    public DateTime RegularDate { get; set; }

    /// <summary>The day it's actually paid.</summary>
    public DateTime PayDate { get; set; }
}
