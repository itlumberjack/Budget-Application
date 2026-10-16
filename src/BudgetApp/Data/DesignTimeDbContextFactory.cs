using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BudgetApp.Data;

/// <summary>
/// Used only by the <c>dotnet ef</c> commands (migrations, database update). They always work against MySQL,
/// whatever Database:Provider is set to, so the database can be created while the app is still in testing mode.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BudgetDbContext>
{
    public BudgetDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<DesignTimeDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connection = config.GetConnectionString("Budget")
            ?? throw new InvalidOperationException("Connection string 'Budget' is missing from appsettings.json.");
        return new BudgetDbContext(new DbContextOptionsBuilder<BudgetDbContext>().UseMySQL(connection).Options);
    }
}
