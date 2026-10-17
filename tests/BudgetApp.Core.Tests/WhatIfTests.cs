using BudgetApp.Models;
using BudgetApp.Services;

namespace BudgetApp.Core.Tests;

public class WhatIfTests
{
    private static readonly DateTime Start = new(2026, 11, 1);
    private static readonly List<DebtInput> Loans =
    [
        new(1, "HELOC", 40000, 9.5m, 400, null),
        new(2, "Visa", 6000, 24.99m, 180, null),
        new(3, "Car", 15000, 7.9m, 450, null)
    ];
    private static readonly DebtPlanSettings Settings = new() { Strategy = PayoffStrategy.Avalanche, ExtraMonthly = 200 };

    private static WhatIfScenario Scenario(List<Consolidation>? cons = null, List<LoanChange>? changes = null, decimal? extra = null) =>
        new("test", [], changes ?? [], cons ?? [], [], extra, null, null, null, null);

    [Fact]
    public void Consolidating_into_the_heloc_keeps_the_payments_and_saves_interest()
    {
        var result = WhatIfCalculator.Run(Loans, Settings, Scenario([new Consolidation(1, null, [2, 3], 7, null, null, null)]), Start);
        var heloc = Assert.Single(result.AfterDebts);
        Assert.Equal(61000, heloc.Balance);
        Assert.Equal(7, heloc.Apr);
        Assert.Equal(1030, heloc.MinPayment);
        Assert.True(result.InterestSaved > 0);
        Assert.Null(result.AfterSamePayment); // payments didn't go down
    }

    [Fact]
    public void A_term_sets_the_payment_and_a_lower_payment_also_shows_paying_the_same()
    {
        var result = WhatIfCalculator.Run(Loans, Settings, Scenario([new Consolidation(1, null, [2, 3], 7, 500, 240, null)]), Start);
        var heloc = Assert.Single(result.AfterDebts);
        Assert.Equal(61500, heloc.Balance);
        Assert.Equal(WhatIfCalculator.Payment(61500, 7, 240), heloc.MinPayment);
        Assert.True(heloc.MinPayment < 1030);
        Assert.NotNull(result.AfterSamePayment);
        Assert.True(result.AfterSamePayment!.TotalInterest < result.After.TotalInterest);
    }

    [Fact]
    public void Payment_matches_the_loan_formula()
    {
        Assert.Equal(1073.65m, WhatIfCalculator.Payment(200000, 5, 360)); // 1073.643 rounded up
        Assert.Equal(100m, WhatIfCalculator.Payment(1200, 0, 12));
    }

    [Fact]
    public void Paying_a_loan_off_and_more_extra_change_the_plan()
    {
        var result = WhatIfCalculator.Run(Loans, Settings, Scenario(changes: [new LoanChange(2, null, null, null, true)], extra: 500), Start);
        Assert.DoesNotContain(result.AfterDebts, d => d.Id == 2);
        Assert.True(result.MonthsSooner > 0);
        Assert.Contains(result.Notes, n => n.Contains("Visa is paid off"));
    }
}
