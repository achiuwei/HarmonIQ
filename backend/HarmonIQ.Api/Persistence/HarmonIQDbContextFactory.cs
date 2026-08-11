using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HarmonIQ.Api.Persistence;

/// <summary>
/// Design-time factory so `dotnet ef migrations add` can construct HarmonIQDbContext
/// without running the full web host (Program.cs is Task 10's exclusively). Not used at
/// runtime; runtime registration goes through Infrastructure/PersistenceModule.cs.
/// </summary>
public class HarmonIQDbContextFactory : IDesignTimeDbContextFactory<HarmonIQDbContext>
{
    public HarmonIQDbContext CreateDbContext(string[] args)
    {
        var dbPath = Environment.GetEnvironmentVariable("HARMONIQ_DB")
            ?? Path.Combine(".harmoniq-local", "harmoniq.db");

        var optionsBuilder = new DbContextOptionsBuilder<HarmonIQDbContext>();
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
        return new HarmonIQDbContext(optionsBuilder.Options);
    }
}
