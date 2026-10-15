using System.Globalization;
using BudgetApp.Data;
using BudgetApp.Services;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
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
builder.Services.AddScoped<BudgetBuilder>();
// The optional AI helper on the month page. Needs Anthropic:ApiKey (see README).
builder.Services.AddHttpClient<BudgetAdvisor>(c => c.Timeout = TimeSpan.FromMinutes(2));

var app = builder.Build();

// Create or upgrade the database schema on startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BudgetDbContext>().Database;
    if (useInMemory) db.EnsureCreated(); // adds the starter categories
    else db.Migrate();
    app.Logger.LogInformation("Using {Provider} database", useInMemory ? "in-memory" : "MySQL");
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
app.UseAuthorization();
app.MapStaticAssets();

app.MapControllerRoute(
    name: "month",
    pattern: "budget/{year:int}/{month:int}",
    defaults: new { controller = "Budgets", action = "Month" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
