using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

/// <summary>The month-by-month budget pages.</summary>
public class BudgetsController(BudgetDbContext db, BudgetBuilder builder) : Controller
{
    public async Task<IActionResult> Index()
    {
        var months = await db.MonthlyBudgets
            .OrderBy(b => b.Year).ThenBy(b => b.Month)
            .Select(b => new MonthSummary(
                b.Year, b.Month,
                b.PayPeriods.Sum(p => p.BaseAmount) + b.PayPeriods.SelectMany(p => p.Extras).Sum(x => x.Amount),
                b.Expenses.Sum(e => e.Amount),
                b.Expenses.Where(e => e.Status == PaymentStatus.Complete).Sum(e => e.Amount)))
            .ToListAsync();

        ViewBag.HasIncome = await db.IncomeSources.AnyAsync();
        ViewBag.HasDebts = await db.RecurringDebts.AnyAsync();
        return View(months);
    }

    /// <summary>Opens a month from the picker on the index page (value looks like "2026-10").</summary>
    public IActionResult Go(string month)
    {
        if (DateTime.TryParseExact(month + "-01", "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d))
            return RedirectToMonth(d.Year, d.Month);
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Month(int year, int month)
    {
        if (month is < 1 or > 12 || year is < 2000 or > 2100) return NotFound();

        var budget = await db.MonthlyBudgets
            .Include(b => b.PayPeriods).ThenInclude(p => p.Extras)
            .Include(b => b.Expenses)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.Year == year && b.Month == month);

        // Build the month on first visit from the pay schedules and recurring debts,
        // as long as there's something set up to build it from.
        if (budget is null && (await db.IncomeSources.AnyAsync(s => s.IsActive) || await db.RecurringDebts.AnyAsync(d => d.IsActive)))
        {
            await builder.CreateMonthAsync(year, month);
            budget = await db.MonthlyBudgets
                .Include(b => b.PayPeriods).ThenInclude(p => p.Extras)
                .Include(b => b.Expenses)
                .AsSplitQuery()
                .FirstAsync(b => b.Year == year && b.Month == month);
        }

        budget?.PayPeriods.Sort((a, b) => a.PayDate != b.PayDate ? a.PayDate.CompareTo(b.PayDate) : string.Compare(a.Label, b.Label, StringComparison.Ordinal));

        return View(new MonthViewModel
        {
            Year = year,
            Month = month,
            Budget = budget,
            Categories = await db.Categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync()
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int year, int month)
    {
        await builder.CreateMonthAsync(year, month);
        return RedirectToMonth(year, month);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Sync(int id)
    {
        var budget = await db.MonthlyBudgets.FindAsync(id);
        if (budget is null) return NotFound();
        var (pay, debts) = await builder.SyncAsync(id);
        TempData["Message"] = pay + debts == 0
            ? "Nothing new to add. This month already has every paycheck and recurring debt."
            : $"Added {pay} paycheck(s) and {debts} recurring debt(s).";
        return RedirectToMonth(budget.Year, budget.Month);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var budget = await db.MonthlyBudgets.FindAsync(id);
        if (budget is null) return NotFound();
        db.MonthlyBudgets.Remove(budget);
        await db.SaveChangesAsync();
        TempData["Message"] = $"Deleted the {budget.Title} budget.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveNotes(int id, string? notes)
    {
        var budget = await db.MonthlyBudgets.FindAsync(id);
        if (budget is null) return NotFound();
        budget.Notes = notes;
        await db.SaveChangesAsync();
        return RedirectToMonth(budget.Year, budget.Month);
    }

    // ----- Paychecks -----

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPaycheck(int budgetId, string label, DateTime payDate, decimal amount)
    {
        var budget = await db.MonthlyBudgets.FindAsync(budgetId);
        if (budget is null) return NotFound();
        if (string.IsNullOrWhiteSpace(label)) label = "Other income";
        db.PayPeriods.Add(new PayPeriod { MonthlyBudgetId = budgetId, Label = label.Trim(), PayDate = payDate, BaseAmount = amount });
        await db.SaveChangesAsync();
        return RedirectToMonth(budget.Year, budget.Month);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePaycheck(int id, decimal baseAmount, DateTime payDate)
    {
        var pay = await db.PayPeriods.Include(p => p.MonthlyBudget).FirstOrDefaultAsync(p => p.Id == id);
        if (pay is null) return NotFound();
        pay.BaseAmount = baseAmount;
        pay.PayDate = payDate;
        await db.SaveChangesAsync();
        return RedirectToMonth(pay.MonthlyBudget!.Year, pay.MonthlyBudget.Month, $"pay-{id}");
    }

    /// <summary>Moves a paycheck to the next (offset 1) or previous (offset -1) month's budget.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MovePaycheck(int id, int offset)
    {
        if (offset is not (1 or -1)) return BadRequest();
        var pay = await db.PayPeriods.Include(p => p.MonthlyBudget).FirstOrDefaultAsync(p => p.Id == id);
        if (pay is null) return NotFound();
        var from = pay.MonthlyBudget!;
        var to = await builder.MovePaycheckAsync(id, offset);
        TempData["Message"] = $"Moved {pay.Label}'s {pay.PayDate:MMMM d} paycheck to {to.Title}. You're looking at {to.Title} now.";
        return RedirectToMonth(to.Year, to.Month);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePaycheck(int id)
    {
        var pay = await db.PayPeriods.Include(p => p.MonthlyBudget).FirstOrDefaultAsync(p => p.Id == id);
        if (pay is null) return NotFound();
        db.PayPeriods.Remove(pay);
        await db.SaveChangesAsync();
        return RedirectToMonth(pay.MonthlyBudget!.Year, pay.MonthlyBudget.Month);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPayExtra(int payPeriodId, string description, decimal amount)
    {
        var pay = await db.PayPeriods.Include(p => p.MonthlyBudget).FirstOrDefaultAsync(p => p.Id == payPeriodId);
        if (pay is null) return NotFound();
        if (amount != 0)
        {
            db.PayPeriodExtras.Add(new PayPeriodExtra
            {
                PayPeriodId = payPeriodId,
                Description = string.IsNullOrWhiteSpace(description) ? "Extra" : description.Trim(),
                Amount = amount
            });
            await db.SaveChangesAsync();
        }
        return RedirectToMonth(pay.MonthlyBudget!.Year, pay.MonthlyBudget.Month, $"pay-{payPeriodId}");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePayExtra(int id)
    {
        var extra = await db.PayPeriodExtras.Include(x => x.PayPeriod).ThenInclude(p => p!.MonthlyBudget)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (extra is null) return NotFound();
        db.PayPeriodExtras.Remove(extra);
        await db.SaveChangesAsync();
        var b = extra.PayPeriod!.MonthlyBudget!;
        return RedirectToMonth(b.Year, b.Month, $"pay-{extra.PayPeriodId}");
    }

    // ----- Expenses -----

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddExpense(int budgetId, int categoryId, int? payPeriodId, string title, string? company, decimal amount, int? dueDay,
        PaymentStatus status = PaymentStatus.NotPaid)
    {
        var budget = await db.MonthlyBudgets.Include(b => b.PayPeriods).FirstOrDefaultAsync(b => b.Id == budgetId);
        if (budget is null) return NotFound();
        if (string.IsNullOrWhiteSpace(title))
        {
            TempData["Error"] = "Give the expense a name.";
            return RedirectToMonth(budget.Year, budget.Month);
        }
        db.BudgetExpenses.Add(new BudgetExpense
        {
            MonthlyBudgetId = budgetId,
            CategoryId = categoryId,
            PayPeriodId = budget.PayPeriods.Any(p => p.Id == payPeriodId) ? payPeriodId : null,
            Title = title.Trim(),
            Company = string.IsNullOrWhiteSpace(company) ? null : company.Trim(),
            Amount = amount,
            DueDay = dueDay is >= 1 and <= 31 ? dueDay : null,
            Status = Enum.IsDefined(status) ? status : PaymentStatus.NotPaid
        });
        await db.SaveChangesAsync();
        return RedirectToMonth(budget.Year, budget.Month);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateExpense(int id, string title, string? company, decimal amount, int? dueDay,
        int categoryId, int? payPeriodId, PaymentStatus status)
    {
        var expense = await db.BudgetExpenses.Include(e => e.MonthlyBudget).ThenInclude(b => b!.PayPeriods)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (expense is null) return NotFound();
        var budget = expense.MonthlyBudget!;
        if (!string.IsNullOrWhiteSpace(title)) expense.Title = title.Trim();
        expense.Company = string.IsNullOrWhiteSpace(company) ? null : company.Trim();
        expense.Amount = amount;
        expense.DueDay = dueDay is >= 1 and <= 31 ? dueDay : null;
        if (await db.Categories.AnyAsync(c => c.Id == categoryId)) expense.CategoryId = categoryId;
        expense.PayPeriodId = budget.PayPeriods.Any(p => p.Id == payPeriodId) ? payPeriodId : null;
        if (Enum.IsDefined(status)) expense.Status = status;
        await db.SaveChangesAsync();
        return RedirectToMonth(budget.Year, budget.Month);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, PaymentStatus status)
    {
        var expense = await db.BudgetExpenses.Include(e => e.MonthlyBudget).FirstOrDefaultAsync(e => e.Id == id);
        if (expense is null) return NotFound();
        if (!Enum.IsDefined(status)) return BadRequest();
        expense.Status = status;
        await db.SaveChangesAsync();
        return RedirectToMonth(expense.MonthlyBudget!.Year, expense.MonthlyBudget.Month);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteExpense(int id)
    {
        var expense = await db.BudgetExpenses.Include(e => e.MonthlyBudget).FirstOrDefaultAsync(e => e.Id == id);
        if (expense is null) return NotFound();
        db.BudgetExpenses.Remove(expense);
        await db.SaveChangesAsync();
        return RedirectToMonth(expense.MonthlyBudget!.Year, expense.MonthlyBudget.Month);
    }

    private RedirectResult RedirectToMonth(int year, int month, string? anchor = null) =>
        Redirect(Url.RouteUrl("month", new { year, month }) + (anchor is null ? "" : "#" + anchor));
}
