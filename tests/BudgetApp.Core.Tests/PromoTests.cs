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
    public void A_promo_that_would_be_missed_moves_to_the_front_of_the_line()
    {
        // Avalanche would put all the extra on the 29.9% car loan and miss the promo; the card goes first instead,
        // and it alone gets the extra until it's paid off.
        var plan = DebtPlanner.Plan([Card(), CarLoan()], PayoffStrategy.Avalanche, 300, Start);
        Assert.True(plan.Promos.Single().ClearsInTime);
        Assert.Equal([1], plan.Schedule[0].Extra.Keys);
        Assert.Equal(300m, plan.Schedule[0].Extra[1]);
        Assert.Equal(1, plan.Debts[0].Debt.Id);
    }

    [Fact]
    public void A_promo_cleared_in_time_anyway_keeps_its_place()
    {
        // A promo ending far out clears after the car loan, so the strategy's order stands.
        var card = Card() with { Promo = new PromoTerms(0, null, new DateTime(2030, 1, 1), false) };
        var plan = DebtPlanner.Plan([card, CarLoan()], PayoffStrategy.Avalanche, 300, Start);
        Assert.True(plan.Promos.Single().ClearsInTime);
        Assert.Equal([2], plan.Schedule[0].Extra.Keys);
    }

    [Fact]
    public void The_snowball_goes_to_one_loan_at_a_time()
    {
        var small = new DebtInput(1, "Small", 500, 20, 25, null);
        var plan = DebtPlanner.Plan([small, CarLoan()], PayoffStrategy.Snowball, 200, Start);
        Assert.Equal([1], plan.Schedule[0].Extra.Keys);
        Assert.Equal([1], plan.Schedule[1].Extra.Keys);
        // The month the small loan is paid off, the rest of the extra moves on to the car loan.
        var payoff = plan.Debts.First(d => d.Debt.Id == 1).PayoffMonth!.Value;
        Assert.Equal([1, 2], plan.Schedule[payoff - 1].Extra.Keys);
        Assert.Equal([2], plan.Schedule[payoff].Extra.Keys);
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
