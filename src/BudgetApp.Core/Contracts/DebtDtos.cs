using BudgetApp.Models;

namespace BudgetApp.Contracts;

/// <summary>The Debt Payoff page: loans, the chosen plan, and how every strategy compares.</summary>
public record DebtPlanDto(decimal TotalOwed, decimal TotalMinimum, decimal AverageRate, decimal MonthlyInterestNow,
    PayoffStrategy Strategy, decimal ExtraMonthly, int? ExtraIncomeSourceId, int ExtraPaydayNumber, List<LumpSum> LumpSums,
    List<LoanDto> Loans, PayoffPlan? Plan, PayoffPlan? MinimumOnly, List<PlanSummaryDto> Comparison)
{
    public static DebtPlanDto From(DebtPageModel m) => new(
        m.TotalOwed, m.TotalMinimum, m.AverageRate, m.MonthlyInterestNow,
        m.Settings.Strategy, m.Settings.ExtraMonthly, m.Settings.ExtraIncomeSourceId, m.Settings.ExtraPaydayNumber, m.Settings.LumpSums,
        m.Loans.Select(l => new LoanDto(l.Id, l.Title, l.Balance ?? 0, l.InterestRate ?? 0, l.MonthlyAmount, l.Amount, l.Frequency, l.PayoffPriority, l.BalanceAsOf,
            l.PromoRate, l.PromoBalance, l.PromoEndDate, l.PromoDeferredInterest)).ToList(),
        m.Plan, m.MinimumOnly,
        m.Comparison.Select(PlanSummaryDto.From).ToList());
}

/// <summary>A loan. MinimumPayment is per month on average; PaymentAmount is what's paid each time, on its PaymentFrequency.</summary>
public record LoanDto(int Id, string Title, decimal Balance, decimal InterestRate, decimal MinimumPayment,
    decimal PaymentAmount, BillFrequency PaymentFrequency, int? PayoffPriority, DateTime? BalanceAsOf,
    decimal? PromoRate, decimal? PromoBalance, DateTime? PromoEndDate, bool PromoDeferredInterest);

public record PlanSummaryDto(PayoffStrategy Strategy, decimal MonthlyPayment, int Months, DateTime? DebtFreeDate,
    decimal TotalInterest, decimal TotalPaid, bool PaysOff)
{
    public static PlanSummaryDto From(PayoffPlan p) =>
        new(p.Strategy, p.MonthlyPayment, p.Months, p.DebtFreeDate, p.TotalInterest, p.TotalPaid, p.PaysOff);
}
