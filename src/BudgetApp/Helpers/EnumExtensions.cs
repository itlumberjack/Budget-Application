using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace BudgetApp.Helpers;

public static class EnumExtensions
{
    /// <summary>The [Display(Name = ...)] text for an enum value, or its name when there isn't one.</summary>
    public static string DisplayName(this Enum value) =>
        value.GetType().GetField(value.ToString())?.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? value.ToString();
}
