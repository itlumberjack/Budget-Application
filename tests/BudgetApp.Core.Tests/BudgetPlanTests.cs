using BudgetApp.Models;

namespace BudgetApp.Core.Tests;

public class BudgetPlanTests
{
    private static (MonthlyBudget Month, MonthViewModel View) October()
    {
        var pay = new PayPeriod { Id = 1, Label = "Jessica", PayDate = new DateTime(2026, 10, 2), BaseAmount = 1500 };
        var month = new MonthlyBudget
        {
            Id = 1, Year = 2026, Month = 10,
            PayPeriods = [pay],
            Expenses =
            [
                new() { Id = 1, Title = "Food", Amount = 200, PayPeriodId = 1 },
                new() { Id = 2, Title = "Power", Amount = 300, PayPeriodId = 1 },
                new() { Id = 3, Title = "Phone", Amount = 100, PayPeriodId = 1 }
            ]
        };
        return (month, new MonthViewModel { Year = 2026, Month = 10, Budget = month });
    }

    [Fact]
    public void Saving_the_plan_records_each_payday_and_bill()
    {
        var (month, view) = October();
        month.SavePlan(new DateTime(2026, 10, 1));
        var column = view.Columns.Single();
        Assert.True(view.HasPlan);
        Assert.Equal(1500, column.PlannedIncome);
        Assert.Equal(600, column.PlannedSpending);
        Assert.Equal(900, column.PlannedLeftOver);
        Assert.All(month.Expenses, e => Assert.Equal(e.Amount, e.PlannedAmount));
    }

    [Fact]
    public void Now_moves_as_bills_are_paid_while_the_plan_stays_put()
    {
        var (month, view) = October();
        month.SavePlan(new DateTime(2026, 10, 1));
        month.Expenses[0].Status = PaymentStatus.Complete;
        month.Expenses[0].Amount = 230; // groceries came in over
        month.Expenses[1].Status = PaymentStatus.Pending;
        month.Expenses.Add(new BudgetExpense { Id = 4, Title = "Gift", Amount = 50, PayPeriodId = 1 });

        var column = view.Columns.Single();
        Assert.Equal(900, column.PlannedLeftOver);
        Assert.Equal(820, column.LeftOver);
        Assert.Equal(230, column.Paid);
        Assert.Equal(300, column.Pending);
        Assert.Equal(150, column.StillToPay);
        Assert.Equal(970, column.LeftNow);
        Assert.Null(month.Expenses[3].PlannedAmount);
    }
}
