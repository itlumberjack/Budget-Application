using BudgetApp.Helpers;
using BudgetApp.Models;

namespace BudgetApp.Services;

/// <summary>
/// Suggests moving unpaid bills between a month's paydays so each payday has about the same amount left over.
/// A bill only moves to a payday on or before its due day (or to the first payday), so nothing is paid late.
/// Like the AI helper it only suggests; nothing changes until the user applies the moves.
/// </summary>
public static class BillBalancer
{
    /// <summary>A move must close the gap between two paydays by at least this much to be worth suggesting.</summary>
    private const decimal MinimumGain = 10m;
    private const int MaxMoves = 50;

    public static ArrangementSuggestion Suggest(MonthViewModel month)
    {
        var columns = month.Columns.Where(c => !c.IsUnassigned).ToList();
        if (columns.Count < 2)
            return new ArrangementSuggestion("This month has only one payday, so there's nothing to even out.", [], ColumnOutcome.For(columns, []));

        var leftOver = columns.Select(c => c.LeftOver).ToArray();
        var original = (decimal[])leftOver.Clone();
        var bills = columns
            .SelectMany((c, i) => c.Expenses.Where(e => e.Status == PaymentStatus.NotPaid).Select(e => (Bill: e, From: i)))
            .ToList();
        var at = bills.ToDictionary(b => b.Bill.Id, b => b.From);

        // Each pass makes the single move that evens things out the most, until no move helps enough.
        for (var pass = 0; pass < MaxMoves; pass++)
        {
            (BudgetExpense Bill, int To, decimal Gain)? best = null;
            foreach (var (bill, _) in bills)
            {
                var from = at[bill.Id];
                for (var to = 0; to < columns.Count; to++)
                {
                    if (to == from || !CanPayFrom(columns[to], to, bill, month)) continue;
                    // Moving amount a from "from" to "to" lowers the spread when "to" has more than a extra left over.
                    var gain = leftOver[to] - leftOver[from] - bill.Amount;
                    if (gain >= MinimumGain && (best is null || bill.Amount * gain > best.Value.Bill.Amount * best.Value.Gain))
                        best = (bill, to, gain);
                }
            }
            if (best is not { } move) break;
            var source = at[move.Bill.Id];
            leftOver[source] += move.Bill.Amount;
            leftOver[move.To] -= move.Bill.Amount;
            at[move.Bill.Id] = move.To;
        }

        var moves = bills
            .Where(b => at[b.Bill.Id] != b.From)
            .Select(b =>
            {
                var (from, to) = (columns[b.From], columns[at[b.Bill.Id]]);
                var reason = $"{from.Heading} had {original[b.From]:C} left and {to.Heading} had {original[at[b.Bill.Id]]:C}."
                             + (b.Bill.DueDay is int due ? $" Still paid before it's due on the {Dates.Ordinal(due)}." : "");
                return new SuggestedMove(b.Bill.Id, b.Bill.Title, b.Bill.Amount, b.Bill.DueDay,
                    from.Heading, to.Heading, to.TargetPayPeriodId!.Value, reason);
            })
            .ToList();

        var summary = moves.Count == 0
            ? $"Left over is already as even as it can get without paying a bill late: {Range(original)} per payday."
            : $"Left over now ranges {Range(original)} per payday. After {(moves.Count == 1 ? "this move" : $"these {moves.Count} moves")} it's {Range(leftOver)}.";
        return new ArrangementSuggestion(summary, moves, ColumnOutcome.For(columns, moves));
    }

    /// <summary>A bill can come out of a payday on or before its due day. The first payday can pay anything.</summary>
    private static bool CanPayFrom(PayColumn column, int index, BudgetExpense bill, MonthViewModel month)
    {
        if (index == 0 || bill.DueDay is not int day) return true;
        var due = new DateTime(month.Year, month.Month, Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)));
        return column.Paychecks.Min(p => p.PayDate.Date) <= due;
    }

    private static string Range(decimal[] values) => $"from {values.Min():C} to {values.Max():C}";
}
