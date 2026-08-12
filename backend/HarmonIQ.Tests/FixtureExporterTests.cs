using HarmonIQ.Api.Commands;
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Persistence;
using HarmonIQ.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HarmonIQ.Tests;

/// <summary>
/// Assembling the fixture file: the <c>analyses</c> rows are the index of what was scored, and the
/// object store holds the bodies the consumer actually renders. A row exists only where both do.
/// </summary>
public class FixtureExporterTests : IDisposable
{
    private const string Engine = "eng-1";

    private readonly string _dbPath;
    private readonly SqliteContextFactory _factory;
    private readonly MemoryObjectStore _store = new();

    public FixtureExporterTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"harmoniq-export-test-{Guid.NewGuid():N}.db");
        _factory = new SqliteContextFactory(_dbPath);
        using var ctx = _factory.Create();
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
        GC.SuppressFinalize(this);
    }

    private sealed class MemoryObjectStore : IObjectStore
    {
        private readonly Dictionary<string, byte[]> _items = new(StringComparer.Ordinal);

        public Task<string> PutAsync(string key, ReadOnlyMemory<byte> body, CancellationToken ct)
        {
            _items[key] = body.ToArray();
            return Task.FromResult(UriFor(key));
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken ct) =>
            Task.FromResult(_items.TryGetValue(key, out var v) ? v : null);

        public string UriFor(string key) => $"memory://{key}";
    }

    private sealed class SqliteContextFactory(string dbPath)
    {
        private readonly string _connectionString = $"Data Source={dbPath}";

        public HarmonIQDbContext Create() =>
            new(new DbContextOptionsBuilder<HarmonIQDbContext>().UseSqlite(_connectionString).Options);
    }

    private async Task SeedAsync(string propertyKey, string subjectId, string principleSet, bool storeBody)
    {
        await using var ctx = _factory.Create();
        if (await ctx.Subjects.FindAsync(subjectId) is null)
        {
            ctx.Subjects.Add(new Subject
            {
                Id = subjectId,
                PropertyKey = propertyKey,
                SubjectType = "property",
                CreatedAt = DateTimeOffset.UnixEpoch,
                LastSeenAt = DateTimeOffset.UnixEpoch,
            });
        }

        ctx.Analyses.Add(new Analysis
        {
            Id = $"{subjectId}:{principleSet}:{Engine}",
            SubjectId = subjectId,
            PrincipleSet = principleSet,
            RulesVersion = $"{principleSet}-1.0",
            EngineVersion = Engine,
            Status = AnalysisStatuses.Ok,
            Mode = "live",
            Score = 70,
            Grade = "B-",
            ComputedAt = DateTimeOffset.UnixEpoch,
        });
        await ctx.SaveChangesAsync();

        if (storeBody)
        {
            await new ReportBodyWriter(_store).WriteAsync(
                new ReportBody(
                    subjectId, principleSet, $"{principleSet}-1.0", Engine, AnalysisStatuses.Ok, "live",
                    70, "B-", 0.7, 0.6, 0.4, "photos/without", 72, 65, "A summary.",
                    [], [], [], [], null, null, null, DateTimeOffset.UnixEpoch),
                CancellationToken.None);
        }
    }

    private async Task<FixtureGradesFile> ExportAsync(params string[] propertyKeys)
    {
        await using var ctx = _factory.Create();
        var exporter = new FixtureExporter(ctx, new ReportBodyWriter(_store));
        return await exporter.ExportAsync(propertyKeys, Engine, CancellationToken.None);
    }

    [Fact]
    public async Task ExportsRowsForTheRequestedPropertyOnly()
    {
        await SeedAsync("349246f", "349246f", PrincipleSets.FengShui, storeBody: true);
        await SeedAsync("tk93cec", "tk93cec", PrincipleSets.FengShui, storeBody: true);

        var file = await ExportAsync("349246f");

        var row = Assert.Single(file.Rows);
        Assert.Equal("349246f", row.ListingId);
    }

    [Fact]
    public async Task SkipsAnAnalysisWhoseReportBodyWasNeverStored()
    {
        await SeedAsync("349246f", "349246f", PrincipleSets.FengShui, storeBody: true);
        await SeedAsync("349246f", "349246f", PrincipleSets.Vastu, storeBody: false);

        var file = await ExportAsync("349246f");

        Assert.Equal([PrincipleSets.FengShui], file.Rows.Select(r => r.PrincipleSet));
    }
}
