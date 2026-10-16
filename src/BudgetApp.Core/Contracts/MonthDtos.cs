using BudgetApp.Models;

namespace BudgetApp.Contracts;

// The shapes the phone app reads and sends. They live in the shared library so the app and the site agree on them.

/// <summary>One month in the months list.</summary>
public record MonthListItemDto(int Year, int Month, string Title, decimal Income, decimal Expenses, decimal Complete,
    decimal Remaining, bool IsArchived);

/// <summary>A whole month: its paydays (columns on the month page) and the bills each one pays.</summary>
public record MonthDto(int Id, int Year, int Month, string Title, bool IsArchived, string? Notes,
    decimal Income, decimal Expenses, decimal Remaining, decimal Complete, decimal Pending, decimal NotPaid,
    SplitDto Split, List<PaydayDto> Paydays)
{
    public static MonthDto From(MonthViewModel month)
    {
        var budget = month.Budget ?? throw new ArgumentException("The month has no budget yet.", nameof(month));
        var household = month.Household;
        return new MonthDto(budget.Id, month.Year, month.Month, month.Title, budget.IsArchived, budget.Notes,
            month.TotalIncome, month.TotalExpenses, month.Remaining,
            month.TotalWithStatus(PaymentStatus.Complete), month.TotalWithStatus(PaymentStatus.Pending), month.TotalWithStatus(PaymentStatus.NotPaid),
            SplitDto.From(month.TotalSplit, household),
            month.Columns.Select(c => new PaydayDto(
                c.Heading, c.IsUnassigned,
                c.Paychecks.Select(p => new PaycheckDto(p.Id, p.Label, p.PayDate, p.BaseAmount, p.Total,
                    p.Extras.Select(x => new PayExtraDto(x.Id, x.Description, x.Amount)).ToList())).ToList(),
                c.Income, c.Spending, c.LeftOver,
                c.IsUnassigned ? null : SplitDto.From(household.Split(c.LeftOver), household),
                c.Expenses.OrderBy(e => e.DueDay ?? 32).ThenBy(e => e.Title)
                    .Select(e => BillDto.From(e, paysLate: !month.PaysOnTime(c, e))).ToList()))
                .ToList());
    }
}

/// <summary>One payday column. The "Not assigned" column has no paychecks and no split.</summary>
public record PaydayDto(string Heading, bool IsUnassigned, List<PaycheckDto> Paychecks,
    decimal Income, decimal Spending, decimal LeftOver, SplitDto? Split, List<BillDto> Bills);

public record PaycheckDto(int Id, string Label, DateTime PayDate, decimal BaseAmount, decimal Total, List<PayExtraDto> Extras);

public record PayExtraDto(int Id, string Description, decimal Amount);

/// <summary>A bill in a month. PaysLate is set when its payday comes after the due day.</summary>
public record BillDto(int Id, string Title, string? Company, decimal Amount, int? DueDay, int CategoryId, string? Category,
    PaymentStatus Status, bool IsFixed, bool IsRecurring, int? PayPeriodId, bool PaysLate)
{
    public static BillDto From(BudgetExpense e, bool paysLate) =>
        new(e.Id, e.Title, e.Company, e.Amount, e.DueDay, e.CategoryId, e.Category?.Name, e.Status, e.IsFixed,
            e.RecurringDebtId is not null, e.PayPeriodId, paysLate);
}

/// <summary>Left over, the buffer held back, and each person's share.</summary>
public record SplitDto(decimal LeftOver, decimal Buffer, decimal First, decimal Second, string FirstName, string SecondName)
{
    public static SplitDto From(LeftoverSplit s, HouseholdSettings h) =>
        new(s.LeftOver, s.Buffer, s.First, s.Second, h.FirstPartnerName, h.SecondPartnerName);
}

public record StatusUpdate(PaymentStatus Status);

/// <summary>Moves a bill to another paycheck in the same month; null puts it under "Not assigned".</summary>
public record PaydayUpdate(int? PayPeriodId);

public record BillMove(int ExpenseId, int PayPeriodId);

public record ApplyMovesRequest(List<BillMove> Moves);

public record ApplyMovesResult(int Moved);

/// <summary>Why a request was turned away, e.g. the month is archived.</summary>
public record ApiError(string Error);
