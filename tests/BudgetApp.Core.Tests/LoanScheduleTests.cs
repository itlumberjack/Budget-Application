using BudgetApp.Helpers;
using BudgetApp.Models;
using BudgetApp.Services;

namespace BudgetApp.Core.Tests;

public class LoanScheduleTests
{
    private static RecurringDebt CarLoan() => new()
    {
        Id = 1, Title = "Car loan", Kind = BillKind.Loan, Amount = 425, DueDay = 25,
        Balance = 18_500, InterestRate = 6.5m
    };

    [Fact]
    public void Every_two_weeks_pays_half_the_monthly_payment()
    {
        var loan = CarLoan();
        loan.ChangePaymentSchedule(BillFrequency.BiWeekly, new DateTime(2026, 10, 9));
        Assert.Equal(212.50m, loan.Amount);
        Assert.Equal([9, 23], loan.DueDaysIn(2026, 10));
        Assert.Equal(460.42m, loan.MonthlyAmount); // 26 half payments a year = 13 monthly payments
    }

    [Fact]
    public void Weekly_pays_a_quarter_rounded_up_to_the_cent()
    {
        var loan = CarLoan();
        loan.Amount = 110;
        loan.ChangePaymentSchedule(BillFrequency.Weekly, new DateTime(2026, 10, 2));
        Assert.Equal(27.50m, loan.Amount);
        Assert.Equal([2, 9, 16, 23, 30], loan.DueDaysIn(2026, 10));

        loan.Amount = 100.01m;
        loan.Frequency = BillFrequency.Monthly;
        loan.ChangePaymentSchedule(BillFrequency.Weekly, new DateTime(2026, 10, 2));
        Assert.Equal(25.01m, loan.Amount);
    }

    [Fact]
    public void Going_back_to_monthly_restores_the_payment_and_due_day()
    {
        var loan = CarLoan();
        loan.ChangePaymentSchedule(BillFrequency.Weekly, new DateTime(2026, 10, 2));
        loan.ChangePaymentSchedule(BillFrequency.BiWeekly, new DateTime(2026, 10, 9));
        Assert.Equal(212.50m, loan.Amount);
        loan.ChangePaymentSchedule(BillFrequency.Monthly, null);
        Assert.Equal(425m, loan.Amount);
        Assert.Equal(BillFrequency.Monthly, loan.Frequency);
        Assert.Equal([25], loan.DueDaysIn(2026, 10));
    }

    [Fact]
    public void Paying_every_two_weeks_clears_the_loan_sooner_than_monthly()
    {
        var loan = CarLoan();
        loan.ChangePaymentSchedule(BillFrequency.BiWeekly, new DateTime(2026, 10, 9));
        var model = DebtPageModel.Build([loan], new DebtPlanSettings { Strategy = PayoffStrategy.Avalanche });
        Assert.NotNull(model.PaidMonthly);
        Assert.True(model.Plan!.Months < model.PaidMonthly!.Months);
        Assert.True(model.Plan.TotalInterest < model.PaidMonthly.TotalInterest);
    }
}
