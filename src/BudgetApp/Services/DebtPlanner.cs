using BudgetApp.Models;

namespace BudgetApp.Services;

/// <summary>One debt going into a payoff plan.</summary>
public record DebtInput(int Id, string Name, decimal Balance, decimal Apr, decimal MinPayment, int? Priority);

/// <summary>How one debt fares under a plan.</summary>
public record DebtOutcome(DebtInput Debt, int Order, int? PayoffMonth, DateTime? PayoffDate, decimal InterestPaid);

/// <summary>One month of a plan: what went to each debt (by Id) and what's left.</summary>
public record ScheduleMonth(int Number, DateTime Month, Dictionary<int, decimal> Payments, Dictionary<int, decimal> Balances,
    decimal Interest, decimal Snowball, decimal TotalBalance);

public record PayoffPlan(PayoffStrategy Strategy, decimal MonthlyPayment, List<DebtOutcome> Debts, List<ScheduleMonth> Schedule,
    decimal TotalInterest, decimal TotalPaid, bool PaysOff)
{
    public int Months => Schedule.Count;
    public DateTime? DebtFreeDate => PaysOff && Schedule.Count > 0 ? Schedule[^1].Month : null;
}

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

    public static PayoffPlan Plan(IReadOnlyList<DebtInput> debts, PayoffStrategy strategy, decimal extraMonthly,
        DateTime firstMonth, decimal oneTimeAmount = 0, DateTime? oneTimeMonth = null)
    {
        var ordered = Order(debts, strategy);
        var snowballOn = strategy != PayoffStrategy.MinimumOnly;
        var monthlyPayment = debts.Sum(d => d.MinPayment) + (snowballOn ? Math.Max(0, extraMonthly) : 0);

        var balance = debts.ToDictionary(d => d.Id, d => d.Balance);
        var interestPaid = debts.ToDictionary(d => d.Id, _ => 0m);
        var payoffMonth = new Dictionary<int, int>();
        var schedule = new List<ScheduleMonth>();

        for (var n = 1; n <= MaxMonths && balance.Values.Any(b => b > 0); n++)
        {
            var month = firstMonth.AddMonths(n - 1);
            var open = ordered.Where(d => balance[d.Id] > 0).ToList();
            var payments = open.ToDictionary(d => d.Id, _ => 0m);
            decimal interestThisMonth = 0;

            foreach (var d in open)
            {
                var interest = Math.Round(balance[d.Id] * d.Apr / 1200m, 2);
                balance[d.Id] += interest;
                interestPaid[d.Id] += interest;
                interestThisMonth += interest;
            }

            // With the snowball the total paid each month stays the same; without it, only open debts' minimums are paid.
            var pool = snowballOn ? monthlyPayment : open.Sum(d => d.MinPayment);
            if (snowballOn && oneTimeAmount > 0 && oneTimeMonth is DateTime otm && otm.Year == month.Year && otm.Month == month.Month)
                pool += oneTimeAmount;

            foreach (var d in open)
            {
                var pay = Math.Min(d.MinPayment, balance[d.Id]);
                payments[d.Id] += pay;
                balance[d.Id] -= pay;
                pool -= pay;
            }
            var snowball = 0m;
            if (snowballOn)
            {
                foreach (var d in open)
                {
                    if (pool <= 0) break;
                    var pay = Math.Min(pool, balance[d.Id]);
                    if (pay <= 0) continue;
                    payments[d.Id] += pay;
                    balance[d.Id] -= pay;
                    pool -= pay;
                    snowball += pay;
                }
            }

            foreach (var d in open.Where(d => balance[d.Id] <= 0))
            {
                balance[d.Id] = 0;
                payoffMonth[d.Id] = n;
            }
            schedule.Add(new ScheduleMonth(n, month, payments, new Dictionary<int, decimal>(balance),
                interestThisMonth, snowball, balance.Values.Sum()));
        }

        var paysOff = balance.Values.All(b => b <= 0);
        var outcomes = ordered.Select((d, i) => new DebtOutcome(d, i + 1,
            payoffMonth.TryGetValue(d.Id, out var m) ? m : null,
            payoffMonth.TryGetValue(d.Id, out var m2) ? firstMonth.AddMonths(m2 - 1) : null,
            interestPaid[d.Id])).ToList();
        return new PayoffPlan(strategy, monthlyPayment, outcomes, schedule,
            interestPaid.Values.Sum(), schedule.Sum(s => s.Payments.Values.Sum()), paysOff);
    }
}
