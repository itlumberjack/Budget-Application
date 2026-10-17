using BudgetApp.Data;
using BudgetApp.Helpers;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

/// <summary>The month-by-month budget pages.</summary>
public class BudgetsController(BudgetDbContext db, BudgetBuilder builder, MonthService months, BudgetAdvisor advisor) : Controller
{
    public async Task<IActionResult> Index()
    {
        var list = await months.ListAsync();

        ViewBag.HasIncome = await db.IncomeSources.AnyAsync();
        ViewBag.HasDebts = await db.RecurringDebts.AnyAsync();
        return View(list);
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
        return View(await months.LoadAsync(year, month));
    }

    /// <summary>Asks the AI helper how to spread this month's unpaid bills across its paydays. Changes nothing.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SuggestArrangement(int id, CancellationToken ct)
    {
        var month = await months.LoadAsync(id, ct);
        if (month is null) return NotFound();
        try
        {
            return Json(await advisor.SuggestAsync(month, ct));
        }
        catch (AdvisorException ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    /// <summary>Suggests moving unpaid bills so each payday has about the same left over. Changes nothing.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EvenOut(int id)
    {
        var month = await months.LoadAsync(id);
        return month is null ? NotFound() : Json(BillBalancer.Suggest(month));
    }

    /// <summary>Applies the suggested moves the user kept. Each move is "expenseId:payPeriodId".</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyArrangement(int id, List<string> moves)
    {
        var budget = await db.MonthlyBudgets.FindAsync(id);
        if (budget is null) return NotFound();
        if (Archived(budget, out var locked)) return locked;
        var parsed = moves.Select(m => m.Split(':'))
            .Where(p => p.Length == 2 && int.TryParse(p[0], out _) && int.TryParse(p[1], out _))
            .Select(p => (int.Parse(p[0]), int.Parse(p[1])));
        var moved = await months.ApplyMovesAsync(budget, parsed);
        TempData["Message"] = moved == 1 ? "Moved 1 bill." : $"Moved {moved} bills.";
        return RedirectToMonth(budget);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Sync(int id)
    {
        var budget = await db.MonthlyBudgets.FindAsync(id);
        if (budget is null) return NotFound();
        if (Archived(budget, out var locked)) return locked;
        var (pay, debts) = await builder.SyncAsync(id);
        await builder.SpreadDebtExtraAsync(id);
        TempData["Message"] = pay + debts == 0
            ? "Nothing new to add. This month already has every paycheck and recurring bill."
            : $"Added {pay} paycheck(s) and {debts} recurring bill(s).";
        return RedirectToMonth(budget);
    }

    /// <summary>
    /// "Resync everything": rebuilds this month's and later open budgets from the current settings. Paychecks follow
    /// Pay &amp; Schedule and Brad's rule, missing bills are added, duplicates removed, and the extra debt payments
    /// placed again. With resetBills, unpaid bills also go back to their amount, due day and payday on Bills &amp;
    /// Expenses. Past paydays, pending or complete items and paychecks changed by hand are kept.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Resync(bool resetBills, string? returnTo)
    {
        await builder.KeepMonthsCurrentAsync();
        var moved = await builder.ApplyRulesToCurrentAndFutureMonthsAsync();
        await builder.RedateMonthEndPaychecksAsync();
        var paydays = 0;
        foreach (var source in await db.IncomeSources.ToListAsync())
            paydays += await builder.UpdateIncomeInFutureMonthsAsync(source);
        var bills = 0;
        foreach (var id in await db.MonthlyBudgets.FromThisMonth().Where(b => !b.IsArchived).Select(b => b.Id).ToListAsync())
            bills += (await builder.SyncAsync(id)).debts;
        if (resetBills)
        {
            var last = Dates.ThisMonth.AddMonths(-1);
            foreach (var debt in await db.RecurringDebts.Where(d => d.IsActive).ToListAsync())
                await builder.CarryForwardAsync(debt.Id, last.Year, last.Month, null, dueDayChanged: true);
        }
        var duplicates = await builder.RemoveDuplicateBillsAsync();
        await builder.SpreadDebtExtraAsync();
        TempData["Message"] = $"Resynced this month and every later one: {paydays + moved} payday change(s), {bills} missing bill(s) added"
            + (duplicates > 0 ? $", {duplicates} duplicate(s) removed" : "")
            + (resetBills ? ", unpaid bills reset to Bills & Expenses" : "") + ", and the extra debt payments placed again.";
        return returnTo is { } r && r.Split('-') is [var y, var m] && int.TryParse(y, out var year) && int.TryParse(m, out var month)
            ? RedirectToMonth(year, month) : RedirectToAction(nameof(Index));
    }

    /// <summary>Archives a month by hand: hidden from the main list and read-only.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(int id)
    {
        var budget = await db.MonthlyBudgets.FindAsync(id);
        if (budget is null) return NotFound();
        budget.IsArchived = true;
        budget.KeepOpen = false;
        await db.SaveChangesAsync();
        TempData["Message"] = $"Archived {budget.Title}. It's under Archive on the Monthly Budgets page.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Reopens an archived month for changes. It stays open until it's archived again by hand.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Unarchive(int id)
    {
        var budget = await db.MonthlyBudgets.FindAsync(id);
        if (budget is null) return NotFound();
        budget.IsArchived = false;
        budget.KeepOpen = true;
        await db.SaveChangesAsync();
        TempData["Message"] = $"{budget.Title} is open for changes again.";
        return RedirectToMonth(budget);
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

    /// <summary>Saves the month as it stands now as the plan each payday is compared against.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePlan(int id)
    {
        var budget = await db.MonthlyBudgets.FindAsync(id);
        if (budget is null) return NotFound();
        if (Archived(budget, out var locked)) return locked;
        var replacing = budget.HasPlan;
        await months.SavePlanAsync(id);
        TempData["Message"] = replacing
            ? "Saved this month as it stands now as the plan."
            : "Saved the plan. Each payday now shows the plan next to where things stand.";
        return RedirectToMonth(budget);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveNotes(int id, string? notes)
    {
        var budget = await db.MonthlyBudgets.FindAsync(id);
        if (budget is null) return NotFound();
        if (Archived(budget, out var locked)) return locked;
        budget.Notes = notes;
        await db.SaveChangesAsync();
        return RedirectToMonth(budget);
    }

    // ----- Paychecks -----

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPaycheck(int budgetId, string label, DateTime payDate, decimal amount)
    {
        var budget = await db.MonthlyBudgets.FindAsync(budgetId);
        if (budget is null) return NotFound();
        if (Archived(budget, out var locked)) return locked;
        db.PayPeriods.Add(new PayPeriod { MonthlyBudgetId = budgetId, Label = label.TrimToNull() ?? "Other income", PayDate = payDate, BaseAmount = amount });
        await db.SaveChangesAsync();
        return RedirectToMonth(budget);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePaycheck(int id, decimal baseAmount, DateTime payDate)
    {
        var pay = await db.PayPeriods.Include(p => p.MonthlyBudget).FirstOrDefaultAsync(p => p.Id == id);
        if (pay is null) return NotFound();
        if (Archived(pay.MonthlyBudget!, out var locked)) return locked;
        if (pay.BaseAmount != baseAmount) pay.AmountSetByHand = true;
        pay.BaseAmount = baseAmount;
        pay.PayDate = payDate;
        await db.SaveChangesAsync();
        return RedirectToMonth(pay.MonthlyBudget!, $"pay-{id}");
    }

    /// <summary>Moves a paycheck to the next (offset 1) or previous (offset -1) month's budget.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MovePaycheck(int id, int offset)
    {
        if (offset is not (1 or -1)) return BadRequest();
        var pay = await db.PayPeriods.Include(p => p.MonthlyBudget).FirstOrDefaultAsync(p => p.Id == id);
        if (pay is null) return NotFound();
        if (Archived(pay.MonthlyBudget!, out var locked)) return locked;
        var to = await builder.MovePaycheckAsync(id, offset, manual: true);
        TempData["Message"] = $"Moved {pay.Label}'s {pay.PayDate:MMMM d} paycheck to {to.Title}. You're looking at {to.Title} now.";
        return RedirectToMonth(to);
    }

    /// <summary>
    /// Shifts a whole payday column (the first one back a month, the last one forward), keeping the page
    /// on this month. Brad's rule leaves shifted paychecks alone.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ShiftPayday(int budgetId, string payIds, int offset)
    {
        if (offset is not (1 or -1)) return BadRequest();
        var budget = await db.MonthlyBudgets.Include(b => b.PayPeriods).FirstOrDefaultAsync(b => b.Id == budgetId);
        if (budget is null) return NotFound();
        if (Archived(budget, out var locked)) return locked;
        var ids = payIds.Split(',').Select(x => int.TryParse(x, out var n) ? n : 0).ToHashSet();
        var pays = budget.PayPeriods.Where(p => ids.Contains(p.Id)).OrderBy(p => p.PayDate).ToList();
        if (pays.Count == 0) return RedirectToMonth(budget);

        MonthlyBudget? to = null;
        foreach (var pay in pays)
            to = await builder.MovePaycheckAsync(pay.Id, offset, manual: true);
        var what = string.Join(" and ", pays.Select(p => $"{p.Label}'s {p.PayDate:MMMM d}"));
        TempData["Message"] = $"Moved {what} {(pays.Count == 1 ? "paycheck" : "paychecks")} to {to!.Title}. " +
                               $"Bills {(pays.Count == 1 ? "it was" : "they were")} covering here moved to the other paydays, and Brad's rule won't move {(pays.Count == 1 ? "it" : "them")} back.";
        return RedirectToMonth(budget);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePaycheck(int id)
    {
        var pay = await db.PayPeriods.Include(p => p.MonthlyBudget).FirstOrDefaultAsync(p => p.Id == id);
        if (pay is null) return NotFound();
        if (Archived(pay.MonthlyBudget!, out var locked)) return locked;
        db.PayPeriods.Remove(pay);
        await db.SaveChangesAsync();
        return RedirectToMonth(pay.MonthlyBudget!);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPayExtra(int payPeriodId, string description, decimal amount)
    {
        var pay = await db.PayPeriods.Include(p => p.MonthlyBudget).FirstOrDefaultAsync(p => p.Id == payPeriodId);
        if (pay is null) return NotFound();
        if (Archived(pay.MonthlyBudget!, out var locked)) return locked;
        if (amount != 0)
        {
            db.PayPeriodExtras.Add(new PayPeriodExtra
            {
                PayPeriodId = payPeriodId,
                Description = description.TrimToNull() ?? "Extra",
                Amount = amount
            });
            await db.SaveChangesAsync();
        }
        return RedirectToMonth(pay.MonthlyBudget!, $"pay-{payPeriodId}");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePayExtra(int id)
    {
        var extra = await db.PayPeriodExtras.Include(x => x.PayPeriod).ThenInclude(p => p!.MonthlyBudget)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (extra is null) return NotFound();
        if (Archived(extra.PayPeriod!.MonthlyBudget!, out var locked)) return locked;
        db.PayPeriodExtras.Remove(extra);
        await db.SaveChangesAsync();
        return RedirectToMonth(extra.PayPeriod!.MonthlyBudget!, $"pay-{extra.PayPeriodId}");
    }

    // ----- Expenses -----

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddExpense(int budgetId, int categoryId, int? payPeriodId, string title, string? company, decimal amount, int? dueDay,
        PaymentStatus status = PaymentStatus.NotPaid, bool isFixed = false)
    {
        var budget = await db.MonthlyBudgets.Include(b => b.PayPeriods).FirstOrDefaultAsync(b => b.Id == budgetId);
        if (budget is null) return NotFound();
        if (Archived(budget, out var locked)) return locked;
        if (string.IsNullOrWhiteSpace(title))
        {
            TempData["Error"] = "Give the expense a name.";
            return RedirectToMonth(budget);
        }
        if (status != PaymentStatus.NotPaid) await months.SavePlanAsync(budget.Id, onlyIfMissing: true);
        db.BudgetExpenses.Add(new BudgetExpense
        {
            MonthlyBudgetId = budgetId,
            CategoryId = categoryId,
            PayPeriodId = budget.PayPeriods.Any(p => p.Id == payPeriodId) ? payPeriodId : null,
            Title = title.Trim(),
            Company = company.TrimToNull(),
            Amount = amount,
            DueDay = dueDay.ValidDay(),
            Status = Enum.IsDefined(status) ? status : PaymentStatus.NotPaid,
            IsFixed = isFixed
        });
        await db.SaveChangesAsync();
        return RedirectToMonth(budget);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateExpense(int id, string title, string? company, decimal amount, int? dueDay,
        int categoryId, int? payPeriodId, PaymentStatus status, bool isFixed, bool applyForward = false)
    {
        var expense = await db.BudgetExpenses.Include(e => e.MonthlyBudget).ThenInclude(b => b!.PayPeriods)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (expense is null) return NotFound();
        var budget = expense.MonthlyBudget!;
        if (Archived(budget, out var locked)) return locked;
        // The plan is saved before this change, so it shows how things looked before anything was paid.
        if (status != PaymentStatus.NotPaid) await months.SavePlanAsync(budget.Id, onlyIfMissing: true);
        if (!string.IsNullOrWhiteSpace(title)) expense.Title = title.Trim();
        expense.Company = company.TrimToNull();
        // A share of the extra payment changed by hand keeps its amount when the month's extra is spread again.
        var reshare = expense.ExtraForDebtId is not null && expense.Amount != amount;
        if (reshare) expense.ExtraPinned = true;
        expense.Amount = amount;
        var oldDue = expense.DueDay;
        var oldPay = expense.PayPeriodId;
        expense.DueDay = dueDay.ValidDay();
        if (await db.Categories.AnyAsync(c => c.Id == categoryId)) expense.CategoryId = categoryId;
        expense.PayPeriodId = budget.PayPeriods.Any(p => p.Id == payPeriodId) ? payPeriodId : null;
        // A new due day with the payday left as it was: put it under the payday before it's now due.
        if (expense.DueDay != oldDue && expense.PayPeriodId == oldPay && !isFixed)
            expense.PayPeriodId = BudgetBuilder.PickPayPeriod(budget.PayPeriods, budget.Year, budget.Month, expense.DueDay)?.Id ?? oldPay;
        if (Enum.IsDefined(status)) expense.Status = status;
        expense.IsFixed = isFixed;
        await db.SaveChangesAsync();
        if (applyForward) await CarryForwardAsync(expense, budget);
        else if (reshare) await RebalanceExtraAsync($"Saved {expense.Title}.", budget);
        return RedirectToMonth(budget);
    }

    /// <summary>
    /// After a share of the extra payment is changed or removed, the month's other paydays make up the difference
    /// so the month still pays the extra set on Debt Payoff.
    /// </summary>
    private async Task RebalanceExtraAsync(string done, MonthlyBudget budget)
    {
        await builder.SpreadDebtExtraAsync(budget.Id);
        var settings = await db.GetDebtPlanAsync();
        var oneTime = settings.LumpSumsIn(budget.Year, budget.Month);
        var monthTotal = await db.BudgetExpenses.Where(e => e.MonthlyBudgetId == budget.Id && e.ExtraForDebtId != null).SumAsync(e => e.Amount);
        var target = settings.ExtraMonthly + oneTime;
        TempData["Message"] = Math.Abs(monthTotal - target) < 0.01m
            ? $"{done} The other paydays were adjusted so {budget.Title} still pays {target:C} extra."
            : $"{done} {budget.Title} now pays {monthTotal:C} extra instead of {target:C}; no other payday had room to make up the difference.";
    }

    /// <summary>
    /// "Also change later months" in the edit dialog. A recurring bill's change goes to the bill on Bills &amp;
    /// Expenses and its unpaid copies in later months. A share of the extra debt payment makes this month's extra
    /// total the new monthly extra on Debt Payoff, spread again over the later months' paydays.
    /// </summary>
    private async Task CarryForwardAsync(BudgetExpense expense, MonthlyBudget budget)
    {
        if (expense.RecurringDebtId is int debtId && await db.RecurringDebts.FindAsync(debtId) is { } debt)
        {
            var dueDayChanged = debt.Frequency == BillFrequency.Monthly && debt.DueDay != expense.DueDay;
            debt.Title = expense.Title;
            debt.Company = expense.Company;
            debt.Amount = expense.Amount;
            debt.CategoryId = expense.CategoryId;
            debt.IsFixed = expense.IsFixed;
            if (debt.Frequency == BillFrequency.Monthly) debt.DueDay = expense.DueDay;
            await db.SaveChangesAsync();
            await builder.CarryForwardAsync(debt.Id, budget.Year, budget.Month, expense.DueDay, dueDayChanged);
            // A loan's payment is its minimum, which changes how much room the extra payment has.
            if (debt.Kind == BillKind.Loan) await builder.SpreadDebtExtraAsync(after: (budget.Year, budget.Month));
            TempData["Message"] = $"Saved {expense.Title} here, on Bills & Expenses, and in the ones after it.";
        }
        else if (expense.ExtraForDebtId is not null)
        {
            var settings = await db.GetDebtPlanAsync();
            var oneTime = settings.LumpSumsIn(budget.Year, budget.Month);
            var monthTotal = await db.BudgetExpenses.Where(e => e.MonthlyBudgetId == budget.Id && e.ExtraForDebtId != null).SumAsync(e => e.Amount);
            settings.ExtraMonthly = Math.Max(0, monthTotal - oneTime);
            await db.SaveChangesAsync();
            await builder.SpreadDebtExtraAsync(after: (budget.Year, budget.Month));
            TempData["Message"] = $"The extra debt payment is now {settings.ExtraMonthly:C} a month, spread over the paydays in later months.";
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, PaymentStatus status)
    {
        var expense = await db.BudgetExpenses.Include(e => e.MonthlyBudget).FirstOrDefaultAsync(e => e.Id == id);
        if (expense is null) return NotFound();
        if (Archived(expense.MonthlyBudget!, out var locked)) return locked;
        if (!Enum.IsDefined(status)) return BadRequest();
        if (status != PaymentStatus.NotPaid) await months.SavePlanAsync(expense.MonthlyBudgetId, onlyIfMissing: true);
        expense.Status = status;
        await db.SaveChangesAsync();
        return RedirectToMonth(expense.MonthlyBudget!);
    }

    /// <summary>
    /// Moves a bill to another payday by dragging it on the month page. Mandatory bills stay put, and a bill
    /// can't be dragged to a payday after it's due (the same rule Even out follows).
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveExpense(int id, int payPeriodId)
    {
        var budgetId = await db.BudgetExpenses.Where(e => e.Id == id).Select(e => (int?)e.MonthlyBudgetId).FirstOrDefaultAsync();
        var month = budgetId is int bid ? await months.LoadAsync(bid) : null;
        var expense = month?.Budget!.Expenses.FirstOrDefault(e => e.Id == id);
        if (month is null || expense is null) return NotFound();
        if (Archived(month.Budget!, out var locked)) return locked;
        var to = month.Columns.FirstOrDefault(c => c.Paychecks.Any(p => p.Id == payPeriodId));
        if (to is null || to.Paychecks.Any(p => p.Id == expense.PayPeriodId)) return RedirectToMonth(month.Budget!);
        if (expense.IsFixed)
            TempData["Error"] = $"{expense.Title} is mandatory, so it stays on its payday. Turn that off in its edit dialog to move it.";
        else if (!month.CanMoveTo(to, expense))
            TempData["Error"] = $"{to.Heading} is after {expense.Title} is due, so it can't go there.";
        else
        {
            expense.PayPeriodId = to.TargetPayPeriodId;
            await db.SaveChangesAsync();
        }
        return RedirectToMonth(month.Budget!);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteExpense(int id)
    {
        var expense = await db.BudgetExpenses.Include(e => e.MonthlyBudget).FirstOrDefaultAsync(e => e.Id == id);
        if (expense is null) return NotFound();
        if (Archived(expense.MonthlyBudget!, out var locked)) return locked;
        if (expense.ExtraForDebtId is not null && expense.Status == PaymentStatus.NotPaid)
        {
            // A removed share stays as $0 so respreading leaves this payday out, and the other paydays make it up.
            expense.Amount = 0;
            expense.ExtraPinned = true;
            await db.SaveChangesAsync();
            await RebalanceExtraAsync($"Removed this payday's {expense.Title}.", expense.MonthlyBudget!);
            return RedirectToMonth(expense.MonthlyBudget!);
        }
        db.BudgetExpenses.Remove(expense);
        await db.SaveChangesAsync();
        return RedirectToMonth(expense.MonthlyBudget!);
    }

    /// <summary>Archived months are read-only: changes are turned away with a note on how to reopen the month.</summary>
    private bool Archived(MonthlyBudget budget, out IActionResult result)
    {
        result = RedirectToMonth(budget);
        if (!budget.IsArchived) return false;
        TempData["Error"] = $"{budget.Title} is archived. Unarchive it to make changes.";
        return true;
    }

    private RedirectResult RedirectToMonth(int year, int month, string? anchor = null) =>
        Redirect(Url.RouteUrl("month", new { year, month }) + (anchor is null ? "" : "#" + anchor));

    private RedirectResult RedirectToMonth(MonthlyBudget budget, string? anchor = null) =>
        RedirectToMonth(budget.Year, budget.Month, anchor);
}
