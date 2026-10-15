using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Data;

public class BudgetDbContext(DbContextOptions<BudgetDbContext> options) : DbContext(options)
{
    public DbSet<IncomeSource> IncomeSources => Set<IncomeSource>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<RecurringDebt> RecurringDebts => Set<RecurringDebt>();
    public DbSet<MonthlyBudget> MonthlyBudgets => Set<MonthlyBudget>();
    public DbSet<PayPeriod> PayPeriods => Set<PayPeriod>();
    public DbSet<PayPeriodExtra> PayPeriodExtras => Set<PayPeriodExtra>();
    public DbSet<BudgetExpense> BudgetExpenses => Set<BudgetExpense>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<MonthlyBudget>().HasIndex(m => new { m.Year, m.Month }).IsUnique();
        b.Entity<Category>().HasIndex(c => c.Name).IsUnique();

        // Categories can't be deleted while something still uses them.
        b.Entity<RecurringDebt>().HasOne(d => d.Category).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<BudgetExpense>().HasOne(e => e.Category).WithMany().OnDelete(DeleteBehavior.Restrict);

        // Deleting a template keeps the month's copy.
        b.Entity<BudgetExpense>().HasOne(e => e.RecurringDebt).WithMany().OnDelete(DeleteBehavior.SetNull);
        b.Entity<PayPeriod>().HasOne(p => p.IncomeSource).WithMany().OnDelete(DeleteBehavior.SetNull);

        // Removing a paycheck moves its bills to "Not assigned" rather than deleting them.
        b.Entity<BudgetExpense>().HasOne(e => e.PayPeriod).WithMany().OnDelete(DeleteBehavior.SetNull);

        // Starter categories, matching the sections of the family's budget spreadsheet.
        b.Entity<Category>().HasData(
            new Category { Id = 1, Name = "Housing", SortOrder = 10 },
            new Category { Id = 2, Name = "Utilities", SortOrder = 20 },
            new Category { Id = 3, Name = "Insurance", SortOrder = 30 },
            new Category { Id = 4, Name = "Loans", SortOrder = 40 },
            new Category { Id = 5, Name = "Investments", SortOrder = 50 },
            new Category { Id = 6, Name = "Child Care", SortOrder = 60 },
            new Category { Id = 7, Name = "Subscriptions", SortOrder = 70 },
            new Category { Id = 8, Name = "Household Needs", SortOrder = 80 },
            new Category { Id = 9, Name = "Additional Deductions", SortOrder = 90 },
            new Category { Id = 10, Name = "Other", SortOrder = 100 });
    }
}
