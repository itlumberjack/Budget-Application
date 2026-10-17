using BudgetApp.Models;
using BudgetApp.Services;

namespace BudgetApp.Core.Tests;

public class BillBalancerTests
{
    /// <summary>October with a $1,000 payday on the 2nd carrying three bills and a $3,000 payday on the 16th carrying none.</summary>
    private static MonthViewModel October()
    {
        var budget = new MonthlyBudget { Id = 1, Year = 2026, Month = 10 };
        budget.PayPeriods.Add(new PayPeriod { Id = 1, Label = "Jessica", PayDate = new DateTime(2026, 10, 2), BaseAmount = 1000 });
        budget.PayPeriods.Add(new PayPeriod { Id = 2, Label = "Brad", PayDate = new DateTime(2026, 10, 16), BaseAmount = 3000 });
        budget.Expenses.Add(new BudgetExpense { Id = 10, Title = "Food", Amount = 300, DueDay = 2, PayPeriodId = 1, IsFixed = true });
        budget.Expenses.Add(new BudgetExpense { Id = 11, Title = "Gifts", Amount = 500, PayPeriodId = 1 });
        budget.Expenses.Add(new BudgetExpense { Id = 12, Title = "Rent", Amount = 100, DueDay = 5, PayPeriodId = 1 });
        return new MonthViewModel { Year = 2026, Month = 10, Budget = budget };
    }

    [Fact]
    public void Moves_only_flexible_bills_that_stay_on_time()
    {
        var moves = BillBalancer.Suggest(October()).Moves;
        // Food is fixed and Rent is due the 5th, before the 16th's payday; only Gifts (no due day) can move.
        var move = Assert.Single(moves);
        Assert.Equal("Gifts", move.Title);
        Assert.Equal(2, move.ToPayPeriodId);
    }

    [Fact]
    public void A_bill_due_before_a_payday_cannot_move_to_it()
    {
        var month = October();
        var rent = month.Budget!.Expenses.Single(e => e.Title == "Rent");
        Assert.False(month.CanMoveTo(month.Columns[1], rent));
        Assert.True(month.CanMoveTo(month.Columns[0], rent));
    }
}
