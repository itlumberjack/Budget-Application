using BudgetApp.Services;

namespace BudgetApp.Core.Tests;

public class DebtExtraTests
{
    [Fact]
    public void Even_paydays_split_the_extra_evenly()
    {
        Assert.Equal([125m, 125m, 125m, 125m], DebtExtraSplitter.Spread([1000, 1000, 1000, 1000], 500));
    }

    [Fact]
    public void Paydays_with_more_left_over_give_more_so_they_end_up_level()
    {
        // Left over before: 1,200 / 900 / 1,000 / 700. The 700 payday gives nothing; the others end up at about 867.
        Assert.Equal([334m, 33m, 133m, 0m], DebtExtraSplitter.Spread([1200, 900, 1000, 700], 500));
    }

    [Fact]
    public void Rounding_keeps_whole_dollars_and_the_total()
    {
        var shares = DebtExtraSplitter.Spread([1000, 1000, 1000], 500);
        Assert.Equal(500m, shares.Sum());
        Assert.All(shares, s => Assert.Equal(Math.Floor(s), s));
        Assert.Equal(1, shares.Max() - shares.Min());
    }

    [Fact]
    public void More_extra_than_left_over_comes_from_every_payday_equally()
    {
        Assert.Equal([300m, 200m], DebtExtraSplitter.Spread([200, 100], 500));
    }

    [Fact]
    public void Shares_go_to_the_first_loan_then_spill_to_the_next()
    {
        var payments = DebtExtraSplitter.Assign([(1, 150), (2, 150)], [(10, 200), (20, 5000)]);
        Assert.Equal(
            [new ExtraPayment(1, 10, 150), new ExtraPayment(2, 10, 50), new ExtraPayment(2, 20, 100)],
            payments);
    }
}
