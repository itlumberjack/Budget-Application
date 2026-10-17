using BudgetApp.Models;

namespace BudgetApp.Services;

/// <summary>
/// Debt payoff math in the style of the Vertex42 debt reduction calculator. Each month every debt
/// gets its interest (balance × APR ÷ 12) and its minimum payment. The rest of the monthly debt payment,
/// the "snowball", goes to the first unpaid debt in the strategy's order, and spills to the next one
/// when that debt is paid off. A paid-off debt's minimum joins the snowball. "No snowball" pays only
/// the minimums: no extra, no one-time payment, and nothing rolls over.
/// </summary>
public static class DebtPlanner
{
    public const int MaxMonths = 600; // 50 years: anything longer is treated as never paid off

    public static List<DebtInput> Order(IEnumerable<DebtInput> debts, PayoffStrategy strategy) => strategy switch
    {
        PayoffStrategy.Avalanche => debts.OrderByDescending(d => d.Apr).ThenBy(d => d.Balance).ToList(),
        PayoffStrategy.Snowball => debts.OrderBy(d => d.Balance).ThenByDescending(d => d.Apr).ToList(),
        PayoffStrategy.HighestBalance => debts.OrderByDescending(d => d.Balance).ToList(),
        PayoffStrategy.CustomLowestFirst => debts.OrderBy(d => d.Priority ?? int.MaxValue).ThenBy(d => d.Id).ToList(),
        PayoffStrategy.CustomHighestFirst => debts.OrderByDescending(d => d.Priority ?? int.MinValue).ThenBy(d => d.Id).ToList(),
        _ => debts.OrderBy(d => d.Id).ToList() // order entered
    };

    /// <summary>
    /// The payoff plan. The snowball goes to one loan at a time, in the strategy's order, and moves on only once
    /// that loan is paid off. A loan with a promo the plan wouldn't clear before it ends moves to the front of
    /// the line (soonest-ending promo first), so the extra clears it in time if it can.
    /// </summary>
    public static PayoffPlan Plan(IReadOnlyList<DebtInput> debts, PayoffStrategy strategy, decimal extraMonthly,
        DateTime firstMonth, IReadOnlyList<LumpSum>? lumpSums = null)
    {
        lumpSums ??= [];
        var order = Order(debts, strategy);
        var plan = Simulate(debts, order, strategy, extraMonthly, firstMonth, lumpSums);
        if (strategy == PayoffStrategy.MinimumOnly) return plan;
        var first = new List<int>();
        var skipped = new HashSet<int>();
        int Cleared(PayoffPlan x) => x.Promos.Count(p => p.ClearsInTime);
        while (plan.Promos.Where(p => !p.ClearsInTime && !first.Contains(p.DebtId) && !skipped.Contains(p.DebtId))
                   .OrderBy(p => p.EndDate).FirstOrDefault() is { } missed)
        {
            var tryFirst = first.Append(missed.DebtId).ToList();
            var promoted = tryFirst.Select(id => order.First(d => d.Id == id)).OrderBy(d => d.Promo!.EndDate).ToList();
            var tried = Simulate(debts, promoted.Concat(order.Where(d => !tryFirst.Contains(d.Id))).ToList(), strategy,
                extraMonthly, firstMonth, lumpSums);
            // Keep the new order only if it clears more promos in time (or as many for less interest).
            if (Cleared(tried) > Cleared(plan) || (Cleared(tried) == Cleared(plan) && tried.TotalInterest < plan.TotalInterest))
                (plan, first) = (tried, tryFirst);
            else
                skipped.Add(missed.DebtId);
        }
        return plan;
    }

