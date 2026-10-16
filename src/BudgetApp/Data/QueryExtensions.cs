using BudgetApp.Helpers;
using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Data;

/// <summary>Queries used in more than one place.</summary>
public static class QueryExtensions
{
    /// <summary>A month with its paychecks (and their extras) and its bills.</summary>
    public static IQueryable<MonthlyBudget> WithDetails(this IQueryable<MonthlyBudget> months) =>
        months.Include(b => b.PayPeriods).ThenInclude(p => p.Extras)
              .Include(b => b.Expenses)
              .AsSplitQuery();

    /// <summary>This month and every month after it.</summary>
    public static IQueryable<MonthlyBudget> FromThisMonth(this IQueryable<MonthlyBudget> months)
    {
        var start = Dates.ThisMonth;
        return months.Where(b => b.Year > start.Year || (b.Year == start.Year && b.Month >= start.Month));
    }

    /// <summary>Months that ended before this one.</summary>
    public static IQueryable<MonthlyBudget> BeforeThisMonth(this IQueryable<MonthlyBudget> months)
    {
        var start = Dates.ThisMonth;
        return months.Where(b => b.Year < start.Year || (b.Year == start.Year && b.Month < start.Month));
    }

    /// <summary>Categories in the order the budget sections are shown.</summary>
    public static IQueryable<Category> InDisplayOrder(this IQueryable<Category> categories) =>
        categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name);

    /// <summary>Active bills with an amount owed, for the Debt Payoff page.</summary>
    public static IQueryable<RecurringDebt> Loans(this IQueryable<RecurringDebt> debts) =>
        debts.Where(d => d.IsActive && d.Balance > 0).OrderBy(d => d.Id);
}
