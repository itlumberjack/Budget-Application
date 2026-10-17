using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BudgetApp.Contracts;
using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Api;

/// <summary>
/// The JSON API for the phone app, under /api. Every call needs a bearer token from POST /api/auth/login
/// (the same email and password as the website). Archived months are read-only here too.
/// </summary>
public static class BudgetApi
{
    public static void MapBudgetApi(this WebApplication app)
    {
        // Sign-in: POST /api/auth/login returns a bearer token and a refresh token; POST /api/auth/refresh renews them.
        // Accounts are only made on the website, so the other Identity endpoints (register, password reset by email) are off.
        app.MapGroup("/api/auth").MapIdentityApi<IdentityUser>().AddEndpointFilter(async (context, next) =>
        {
            var path = context.HttpContext.Request.Path.Value ?? "";
            return path.EndsWith("/login", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/refresh", StringComparison.OrdinalIgnoreCase)
                ? await next(context)
                : Results.NotFound();
        });

        // Bearer tokens only: the website's sign-in cookie doesn't open the API, so other sites can't make calls with it.
        var api = app.MapGroup("/api").RequireAuthorization(p => p
            .AddAuthenticationSchemes(IdentityConstants.BearerScheme)
            .RequireAuthenticatedUser());

        api.MapGet("/me", (ClaimsPrincipal user) => new MeDto(user.Identity?.Name ?? ""));

        // ----- Months -----

        api.MapGet("/months", async (MonthService months) =>
            (await months.ListAsync())
                .Select(m => new MonthListItemDto(m.Year, m.Month, m.Title, m.Income, m.Expenses, m.Complete, m.Remaining, m.IsArchived))
                .ToList());

        api.MapGet("/months/{year:int}/{month:int}", async (int year, int month, MonthService months) =>
        {
            if (month is < 1 or > 12 || year is < 2000 or > 2100) return Results.NotFound();
            var model = await months.LoadAsync(year, month);
            return model.Budget is null
                ? Results.NotFound(new ApiError("Nothing is set up to build this month from yet. Add income or bills on the website."))
                : Results.Ok(MonthDto.From(model));
        });

        // Suggests moves so each payday has about the same left over. Changes nothing; send the ones to keep to /moves.
        api.MapPost("/months/{id:int}/even-out", async (int id, MonthService months) =>
        {
            var model = await months.LoadAsync(id);
            return model is null ? Results.NotFound() : Results.Ok(BillBalancer.Suggest(model));
        });

        api.MapPost("/months/{id:int}/moves", async (int id, ApplyMovesRequest request, BudgetDbContext db, MonthService months) =>
        {
            var budget = await db.MonthlyBudgets.FindAsync(id);
            if (budget is null) return Results.NotFound();
            if (budget.IsArchived) return Archived(budget);
            var moved = await months.ApplyMovesAsync(budget, request.Moves.Select(m => (m.ExpenseId, m.PayPeriodId)));
            return Results.Ok(new ApplyMovesResult(moved));
        });

        // Saves the month as it stands as the plan each payday is compared against (also done on the first payment).
        api.MapPost("/months/{id:int}/plan", async (int id, BudgetDbContext db, MonthService months) =>
        {
            var budget = await db.MonthlyBudgets.FindAsync(id);
            if (budget is null) return Results.NotFound();
            if (budget.IsArchived) return Archived(budget);
            await months.SavePlanAsync(id);
            return Results.Ok(MonthDto.From((await months.LoadAsync(id))!));
        });

        // ----- Bills in a month -----

        api.MapPut("/bills/{id:int}/status", async (int id, StatusUpdate update, BudgetDbContext db, MonthService months) =>
        {
            if (!Enum.IsDefined(update.Status)) return Results.BadRequest(new ApiError("Unknown status."));
            var bill = await db.BudgetExpenses.Include(e => e.MonthlyBudget).FirstOrDefaultAsync(e => e.Id == id);
            if (bill is null) return Results.NotFound();
            if (bill.MonthlyBudget!.IsArchived) return Archived(bill.MonthlyBudget);
            if (update.Status != PaymentStatus.NotPaid) await months.SavePlanAsync(bill.MonthlyBudgetId, onlyIfMissing: true);
            bill.Status = update.Status;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapPut("/bills/{id:int}/payday", async (int id, PaydayUpdate update, BudgetDbContext db) =>
        {
            var bill = await db.BudgetExpenses.Include(e => e.MonthlyBudget).ThenInclude(b => b!.PayPeriods)
                .FirstOrDefaultAsync(e => e.Id == id);
            if (bill is null) return Results.NotFound();
            var budget = bill.MonthlyBudget!;
            if (budget.IsArchived) return Archived(budget);
            if (update.PayPeriodId is int payId && budget.PayPeriods.All(p => p.Id != payId))
                return Results.BadRequest(new ApiError("That paycheck isn't in this bill's month."));
            bill.PayPeriodId = update.PayPeriodId;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ----- Settings, debt plan, and lists -----

        api.MapGet("/settings", async (BudgetDbContext db) => SettingsDto.From(await db.GetHouseholdAsync()));

        api.MapPut("/settings", async (SettingsDto form, BudgetDbContext db) =>
        {
            var problems = new List<ValidationResult>();
            if (!Validator.TryValidateObject(form, new ValidationContext(form), problems, validateAllProperties: true))
                return Results.ValidationProblem(problems
                    .SelectMany(p => p.MemberNames.DefaultIfEmpty(""), (p, name) => (name, p.ErrorMessage ?? "Invalid."))
                    .GroupBy(x => x.name).ToDictionary(g => g.Key, g => g.Select(x => x.Item2).ToArray()));
            var settings = await db.GetHouseholdAsync();
            form.ApplyTo(settings);
            await db.SaveChangesAsync();
            return Results.Ok(SettingsDto.From(settings));
        });

        api.MapGet("/debt-plan", async (BudgetDbContext db) =>
            DebtPlanDto.From(DebtPageModel.Build(await db.RecurringDebts.Loans().ToListAsync(), await db.GetDebtPlanAsync())));

        api.MapGet("/categories", async (BudgetDbContext db) => await db.Categories.InDisplayOrder().ToListAsync());

        api.MapGet("/recurring-bills", async (BudgetDbContext db) =>
            await db.RecurringDebts.Include(d => d.Category).OrderBy(d => d.Title).ToListAsync());

        api.MapGet("/income", async (BudgetDbContext db) => await db.IncomeSources.OrderBy(s => s.Name).ToListAsync());
    }

    private static IResult Archived(MonthlyBudget budget) =>
        Results.Conflict(new ApiError($"{budget.Title} is archived. Unarchive it on the website to make changes."));
}
