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
