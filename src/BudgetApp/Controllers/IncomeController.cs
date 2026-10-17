using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

/// <summary>Take-home pay and the schedule each paycheck follows.</summary>
public class IncomeController(BudgetDbContext db, BudgetBuilder builder, StubReader stubs) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.Rules = await db.GetRulesAsync();
        return View(await db.IncomeSources.Include(s => s.StubLines).OrderBy(s => s.Name).ToListAsync());
    }

    /// <summary>Turns the household's budget rules on or off and re-applies them to this month and later.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRules(bool endOfMonthPayGoesToNextMonth, bool weeklyClosestToFirstStartsMonth)
    {
        var rules = await db.GetRulesAsync();
        rules.EndOfMonthPayGoesToNextMonth = endOfMonthPayGoesToNextMonth;
        rules.WeeklyClosestToFirstStartsMonth = weeklyClosestToFirstStartsMonth;
        await db.SaveChangesAsync();

        var moved = await builder.ApplyRulesToCurrentAndFutureMonthsAsync();
        TempData["Message"] = moved == 0
            ? "Brad's rule saved. It applies to this month and every month after."
            : $"Brad's rule saved. {moved} paycheck(s) moved to the month the rule puts them in, along with the bills they cover.";
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
        if (!isNew)
        {
            // The form doesn't carry the last check stub; keep it.
            var stub = await db.IncomeSources.AsNoTracking().Where(s => s.Id == source.Id)
                .Select(s => new { s.StubPayDate, s.StubEmployer, s.StubGross }).FirstOrDefaultAsync();
            if (stub is null) return NotFound();
            (source.StubPayDate, source.StubEmployer, source.StubGross) = (stub.StubPayDate, stub.StubEmployer, stub.StubGross);
        }
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
            // A month-end payday follows the weekend setting in months already built, too.
            await builder.RedateMonthEndPaychecksAsync();
            var changed = await builder.UpdateIncomeInFutureMonthsAsync(source);
            // Paychecks pay for the extra debt payments, so those are placed again too.
            await builder.SpreadDebtExtraAsync();
            TempData["Message"] = changed == 0
                ? $"Saved {source.Name}."
                : $"Saved {source.Name} and updated its paydays from today on in this month's and later budgets. Paydays you changed by hand keep their amount; past paydays stay as they were.";
        }
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Reads an uploaded check stub with the AI helper and shows what it found for review. The file isn't kept.</summary>
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(StatementReader.MaxBytes + 1024 * 1024)]
    public async Task<IActionResult> ReadStub(int id, IFormFile? stub, string? returnTo, CancellationToken ct)
    {
        var source = await db.IncomeSources.FindAsync(id);
        if (source is null) return NotFound();
        IActionResult Back() => returnTo == "edit" ? RedirectToAction(nameof(Edit), new { id }) : RedirectToAction(nameof(Index));
        if (stub is null || stub.Length == 0)
        {
            TempData["Error"] = "Pick the check stub file first.";
            return Back();
        }
        try
        {
            using var buffer = new MemoryStream();
            await stub.CopyToAsync(buffer, ct);
            var findings = await stubs.ReadAsync(source, stub.FileName, buffer.ToArray(), ct);
            ViewBag.Income = source;
            ViewBag.Upcoming = await db.PayPeriods.CountAsync(p => p.IncomeSourceId == id && p.PayDate >= DateTime.Today, ct);
            return View("Stub", findings);
        }
        catch (AdvisorException ex)
        {
            TempData["Error"] = ex.Message;
            return Back();
        }
    }

    /// <summary>
    /// Saves a check stub's breakdown on the paycheck and, if picked, its take-home pay: on the paycheck for new
    /// months and, when updateBuilt is on, on its paydays from today on in months already built.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyStub(int id, DateTime? payDate, string? employer, decimal? gross,
        List<PayStubLine>? lines, bool useNet, decimal? net, bool updateBuilt)
    {
        var source = await db.IncomeSources.Include(s => s.StubLines).FirstOrDefaultAsync(s => s.Id == id);
        if (source is null) return NotFound();

        source.StubPayDate = payDate;
        source.StubEmployer = string.IsNullOrWhiteSpace(employer) ? null : employer.Trim()[..Math.Min(100, employer.Trim().Length)];
        source.StubGross = gross;
        source.StubLines.Clear();
        foreach (var line in (lines ?? []).Where(l => !string.IsNullOrWhiteSpace(l.Name) && l.Amount != 0))
            source.StubLines.Add(new PayStubLine { Kind = line.Kind, Name = line.Name.Trim(), Amount = line.Amount, YearToDate = line.YearToDate });

        var message = $"Saved the check stub breakdown on {source.Name}.";
        if (useNet && net is decimal take && take > 0)
        {
            var old = source.TakeHomeAmount;
            source.TakeHomeAmount = Math.Round(take, 2);
            var changed = 0;
            if (updateBuilt)
            {
                var upcoming = await db.PayPeriods.Where(p => p.IncomeSourceId == id && p.PayDate >= DateTime.Today).ToListAsync();
                foreach (var p in upcoming.Where(p => p.BaseAmount != source.TakeHomeAmount))
                {
                    p.BaseAmount = source.TakeHomeAmount;
                    p.AmountSetByHand = false;
                    changed++;
                }
            }
            message = $"{source.Name}'s take-home pay is now {source.TakeHomeAmount:C} (was {old:C})" +
                      (changed > 0 ? $", including {changed} upcoming payday(s) in months already built." : ". New months use it.");
        }
        await db.SaveChangesAsync();
        TempData["Message"] = message;
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
