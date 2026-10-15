using BudgetApp.Data;
using BudgetApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

/// <summary>Take-home pay and the schedule each paycheck follows.</summary>
public class IncomeController(BudgetDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.IncomeSources.OrderBy(s => s.Name).ToListAsync());

    public IActionResult Create() => View("Edit", new IncomeSource());

    public async Task<IActionResult> Edit(int id)
    {
        var source = await db.IncomeSources.FindAsync(id);
        return source is null ? NotFound() : View(source);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(IncomeSource source)
    {
        if (source.Frequency is PayFrequency.Weekly or PayFrequency.BiWeekly && source.AnchorDate is null)
            ModelState.AddModelError(nameof(source.AnchorDate), "Pick any recent payday so the app can work out the rest.");
        if (!ModelState.IsValid)
            return View("Edit", source);

        if (source.Id == 0) db.IncomeSources.Add(source);
        else db.IncomeSources.Update(source);
        await db.SaveChangesAsync();
        TempData["Message"] = $"Saved {source.Name}. Months you create from now on use this pay. Use \"Add missing items\" on an existing month to pick up new paychecks.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var source = await db.IncomeSources.FindAsync(id);
        if (source is not null)
        {
            db.IncomeSources.Remove(source);
            await db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }
}
