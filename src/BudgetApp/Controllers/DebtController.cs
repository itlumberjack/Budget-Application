using BudgetApp.Data;
using BudgetApp.Helpers;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

/// <summary>Debt statistics and payoff plans for bills that have an amount owed (loans, cards).</summary>
public class DebtController(BudgetDbContext db, BudgetBuilder builder) : Controller
{
    public async Task<IActionResult> Index()
    {
        var loans = await db.RecurringDebts.Loans().ToListAsync();
        var settings = await db.GetDebtPlanAsync();
        // Weekly or every-two-weeks payments start on the next weekly or every-two-weeks payday unless one was picked.
        ViewBag.PaymentStart = settings.PaymentStartDate ?? await NextFrequentPaydayAsync() ?? DateTime.Today;
        return View(DebtPageModel.Build(loans, settings));
    }

    private async Task<DateTime?> NextFrequentPaydayAsync()
    {
        var source = await db.IncomeSources
            .Where(s => s.IsActive && s.AnchorDate != null && (s.Frequency == PayFrequency.Weekly || s.Frequency == PayFrequency.BiWeekly))
            .OrderBy(s => s.Frequency).FirstOrDefaultAsync();
        if (source is null) return null;
        var step = source.Frequency == PayFrequency.Weekly ? 7 : 14;
        var day = source.AnchorDate!.Value.Date;
        var behind = (DateTime.Today - day).Days;
        return behind <= 0 ? day : day.AddDays((behind + step - 1) / step * step);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePlan(PayoffStrategy strategy, decimal extraMonthly, decimal oneTimeAmount, DateTime? oneTimeMonth,
        BillFrequency paymentFrequency = BillFrequency.Monthly, DateTime? paymentStartDate = null)
    {
        var settings = await db.GetDebtPlanAsync();
        if (Enum.IsDefined(strategy)) settings.Strategy = strategy;
        settings.ExtraMonthly = Math.Max(0, extraMonthly);
        settings.OneTimeAmount = Math.Max(0, oneTimeAmount);
        settings.OneTimeMonth = settings.OneTimeAmount > 0 ? oneTimeMonth : null;

        // A new payment schedule changes every loan's payments, here and in this month's and later budgets.
        if (!Enum.IsDefined(paymentFrequency)) paymentFrequency = BillFrequency.Monthly;
        DateTime? start = paymentFrequency == BillFrequency.Monthly ? null : (paymentStartDate ?? DateTime.Today).Date;
        var scheduleChanged = paymentFrequency != settings.PaymentFrequency || start != settings.PaymentStartDate;
        settings.PaymentFrequency = paymentFrequency;
        settings.PaymentStartDate = start;
        var loans = scheduleChanged ? await db.RecurringDebts.Where(d => d.Kind == BillKind.Loan).ToListAsync() : [];
        foreach (var loan in loans)
            loan.ChangePaymentSchedule(paymentFrequency, start);
        await db.SaveChangesAsync();

        if (loans.Count > 0)
        {
            await builder.RescheduleAsync(loans.Select(l => l.Id).ToList());
            TempData["Message"] = paymentFrequency switch
            {
                BillFrequency.Weekly => $"Loans are now paid weekly, a quarter of the monthly payment each {start:dddd} from {start:MMMM d}.",
                BillFrequency.BiWeekly => $"Loans are now paid every two weeks, half the monthly payment each time from {start:MMMM d}.",
                _ => "Loans are now paid once a month."
            } + " This month's budget and later ones were updated; payments already marked pending or complete were left as they are.";
        }
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
        TempData["Message"] = "Saved the amounts owed.";
        return RedirectToAction(nameof(Index));
    }
}
