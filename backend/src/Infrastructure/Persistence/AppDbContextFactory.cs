using AiPlatform.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace AiPlatform.Infrastructure.Persistence;

// Design-time factory — `dotnet ef` এটা দিয়ে DbContext বানায়, পুরো web host না চালিয়ে।
// startup project (Api) এর appsettings + env var থেকে connection string পড়ে।
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default পাওয়া যায়নি (appsettings.Development.json বা env)।");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        // design-time-এ কোনো request নেই → system tenant (filter migration-এ irrelevant)।
        return new AppDbContext(options, new DesignTimeCurrentUser());
    }

    private sealed class DesignTimeCurrentUser : ICurrentUser
    {
        public string? UserId => null;
        public Guid TenantId => Guid.Empty;
    }
}
