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
            && payday.Day == DateTime.DaysInMonth(payday.Year, payday.Month))
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

    /// <summary>Every payday for this income source that falls inside the given month.</summary>
    public static IReadOnlyList<DateTime> PaydaysInMonth(IncomeSource source, int year, int month)
    {
        var first = new DateTime(year, month, 1);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var last = new DateTime(year, month, daysInMonth);

        switch (source.Frequency)
        {
            case PayFrequency.Monthly:
                var day = source.DayOfMonth is int d && d < daysInMonth ? d : daysInMonth;
                return [new DateTime(year, month, day)];

            case PayFrequency.SemiMonthly:
                return [new DateTime(year, month, 15), last];

            case PayFrequency.Weekly:
            case PayFrequency.BiWeekly:
                var step = source.Frequency == PayFrequency.Weekly ? 7 : 14;
                var anchor = (source.AnchorDate ?? first).Date;
                // Jump to the first payday on or after the 1st (works for anchors before or after this month).
                var offset = (first - anchor).Days;
                var periods = (int)Math.Ceiling(offset / (double)step);
                var payday = anchor.AddDays(periods * step);
                var result = new List<DateTime>();
                for (; payday <= last; payday = payday.AddDays(step))
                    result.Add(payday);
                return result;

            default:
                return [];
        }
    }
}
