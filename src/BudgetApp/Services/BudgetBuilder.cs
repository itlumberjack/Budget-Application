using BudgetApp.Data;
using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

/// <summary>Builds a month's budget from the pay schedules and recurring debts.</summary>
public class BudgetBuilder(BudgetDbContext db)
{
    public async Task<MonthlyBudget> CreateMonthAsync(int year, int month)
    {
        var budget = await db.MonthlyBudgets.FirstOrDefaultAsync(b => b.Year == year && b.Month == month);
        if (budget is null)
        {
            budget = new MonthlyBudget { Year = year, Month = month };
            db.MonthlyBudgets.Add(budget);
            await db.SaveChangesAsync();
        }
        await SyncAsync(budget.Id);
        return budget;
    }

    /// <summary>
    /// Adds any paychecks and recurring debts that are missing from the month.
    /// Never changes or removes lines that are already there, so manual adjustments are kept.
    /// </summary>
    public async Task<(int paychecks, int debts)> SyncAsync(int budgetId)
    {
        var budget = await db.MonthlyBudgets
            .Include(b => b.PayPeriods)
            .Include(b => b.Expenses)
            .FirstAsync(b => b.Id == budgetId);

        var addedPay = 0;
        foreach (var source in await db.IncomeSources.Where(s => s.IsActive).ToListAsync())
        {
            foreach (var payday in PaySchedule.PaydaysInMonth(source, budget.Year, budget.Month))
            {
                if (budget.PayPeriods.Any(p => p.IncomeSourceId == source.Id && p.PayDate.Date == payday))
                    continue;
                budget.PayPeriods.Add(new PayPeriod
                {
                    IncomeSourceId = source.Id,
                    Label = source.Name,
                    PayDate = payday,
                    BaseAmount = source.TakeHomeAmount
                });
                addedPay++;
            }
        }

        var addedDebts = 0;
        foreach (var debt in await db.RecurringDebts.Where(d => d.IsActive).ToListAsync())
        {
            if (budget.Expenses.Any(e => e.RecurringDebtId == debt.Id))
                continue;
            budget.Expenses.Add(new BudgetExpense
            {
                RecurringDebtId = debt.Id,
                Title = debt.Title,
                Amount = debt.Amount,
                CategoryId = debt.CategoryId,
                DueDay = debt.DueDay
            });
            addedDebts++;
        }

        await db.SaveChangesAsync();
        return (addedPay, addedDebts);
    }
}
