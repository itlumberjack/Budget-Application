using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

/// <summary>The Bills &amp; Expenses page: bills, living expenses and loans that repeat. Each month's budget gets a copy.</summary>
public class RecurringDebtsController(BudgetDbContext db, BudgetBuilder builder, StatementReader statements) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.RecurringDebts.Include(d => d.Category)
            .OrderBy(d => d.Category!.SortOrder).ThenBy(d => d.DueDay).ThenBy(d => d.Title)
            .ToListAsync());

    /// <summary>A new item. A living expense starts out weekly and mandatory, since that's what food and gas usually are.</summary>
    public async Task<IActionResult> Create(BillKind kind = BillKind.Bill)
    {
        await LoadCategoriesAsync();
        var item = new RecurringDebt { Kind = Enum.IsDefined(kind) ? kind : BillKind.Bill };
        if (item.Kind == BillKind.LivingExpense)
        {
            item.Frequency = BillFrequency.Weekly;
            item.IsFixed = true;
        }
        return View("Edit", item);
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
        if (!Enum.IsDefined(debt.Kind)) debt.Kind = BillKind.Bill;
        // Whose payday only applies to mandatory items, and only to a paycheck that exists.
        if (!debt.IsFixed || !await db.IncomeSources.AnyAsync(s => s.Id == debt.PaidFromIncomeSourceId))
            debt.PaidFromIncomeSourceId = null;
        debt.ClearLoanDetailsUnlessLoan();
        if (!ModelState.IsValid)
        {
            await LoadCategoriesAsync();
            return View("Edit", debt);
        }
        var isNew = debt.Id == 0;
        var old = isNew ? null : await db.RecurringDebts.AsNoTracking().Where(d => d.Id == debt.Id).Select(d => new { d.Balance, d.Kind, d.PaidFromIncomeSourceId }).FirstOrDefaultAsync();
        if (debt.Balance != old?.Balance) debt.BalanceAsOf = debt.Balance is null ? null : DateTime.Today;

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
            // A new choice of whose payday moves its unpaid copies in this month and later ones.
            if (old is not null && old.PaidFromIncomeSourceId != debt.PaidFromIncomeSourceId)
                await builder.PlaceCopiesAsync(debt.Id);
            TempData["Message"] = $"Saved {debt.Title}. New months use the change; months already built keep their own amounts.";
        }
        // Loans decide where the Debt Payoff page's extra payment goes, so it's spread again.
        if (debt.Kind == BillKind.Loan || old?.Kind == BillKind.Loan) await builder.SpreadDebtExtraAsync();
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

    /// <summary>
    /// Has the AI read an uploaded loan statement and shows what it found next to what the loan has now.
    /// Nothing changes until the user picks what to keep (ApplyStatement). The file isn't stored.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(StatementReader.MaxBytes + 1024 * 1024)]
    public async Task<IActionResult> ReadStatement(int id, IFormFile? statement, CancellationToken ct)
    {
        var loan = await db.RecurringDebts.FindAsync(id);
        if (loan is null) return NotFound();
        if (statement is null || statement.Length == 0)
        {
            TempData["Error"] = "Pick the statement file first.";
            return RedirectToAction(nameof(Edit), new { id });
        }
        try
        {
            using var buffer = new MemoryStream();
            await statement.CopyToAsync(buffer, ct);
            var findings = await statements.ReadAsync(loan, statement.FileName, buffer.ToArray(), ct);
            ViewBag.Loan = loan;
            return View("Statement", findings);
        }
        catch (AdvisorException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Edit), new { id });
        }
    }

    /// <summary>Saves the parts of a statement the user kept: amount owed, rate, minimum payment and the promo.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyStatement(int id, DateTime? statementDate,
        bool useBalance, decimal? balance, bool useApr, decimal? apr, bool useMinimum, decimal? minimum,
        string? promo, decimal? promoRate, decimal? promoBalance, DateTime? promoEnd, bool promoDeferred)
    {
        var loan = await db.RecurringDebts.FindAsync(id);
        if (loan is null) return NotFound();
        var changed = new List<string>();
        if (useBalance && balance is >= 0)
        {
            loan.Balance = balance;
            loan.BalanceAsOf = statementDate ?? DateTime.Today;
            changed.Add("amount owed");
        }
        if (useApr && apr is >= 0 and <= 100) { loan.InterestRate = apr; changed.Add("interest rate"); }
        var minimumChanged = useMinimum && minimum is > 0 && minimum != loan.Amount;
        if (minimumChanged) { loan.Amount = minimum!.Value; changed.Add("payment"); }
        if (promo == "use" && promoEnd is not null)
        {
            loan.PromoRate = Math.Clamp(promoRate ?? 0, 0, 100);
            loan.PromoBalance = promoBalance is >= 0 && promoBalance < loan.Balance ? promoBalance : null;
            loan.PromoEndDate = promoEnd;
            loan.PromoDeferredInterest = promoDeferred;
            changed.Add("promo");
        }
        else if (promo == "clear" && loan.PromoEndDate is not null)
        {
            loan.ClearPromo();
            changed.Add("promo (removed)");
        }
        await db.SaveChangesAsync();
        // A new payment goes to this month's and later months' unpaid copies; the extra payment is spread again.
        if (minimumChanged)
        {
            var lastMonth = Helpers.Dates.ThisMonth.AddMonths(-1);
            await builder.CarryForwardAsync(loan.Id, lastMonth.Year, lastMonth.Month, null, dueDayChanged: false);
        }
        await builder.SpreadDebtExtraAsync();
        TempData["Message"] = changed.Count == 0
            ? $"Nothing changed on {loan.Title}."
            : $"Updated {loan.Title}'s {string.Join(", ", changed)} from the statement.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    private async Task LoadCategoriesAsync()
    {
        ViewBag.Categories = new SelectList(await db.Categories.InDisplayOrder().ToListAsync(), "Id", "Name");
        ViewBag.IncomeSources = await db.IncomeSources.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
    }
}
