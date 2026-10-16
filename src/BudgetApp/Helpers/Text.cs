namespace BudgetApp.Helpers;

public static class Text
{
    /// <summary>Trimmed text, or null when it's empty or only spaces.</summary>
    public static string? TrimToNull(this string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>A day of the month (1 to 31), or null for anything else.</summary>
    public static int? ValidDay(this int? day) => day is >= 1 and <= 31 ? day : null;
}
