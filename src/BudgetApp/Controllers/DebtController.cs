using BudgetApp.Data;
using BudgetApp.Helpers;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BudgetApp.Controllers;

/// <summary>Debt statistics and payoff plans for bills that have an amount owed (loans, cards).</summary>
public class DebtController(BudgetDbContext db, BudgetBuilder builder, MonthService months, WhatIfAdvisor whatIf) : Controller
{
    public async Task<IActionResult> Index()
    {
        await builder.KeepMonthsCurrentAsync();
        var loans = await db.RecurringDebts.Loans().ToListAsync();
        var settings = await db.GetDebtPlanAsync();
        // This month's extra payment, payday by payday, as it was spread on the month page.
        var month = await db.MonthlyBudgets.Include(b => b.PayPeriods).Include(b => b.Expenses)
            .FirstOrDefaultAsync(b => b.Year == DateTime.Today.Year && b.Month == DateTime.Today.Month);
        // What's left on each loan after each of this month's payments, and after the month's last one.
        Dictionary<int, decimal> owedAfter = month is null ? new() : await months.OwedAfterAsync(month);
        ViewBag.OwedAfter = owedAfter;
        ViewBag.OwedAfterMonth = month is null ? new Dictionary<int, decimal>() : month.Expenses
            .Where(e => owedAfter.ContainsKey(e.Id))
            .GroupBy(e => e.ExtraForDebtId ?? e.RecurringDebtId!.Value)
            .ToDictionary(g => g.Key, g => g.Min(e => owedAfter[e.Id]));
        ViewBag.ThisMonth = month;
        ViewBag.ExtraThisMonth = month is null ? new List<BudgetExpense>()
            : await db.BudgetExpenses.Include(e => e.PayPeriod).Include(e => e.ExtraForDebt)
                .Where(e => e.MonthlyBudgetId == month.Id && e.ExtraForDebtId != null && !(e.ExtraPinned && e.Amount == 0))
                .OrderBy(e => e.PayPeriod!.PayDate).ThenBy(e => e.Title).ToListAsync();
        ViewBag.Incomes = await db.IncomeSources.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
        ViewBag.Lumps = await db.DebtLumpSums.OrderBy(l => l.Date).ToListAsync();
        ViewBag.WhatIfs = await db.WhatIfQuestions.OrderByDescending(q => q.AskedAt).Take(5).ToListAsync();
        return View(DebtPageModel.Build(loans, settings));
    }

