using BudgetApp.Data;
using BudgetApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

public class CategoriesController(BudgetDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.Categories.InDisplayOrder().ToListAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(Category category)
    {
        if (string.IsNullOrWhiteSpace(category.Name))
        {
            TempData["Error"] = "Give the category a name.";
            return RedirectToAction(nameof(Index));
        }
        category.Name = category.Name.Trim();
        if (await db.Categories.AnyAsync(c => c.Name == category.Name && c.Id != category.Id))
        {
            TempData["Error"] = $"There is already a category called {category.Name}.";
            return RedirectToAction(nameof(Index));
        }
        if (category.Id == 0)
        {
            if (category.SortOrder == 0)
                category.SortOrder = (await db.Categories.MaxAsync(c => (int?)c.SortOrder) ?? 0) + 10;
            db.Categories.Add(category);
        }
        else db.Categories.Update(category);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var inUse = await db.RecurringDebts.AnyAsync(d => d.CategoryId == id)
                    || await db.BudgetExpenses.AnyAsync(e => e.CategoryId == id);
        if (inUse)
        {
            TempData["Error"] = "That category is still used by a recurring debt or a month's budget, so it can't be deleted.";
            return RedirectToAction(nameof(Index));
        }
        var category = await db.Categories.FindAsync(id);
        if (category is not null)
        {
            db.Categories.Remove(category);
            await db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }
}
