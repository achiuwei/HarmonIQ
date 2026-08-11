using HarmonIQ.Api.Commands;
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Persistence;
using HarmonIQ.Api.Services;
using HarmonIQ.Api.Services.Batch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Orientation = HarmonIQ.Api.Services.Orientation;

namespace HarmonIQ.Tests;

/// <summary>
/// The backfill command end to end, in demo mode (no CLAUDE_API_KEY on this machine — the point
/// of the perception/judgment split from Task 7 is that this is a legitimate, fully-functional
/// path, not a degraded one).
/// </summary>
public class BackfillCommandTests : IDisposable
{
    private readonly List<string> _dbPaths = [];

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in _dbPaths)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    // ---------------------------------------------------------------- harness

    private sealed class MemoryObjectStore : IObjectStore
    {
        public Dictionary<string, byte[]> Items { get; } = new(StringComparer.Ordinal);

        public Task<string> PutAsync(string key, ReadOnlyMemory<byte> body, CancellationToken ct)
        {
            Items[key] = body.ToArray();
            return Task.FromResult(UriFor(key));
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken ct) =>
            Task.FromResult(Items.TryGetValue(key, out var v) ? v : null);

        public string UriFor(string key) => $"memory://{key}";
    }

    private sealed class NullEvidenceLoader : IEvidenceLoader
    {
        public Task<byte[]?> LoadAsync(Subject subject, EvidenceRef reference, CancellationToken ct) =>
            Task.FromResult<byte[]?>(null);
    }

    private sealed class NullClaudeClient : IClaudeClient
    {
        public bool IsConfigured => false;
        public string Model => "unconfigured";
        public Task<System.Text.Json.JsonElement> MessagesAsync(object payload, CancellationToken ct = default) =>
            throw new ClaudeUnavailableException("no key on this machine");
    }

    /// <summary>A lens that returns a canned, low-friction observation — every rule is satisfied.</summary>
    private sealed class StubLens(FloorPlanObservation observation) : IFloorPlanLens
    {
        public int Calls { get; private set; }

        public Task<FloorPlanObservation> ReadAsync(Subject subject, byte[] planImage, bool live, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(FloorPlanLensService.Sanitize(observation));
        }
    }

    /// <summary>A lens that fails every time — exercises the retries-exhausted -> failed path.</summary>
    private sealed class ThrowingLens : IFloorPlanLens
    {
        public int Calls { get; private set; }

        public Task<FloorPlanObservation> ReadAsync(Subject subject, byte[] planImage, bool live, CancellationToken ct)
        {
            Calls++;
            throw new InvalidOperationException("the lens must not be called on a fingerprint-unchanged skip");
        }
    }

    private sealed class FakePlanSource : Dictionary<string, IReadOnlyList<ScrapedPlan>?>, IPlanSource
    {
        public Task<IReadOnlyList<ScrapedPlan>?> GetPlansAsync(string propertyKey, CancellationToken ct) =>
            Task.FromResult(TryGetValue(propertyKey, out var plans) ? plans : null);
    }

    private sealed class FakePlanImageLoader : IPlanImageLoader
    {
        public Task<byte[]?> LoadAsync(string? planImageUrl, CancellationToken ct) => Task.FromResult<byte[]?>(null);
    }

    private sealed class FakeOrientationProvider : Orientation.IOrientationProvider
    {
        public Task<Orientation.SubjectOrientation?> ResolveAsync(string propertyKey, string subjectId, CancellationToken ct) =>
            Task.FromResult<Orientation.SubjectOrientation?>(null);
    }

    /// <summary>
    /// Mirrors <see cref="Orientation.FixtureOrientationProvider"/>'s real behaviour: every
    /// resolution is stamped with the live wall-clock time, exactly like a real SightMap client
    /// would. This is what makes a naive "always re-snapshot and compare" fingerprint check
    /// defeat itself for any subject whose orientation actually resolves.
    /// </summary>
    private sealed class FakeChurningOrientationProvider : Orientation.IOrientationProvider
    {
        public Task<Orientation.SubjectOrientation?> ResolveAsync(string propertyKey, string subjectId, CancellationToken ct) =>
            Task.FromResult<Orientation.SubjectOrientation?>(
                new Orientation.SubjectOrientation(subjectId, 10.0, "north", "sightmap", 0.9, DateTimeOffset.UtcNow));
    }

    private sealed class FakeListingService : IListingService
    {
        public Task<ListingResponse> GetListingAsync(string listingId, CancellationToken ct) =>
            throw new ListingNotFoundException("not in fake");

        public Task<PhotoBytes?> GetPhotoAsync(string listingId, string photoId, int? width, CancellationToken ct) =>
            Task.FromResult<PhotoBytes?>(null);

        public Task<ListingEnvironment?> GetPropertyEnvironmentAsync(string propertyKey, CancellationToken ct) =>
            Task.FromResult<ListingEnvironment?>(null);
    }

    private static ScrapedPlan Plan(string rentalKey, string model, int beds = 1, double baths = 1, string? imageUrl = "img.png") =>
        new(rentalKey, model, $"att-{rentalKey}", imageUrl, beds, baths, 500, 600,
            [new ScrapedUnit($"{rentalKey}-1", 1, 500, 1500m)]);

    private static FloorPlanObservation DefaultObservation() => new(
        NotDeterminable: false,
        NotDeterminableReason: null,
        BoundaryFullyDrawn: true,
        Findings: [],
        Suggestions: [],
        Coverage: 0.9);

    /// <summary>
    /// Builds a fresh DI container wiring the same real services BackfillCommand runs against in
    /// production (SubjectService, AnalysisPipeline, EngineVersionService, PublicationService,
    /// InteractiveScoringDriver) on a throwaway SQLite file, with only the lens/plan-source edges
    /// faked. <c>services.AddSingleton&lt;IHarmonIQCommand, BackfillCommand&gt;</c> mirrors
    /// CommandsModule so a resolve-from-root check (matching how CommandRunner resolves it in
    /// Program.cs, i.e. with no ambient scope) is part of what this harness proves.
    /// </summary>
    private (ServiceProvider Provider, FakePlanSource PlanSource, IConfiguration Config) BuildProvider(
        IFloorPlanLens? lens = null, IConfiguration? config = null,
        Orientation.IOrientationProvider? orientationProvider = null)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"harmoniq-backfill-{Guid.NewGuid():N}.db");
        _dbPaths.Add(dbPath);

        config ??= new ConfigurationBuilder().Build();
        var planSource = new FakePlanSource();

        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(config);
        services.AddDbContext<HarmonIQDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

        services.AddSingleton<IObjectStore>(new MemoryObjectStore());
        services.AddSingleton<IEvidenceLoader, NullEvidenceLoader>();
        services.AddSingleton<IPlanSource>(planSource);
        services.AddSingleton<IPlanImageLoader, FakePlanImageLoader>();
        services.AddSingleton<Orientation.IOrientationProvider>(orientationProvider ?? new FakeOrientationProvider());
        services.AddSingleton<IListingService, FakeListingService>();

        services.AddSingleton<IFloorPlanLens>(lens ?? new StubLens(DefaultObservation()));
        services.AddSingleton(new MockAnalysisService(AppContext.BaseDirectory));
        services.AddSingleton(new ClaudeAnalysisService(new NullClaudeClient(), NullLogger<ClaudeAnalysisService>.Instance));
        services.AddSingleton<SiteAnalysisService>();
        services.AddSingleton<NumerologyService>();
        services.AddScoped<ReportBodyWriter>();
        services.AddScoped<IAnalysisPipeline>(sp => new AnalysisPipeline(
            sp.GetRequiredService<HarmonIQDbContext>(),
            sp.GetRequiredService<IFloorPlanLens>(),
            sp.GetRequiredService<ClaudeAnalysisService>(),
            sp.GetRequiredService<MockAnalysisService>(),
            sp.GetRequiredService<SiteAnalysisService>(),
            sp.GetRequiredService<NumerologyService>(),
            sp.GetRequiredService<ReportBodyWriter>(),
            sp.GetRequiredService<IEvidenceLoader>(),
            NullLogger<AnalysisPipeline>.Instance)
        {
            RetryDelay = TimeSpan.Zero,
        });
        services.AddScoped<ISubjectService, SubjectService>();
        services.AddScoped<IEngineVersionService, EngineVersionService>();
        services.AddScoped<IPublicationService, PublicationService>();
        services.AddScoped<IScoringDriver, InteractiveScoringDriver>();
        services.AddSingleton<IBatchScoringClient, StubBatchScoringClient>();
        services.AddSingleton<IHarmonIQCommand, BackfillCommand>();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<HarmonIQDbContext>().Database.EnsureCreated();

        return (provider, planSource, config);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(ServiceProvider provider, params string[] args)
    {
        var command = provider.GetRequiredService<IHarmonIQCommand>();
        var originalOut = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        int exitCode;
        try
        {
            exitCode = await command.RunAsync(args, CancellationToken.None);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
        return (exitCode, writer.ToString());
    }

    /// <summary>
    /// Verification queries run in their own scope, exactly like the command's own DB access
    /// does (BackfillCommand never resolves the scoped <see cref="HarmonIQDbContext"/> off the
    /// root provider passed by CommandRunner — resolving it here the same way is deliberate,
    /// not just convenient).
    /// </summary>
    private static IServiceScope OpenScope(ServiceProvider provider) => provider.CreateScope();

    private static HarmonIQDbContext Db(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<HarmonIQDbContext>();

    // ---------------------------------------------------------------- tests

    [Fact]
    public async Task EnqueuesOneScoringJobPerSubject()
    {
        var (provider, planSource, _) = BuildProvider();
        planSource["prop"] = [Plan("rk-1", "A"), Plan("rk-2", "B")];

        var (exitCode, output) = await RunAsync(provider, "--property", "prop", "--demo");

        Assert.Equal(0, exitCode);
        using var scope = OpenScope(provider);
        var db = Db(scope);
        var jobs = await db.ScoringJobs.ToListAsync();
        Assert.Equal(2, jobs.Count);
        Assert.Equal(["prop:rk-1", "prop:rk-2"], jobs.Select(j => j.SubjectId).Order());
        Assert.All(jobs, j => Assert.Equal("ok", j.Status));
        Assert.All(jobs, j => Assert.Equal(BackfillReasons.Backfill, j.Reason));
        Assert.Contains("2 enqueued, 2 ok, 0 skipped, 0 failed", output);
    }

    [Fact]
    public async Task UnchangedFingerprint_YieldsSkipped_WithZeroNewObservations()
    {
        // Two plans so the property takes the multi-plan/floor-plan evidence path (a single plan
        // is the property-photos path instead, design §5's discriminator) and actually reaches
        // the floor-plan lens this test is exercising.
        var (provider, planSource, _) = BuildProvider();
        planSource["prop"] = [Plan("rk-1", "A"), Plan("rk-2", "B")];

        var first = await RunAsync(provider, "--property", "prop", "--demo");
        Assert.Equal(0, first.ExitCode);

        int observationsAfterFirst;
        using (var scope = OpenScope(provider))
        {
            observationsAfterFirst = await Db(scope).Observations.CountAsync();
        }
        Assert.True(observationsAfterFirst > 0, "first run should have perceived at least one observation");

        var second = await RunAsync(provider, "--property", "prop", "--demo");

        Assert.Equal(0, second.ExitCode);
        Assert.Contains("2 enqueued, 0 ok, 2 skipped, 0 failed", second.Output);

        using var verify = OpenScope(provider);
        var db = Db(verify);
        Assert.Equal(observationsAfterFirst, await db.Observations.CountAsync());
        var jobs = await db.ScoringJobs.Where(j => j.SubjectId == "prop:rk-1").ToListAsync();
        Assert.Equal(2, jobs.Count);
        Assert.Contains(jobs, j => j.Status == "skipped");
        var skippedJob = jobs.Single(j => j.Status == "skipped");
        Assert.Equal(0, skippedJob.Attempts);
    }

    /// <summary>
    /// Regression: a naive "always take a fresh snapshot and compare fingerprints" check
    /// defeats itself for any subject whose orientation provider actually resolves, because
    /// every resolution is stamped with a live wall-clock <c>resolvedAt</c> (exactly what
    /// <c>FixtureOrientationProvider</c> and a real SightMap client both do) — the freshly-taken
    /// snapshot never matches the prior one, so the subject would be re-"perceived" forever. The
    /// default "backfill" reason must reuse the already-stored snapshot rather than re-resolving.
    /// </summary>
    [Fact]
    public async Task BackfillReason_StillSkipsOnASecondRun_EvenWhenOrientationResolvesWithALiveTimestampEveryCall()
    {
        var (provider, planSource, _) = BuildProvider(orientationProvider: new FakeChurningOrientationProvider());
        planSource["prop"] = [Plan("rk-1", "A"), Plan("rk-2", "B")];

        var first = await RunAsync(provider, "--property", "prop", "--demo");
        Assert.Equal(0, first.ExitCode);
        Assert.Contains("2 enqueued, 2 ok, 0 skipped, 0 failed", first.Output);

        var second = await RunAsync(provider, "--property", "prop", "--demo");

        Assert.Equal(0, second.ExitCode);
        Assert.Contains("2 enqueued, 0 ok, 2 skipped, 0 failed", second.Output);
    }

    [Fact]
    public async Task ForcedFailure_RetriesThreeTimes_ThenFailed_WithNoProjectionRow()
    {
        var throwing = new ThrowingLens();
        var (provider, planSource, _) = BuildProvider(throwing);
        planSource["prop"] = [Plan("rk-1", "A"), Plan("rk-2", "B")];

        var (exitCode, output) = await RunAsync(provider, "--property", "prop", "--demo", "--publish");

        Assert.Equal(1, exitCode);
        Assert.Equal(6, throwing.Calls); // AnalysisPipeline.MaxAttempts (3) x 2 floor-plan subjects

        using var scope = OpenScope(provider);
        var db = Db(scope);
        var job = await db.ScoringJobs.SingleAsync(j => j.SubjectId == "prop:rk-1");
        Assert.Equal("failed", job.Status);
        Assert.Equal(3, job.Attempts);

        var analyses = await db.Analyses.Where(a => a.SubjectId == "prop:rk-1").ToListAsync();
        Assert.NotEmpty(analyses);
        Assert.All(analyses, a => Assert.Equal(AnalysisStatuses.Failed, a.Status));
        Assert.All(analyses, a => Assert.Null(a.Score));

        Assert.Equal(0, await db.ProjectionRows.CountAsync());
        Assert.Contains("wrote 0 rows", output);
    }

    [Fact]
    public async Task LimitIsHonoured()
    {
        var (provider, planSource, _) = BuildProvider();
        planSource["prop"] = [Plan("rk-1", "A"), Plan("rk-2", "B"), Plan("rk-3", "C")];

        var (exitCode, output) = await RunAsync(provider, "--property", "prop", "--demo", "--limit", "2");

        Assert.Equal(0, exitCode);
        using var scope = OpenScope(provider);
        Assert.Equal(2, await Db(scope).ScoringJobs.CountAsync());
        Assert.Contains("2 enqueued", output);
    }

    [Fact]
    public async Task DemoPublish_WritesZeroProjectionRows_ButCompletesAndExplainsWhy()
    {
        var (provider, planSource, _) = BuildProvider();
        planSource["prop"] = [Plan("rk-1", "A"), Plan("rk-2", "B")];

        var (exitCode, output) = await RunAsync(provider, "--property", "prop", "--demo", "--publish");

        Assert.Equal(0, exitCode);
        using var scope = OpenScope(provider);
        var db = Db(scope);
        Assert.Equal(0, await db.ProjectionRows.CountAsync());
        Assert.Contains("wrote 0 rows", output);
        Assert.Contains("demo", output, StringComparison.OrdinalIgnoreCase);

        var engine = await db.EngineVersions.SingleAsync();
        Assert.NotNull(engine.PublishedAt); // the version still flips, atomically, with zero eligible rows
    }

    [Fact]
    public async Task DemoRun_RecordsExplicitZeroTokenAndCostFields_RatherThanLeavingThemNull()
    {
        var (provider, planSource, _) = BuildProvider();
        planSource["prop"] = [Plan("rk-1", "A"), Plan("rk-2", "B")];

        await RunAsync(provider, "--property", "prop", "--demo");

        using var scope = OpenScope(provider);
        var job = await Db(scope).ScoringJobs.SingleAsync(j => j.SubjectId == "prop:rk-1");
        Assert.NotNull(job.InputTokens);
        Assert.NotNull(job.OutputTokens);
        Assert.NotNull(job.CostUsd);
        Assert.Equal(0, job.InputTokens);
        Assert.Equal(0, job.OutputTokens);
        Assert.Equal(0.0, job.CostUsd);
    }

    [Fact]
    public async Task EngineUpgrade_RederivesWithoutAnyModelCall()
    {
        var lens = new StubLens(new FloorPlanObservation(
            false, null, true,
            [new LensFinding(FloorPlanRules.BathAdjacentKitchen, "Water Room Beside the Cooking Zone",
                "The bathroom shares a wall with the kitchen.", "both", 0.8, "moderate")],
            [], 0.9));
        var (provider, planSource, _) = BuildProvider(lens);
        planSource["prop"] = [Plan("rk-1", "A"), Plan("rk-2", "B")];

        var first = await RunAsync(provider, "--property", "prop", "--demo");
        Assert.Equal(0, first.ExitCode);
        Assert.Equal(2, lens.Calls); // one call per floor-plan subject

        int beforeAnalyses;
        using (var scope = OpenScope(provider))
        {
            beforeAnalyses = await Db(scope).Analyses.CountAsync();
        }
        Assert.True(beforeAnalyses > 0);

        var second = await RunAsync(provider, "--property", "prop", "--demo", "--reason", "engine_upgrade");

        Assert.Equal(0, second.ExitCode);
        // The bump did not change the engine identity (site/rules constants are fixed in this
        // harness), so RederiveAsync legitimately re-derives the *same* (subject, set,
        // rulesVersion) rows in place rather than adding new ones — the load-bearing assertion is
        // that no further lens call happened.
        Assert.Equal(2, lens.Calls);

        using var verify = OpenScope(provider);
        var job = await Db(verify).ScoringJobs.Where(j => j.Reason == BackfillReasons.EngineUpgrade).ToListAsync();
        Assert.Equal(2, job.Count);
        Assert.All(job, j => Assert.Contains(j.Status, new[] { "ok", "skipped" }));
    }

    [Fact]
    public void HelpListsBackfillCommand()
    {
        var (provider, _, _) = BuildProvider();
        var command = provider.GetRequiredService<IHarmonIQCommand>();

        Assert.Equal("backfill", command.Name);
        Assert.False(string.IsNullOrWhiteSpace(command.Description));
    }
}
