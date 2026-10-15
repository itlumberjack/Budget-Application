using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

/// <summary>Take-home pay and the schedule each paycheck follows.</summary>
public class IncomeController(BudgetDbContext db, BudgetBuilder builder) : Controller
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

        var isNew = source.Id == 0;
        if (isNew) db.IncomeSources.Add(source);
        else db.IncomeSources.Update(source);
        await db.SaveChangesAsync();
        if (isNew)
        {
            await builder.AddToCurrentAndFutureMonthsAsync(source: source);
            TempData["Message"] = $"Added {source.Name}. It's now in this month's budget and every month after.";
        }
        else
        {
            TempData["Message"] = $"Saved {source.Name}. New months use the new amount; months already built keep theirs so your adjustments aren't lost.";
        }
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