    [HttpPost, ValidateAntiForgeryToken]
    /// <summary>
    /// Saves the plan: strategy, monthly extra and the payday it comes from ("spread", or "sourceId:n" for that
    /// income's nth paycheck of the month, 0 for the last), and the one-time extras, which replace the ones saved.
    /// </summary>
    public async Task<IActionResult> SavePlan(PayoffStrategy strategy, decimal extraMonthly, string? extraPayday, List<DebtLumpSum>? lumps,
        bool extraMonthlyIsIncome = false)
    {
        var settings = await db.GetDebtPlanAsync();
        if (Enum.IsDefined(strategy)) settings.Strategy = strategy;
        settings.ExtraMonthly = Math.Max(0, extraMonthly);
        settings.ExtraMonthlyIsIncome = extraMonthlyIsIncome;
        var pick = (extraPayday ?? "").Split(':');
        if (pick.Length == 2 && int.TryParse(pick[0], out var sourceId) && int.TryParse(pick[1], out var n)
            && await db.IncomeSources.AnyAsync(s => s.Id == sourceId))
            (settings.ExtraIncomeSourceId, settings.ExtraPaydayNumber) = (sourceId, Math.Clamp(n, 0, 5));
        else
            (settings.ExtraIncomeSourceId, settings.ExtraPaydayNumber) = (null, 0);

        db.DebtLumpSums.RemoveRange(await db.DebtLumpSums.ToListAsync());
        foreach (var lump in (lumps ?? []).Where(l => l.Amount > 0 && l.Date > DateTime.MinValue))
            db.DebtLumpSums.Add(new DebtLumpSum { Date = lump.Date.Date, Amount = Math.Round(lump.Amount, 2), Note = lump.Note?.Trim() is { Length: > 0 } note ? note[..Math.Min(100, note.Length)] : null });
        await db.SaveChangesAsync();

        // The extra is paid from the paydays in each month's budget, so those are redone too.
        await builder.SpreadDebtExtraAsync();
        var from = settings.ExtraIncomeSourceId is int id ? $"comes out of {PaydayName(await db.IncomeSources.FindAsync(id), settings.ExtraPaydayNumber)} each month"
            : "is spread over each month's paydays so they're left with about the same";
        TempData["Message"] = settings.ExtraMonthly > 0 && settings.Strategy != PayoffStrategy.MinimumOnly
            ? $"Saved. The {settings.ExtraMonthly:C} extra {from}, from this month on."
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

    /// <summary>"Brad's paycheck" or "Jessica's 2nd paycheck", for a payday picked for the extra.</summary>
    public static string PaydayName(IncomeSource? source, int n) => source is null ? "the chosen payday"
        : source.Frequency == PayFrequency.Monthly ? $"{source.Name}'s paycheck"
        : $"{source.Name}'s {(n switch { 1 => "1st", 2 => "2nd", 3 => "3rd", 4 => "4th", 5 => "5th", _ => "last" })} paycheck";

    // ----- What if -----

    /// <summary>The What If page: questions asked so far, and the answer to one of them worked out with today's loans.</summary>
    public async Task<IActionResult> WhatIf(int? id)
    {
        ViewBag.Questions = await db.WhatIfQuestions.OrderByDescending(q => q.AskedAt).ToListAsync();
        var question = id is int qid ? await db.WhatIfQuestions.FindAsync(qid) : null;
        if (id is not null && question is null) return NotFound();
        ViewBag.Question = question;
        if (question is null) return View();

        var scenario = JsonSerializer.Deserialize<WhatIfScenario>(question.ScenarioJson)!;
        var loans = await db.RecurringDebts.Loans().ToListAsync();
        var settings = await db.GetDebtPlanAsync();
        return View(WhatIfCalculator.Run(loans.Select(l => l.ToDebtInput()).ToList(), settings, scenario, Dates.ThisMonth.AddMonths(1)));
    }

    /// <summary>Has the AI helper turn a what-if question into changes to the loans, then shows the answer.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AskWhatIf(string? question, CancellationToken ct)
    {
        question = question?.Trim();
        if (string.IsNullOrEmpty(question))
        {
            TempData["Error"] = "Type a what-if question first.";
            return RedirectToAction(nameof(WhatIf));
        }
        if (question.Length > 1000) question = question[..1000];
        var loans = await db.RecurringDebts.Loans().ToListAsync();
        if (loans.Count == 0)
        {
            TempData["Error"] = "Add your loans first; what-if questions are worked out from them.";
            return RedirectToAction(nameof(WhatIf));
        }
        try
        {
            var scenario = await whatIf.AskAsync(question, loans, await db.GetDebtPlanAsync(), ct);
            if (scenario.Unsupported is not null && scenario.LoanChanges.Count + scenario.Consolidations.Count + scenario.NewLoans.Count == 0
                && scenario.ExtraMonthly is null && scenario.Strategy is null && scenario.OneTimeAmount is null)
            {
                TempData["Error"] = "That one can't be worked out here: " + scenario.Unsupported;
                TempData["WhatIfQuestion"] = question;
                return RedirectToAction(nameof(WhatIf));
            }
            var saved = new WhatIfQuestion
            {
                Question = question, Title = scenario.Title, ScenarioJson = JsonSerializer.Serialize(scenario), AskedAt = DateTime.Now
            };
            db.WhatIfQuestions.Add(saved);
            await db.SaveChangesAsync();
            return RedirectToAction(nameof(WhatIf), new { id = saved.Id });
        }
        catch (AdvisorException ex)
        {
            TempData["Error"] = ex.Message;
            TempData["WhatIfQuestion"] = question;
            return RedirectToAction(nameof(WhatIf));
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteWhatIf(int id)
    {
        if (await db.WhatIfQuestions.FindAsync(id) is { } q)
        {
            db.WhatIfQuestions.Remove(q);
            await db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(WhatIf));
    }
}
