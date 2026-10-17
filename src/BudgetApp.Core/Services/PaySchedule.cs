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
        // A payday can only ever be budgeted in its own month or the one after. Which month is decided by its
        // regular date, so a payday that comes early (or late) stays in the same budget.
        return RegularPaydaysInMonth(source, previous.Year, previous.Month)
            .Concat(RegularPaydaysInMonth(source, year, month))
            .Where(d => BudgetMonthForRegular(source, d, rules) == target)
            .Select(d => ActualDate(source, d))
            .ToList();
    }

    /// <summary>When a regular payday is actually paid: its custom date if one is set, otherwise the same day.</summary>
    public static DateTime ActualDate(IncomeSource source, DateTime regular) =>
        source.PaydayOverrides.FirstOrDefault(o => o.RegularDate.Date == regular.Date)?.PayDate.Date ?? regular.Date;

    /// <summary>The regular date of a payday that may have been moved.</summary>
    public static DateTime RegularDate(IncomeSource source, DateTime payday) =>
        source.PaydayOverrides.FirstOrDefault(o => o.PayDate.Date == payday.Date)?.RegularDate.Date ?? payday.Date;

    /// <summary>The regular paydays from one date through another, for the schedule on Pay &amp; Schedule.</summary>
    public static IReadOnlyList<DateTime> RegularPaydays(IncomeSource source, DateTime from, DateTime until)
    {
        var list = new List<DateTime>();
        for (var m = from.MonthStart(); m <= until; m = m.AddMonths(1))
            list.AddRange(RegularPaydaysInMonth(source, m.Year, m.Month).Where(d => d >= from.Date && d <= until.Date));
        return list;
    }

    /// <summary>The first day of the month whose budget this payday belongs to (by its regular date).</summary>
    public static DateTime BudgetMonthFor(IncomeSource source, DateTime payday, BudgetRules? rules = null) =>
        BudgetMonthForRegular(source, RegularDate(source, payday), rules);

    private static DateTime BudgetMonthForRegular(IncomeSource source, DateTime payday, BudgetRules? rules)
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

    /// <summary>Every payday for this income source that falls inside the given month, custom paydays included.</summary>
    public static IReadOnlyList<DateTime> PaydaysInMonth(IncomeSource source, int year, int month)
    {
        if (source.PaydayOverrides.Count == 0) return RegularPaydaysInMonth(source, year, month);
        var first = new DateTime(year, month, 1);
        return new[] { first.AddMonths(-1), first, first.AddMonths(1) }
            .SelectMany(m => RegularPaydaysInMonth(source, m.Year, m.Month))
            .Select(d => ActualDate(source, d))
            .Where(d => d.Year == year && d.Month == month)
            .Distinct().Order().ToList();
    }

    /// <summary>Every regular payday on this income source's schedule that falls inside the given month.</summary>
    public static IReadOnlyList<DateTime> RegularPaydaysInMonth(IncomeSource source, int year, int month)
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
