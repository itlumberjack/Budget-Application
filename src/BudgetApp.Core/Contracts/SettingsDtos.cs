using System.ComponentModel.DataAnnotations;
using BudgetApp.Models;

namespace BudgetApp.Contracts;

/// <summary>The buffer and leftover split from the Settings page.</summary>
public record SettingsDto(
    [property: Range(0, 100)] decimal BufferPercent,
    [property: Required, StringLength(40)] string FirstPartnerName,
    [property: Required, StringLength(40)] string SecondPartnerName,
    [property: Range(0, 100)] decimal FirstPartnerPercent)
{
    public static SettingsDto From(HouseholdSettings s) =>
        new(s.BufferPercent, s.FirstPartnerName, s.SecondPartnerName, s.FirstPartnerPercent);

    public void ApplyTo(HouseholdSettings s)
    {
        s.BufferPercent = BufferPercent;
        s.FirstPartnerName = FirstPartnerName.Trim();
        s.SecondPartnerName = SecondPartnerName.Trim();
        s.FirstPartnerPercent = FirstPartnerPercent;
    }
}

/// <summary>Who is signed in.</summary>
public record MeDto(string Email);
