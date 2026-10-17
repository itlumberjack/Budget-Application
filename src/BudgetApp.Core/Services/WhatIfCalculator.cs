using BudgetApp.Models;

namespace BudgetApp.Services;

/// <summary>The current payoff plan next to the plan with a what-if's changes, and the loans each one starts from.</summary>
public record WhatIfResult(WhatIfScenario Scenario, List<DebtInput> BeforeDebts, List<DebtInput> AfterDebts,
    PayoffPlan Before, PayoffPlan After, PayoffPlan? AfterSamePayment, List<string> Notes)
{
    public decimal InterestSaved => Before.TotalInterest - After.TotalInterest;
    public int MonthsSooner => Before.Schedule.Count - After.Schedule.Count;
    public decimal MinimumsBefore => BeforeDebts.Sum(d => d.MinPayment);
    public decimal MinimumsAfter => AfterDebts.Sum(d => d.MinPayment);
}

/// <summary>Applies a what-if to the loans and plan settings and runs both payoff plans with DebtPlanner.</summary>
public static class WhatIfCalculator
{
    public static WhatIfResult Run(IReadOnlyList<DebtInput> debts, DebtPlanSettings settings, WhatIfScenario scenario, DateTime firstMonth)
    {
        var notes = new List<string>();
        var after = debts.ToList();
        var nextId = -1;
        DebtInput? Find(int id) => after.FirstOrDefault(d => d.Id == id);

        foreach (var c in scenario.LoanChanges)
        {
            if (Find(c.LoanId) is not { } loan) continue;
            after.Remove(loan);
            if (c.PaidOff) { notes.Add($"{loan.Name} is paid off up front ({loan.Balance:C}), with money from outside this plan."); continue; }
            after.Add(loan with
            {
                Balance = c.Balance ?? loan.Balance, Apr = c.Apr ?? loan.Apr, MinPayment = c.MinimumPayment ?? loan.MinPayment,
                // A new rate replaces any promo.
                Promo = c.Apr is null ? loan.Promo : null
            });
        }

        foreach (var c in scenario.Consolidations)
        {
            var into = c.IntoLoanId is int intoId ? Find(intoId) : null;
            var rolled = c.LoanIds.Where(id => id != into?.Id).Select(Find).OfType<DebtInput>().ToList();
            if (into is null && rolled.Count == 0) continue;
            foreach (var r in rolled) after.Remove(r);
            if (into is not null) after.Remove(into);
            var fee = Math.Max(0, c.Fee ?? 0);
            var balance = (into?.Balance ?? 0) + rolled.Sum(r => r.Balance) + fee;
            var oldPayments = (into?.MinPayment ?? 0) + rolled.Sum(r => r.MinPayment);
            decimal payment;
            if (c.MinimumPayment is decimal m && m > 0) payment = m;
            else if (c.TermMonths is int n && n > 0)
            {
                payment = Payment(balance, c.Apr, n);
                notes.Add($"Payment of {payment:C} a month pays {balance:C} off over {n} months at {c.Apr:0.###}%.");
            }
            else
            {
                payment = oldPayments;
                notes.Add($"Payment stays at {payment:C} a month, what the old payments added up to.");
            }
            var name = into?.Name ?? (string.IsNullOrWhiteSpace(c.NewLoanName) ? "Consolidation loan" : c.NewLoanName!);
            after.Add(new DebtInput(into?.Id ?? nextId--, name, balance, c.Apr, payment, into?.Priority));
            notes.Add($"{name}: {balance:C} at {c.Apr:0.###}%" + (rolled.Count > 0 ? $", taking in {string.Join(", ", rolled.Select(r => $"{r.Name} ({r.Balance:C})"))}" : "")
                + (fee > 0 ? $", plus a {fee:C} fee" : "") + ".");
        }

        foreach (var n in scenario.NewLoans)
            after.Add(new DebtInput(nextId--, n.Name, n.Balance, n.Apr, n.MinimumPayment, null));

        var extra = scenario.ExtraMonthly ?? settings.ExtraMonthly;
        var strategy = scenario.Strategy ?? settings.Strategy;
        // A lump sum in the question comes on top of the one-time extras already planned.
        var lumps = settings.LumpSums.ToList();
        if (scenario.OneTimeAmount is decimal oneTime) lumps.Add(new LumpSum(scenario.OneTimeMonth ?? firstMonth, oneTime));

        var before = DebtPlanner.Plan(debts, settings.Strategy, settings.ExtraMonthly, firstMonth, settings.LumpSums);
        var plan = DebtPlanner.Plan(after, strategy, extra, firstMonth, lumps);
        // When the payments go down, also show what happens if the household keeps paying what it pays now.
        PayoffPlan? samePayment = null;
        var drop = debts.Sum(d => d.MinPayment) - after.Sum(d => d.MinPayment);
        if (drop > 0 && strategy != PayoffStrategy.MinimumOnly && scenario.ExtraMonthly is null)
            samePayment = DebtPlanner.Plan(after, strategy, extra + drop, firstMonth, lumps);
        return new WhatIfResult(scenario, debts.ToList(), after, before, plan, samePayment, notes);
    }

    /// <summary>The monthly payment that pays a balance off over a number of months at a yearly rate, rounded up to the cent.</summary>
    public static decimal Payment(decimal balance, decimal apr, int months)
    {
        if (months <= 0) return balance;
        if (apr <= 0) return Math.Ceiling(balance / months * 100) / 100;
        var r = (double)apr / 1200;
        var p = (double)balance * r / (1 - Math.Pow(1 + r, -months));
        return Math.Ceiling((decimal)p * 100) / 100;
    }
}
