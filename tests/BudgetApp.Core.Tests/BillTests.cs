using BudgetApp.Models;

namespace BudgetApp.Core.Tests;

public class BillTests
{
    [Fact]
    public void Monthly_bill_comes_once_on_its_due_day()
    {
        var bill = new RecurringDebt { Frequency = BillFrequency.Monthly, DueDay = 20 };
        Assert.Equal([20], bill.DueDaysIn(2026, 10));
    }

    [Fact]
    public void Weekly_bill_comes_every_week_from_its_first_due_date()
    {
        var bill = new RecurringDebt { Frequency = BillFrequency.Weekly, StartDate = new DateTime(2026, 10, 16) };
        Assert.Equal([16, 23, 30], bill.DueDaysIn(2026, 10));
        Assert.Equal([6, 13, 20, 27], bill.DueDaysIn(2026, 11));
    }

    [Fact]
    public void Every_two_weeks_bill_skips_a_week()
    {
        var bill = new RecurringDebt { Frequency = BillFrequency.BiWeekly, StartDate = new DateTime(2026, 10, 9) };
        Assert.Equal([9, 23], bill.DueDaysIn(2026, 10));
    }

    [Fact]
    public void Monthly_amount_counts_a_weekly_bill_52_times_a_year()
    {
        Assert.Equal(650m, new RecurringDebt { Amount = 150, Frequency = BillFrequency.Weekly }.MonthlyAmount);
        Assert.Equal(130m, new RecurringDebt { Amount = 60, Frequency = BillFrequency.BiWeekly }.MonthlyAmount);
    }

    [Fact]
    public void Left_over_is_buffered_then_split()
    {
        var settings = new HouseholdSettings { BufferPercent = 20, FirstPartnerPercent = 50 };
        Assert.Equal(new LeftoverSplit(200, 40, 80, 80), settings.Split(200));
        Assert.Equal(0, settings.Split(-50).Shared);
    }
}
