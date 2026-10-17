using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BudgetApp.Helpers;

namespace BudgetApp.Models;

/// <summary>The budget for one calendar month.</summary>
public class MonthlyBudget
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    /// <summary>A month that's over is archived: hidden from the main list and read-only.</summary>
    public bool IsArchived { get; set; }

    /// <summary>Set when someone unarchives a past month, so it isn't archived again automatically.</summary>
    public bool KeepOpen { get; set; }

    /// <summary>
    /// When the original plan was saved, so each payday can show the plan next to where things are now.
    /// Saved when the first bill is marked pending or complete, or by hand.
    /// </summary>
    public DateTime? PlanSavedAt { get; set; }

    public bool HasPlan => PlanSavedAt is not null;

    /// <summary>
    /// Saves the month as it stands as its original plan: each paycheck's income and spending and each bill's
    /// amount. Needs the paychecks (with extras) and bills loaded.
    /// </summary>
    public void SavePlan(DateTime now)
    {
        foreach (var pay in PayPeriods)
        {
            pay.PlannedIncome = pay.Total;
            pay.PlannedSpending = Expenses.Where(e => e.PayPeriodId == pay.Id).Sum(e => e.Amount);
        }
        foreach (var bill in Expenses) bill.PlannedAmount = bill.Amount;
        PlanSavedAt = now;
    }

    public List<PayPeriod> PayPeriods { get; set; } = new();
    public List<BudgetExpense> Expenses { get; set; } = new();

    [NotMapped]
    public DateTime FirstDay => new(Year, Month, 1);

    [NotMapped]
    public string Title => FirstDay.MonthTitle();
}
