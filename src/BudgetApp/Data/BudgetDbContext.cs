using BudgetApp.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Data;

/// <summary>The budget tables, plus the sign-in tables (people who can sign in) and the keys that protect sign-ins.</summary>
public class BudgetDbContext(DbContextOptions<BudgetDbContext> options)
    : IdentityDbContext<IdentityUser>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<IncomeSource> IncomeSources => Set<IncomeSource>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<BudgetRules> BudgetRules => Set<BudgetRules>();
    public DbSet<DebtPlanSettings> DebtPlanSettings => Set<DebtPlanSettings>();
    public DbSet<HouseholdSettings> HouseholdSettings => Set<HouseholdSettings>();
    public DbSet<RecurringDebt> RecurringDebts => Set<RecurringDebt>();
    public DbSet<MonthlyBudget> MonthlyBudgets => Set<MonthlyBudget>();
    public DbSet<PayPeriod> PayPeriods => Set<PayPeriod>();
    public DbSet<PayPeriodExtra> PayPeriodExtras => Set<PayPeriodExtra>();
    public DbSet<BudgetExpense> BudgetExpenses => Set<BudgetExpense>();

    /// <summary>The household's Brad's rule settings (a single row).</summary>
    public Task<BudgetRules> GetRulesAsync() => SingleRowAsync(BudgetRules);

    /// <summary>The Debt Payoff page's plan (a single row).</summary>
    public Task<DebtPlanSettings> GetDebtPlanAsync() => SingleRowAsync(DebtPlanSettings);

    /// <summary>The buffer and the leftover split, from the Settings page (a single row).</summary>
    public Task<HouseholdSettings> GetHouseholdAsync() => SingleRowAsync(HouseholdSettings);

    /// <summary>Row 1 of a one-row settings table, added if it's missing.</summary>
    private static async Task<T> SingleRowAsync<T>(DbSet<T> set) where T : class, ISingleRow, new() =>
        await set.FindAsync(1) ?? set.Add(new T { Id = 1 }).Entity;

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<MonthlyBudget>().HasIndex(m => new { m.Year, m.Month }).IsUnique();
        b.Entity<Category>().HasIndex(c => c.Name).IsUnique();

        // Categories can't be deleted while something still uses them.
        b.Entity<RecurringDebt>().HasOne(d => d.Category).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<BudgetExpense>().HasOne(e => e.Category).WithMany().OnDelete(DeleteBehavior.Restrict);

        // Deleting a template keeps the month's copy.
        b.Entity<BudgetExpense>().HasOne(e => e.RecurringDebt).WithMany().OnDelete(DeleteBehavior.SetNull);
        b.Entity<IncomeSource>().HasMany(s => s.StubLines).WithOne().HasForeignKey(l => l.IncomeSourceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<PayPeriod>().HasOne(p => p.IncomeSource).WithMany().OnDelete(DeleteBehavior.SetNull);
        // Removing a paycheck source puts bills tied to it back on "whichever payday comes first".
        b.Entity<BudgetExpense>().HasOne(e => e.ExtraForDebt).WithMany().HasForeignKey(e => e.ExtraForDebtId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<RecurringDebt>().HasOne(d => d.PaidFrom).WithMany().HasForeignKey(d => d.PaidFromIncomeSourceId).OnDelete(DeleteBehavior.SetNull);

        // Removing a paycheck moves its bills to "Not assigned" rather than deleting them.
        b.Entity<BudgetExpense>().HasOne(e => e.PayPeriod).WithMany().OnDelete(DeleteBehavior.SetNull);

        b.Entity<BudgetRules>().HasData(new BudgetRules { Id = 1 });
        b.Entity<DebtPlanSettings>().HasData(new DebtPlanSettings { Id = 1 });
        b.Entity<HouseholdSettings>().HasData(new HouseholdSettings { Id = 1 });

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
