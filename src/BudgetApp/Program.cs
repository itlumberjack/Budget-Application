using System.Globalization;
using System.Text.Json.Serialization;
using BudgetApp.Api;
using BudgetApp.Data;
using BudgetApp.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
// User secrets (where the Anthropic API key lives) load in every environment, not only Development.
builder.Configuration.AddUserSecrets<Program>(optional: true);

// Every page needs someone signed in, apart from the sign-in pages themselves.
builder.Services.AddControllersWithViews(o => o.Filters.Add(new AuthorizeFilter()));
// "InMemory" keeps everything in memory (handy for testing; data is lost when the app stops).
// "MySql" uses the Budget connection string.
var useInMemory = string.Equals(builder.Configuration["Database:Provider"], "InMemory", StringComparison.OrdinalIgnoreCase);
builder.Services.AddDbContext<BudgetDbContext>(options =>
{
    if (useInMemory)
        options.UseInMemoryDatabase("Budget");
    else
        options.UseMySQL(builder.Configuration.GetConnectionString("Budget")
            ?? throw new InvalidOperationException("Connection string 'Budget' is missing from appsettings.json."));
});

// Sign-in for the website (a cookie) and the phone app (bearer tokens from /api/auth/login).
// Accounts are added on the website: the first one on the setup page, the rest under Settings > Sign-ins.
builder.Services.AddIdentityApiEndpoints<IdentityUser>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<BudgetDbContext>();
// Pages use the sign-in cookie (and send people to the sign-in page); only /api uses bearer tokens.
builder.Services.AddAuthentication(o => o.DefaultScheme = IdentityConstants.ApplicationScheme);
builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/Login";
    o.ExpireTimeSpan = TimeSpan.FromDays(30);
    o.SlidingExpiration = true;
});
// Check the account on every request, so removing someone's sign-in signs them out right away.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
// Keep the keys that protect sign-ins in the database, so a restart or a new publish doesn't sign everyone out.
builder.Services.AddDataProtection().PersistKeysToDbContext<BudgetDbContext>().SetApplicationName("BudgetApp");
// The API sends statuses and strategies as words ("Complete"), not numbers.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddScoped<BudgetBuilder>();
builder.Services.AddScoped<MonthService>();
// The optional AI helper on the month page. Needs Anthropic:ApiKey (see README).
builder.Services.AddHttpClient<ClaudeApi>(c => c.Timeout = TimeSpan.FromMinutes(3));
builder.Services.AddScoped<BudgetAdvisor>();
builder.Services.AddScoped<StatementReader>();

var app = builder.Build();

// Create or upgrade the database schema on startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>().Database;
    if (useInMemory) db.EnsureCreated(); // adds the starter categories
    else db.Migrate();
    app.Logger.LogInformation("Using {Provider} database", useInMemory ? "in-memory" : "MySQL");
    // Archive months that have ended and build the next six months' budgets.
    var monthBuilder = scope.ServiceProvider.GetRequiredService<BudgetBuilder>();
    await monthBuilder.RemoveDuplicateBillsAsync();
    await monthBuilder.RedateMonthEndPaychecksAsync();
    await monthBuilder.UndoLoanPaymentScheduleAsync();
    await monthBuilder.KeepMonthsCurrentAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Dollar formatting and form input parsing use US conventions.
var usCulture = new CultureInfo("en-US");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(usCulture),
    SupportedCultures = [usCulture],
    SupportedUICultures = [usCulture]
});

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();

app.MapBudgetApi();

app.MapControllerRoute(
    name: "month",
    pattern: "budget/{year:int}/{month:int}",
    defaults: new { controller = "Budgets", action = "Month" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