    private static PayoffPlan Simulate(IReadOnlyList<DebtInput> debts, List<DebtInput> ordered, PayoffStrategy strategy,
        decimal extraMonthly, DateTime firstMonth, IReadOnlyList<LumpSum> lumpSums)
    {
        var snowballOn = strategy != PayoffStrategy.MinimumOnly;
        var monthlyPayment = debts.Sum(d => d.MinPayment) + (snowballOn ? Math.Max(0, extraMonthly) : 0);

        var balance = debts.ToDictionary(d => d.Id, d => d.Balance);
        var interestPaid = debts.ToDictionary(d => d.Id, _ => 0m);
        var payoffMonth = new Dictionary<int, int>();
        var schedule = new List<ScheduleMonth>();

        // Promos: the part of each balance still under its promo rate, and what deferred interest would add up to.
        var promos = debts.Where(d => d.Promo is not null && d.Promo.EndDate >= firstMonth).ToDictionary(d => d.Id, d => d.Promo!);
        var promoLeft = promos.ToDictionary(p => p.Key, p => Math.Min(p.Value.Balance ?? balance[p.Key], balance[p.Key]));
        var promoStart = new Dictionary<int, decimal>(promoLeft);
        var deferred = promos.Keys.ToDictionary(id => id, _ => 0m);
        var deferredCharged = promos.Keys.ToDictionary(id => id, _ => 0m);
        var promoCleared = new Dictionary<int, DateTime>();
        var leftAtEnd = new Dictionary<int, decimal>();
        // Months of payments left before a promo ends, counting the month it ends in.
        int MonthsLeft(PromoTerms promo, DateTime month) => (promo.EndDate.Year - month.Year) * 12 + promo.EndDate.Month - month.Month + 1;

        for (var n = 1; n <= MaxMonths && balance.Values.Any(b => b > 0); n++)
        {
            var month = firstMonth.AddMonths(n - 1);
            var open = ordered.Where(d => balance[d.Id] > 0).ToList();
            var payments = open.ToDictionary(d => d.Id, _ => 0m);
            var extra = new Dictionary<int, decimal>();
            decimal interestThisMonth = 0;

            foreach (var d in open)
            {
                var regular = balance[d.Id];
                decimal interest = 0;
                if (promos.TryGetValue(d.Id, out var promo) && MonthsLeft(promo, month) >= 1 && promoLeft[d.Id] > 0)
                {
                    var promoPart = Math.Min(promoLeft[d.Id], regular);
                    regular -= promoPart;
                    interest += Math.Round(promoPart * promo.Rate / 1200m, 2);
                    if (promo.DeferredInterest) deferred[d.Id] += Math.Round(promoPart * d.Apr / 1200m, 2);
                }
                interest += Math.Round(regular * d.Apr / 1200m, 2);
                balance[d.Id] += interest;
                interestPaid[d.Id] += interest;
                interestThisMonth += interest;
            }

            // With the snowball the total paid each month stays the same; without it, only open debts' minimums are paid.
            var pool = snowballOn ? monthlyPayment : open.Sum(d => d.MinPayment);
            if (snowballOn)
                pool += lumpSums.Where(l => l.Amount > 0 && l.Date.Year == month.Year && l.Date.Month == month.Month).Sum(l => l.Amount);

            void Pay(DebtInput d, decimal amount, bool isExtra)
            {
                if (amount <= 0) return;
                payments[d.Id] += amount;
                balance[d.Id] -= amount;
                pool -= amount;
                if (isExtra) extra[d.Id] = extra.GetValueOrDefault(d.Id) + amount;
                // Payments come off the promo part last, so the higher-rate part goes first.
                if (promoLeft.ContainsKey(d.Id)) promoLeft[d.Id] = Math.Min(promoLeft[d.Id], balance[d.Id]);
            }

            foreach (var d in open)
                Pay(d, Math.Min(d.MinPayment, balance[d.Id]), false);
            var snowball = 0m;
            if (snowballOn)
            {
                foreach (var d in open)
                {
                    if (pool <= 0) break;
                    var pay = Math.Min(pool, balance[d.Id]);
                    if (pay <= 0) continue;
                    Pay(d, pay, true);
                    snowball += pay;
                }
            }

            // A promo that ends this month: note whether it was cleared, and charge deferred interest if not.
            foreach (var (id, promo) in promos)
            {
                if (!promoCleared.ContainsKey(id) && !leftAtEnd.ContainsKey(id) && promoLeft[id] <= 0 && MonthsLeft(promo, month) >= 1)
                    promoCleared[id] = month;
                if (MonthsLeft(promo, month) != 1 || promoCleared.ContainsKey(id)) continue;
                leftAtEnd[id] = promoLeft[id];
                if (promo.DeferredInterest && promoLeft[id] > 0)
                {
                    balance[id] += deferred[id];
                    interestPaid[id] += deferred[id];
                    interestThisMonth += deferred[id];
                    deferredCharged[id] = deferred[id];
                }
                promoLeft[id] = 0;
            }

            foreach (var d in open.Where(d => balance[d.Id] <= 0))
            {
                balance[d.Id] = 0;
                payoffMonth[d.Id] = n;
            }
            schedule.Add(new ScheduleMonth(n, month, payments, new Dictionary<int, decimal>(balance),
                interestThisMonth, snowball, balance.Values.Sum()) { Extra = extra });
        }

        var paysOff = balance.Values.All(b => b <= 0);
        var outcomes = ordered.Select((d, i) => new DebtOutcome(d, i + 1,
            payoffMonth.TryGetValue(d.Id, out var m) ? m : null,
            payoffMonth.TryGetValue(d.Id, out var m2) ? firstMonth.AddMonths(m2 - 1) : null,
            interestPaid[d.Id])).ToList();
        var checks = promos.Select(p =>
        {
            var d = debts.First(x => x.Id == p.Key);
            var months = Math.Max(1, MonthsLeft(p.Value, firstMonth));
            var cleared = promoCleared.TryGetValue(p.Key, out var when) ? when : (DateTime?)null;
            return new PromoCheck(d.Id, d.Name, promoStart[p.Key], p.Value.Rate, p.Value.EndDate, p.Value.DeferredInterest,
                cleared, cleared is not null, Math.Ceiling(d.Balance / months * 100) / 100,
                leftAtEnd.GetValueOrDefault(p.Key), deferredCharged[p.Key]);
        }).ToList();
        return new PayoffPlan(strategy, monthlyPayment, outcomes, schedule,
            interestPaid.Values.Sum(), schedule.Sum(s => s.Payments.Values.Sum()), paysOff) { Promos = checks };
    }

