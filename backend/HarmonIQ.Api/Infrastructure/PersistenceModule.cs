using HarmonIQ.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HarmonIQ.Api.Infrastructure;

/// <summary>
/// Registers HarmonIQDbContext (SQLite, path from HARMONIQ_DB, default
/// "./.harmoniq-local/harmoniq.db") and IObjectStore (FileSystemObjectStore).
/// </summary>
public class PersistenceModule : IServiceModule
{
    public void Register(IServiceCollection services, IConfiguration config)
    {
        var dbPath = config["HARMONIQ_DB"]
            ?? Environment.GetEnvironmentVariable("HARMONIQ_DB")
            ?? Path.Combine(".harmoniq-local", "harmoniq.db");

        var dbDir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (dbDir is not null)
        {
            Directory.CreateDirectory(dbDir);
        }

        services.AddDbContext<HarmonIQDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        services.AddSingleton<IObjectStore, FileSystemObjectStore>();
    }
}
