using BudgetApp.Data;
using BudgetApp.Helpers;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

/// <summary>Debt statistics and payoff plans for bills that have an amount owed (loans, cards).</summary>
public class DebtController(BudgetDbContext db, BudgetBuilder builder) : Controller
{
    public async Task<IActionResult> Index()
    {
        await builder.KeepMonthsCurrentAsync();
        var loans = await db.RecurringDebts.Loans().ToListAsync();
        var settings = await db.GetDebtPlanAsync();
        // This month's extra payment, payday by payday, as it was spread on the month page.
        var month = await db.MonthlyBudgets.FirstOrDefaultAsync(b => b.Year == DateTime.Today.Year && b.Month == DateTime.Today.Month);
        ViewBag.ThisMonth = month;
        ViewBag.ExtraThisMonth = month is null ? new List<BudgetExpense>()
            : await db.BudgetExpenses.Include(e => e.PayPeriod).Include(e => e.ExtraForDebt)
                .Where(e => e.MonthlyBudgetId == month.Id && e.ExtraForDebtId != null)
                .OrderBy(e => e.PayPeriod!.PayDate).ThenBy(e => e.Title).ToListAsync();
        return View(DebtPageModel.Build(loans, settings));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePlan(PayoffStrategy strategy, decimal extraMonthly, decimal oneTimeAmount, DateTime? oneTimeMonth)
    {
        var settings = await db.GetDebtPlanAsync();
        if (Enum.IsDefined(strategy)) settings.Strategy = strategy;
        settings.ExtraMonthly = Math.Max(0, extraMonthly);
        settings.OneTimeAmount = Math.Max(0, oneTimeAmount);
        settings.OneTimeMonth = settings.OneTimeAmount > 0 ? oneTimeMonth : null;
        await db.SaveChangesAsync();

        // The extra is paid from the paydays in each month's budget, so those are redone too.
        await builder.SpreadDebtExtraAsync();
        TempData["Message"] = settings.ExtraMonthly > 0 && settings.Strategy != PayoffStrategy.MinimumOnly
            ? $"Saved. The {settings.ExtraMonthly:C} extra is spread over each month's paydays, from this month on, so they're left with about the same."
            : "Saved the plan.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Saves new amounts owed from the quick-update list.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateBalances(Dictionary<int, decimal> balances)
    {
        var debts = await db.RecurringDebts.Where(d => d.Kind == BillKind.Loan && balances.Keys.Contains(d.Id)).ToListAsync();
        foreach (var d in debts)
        {
            var value = Math.Max(0, balances[d.Id]);
            if (d.Balance == value) continue;
            d.Balance = value;
            d.BalanceAsOf = DateTime.Today;
        }
        await db.SaveChangesAsync();
        await builder.SpreadDebtExtraAsync();
        TempData["Message"] = "Saved the amounts owed.";
        return RedirectToAction(nameof(Index));
    }
}