    /// <summary>
    /// Each loan's balance at the start of a month: today's balance for this month, and for later months what
    /// the payoff plan projects (interest and payments included). Months the plan never reaches count as paid off.
    /// </summary>
    public static Dictionary<int, decimal> StartBalances(IReadOnlyList<DebtInput> debts, DebtPlanSettings settings,
        DateTime thisMonth, int year, int month)
    {
        var index = (year - thisMonth.Year) * 12 + month - thisMonth.Month;
        if (index <= 0) return debts.ToDictionary(d => d.Id, d => d.Balance);
        var plan = Plan(debts, settings.Strategy, settings.ExtraMonthly, thisMonth, settings.LumpSums);
        return index <= plan.Schedule.Count
            ? new Dictionary<int, decimal>(plan.Schedule[index - 1].Balances)
            : debts.ToDictionary(d => d.Id, _ => 0m);
    }

    /// <summary>
    /// What's left on the loan after each of a month's loan payments (its regular payment and any extra), in
    /// payday order, starting from the loan's balance at the start of the month. Keyed by the month line's id.
    /// </summary>
    public static Dictionary<int, decimal> OwedAfterEachPayment(MonthlyBudget budget, IReadOnlyDictionary<int, decimal> startBalances)
    {
        var payDates = budget.PayPeriods.ToDictionary(p => p.Id, p => p.PayDate);
        var owedAfter = new Dictionary<int, decimal>();
        var byLoan = budget.Expenses
            .Select(e => (Expense: e, LoanId: e.ExtraForDebtId ?? e.RecurringDebtId))
            .Where(x => x.LoanId is int id && startBalances.ContainsKey(id))
            .GroupBy(x => x.LoanId!.Value);
        foreach (var loan in byLoan)
        {
            var owed = startBalances[loan.Key];
            foreach (var (e, _) in loan
                         .OrderBy(x => x.Expense.PayPeriodId is int p && payDates.TryGetValue(p, out var date) ? date : DateTime.MaxValue)
                         .ThenBy(x => x.Expense.ExtraForDebtId is null ? 0 : 1)
                         .ThenBy(x => x.Expense.DueDay ?? 32).ThenBy(x => x.Expense.Id))
            {
                owed = Math.Max(0, owed - e.Amount);
                owedAfter[e.Id] = owed;
            }
        }
        return owedAfter;
    }
}
