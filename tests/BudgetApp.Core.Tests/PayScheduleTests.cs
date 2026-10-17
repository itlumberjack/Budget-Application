using BudgetApp.Models;
using BudgetApp.Services;

namespace BudgetApp.Core.Tests;

public class PayScheduleTests
{
    private static readonly IncomeSource Jessica = new() { Id = 1, Frequency = PayFrequency.Weekly, AnchorDate = new DateTime(2026, 10, 2) };
    private static readonly IncomeSource Brad = new() { Id = 2, Frequency = PayFrequency.Monthly };

    [Fact]
    public void Weekly_pay_lands_on_every_friday()
    {
        var days = PaySchedule.PaydaysInMonth(Jessica, 2026, 10).Select(d => d.Day);
        Assert.Equal([2, 9, 16, 23, 30], days);
    }

    [Fact]
    public void Monthly_pay_without_a_day_lands_on_the_last_day()
    {
        Assert.Equal(new DateTime(2026, 11, 30), PaySchedule.PaydaysInMonth(Brad, 2026, 11).Single());
    }

    [Fact]
    public void Rule_one_moves_end_of_month_pay_to_next_month()
    {
        var rules = new BudgetRules { EndOfMonthPayGoesToNextMonth = true };
        Assert.Equal(new DateTime(2026, 11, 1), PaySchedule.BudgetMonthFor(Brad, new DateTime(2026, 10, 31), rules));
        Assert.Equal(new DateTime(2026, 10, 1), PaySchedule.BudgetMonthFor(Brad, new DateTime(2026, 10, 31)));
    }

    [Fact]
    public void Rule_two_gives_the_payday_closest_to_the_first_to_the_new_month()
    {
        var rules = new BudgetRules { WeeklyClosestToFirstStartsMonth = true };
        // October 30 is 2 days before November 1; November 6 is 5 days after, so October 30 starts November.
        Assert.Equal(new DateTime(2026, 11, 1), PaySchedule.BudgetMonthFor(Jessica, new DateTime(2026, 10, 30), rules));
        Assert.Equal(new DateTime(2026, 10, 1), PaySchedule.BudgetMonthFor(Jessica, new DateTime(2026, 10, 23), rules));
    }
}

public class MonthEndBusinessDayTests
{
    private static IncomeSource Brad(bool businessDay = true) =>
        new() { Name = "Brad", Frequency = PayFrequency.Monthly, MonthEndOnBusinessDay = businessDay };

    [Theory]
    [InlineData(2026, 10, 30)] // Oct 31, 2026 is a Saturday
    [InlineData(2027, 1, 29)]  // Jan 31, 2027 is a Sunday
    [InlineData(2026, 11, 30)] // Nov 30, 2026 is a Monday
    [InlineData(2026, 12, 31)] // Dec 31, 2026 is a Thursday
    public void Last_day_pay_moves_to_the_Friday_before_a_weekend(int year, int month, int day) =>
        Assert.Equal([new DateTime(year, month, day)], PaySchedule.PaydaysInMonth(Brad(), year, month));

    [Fact]
    public void Can_still_pay_on_the_weekend_last_day()
    {
        Assert.Equal([new DateTime(2026, 10, 31)], PaySchedule.PaydaysInMonth(Brad(businessDay: false), 2026, 10));
    }

    [Fact]
    public void A_set_day_of_month_is_not_moved()
    {
        var source = Brad();
        source.DayOfMonth = 17; // Oct 17, 2026 is a Saturday
        Assert.Equal([new DateTime(2026, 10, 17)], PaySchedule.PaydaysInMonth(source, 2026, 10));
    }

    [Fact]
    public void Twice_a_month_moves_its_second_check()
    {
        var source = new IncomeSource { Frequency = PayFrequency.SemiMonthly };
        Assert.Equal([new DateTime(2026, 10, 15), new DateTime(2026, 10, 30)], PaySchedule.PaydaysInMonth(source, 2026, 10));
    }

    [Fact]
    public void Brads_rule_still_sends_the_early_Friday_check_to_next_month()
    {
        var rules = new BudgetRules { EndOfMonthPayGoesToNextMonth = true };
        Assert.Equal(new DateTime(2026, 11, 1), PaySchedule.BudgetMonthFor(Brad(), new DateTime(2026, 10, 30), rules));
        Assert.Equal([new DateTime(2026, 10, 30)], PaySchedule.PaydaysForBudget(Brad(), 2026, 11, rules));
        Assert.Equal([new DateTime(2026, 9, 30)], PaySchedule.PaydaysForBudget(Brad(), 2026, 10, rules));
    }

    [Fact]
    public void A_custom_payday_moves_the_paycheck_but_keeps_its_budget_month()
    {
        // Dec 31 pay comes early on Dec 23 for the holiday; with rule one it still pays January's bills.
        var brad = new IncomeSource { Id = 3, Frequency = PayFrequency.Monthly };
        brad.PaydayOverrides.Add(new PaydayOverride { RegularDate = new DateTime(2026, 12, 31), PayDate = new DateTime(2026, 12, 23) });
        var rules = new BudgetRules { EndOfMonthPayGoesToNextMonth = true };
        Assert.Equal(new DateTime(2026, 12, 23), PaySchedule.PaydaysInMonth(brad, 2026, 12).Single());
        Assert.Equal(new DateTime(2026, 12, 23), PaySchedule.PaydaysForBudget(brad, 2027, 1, rules).Single());
        Assert.Equal(new DateTime(2026, 11, 30), PaySchedule.PaydaysForBudget(brad, 2026, 12, rules).Single());
        Assert.Equal(new DateTime(2027, 1, 1), PaySchedule.BudgetMonthFor(brad, new DateTime(2026, 12, 23), rules));
        // Across a month line: Nov 6 paid Oct 30 still counts for November.
        var jess = new IncomeSource { Id = 4, Frequency = PayFrequency.Weekly, AnchorDate = new DateTime(2026, 10, 2) };
        jess.PaydayOverrides.Add(new PaydayOverride { RegularDate = new DateTime(2026, 11, 6), PayDate = new DateTime(2026, 11, 5) });
        Assert.Contains(new DateTime(2026, 11, 5), PaySchedule.PaydaysForBudget(jess, 2026, 11));
        Assert.DoesNotContain(new DateTime(2026, 11, 6), PaySchedule.PaydaysInMonth(jess, 2026, 11));
    }
}
