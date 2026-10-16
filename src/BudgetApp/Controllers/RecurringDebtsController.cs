using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

/// <summary>Bills that repeat every month. These are copied into each new month's budget.</summary>
public class RecurringDebtsController(BudgetDbContext db, BudgetBuilder builder) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.RecurringDebts.Include(d => d.Category)
            .OrderBy(d => d.Category!.SortOrder).ThenBy(d => d.DueDay).ThenBy(d => d.Title)
            .ToListAsync());

    public async Task<IActionResult> Create()
    {
        await LoadCategoriesAsync();
        return View("Edit", new RecurringDebt());
    }

    public async Task<IActionResult> Edit(int id)
    {
        var debt = await db.RecurringDebts.FindAsync(id);
        if (debt is null) return NotFound();
        await LoadCategoriesAsync();
        return View(debt);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(RecurringDebt debt)
    {
        if (debt.Frequency != BillFrequency.Monthly && debt.StartDate is null)
            ModelState.AddModelError(nameof(debt.StartDate), "Pick any date this bill is due so the app can work out the rest.");
        // Each schedule uses one of the two: a day of the month, or a first due date to count weeks from.
        if (debt.Frequency == BillFrequency.Monthly) debt.StartDate = null;
        else debt.DueDay = null;
        if (!ModelState.IsValid)
        {
            await LoadCategoriesAsync();
            return View("Edit", debt);
        }
        var isNew = debt.Id == 0;
        var oldBalance = isNew ? null : await db.RecurringDebts.Where(d => d.Id == debt.Id).Select(d => d.Balance).FirstOrDefaultAsync();
        if (debt.Balance != oldBalance) debt.BalanceAsOf = debt.Balance is null ? null : DateTime.Today;
        if (isNew) db.RecurringDebts.Add(debt);
        else db.RecurringDebts.Update(debt);
        await db.SaveChangesAsync();
        if (isNew)
        {
            await builder.AddToCurrentAndFutureMonthsAsync(debt: debt);
            TempData["Message"] = $"Added {debt.Title}. It's now in this month's budget and every month after.";
        }
        else
        {
            // Fixed or flexible applies to this month's and later months' copies straight away.
            var months = db.MonthlyBudgets.FromThisMonth().Select(b => b.Id);
            await db.BudgetExpenses.Where(e => e.RecurringDebtId == debt.Id && months.Contains(e.MonthlyBudgetId))
                .ExecuteUpdateAsync(u => u.SetProperty(e => e.IsFixed, debt.IsFixed));
            TempData["Message"] = $"Saved {debt.Title}. New months use the change; months already built keep their own amounts.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var debt = await db.RecurringDebts.FindAsync(id);
        if (debt is not null)
        {
            db.RecurringDebts.Remove(debt);
            await db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadCategoriesAsync() =>
        ViewBag.Categories = new SelectList(
            await db.Categories.InDisplayOrder().ToListAsync(), "Id", "Name");
}
