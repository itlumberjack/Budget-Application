using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

/// <summary>Take-home pay and the schedule each paycheck follows.</summary>
public class IncomeController(BudgetDbContext db, BudgetBuilder builder) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.Rules = await builder.GetRulesAsync();
        return View(await db.IncomeSources.OrderBy(s => s.Name).ToListAsync());
    }

    /// <summary>Turns the household's budget rules on or off and re-applies them to this month and later.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRules(bool endOfMonthPayGoesToNextMonth, bool weeklyClosestToFirstStartsMonth)
    {
        var rules = await db.BudgetRules.FirstOrDefaultAsync();
        if (rules is null) db.BudgetRules.Add(rules = new BudgetRules { Id = 1 });
        rules.EndOfMonthPayGoesToNextMonth = endOfMonthPayGoesToNextMonth;
        rules.WeeklyClosestToFirstStartsMonth = weeklyClosestToFirstStartsMonth;
        await db.SaveChangesAsync();

        var moved = await builder.ApplyRulesToCurrentAndFutureMonthsAsync();
        TempData["Message"] = moved == 0
            ? "Budget rules saved. They apply to this month and every month after."
            : $"Budget rules saved. {moved} paycheck(s) moved to the month the rules put them in, along with the bills they cover.";
        return RedirectToAction(nameof(Index));
    }

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
