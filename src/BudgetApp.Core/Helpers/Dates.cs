namespace BudgetApp.Helpers;

/// <summary>Month and day helpers shared by the budget, pay and debt pages.</summary>
public static class Dates
{
    /// <summary>The first day of the month <paramref name="date"/> falls in.</summary>
    public static DateTime MonthStart(this DateTime date) => new(date.Year, date.Month, 1);

    /// <summary>The first day of the current month.</summary>
    public static DateTime ThisMonth => DateTime.Today.MonthStart();

    /// <summary>"October 2026".</summary>
    public static string MonthTitle(this DateTime date) => date.ToString("MMMM yyyy");

    /// <summary>Whole months from <paramref name="from"/> to <paramref name="to"/>, e.g. October to November is 1.</summary>
    public static int MonthsBetween(DateTime from, DateTime to) => (to.Year - from.Year) * 12 + to.Month - from.Month;

    /// <summary>"3 years, 2 months", "1 year" or "5 months".</summary>
    public static string YearsAndMonths(int months)
    {
        int years = months / 12, rest = months % 12;
        var parts = new List<string>();
        if (years > 0) parts.Add(years == 1 ? "1 year" : $"{years} years");
        if (rest > 0 || years == 0) parts.Add(rest == 1 ? "1 month" : $"{rest} months");
        return string.Join(", ", parts);
    }

    /// <summary>1st, 2nd, 3rd, 4th, 11th, 21st...</summary>
    public static string Ordinal(int day) => day + (day % 100 is 11 or 12 or 13 ? "th" : (day % 10) switch
    {
        1 => "st",
        2 => "nd",
        3 => "rd",
        _ => "th"
    });

    /// <summary>
    /// Every date in the month that falls a whole number of <paramref name="stepDays"/> from
    /// <paramref name="anchor"/> (counting forward or back), e.g. every other Friday.
    /// </summary>
    public static IReadOnlyList<DateTime> RepeatingInMonth(DateTime anchor, int stepDays, int year, int month)
    {
        var first = new DateTime(year, month, 1);
        var last = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        var periods = (int)Math.Ceiling((first - anchor.Date).Days / (double)stepDays);
        var result = new List<DateTime>();
        for (var day = anchor.Date.AddDays(periods * stepDays); day <= last; day = day.AddDays(stepDays))
            result.Add(day);
        return result;
    }
}
