using BudgetApp.Data;
using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

/// <summary>Loads a month for the month page and the phone app, and moves its bills between paydays.</summary>
public class MonthService(BudgetDbContext db, BudgetBuilder builder)
{
    /// <summary>Every month's totals, oldest first, after archiving finished months and building the next six.</summary>
    public async Task<List<MonthSummary>> ListAsync()
    {
        await builder.KeepMonthsCurrentAsync();
        return await db.MonthlyBudgets
            .OrderBy(b => b.Year).ThenBy(b => b.Month)
            .Select(b => new MonthSummary(
                b.Year, b.Month,
                b.PayPeriods.Sum(p => p.BaseAmount) + b.PayPeriods.SelectMany(p => p.Extras).Sum(x => x.Amount),
                b.Expenses.Sum(e => e.Amount),
                b.Expenses.Where(e => e.Status == PaymentStatus.Complete).Sum(e => e.Amount),
                b.IsArchived))
            .ToListAsync();
    }

    /// <summary>
    /// A month with its paydays and bills. The month is built on first visit from the pay schedules and
    /// recurring bills, as long as there's something set up to build it from.
    /// </summary>
    public async Task<MonthViewModel> LoadAsync(int year, int month)
    {
        await builder.KeepMonthsCurrentAsync();
        var budget = await db.MonthlyBudgets.WithDetails().FirstOrDefaultAsync(b => b.Year == year && b.Month == month);
        if (budget is null && (await db.IncomeSources.AnyAsync(s => s.IsActive) || await db.RecurringDebts.AnyAsync(d => d.IsActive)))
        {
            var created = await builder.CreateMonthAsync(year, month);
            budget = await db.MonthlyBudgets.WithDetails().FirstAsync(b => b.Id == created.Id);
        }
        return await ModelAsync(year, month, budget);
    }

    /// <summary>An existing month by id, or null.</summary>
    public async Task<MonthViewModel?> LoadAsync(int budgetId, CancellationToken ct = default)
    {
        var budget = await db.MonthlyBudgets.WithDetails().FirstOrDefaultAsync(b => b.Id == budgetId, ct);
        return budget is null ? null : await ModelAsync(budget.Year, budget.Month, budget);
    }

    private async Task<MonthViewModel> ModelAsync(int year, int month, MonthlyBudget? budget)
    {
        budget?.PayPeriods.Sort((a, b) => a.PayDate != b.PayDate ? a.PayDate.CompareTo(b.PayDate) : string.Compare(a.Label, b.Label, StringComparison.Ordinal));
        return new MonthViewModel
        {
            Year = year,
            Month = month,
            Budget = budget,
            Categories = await db.Categories.InDisplayOrder().ToListAsync(),
            Household = await db.GetHouseholdAsync()
        };
    }

    /// <summary>
    /// Saves the month as it stands as its original plan. With onlyIfMissing (used when a bill is first marked
    /// pending or complete), a plan that's already saved is kept.
    /// </summary>
    public async Task SavePlanAsync(int budgetId, bool onlyIfMissing = false)
    {
        var budget = await db.MonthlyBudgets.Include(b => b.PayPeriods).ThenInclude(p => p.Extras).Include(b => b.Expenses)
            .FirstOrDefaultAsync(b => b.Id == budgetId);
        if (budget is null || budget.IsArchived || (onlyIfMissing && budget.HasPlan)) return;
        budget.SavePlan(DateTime.Now);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Moves bills to other paychecks in the same month (from Even out, the AI helper, or the phone app).
    /// Moves naming a bill or paycheck from another month are skipped. Returns how many bills moved.
    /// </summary>
    public async Task<int> ApplyMovesAsync(MonthlyBudget budget, IEnumerable<(int ExpenseId, int PayPeriodId)> moves)
    {
        await db.Entry(budget).Collection(b => b.PayPeriods).LoadAsync();
        await db.Entry(budget).Collection(b => b.Expenses).LoadAsync();
        var moved = 0;
        foreach (var (expenseId, payId) in moves)
        {
            var expense = budget.Expenses.FirstOrDefault(e => e.Id == expenseId);
            if (expense is null || budget.PayPeriods.All(p => p.Id != payId)) continue;
            expense.PayPeriodId = payId;
            moved++;
        }
        await db.SaveChangesAsync();
        return moved;
    }
}
