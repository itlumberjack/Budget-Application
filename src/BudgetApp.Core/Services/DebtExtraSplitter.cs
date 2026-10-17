namespace BudgetApp.Services;

/// <summary>One payday's share of the month's extra debt payment, and the loan it goes to.</summary>
public record ExtraPayment(int PayPeriodId, int DebtId, decimal Amount);

/// <summary>
/// Spreads the Debt Payoff page's extra monthly payment over a month's paydays. Paydays with more left over
/// give more, so every payday ends up with about the same left over. Each payday's share then goes to the
/// loans in the payoff strategy's order, moving on to the next loan once one is covered.
/// </summary>
public static class DebtExtraSplitter
{
    /// <summary>
    /// How much of the total each payday gives, given what each has left over before it. Takes from the
    /// paydays with the most left over first until they're level, then from all of them equally. Whole
    /// dollars when the total is whole dollars, otherwise cents; the shares always add up to the total.
    /// </summary>
    public static List<decimal> Spread(IReadOnlyList<decimal> leftovers, decimal total)
    {
        var n = leftovers.Count;
        if (n == 0 || total <= 0) return leftovers.Select(_ => 0m).ToList();

        // The level every payday that gives anything ends up at: the x where the sum of (leftover - x) is the total.
        var sorted = leftovers.OrderByDescending(l => l).ToList();
        decimal level = 0, running = 0;
        for (var k = 1; k <= n; k++)
        {
            running += sorted[k - 1];
            level = (running - total) / k;
            if (k == n || level >= sorted[k]) break;
        }
        var ideal = leftovers.Select(l => Math.Max(0, l - level)).ToList();

        // Round down to the unit, then hand the units left over to the shares that lost the most in rounding.
        var unit = total == Math.Floor(total) ? 1m : 0.01m;
        var shares = ideal.Select(x => Math.Floor(x / unit) * unit).ToList();
        var left = (int)Math.Round((total - shares.Sum()) / unit);
        foreach (var i in Enumerable.Range(0, n).OrderByDescending(i => ideal[i] - shares[i]).ThenByDescending(i => leftovers[i]).Take(left))
            shares[i] += unit;
        return shares;
    }

    /// <summary>
    /// Gives each payday's share to the loans in order, up to what each still owes (capacity). A share that
    /// covers one loan carries on to the next; anything beyond every loan's capacity is left out.
    /// </summary>
    public static List<ExtraPayment> Assign(IReadOnlyList<(int PayPeriodId, decimal Amount)> shares,
        IReadOnlyList<(int DebtId, decimal Capacity)> loans)
    {
        var payments = new List<ExtraPayment>();
        var capacity = loans.Select(l => l.Capacity).ToArray();
        var current = 0;
        foreach (var (payId, amount) in shares)
        {
            var left = amount;
            while (left > 0 && current < loans.Count)
            {
                var pay = Math.Min(left, capacity[current]);
                if (pay > 0)
                {
                    var same = payments.FindIndex(p => p.PayPeriodId == payId && p.DebtId == loans[current].DebtId);
                    if (same >= 0) payments[same] = payments[same] with { Amount = payments[same].Amount + pay };
                    else payments.Add(new ExtraPayment(payId, loans[current].DebtId, pay));
                    capacity[current] -= pay;
                    left -= pay;
                }
                if (capacity[current] <= 0) current++;
            }
        }
        return payments;
    }
}
