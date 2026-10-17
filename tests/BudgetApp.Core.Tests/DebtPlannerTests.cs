using BudgetApp.Models;
using BudgetApp.Services;

namespace BudgetApp.Core.Tests;

public class DebtPlannerTests
{
    private static readonly List<DebtInput> Debts =
    [
        new(1, "Car loan", 18500, 6.5m, 425, null),
        new(2, "Student loan", 9800, 5m, 110, null),
        new(3, "Visa", 4200, 22.9m, 120, null),
    ];

    private static PayoffPlan Plan(PayoffStrategy strategy) =>
        DebtPlanner.Plan(Debts, strategy, extraMonthly: 300, firstMonth: new DateTime(2026, 11, 1));

    [Fact]
    public void Avalanche_pays_the_highest_rate_first()
    {
        var plan = Plan(PayoffStrategy.Avalanche);
        Assert.Equal("Visa", plan.Debts[0].Debt.Name);
        Assert.Equal(3541.43m, plan.TotalInterest);
        Assert.Equal(38, plan.Months);
    }

    [Fact]
    public void Snowball_costs_more_interest_than_avalanche_here()
    {
        Assert.Equal(3679.11m, Plan(PayoffStrategy.Snowball).TotalInterest);
    }

    [Fact]
    public void Minimums_only_takes_over_nine_years()
    {
        var plan = Plan(PayoffStrategy.MinimumOnly);
        Assert.Equal(7928.30m, plan.TotalInterest);
        Assert.Equal(112, plan.Months);
    }

    [Fact]
    public void A_paid_off_loan_has_a_last_payment_then_none_and_its_payment_rolls_into_the_snowball()
    {
        var plan = Plan(PayoffStrategy.Avalanche);
        var visa = plan.Debts.First(d => d.Debt.Id == 3);
        var last = visa.PayoffMonth!.Value - 1;
        // Full minimum before the payoff month, what's left in it, nothing after.
        Assert.Equal(120, DebtPlanner.RegularPayment(plan, 3, last - 1));
        Assert.InRange(DebtPlanner.RegularPayment(plan, 3, last), 0.01m, 120);
        Assert.Equal(0, DebtPlanner.RegularPayment(plan, 3, last + 1));
        Assert.Equal(0, DebtPlanner.RegularPayment(plan, 3, 9999));
        // The month after, the snowball is the extra plus the Visa's freed-up minimum.
        Assert.Equal(300, DebtPlanner.MonthlySnowball(plan, 0, 0));
        Assert.Equal(420, DebtPlanner.MonthlySnowball(plan, last + 1, 0));
    }
}
