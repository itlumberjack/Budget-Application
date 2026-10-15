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
        if (!ModelState.IsValid)
        {
            await LoadCategoriesAsync();
            return View("Edit", debt);
        }
        var isNew = debt.Id == 0;
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
            await db.Categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(), "Id", "Name");
}
