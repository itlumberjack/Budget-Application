using BudgetApp.Data;
using BudgetApp.Helpers;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

/// <summary>Debt statistics and payoff plans for bills that have an amount owed (loans, cards).</summary>
public class DebtController(BudgetDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var loans = await db.RecurringDebts.Loans().ToListAsync();
        var model = DebtPageModel.Build(loans, await db.GetDebtPlanAsync());
        return View(model);
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
        TempData["Message"] = "Saved the amounts owed.";
        return RedirectToAction(nameof(Index));
    }
}
