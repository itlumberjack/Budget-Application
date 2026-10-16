using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BudgetApp.Models;

/// <summary>
/// How the money left over on each payday is shared, from the Settings page. One row (Id = 1).
/// A buffer is held back first, and the rest is split between the two partners.
/// </summary>
public class HouseholdSettings : ISingleRow
{
    public int Id { get; set; }

    /// <summary>Share of each payday's left over kept as padding before the split.</summary>
    [Range(0, 100), Column(TypeName = "decimal(5,2)")]
    [Display(Name = "Buffer")]
    public decimal BufferPercent { get; set; } = 20;

    [Required, StringLength(40)]
    [Display(Name = "First person")]
    public string FirstPartnerName { get; set; } = "Brad";

    [Required, StringLength(40)]
    [Display(Name = "Second person")]
    public string SecondPartnerName { get; set; } = "Jessica";

    /// <summary>The first person's share of what's split; the second person gets the rest.</summary>
    [Range(0, 100), Column(TypeName = "decimal(5,2)")]
    [Display(Name = "First person's share")]
    public decimal FirstPartnerPercent { get; set; } = 50;

    [NotMapped]
    public decimal SecondPartnerPercent => 100 - FirstPartnerPercent;

    /// <summary>Splits one payday's left over. Nothing is split when there's nothing left.</summary>
    public LeftoverSplit Split(decimal leftOver)
    {
        if (leftOver <= 0) return new LeftoverSplit(leftOver, 0, 0, 0);
        var buffer = Math.Round(leftOver * BufferPercent / 100, 2);
        var toSplit = leftOver - buffer;
        var first = Math.Round(toSplit * FirstPartnerPercent / 100, 2);
        return new LeftoverSplit(leftOver, buffer, first, toSplit - first);
    }
}
