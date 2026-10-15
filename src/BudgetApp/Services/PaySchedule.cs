using BudgetApp.Models;

namespace BudgetApp.Services;

public static class PaySchedule
{
    /// <summary>
    /// The paydays that belong to a month's budget. Usually the paydays inside the month, but for a
    /// paycheck marked "budget in the following month" it's the paydays in the month before.
    /// </summary>
    public static IReadOnlyList<DateTime> PaydaysForBudget(IncomeSource source, int year, int month)
    {
        if (!source.CountsTowardNextMonth) return PaydaysInMonth(source, year, month);
        var previous = new DateTime(year, month, 1).AddMonths(-1);
        return PaydaysInMonth(source, previous.Year, previous.Month);
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
