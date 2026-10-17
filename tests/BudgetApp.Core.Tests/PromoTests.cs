using BudgetApp.Models;
using BudgetApp.Services;

namespace BudgetApp.Core.Tests;

public class PromoTests
{
    private static readonly DateTime Start = new(2026, 11, 1);

    // $1,200 at 0% until April 2027 (six payments: Nov through Apr), 26.99% after; $25 minimum.
    private static DebtInput Card(bool deferred = false) =>
        new(1, "Store card", 1200, 26.99m, 25, null, new PromoTerms(0, null, new DateTime(2027, 4, 15), deferred));

    private static DebtInput CarLoan() => new(2, "Car loan", 9000, 29.9m, 300, null);

    [Fact]
    public void Enough_extra_clears_the_promo_in_time_with_no_interest()
    {
        var plan = DebtPlanner.Plan([Card()], PayoffStrategy.Avalanche, 200, Start);
        var check = Assert.Single(plan.Promos);
        Assert.True(check.ClearsInTime);
        Assert.Equal(new DateTime(2027, 4, 1), check.ClearedMonth);
        Assert.Equal(0, plan.TotalInterest);
        Assert.Equal(200m, check.NeededMonthly);
    }

    [Fact]
    public void Promo_deadline_comes_before_the_strategy_order()
    {
        // Avalanche would put all the extra on the 29.9% car loan; the promo card still gets what it needs first.
        var plan = DebtPlanner.Plan([Card(), CarLoan()], PayoffStrategy.Avalanche, 300, Start);
        Assert.True(plan.Promos.Single().ClearsInTime);
        Assert.Equal(175m, plan.Schedule[0].Extra[1]); // 200 a month needed, 25 of it is the minimum
        Assert.Equal(125m, plan.Schedule[0].Extra[2]);
        Assert.Equal([1, 2], plan.Schedule[0].Extra.Keys);
    }

    [Fact]
    public void Missing_a_deferred_interest_promo_charges_the_interest_back()
    {
        var plan = DebtPlanner.Plan([Card(deferred: true)], PayoffStrategy.Avalanche, 50, Start);
        var check = plan.Promos.Single();
        Assert.False(check.ClearsInTime);
        Assert.Equal(750m, check.LeftAtEnd); // six payments of $75
        Assert.True(check.DeferredInterestCharged > 100);
        Assert.Equal(check.DeferredInterestCharged, plan.Schedule[5].Interest);
    }

    [Fact]
    public void Without_deferred_interest_only_the_rest_starts_charging_interest()
    {
        var plan = DebtPlanner.Plan([Card()], PayoffStrategy.Avalanche, 50, Start);
        Assert.Equal(0, plan.Promos.Single().DeferredInterestCharged);
        Assert.Equal(0, plan.Schedule[5].Interest);
        Assert.True(plan.Schedule[6].Interest > 0);
    }
}
