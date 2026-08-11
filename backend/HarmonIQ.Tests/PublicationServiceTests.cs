using HarmonIQ.Api.Models;
using HarmonIQ.Api.Persistence;
using HarmonIQ.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace HarmonIQ.Tests;

public class PublicationServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteContextFactory _factory;

    public PublicationServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"harmoniq-pub-test-{Guid.NewGuid():N}.db");
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
    }

    private static Subject MakeSubject(string id, string propertyKey, string subjectType = "property", string? externalPlanKey = null) => new()
    {
        Id = id,
        PropertyKey = propertyKey,
        SubjectType = subjectType,
        ExternalPlanKey = externalPlanKey,
        CreatedAt = DateTimeOffset.UtcNow,
        LastSeenAt = DateTimeOffset.UtcNow,
    };

    private static EngineVersion MakeEngineVersion(string version) => new()
    {
        Version = version,
        RulesVersionFengshui = "fengshui-2.0",
        RulesVersionVastu = "vastu-2.0",
        PromptVersion = "v2.0",
        ModelId = "claude-sonnet-5",
        CreatedAt = DateTimeOffset.UtcNow,
        PublishedAt = null,
    };

    private static Analysis MakeAnalysis(
        string id, string subjectId, string engineVersion, string principleSet,
        string mode = "live", string status = "ok", int? score = 88, string? grade = "B+", string? rulesVersion = null) => new()
    {
        Id = id,
        SubjectId = subjectId,
        PrincipleSet = principleSet,
        RulesVersion = rulesVersion ?? $"fengshui-2.0-{engineVersion}",
        EngineVersion = engineVersion,
        Status = status,
        Score = score,
        Grade = grade,
        Confidence = 0.8,
        Mode = mode,
        CohortEvidencePath = "photos",
        CohortOrientationPath = "with",
        ComputedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task PublishVersionAsync_DemoModeAnalyses_ProduceZeroProjectionRows()
    {
        using var ctx = _factory.Create();
        ctx.Subjects.Add(MakeSubject("s1", "prop1"));
        ctx.EngineVersions.Add(MakeEngineVersion("e1"));
        ctx.Analyses.Add(MakeAnalysis("a1", "s1", "e1", PrincipleSets.FengShui, mode: "demo"));
        await ctx.SaveChangesAsync();

        var service = new PublicationService(ctx);
        var result = await service.PublishVersionAsync("e1", CancellationToken.None);

        Assert.Equal(0, result.RowsWritten);
        Assert.Empty(ctx.ProjectionRows);
    }

    [Fact]
    public async Task PublishVersionAsync_InsufficientEvidenceAndFailed_ProduceZeroRows()
    {
        using var ctx = _factory.Create();
        ctx.Subjects.Add(MakeSubject("s1", "prop1"));
        ctx.Subjects.Add(MakeSubject("s2", "prop2"));
        ctx.EngineVersions.Add(MakeEngineVersion("e1"));
        ctx.Analyses.Add(MakeAnalysis("a1", "s1", "e1", PrincipleSets.Vastu, status: AnalysisStatuses.InsufficientEvidence, score: null, grade: null));
        ctx.Analyses.Add(MakeAnalysis("a2", "s2", "e1", PrincipleSets.FengShui, status: AnalysisStatuses.Failed, score: null, grade: null));
        await ctx.SaveChangesAsync();

        var service = new PublicationService(ctx);
        var result = await service.PublishVersionAsync("e1", CancellationToken.None);

        Assert.Equal(0, result.RowsWritten);
        Assert.Empty(ctx.ProjectionRows);
    }

    [Fact]
    public async Task PublishVersionAsync_EligibleLiveOkAnalysis_WritesRowAndFlipsPublishedAt()
    {
        using var ctx = _factory.Create();
        ctx.Subjects.Add(MakeSubject("s1", "prop1"));
        ctx.EngineVersions.Add(MakeEngineVersion("e1"));
        ctx.Analyses.Add(MakeAnalysis("a1", "s1", "e1", PrincipleSets.FengShui));
        await ctx.SaveChangesAsync();

        var service = new PublicationService(ctx);
        var result = await service.PublishVersionAsync("e1", CancellationToken.None);

        Assert.Equal(1, result.RowsWritten);
        var row = Assert.Single(ctx.ProjectionRows);
        Assert.Equal("prop1", row.ListingId);
        Assert.Null(row.FloorPlanId);
        Assert.Equal("B+", row.Grade);
        Assert.Equal("e1", row.EngineVersion);

        var engineVersion = await ctx.EngineVersions.FirstAsync(e => e.Version == "e1");
        Assert.NotNull(engineVersion.PublishedAt);
    }

    [Fact]
    public async Task PublishVersionAsync_FloorplanSubject_CarriesFloorPlanIdFromExternalPlanKey()
    {
        using var ctx = _factory.Create();
        ctx.Subjects.Add(MakeSubject("prop1:rk1", "prop1", subjectType: "floorplan", externalPlanKey: "rk1"));
        ctx.EngineVersions.Add(MakeEngineVersion("e1"));
        ctx.Analyses.Add(MakeAnalysis("a1", "prop1:rk1", "e1", PrincipleSets.FengShui));
        await ctx.SaveChangesAsync();

        var service = new PublicationService(ctx);
        await service.PublishVersionAsync("e1", CancellationToken.None);

        var row = Assert.Single(ctx.ProjectionRows);
        Assert.Equal("prop1", row.ListingId);
        Assert.Equal("rk1", row.FloorPlanId);
    }

    [Fact]
    public async Task PublishVersionAsync_IsIdempotent_SecondCallIsNoOpReturningSameResult()
    {
        using var ctx = _factory.Create();
        ctx.Subjects.Add(MakeSubject("s1", "prop1"));
        ctx.EngineVersions.Add(MakeEngineVersion("e1"));
        ctx.Analyses.Add(MakeAnalysis("a1", "s1", "e1", PrincipleSets.FengShui));
        await ctx.SaveChangesAsync();

        var service = new PublicationService(ctx);
        var first = await service.PublishVersionAsync("e1", CancellationToken.None);
        var second = await service.PublishVersionAsync("e1", CancellationToken.None);

        Assert.Equal(first.PublishedAt, second.PublishedAt);
        Assert.Equal(first.RowsWritten, second.RowsWritten);
        Assert.Equal(1, await ctx.ProjectionRows.CountAsync());
    }

    [Fact]
    public async Task PublishVersionAsync_PartialFailureMidPublish_LeavesPublishedAtNullAndNoNewRows()
    {
        using var ctx = _factory.Create();
        ctx.Subjects.Add(MakeSubject("s1", "prop1"));
        ctx.Subjects.Add(MakeSubject("s2", "prop2"));
        ctx.EngineVersions.Add(MakeEngineVersion("e1"));
        ctx.Analyses.Add(MakeAnalysis("a1", "s1", "e1", PrincipleSets.FengShui));
        ctx.Analyses.Add(MakeAnalysis("a2", "s2", "e1", PrincipleSets.FengShui));
        // Pre-seed a projection row whose Id collides with the deterministic Id the service
        // will compute for subject s2's analysis, forcing a unique-key failure partway
        // through the publish's insert batch.
        ctx.ProjectionRows.Add(new ProjectionRow
        {
            Id = "e1:s2:fengshui",
            ListingId = "prop2",
            PrincipleSet = PrincipleSets.FengShui,
            EngineVersion = "e1",
            ComputedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();

        // Fresh, untracked context — mirrors a real request/job resolving its own scoped
        // DbContext — so the pre-seeded row collides at SaveChanges (a DbUpdateException),
        // not at Add() time against an already-tracked in-memory instance.
        using var serviceCtx = _factory.Create();
        var service = new PublicationService(serviceCtx);

        await Assert.ThrowsAsync<DbUpdateException>(() => service.PublishVersionAsync("e1", CancellationToken.None));

        using var verifyCtx = _factory.Create();
        var engineVersion = await verifyCtx.EngineVersions.FirstAsync(e => e.Version == "e1");
        Assert.Null(engineVersion.PublishedAt);
        // Only the pre-seeded row exists — the row for s1's analysis, which would have been
        // added in the same transaction, was rolled back rather than left half-written.
        Assert.Equal(1, await verifyCtx.ProjectionRows.CountAsync());
    }

    [Fact]
    public async Task GetFeedAsync_TwoVersionsCoexist_FeedReturnsOnlyRequestedVersionsRows()
    {
        using var ctx = _factory.Create();
        ctx.Subjects.Add(MakeSubject("s1", "prop1"));
        ctx.EngineVersions.Add(MakeEngineVersion("e1"));
        ctx.EngineVersions.Add(MakeEngineVersion("e2"));
        ctx.Analyses.Add(MakeAnalysis("a1", "s1", "e1", PrincipleSets.FengShui, grade: "B+"));
        ctx.Analyses.Add(MakeAnalysis("a2", "s1", "e2", PrincipleSets.FengShui, grade: "A-"));
        await ctx.SaveChangesAsync();

        var service = new PublicationService(ctx);
        await service.PublishVersionAsync("e1", CancellationToken.None);
        await service.PublishVersionAsync("e2", CancellationToken.None);

        var feedE1 = await service.GetFeedAsync("e1", null, 10, CancellationToken.None);
        var feedE2 = await service.GetFeedAsync("e2", null, 10, CancellationToken.None);

        Assert.Equal("e1", feedE1.EngineVersion);
        Assert.All(feedE1.Rows, r => Assert.Equal("e1", r.EngineVersion));
        Assert.Single(feedE1.Rows);
        Assert.Equal("B+", feedE1.Rows[0].Grade);

        Assert.All(feedE2.Rows, r => Assert.Equal("e2", r.EngineVersion));
        Assert.Single(feedE2.Rows);
        Assert.Equal("A-", feedE2.Rows[0].Grade);
    }

    [Fact]
    public async Task GetFeedAsync_CursorPaging_IsStableAndTerminates()
    {
        using var ctx = _factory.Create();
        ctx.EngineVersions.Add(MakeEngineVersion("e1"));
        for (var i = 0; i < 5; i++)
        {
            ctx.Subjects.Add(MakeSubject($"s{i}", $"prop{i}"));
            ctx.Analyses.Add(MakeAnalysis($"a{i}", $"s{i}", "e1", PrincipleSets.FengShui));
        }
        await ctx.SaveChangesAsync();

        var service = new PublicationService(ctx);
        await service.PublishVersionAsync("e1", CancellationToken.None);

        var seen = new List<ProjectionRow>();
        string? cursor = null;
        var pages = 0;
        while (true)
        {
            var page = await service.GetFeedAsync("e1", cursor, 2, CancellationToken.None);
            seen.AddRange(page.Rows);
            pages++;
            Assert.True(pages <= 10, "Cursor paging did not terminate.");
            if (page.NextCursor is null)
            {
                break;
            }
            cursor = page.NextCursor;
        }

        Assert.Equal(5, seen.Count);
        Assert.Equal(seen.Select(r => r.Id).Distinct().Count(), seen.Count);
    }

    [Fact]
    public async Task GetPublishedAsync_ReturnsLatestPublished_OnSqlite()
    {
        // Regression: ordering by DateTimeOffset cannot be translated by the SQLite provider,
        // so this threw NotSupportedException on every call.
        using var ctx = _factory.Create();
        var older = MakeEngineVersion("aaaa11112222");
        older.PublishedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var newer = MakeEngineVersion("bbbb33334444");
        newer.PublishedAt = DateTimeOffset.UtcNow;
        ctx.EngineVersions.AddRange(older, newer, MakeEngineVersion("cccc55556666"));
        await ctx.SaveChangesAsync();

        var svc = new EngineVersionService(ctx, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var published = await svc.GetPublishedAsync(CancellationToken.None);

        Assert.NotNull(published);
        Assert.Equal("bbbb33334444", published!.Version);
    }

    [Fact]
    public async Task GetPublishedAsync_ReturnsNull_WhenNothingPublished()
    {
        using var ctx = _factory.Create();
        ctx.EngineVersions.Add(MakeEngineVersion("dddd77778888"));
        await ctx.SaveChangesAsync();

        var svc = new EngineVersionService(ctx, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        Assert.Null(await svc.GetPublishedAsync(CancellationToken.None));
    }

    private sealed class SqliteContextFactory(string dbPath)
    {
        private readonly string _connectionString = $"Data Source={dbPath}";

        public HarmonIQDbContext Create()
        {
            var options = new DbContextOptionsBuilder<HarmonIQDbContext>()
                .UseSqlite(_connectionString)
                .Options;
            return new HarmonIQDbContext(options);
        }
    }
}
