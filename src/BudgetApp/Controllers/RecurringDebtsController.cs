using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

/// <summary>The Bills &amp; Expenses page: bills, living expenses and loans that repeat. Each month's budget gets a copy.</summary>
public class RecurringDebtsController(BudgetDbContext db, BudgetBuilder builder) : Controller
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

        // A monthly loan that's new (or just became a loan) joins the Debt Payoff page's weekly or every-two-weeks
        // payments; the amount entered is the monthly payment it's split from.
        var plan = await db.GetDebtPlanAsync();
        var joinsSchedule = debt.Kind == BillKind.Loan && old?.Kind != BillKind.Loan
            && debt.Frequency == BillFrequency.Monthly && plan.PaymentFrequency != BillFrequency.Monthly;
        if (joinsSchedule) debt.ChangePaymentSchedule(plan.PaymentFrequency, plan.PaymentStartDate);
        var scheduleNote = joinsSchedule
            ? $" Like your other loans it's paid {(plan.PaymentFrequency == BillFrequency.Weekly ? "weekly" : "every two weeks")}: {debt.Amount:C} each time."
            : "";
        if (isNew) db.RecurringDebts.Add(debt);
        else db.RecurringDebts.Update(debt);
        await db.SaveChangesAsync();
        if (isNew)
        {
            await builder.AddToCurrentAndFutureMonthsAsync(debt: debt);
            TempData["Message"] = $"Added {debt.Title}. It's now in this month's budget and every month after." + scheduleNote;
        }
        else
        {
            // Fixed or flexible applies to this month's and later months' copies straight away.
            var months = db.MonthlyBudgets.FromThisMonth().Select(b => b.Id);
            await db.BudgetExpenses.Where(e => e.RecurringDebtId == debt.Id && months.Contains(e.MonthlyBudgetId))
                .ExecuteUpdateAsync(u => u.SetProperty(e => e.IsFixed, debt.IsFixed));
            if (joinsSchedule) await builder.RescheduleAsync([debt.Id]);
            // A new choice of whose payday moves its unpaid copies in this month and later ones.
            if (old is not null && old.PaidFromIncomeSourceId != debt.PaidFromIncomeSourceId)
                await builder.PlaceCopiesAsync(debt.Id);
            TempData["Message"] = $"Saved {debt.Title}. New months use the change; months already built keep their own amounts." + scheduleNote;
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

    private async Task LoadCategoriesAsync()
    {
        ViewBag.Categories = new SelectList(await db.Categories.InDisplayOrder().ToListAsync(), "Id", "Name");
        ViewBag.IncomeSources = await db.IncomeSources.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
    }
}
