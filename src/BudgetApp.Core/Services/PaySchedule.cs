using BudgetApp.Helpers;
using BudgetApp.Models;

namespace BudgetApp.Services;

public static class PaySchedule
{
    /// <summary>The paydays that belong to a month's budget, after applying the household's budget rules.</summary>
    public static IReadOnlyList<DateTime> PaydaysForBudget(IncomeSource source, int year, int month, BudgetRules? rules = null)
    {
        var target = new DateTime(year, month, 1);
        var previous = target.AddMonths(-1);
        // A payday can only ever be budgeted in its own month or the one after.
        return PaydaysInMonth(source, previous.Year, previous.Month)
            .Concat(PaydaysInMonth(source, year, month))
            .Where(d => BudgetMonthFor(source, d, rules) == target)
            .ToList();
    }

    /// <summary>The first day of the month whose budget this payday belongs to.</summary>
    public static DateTime BudgetMonthFor(IncomeSource source, DateTime payday, BudgetRules? rules = null)
    {
        var ownMonth = payday.MonthStart();
        var nextMonth = ownMonth.AddMonths(1);

        if (source.CountsTowardNextMonth)
            return nextMonth;

        if (rules?.EndOfMonthPayGoesToNextMonth == true && source.Frequency == PayFrequency.Monthly
            && IsMonthEndPayday(source, payday))
            return nextMonth;

        if (rules?.WeeklyClosestToFirstStartsMonth == true && source.Frequency == PayFrequency.Weekly)
        {
            var following = payday.AddDays(7);
            if (following.Month != payday.Month)
            {
                // This is the last payday of its month. It starts next month if it's at least as close
                // to the 1st as the following payday is.
                var daysBefore = (nextMonth - payday.Date).Days;
                var daysAfter = (following.Date - nextMonth).Days;
                if (daysBefore <= daysAfter) return nextMonth;
            }
        }

        return ownMonth;
    }

    /// <summary>
    /// When the last day of the month is paid: that day, or the Friday before when it's a weekend and the
    /// income source pays on the last business day.
    /// </summary>
    public static DateTime MonthEndPayday(IncomeSource source, int year, int month)
    {
        var last = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        return source.MonthEndOnBusinessDay ? last.WeekdayOrFridayBefore() : last;
    }

    /// <summary>A monthly paycheck scheduled for the last day of its month (even if it came early for a weekend).</summary>
    public static bool IsMonthEndPayday(IncomeSource source, DateTime payday)
    {
        var days = DateTime.DaysInMonth(payday.Year, payday.Month);
        var paysLastDay = source.DayOfMonth is not int d || d >= days;
        return paysLastDay && (payday.Day == days || payday.Date == MonthEndPayday(source, payday.Year, payday.Month));
    }

    /// <summary>Every payday for this income source that falls inside the given month.</summary>
    public static IReadOnlyList<DateTime> PaydaysInMonth(IncomeSource source, int year, int month)
    {
        var first = new DateTime(year, month, 1);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var last = new DateTime(year, month, daysInMonth);

        switch (source.Frequency)
        {
            case PayFrequency.Monthly:
                return source.DayOfMonth is int d && d < daysInMonth
                    ? [new DateTime(year, month, d)]
                    : [MonthEndPayday(source, year, month)];

            case PayFrequency.SemiMonthly:
                return [new DateTime(year, month, 15), MonthEndPayday(source, year, month)];

            case PayFrequency.Weekly:
                return Dates.RepeatingInMonth(source.AnchorDate ?? first, 7, year, month);

            case PayFrequency.BiWeekly:
                return Dates.RepeatingInMonth(source.AnchorDate ?? first, 14, year, month);

            default:
                return [];
        }
    }
}
