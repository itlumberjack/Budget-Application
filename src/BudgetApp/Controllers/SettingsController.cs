using BudgetApp.Data;
using BudgetApp.Models;
using Microsoft.AspNetCore.Mvc;

namespace BudgetApp.Controllers;

/// <summary>Household settings: the buffer held back from each payday and how the rest is split.</summary>
public class SettingsController(BudgetDbContext db) : Controller
{
    public async Task<IActionResult> Index() => View(await db.GetHouseholdAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(HouseholdSettings form)
    {
        if (!ModelState.IsValid) return View(nameof(Index), form);
        var settings = await db.GetHouseholdAsync();
        settings.BufferPercent = form.BufferPercent;
        settings.FirstPartnerName = form.FirstPartnerName.Trim();
        settings.SecondPartnerName = form.SecondPartnerName.Trim();
        settings.FirstPartnerPercent = form.FirstPartnerPercent;
        await db.SaveChangesAsync();
        TempData["Message"] = "Settings saved. Every month's page now uses them.";
        return RedirectToAction(nameof(Index));
    }
}
