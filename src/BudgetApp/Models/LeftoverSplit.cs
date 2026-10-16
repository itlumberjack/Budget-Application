namespace BudgetApp.Models;

/// <summary>One payday's left over: the buffer held back and each partner's share of the rest.</summary>
public record LeftoverSplit(decimal LeftOver, decimal Buffer, decimal First, decimal Second)
{
    public decimal Shared => First + Second;

    public static LeftoverSplit Sum(IEnumerable<LeftoverSplit> splits) => splits.Aggregate(
        new LeftoverSplit(0, 0, 0, 0),
        (a, b) => new LeftoverSplit(a.LeftOver + b.LeftOver, a.Buffer + b.Buffer, a.First + b.First, a.Second + b.Second));
}
