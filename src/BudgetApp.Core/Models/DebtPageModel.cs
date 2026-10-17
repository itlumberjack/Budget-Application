using BudgetApp.Helpers;
using BudgetApp.Services;

namespace BudgetApp.Models;

public class DebtPageModel
{
    public List<RecurringDebt> Loans { get; init; } = new();
    public DebtPlanSettings Settings { get; init; } = new();
    public PayoffPlan? Plan { get; set; }
    public PayoffPlan? MinimumOnly { get; set; }

    /// <summary>
    /// With weekly or every-two-weeks payments: the same plan paid monthly instead, to show what the
    /// schedule saves.
    /// </summary>
    public PayoffPlan? PaidMonthly { get; set; }
    public List<PayoffPlan> Comparison { get; set; } = new();

    public decimal TotalOwed => Loans.Sum(l => l.Balance ?? 0);
    public decimal TotalMinimum => Loans.Sum(l => l.MonthlyAmount);
    /// <summary>APR averaged by balance.</summary>
    public decimal AverageRate => TotalOwed == 0 ? 0 : Loans.Sum(l => (l.Balance ?? 0) * (l.InterestRate ?? 0)) / TotalOwed;
    public decimal MonthlyInterestNow => Loans.Sum(l => Math.Round((l.Balance ?? 0) * (l.InterestRate ?? 0) / 1200m, 2));

    /// <summary>
    /// Runs the chosen plan and every strategy to compare it with, starting next month. The custom orders
    /// are only compared when a loan has a payoff priority.
    /// </summary>
    public static DebtPageModel Build(List<RecurringDebt> loans, DebtPlanSettings settings)
    {
        var inputs = loans.Select(d => new DebtInput(d.Id, d.Title, d.Balance!.Value, d.InterestRate ?? 0, d.MonthlyAmount, d.PayoffPriority)).ToList();
        var firstMonth = Dates.ThisMonth.AddMonths(1);

        PayoffPlan Run(PayoffStrategy s) =>
            DebtPlanner.Plan(inputs, s, settings.ExtraMonthly, firstMonth, settings.OneTimeAmount, settings.OneTimeMonth);

        var model = new DebtPageModel { Loans = loans, Settings = settings };
        if (inputs.Count > 0)
        {
            model.Comparison = Enum.GetValues<PayoffStrategy>()
                .Where(s => s is not (PayoffStrategy.CustomLowestFirst or PayoffStrategy.CustomHighestFirst) || loans.Any(l => l.PayoffPriority is not null))
                .Select(Run).ToList();
            // Reuse the comparison's runs rather than simulating the same plans again.
            model.Plan = model.Comparison.FirstOrDefault(p => p.Strategy == settings.Strategy) ?? Run(settings.Strategy);
            model.MinimumOnly = model.Comparison.First(p => p.Strategy == PayoffStrategy.MinimumOnly);
            if (loans.Any(l => l.Frequency != BillFrequency.Monthly))
            {
                var monthly = loans.Select(d => new DebtInput(d.Id, d.Title, d.Balance!.Value, d.InterestRate ?? 0, d.MonthlyPaymentBeforeSplit, d.PayoffPriority)).ToList();
                model.PaidMonthly = DebtPlanner.Plan(monthly, settings.Strategy, settings.ExtraMonthly, firstMonth, settings.OneTimeAmount, settings.OneTimeMonth);
            }
        }
        return model;
    }
}
