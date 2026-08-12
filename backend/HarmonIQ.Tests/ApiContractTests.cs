using System.IO.Compression;
using System.Text.Json;
using HarmonIQ.Api.Controllers;
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Persistence;
using HarmonIQ.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Orientation = HarmonIQ.Api.Services.Orientation;

namespace HarmonIQ.Tests;

/// <summary>
/// The v2 API surface against the real Enzo fixture (<c>349246f</c>), on SQLite, in demo mode
/// (no CLAUDE_API_KEY exists on the verification machine and none is needed).
///
/// The controllers are constructed directly rather than through <c>WebApplicationFactory</c>:
/// <c>Program.cs</c> uses top-level statements — its generated entry point is internal, and
/// exposing it would mean editing <c>HarmonIQ.Api.csproj</c>, which this task does not own. What
/// matters for a contract test is the response shape and status code, both of which an action
/// result carries faithfully; the routing itself is declared by attributes on the same methods.
/// </summary>
public class ApiContractTests : IDisposable
{
    private const string MultiPlan = "349246f";

    /// <summary>
    /// A scored plan that resolves a facing (it is in <c>sample-orientation.json</c>), so the
    /// with-orientation path is exercised.
    /// </summary>
    private const string ScoredPlanKey = "0xbkbx0";      // "Crane", 9 units
    private const string ScoredUnitNumber = "3350";

    /// <summary>A plan absent from the orientation fixture, so Vastu stays gated off.</summary>
    private const string UnorientedPlanKey = "ry5b9z1";  // "Olive", 6 units

    /// <summary>
    /// The plan whose image <see cref="ImagelessPlanSource"/> strips.
    ///
    /// Every real Enzo plan carries an image, so unlike the old invented fixture there is no
    /// naturally imageless plan to lean on. The condition is therefore created deliberately here
    /// rather than encoded into the fixture: the fixture's job is to be a faithful capture of the
    /// LDP, and edge-case coverage belongs in the test that needs it.
    /// </summary>
    private const string ImagelessPlanKey = "1n992v6";   // "Swallow", 1 unit

    private readonly string _dbPath;
    private readonly string _storeRoot;
    private readonly HarmonIQDbContext _db;
    private readonly SubjectsReadService _read;
    private readonly SubjectsController _subjects;
    private readonly GradesFeedController _feed;
    private readonly AnalysisController _analysis;
    private readonly IObjectStore _store;

    public ApiContractTests()
    {
        var apiRoot = ApiContentRoot();
        _dbPath = Path.Combine(Path.GetTempPath(), $"harmoniq-api-{Guid.NewGuid():N}.db");
        _storeRoot = Path.Combine(Path.GetTempPath(), $"harmoniq-api-store-{Guid.NewGuid():N}");

        _db = new HarmonIQDbContext(
            new DbContextOptionsBuilder<HarmonIQDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        _db.Database.EnsureCreated();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HARMONIQ_OBJECT_STORE"] = _storeRoot,
                ["Claude:Model"] = "claude-sonnet-5",
            })
            .Build();

        var env = new FakeEnv(apiRoot);
        var claude = new UnconfiguredClaudeClient();
        var sampleProvider = new SampleListingProvider(env);
        var planSource = new ImagelessPlanSource(sampleProvider, ImagelessPlanKey);
        var listings = new FixtureListingService(sampleProvider);
        var mock = new MockAnalysisService(env);
        var numerology = new NumerologyService();
        var site = new SiteAnalysisService();
        _store = new FileSystemObjectStore(config);

        var subjectService = new SubjectService(
            _db, planSource, new FilePlanImageLoader(env),
            new Orientation.FixtureOrientationProvider(Path.Combine(apiRoot, "Data", "sample-orientation.json")),
            listings, config, NullLogger<SubjectService>.Instance);

        var pipeline = new AnalysisPipeline(
            _db,
            new FloorPlanLensService(claude, mock, NullLogger<FloorPlanLensService>.Instance),
            new ClaudeAnalysisService(claude, NullLogger<ClaudeAnalysisService>.Instance),
            mock, site, numerology, new ReportBodyWriter(_store),
            new FileEvidenceLoader(env), NullLogger<AnalysisPipeline>.Instance)
        { RetryDelay = TimeSpan.Zero };

        var engineVersions = new EngineVersionService(_db, config);

        _read = new SubjectsReadService(
            _db, subjectService, pipeline, engineVersions, planSource, listings, numerology, claude,
            NullLogger<SubjectsReadService>.Instance);

        _subjects = WithHttpContext(new SubjectsController(_read, _db, _store));
        _feed = WithHttpContext(new GradesFeedController(_read, new PublicationService(_db)));
        _analysis = WithHttpContext(new AnalysisController(
            _db, _read, site, numerology, NullLogger<AnalysisController>.Instance));
    }

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        if (Directory.Exists(_storeRoot)) Directory.Delete(_storeRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    // ---------------------------------------------------------------- bulk subjects

    [Fact]
    public async Task BulkSubjects_ReturnsEverySubject_WithExactlyOneCarryingNoSets()
    {
        var body = await Subjects();

        Assert.Equal(MultiPlan, body.PropertyKey);
        Assert.Equal("demo", body.Mode);
        Assert.Equal(20, body.Subjects.Count);

        var unscored = body.Subjects.Where(s => s.Sets.Count == 0).ToList();
        var only = Assert.Single(unscored);
        Assert.Equal(ImagelessPlanKey, only.PlanKey);

        // The imageless plan is still present, with its identity intact, so the section's
        // footprint is known at first paint and nothing shifts when the grades land.
        Assert.Equal("floorplan", only.SubjectType);
        Assert.Equal("Swallow", only.PlanName);
    }

    [Fact]
    public async Task VastuWithoutOrientation_IsInsufficientEvidence_NeverALowGrade()
    {
        var body = await Subjects();

        var withoutOrientation = body.Subjects
            .SelectMany(s => s.Sets)
            .Where(g => g.PrincipleSet == PrincipleSets.Vastu && g.OrientationPath == Cohort.Without)
            .ToList();

        Assert.NotEmpty(withoutOrientation);
        Assert.All(withoutOrientation, g =>
        {
            Assert.Equal(AnalysisStatuses.InsufficientEvidence, g.Status);
            Assert.Null(g.Score);
            Assert.Null(g.Grade);
        });
    }

    [Fact]
    public async Task EveryServedGrade_CarriesItsCohort_SoFilteringStaysWithinCohort()
    {
        var body = await Subjects();
        var sets = body.Subjects.SelectMany(s => s.Sets).ToList();

        Assert.NotEmpty(sets);
        Assert.All(sets, g =>
        {
            Assert.Contains(g.EvidencePath, new[] { Cohort.Photos, Cohort.FloorPlan });
            Assert.Contains(g.OrientationPath, new[] { Cohort.With, Cohort.Without });
            Assert.InRange(g.Confidence, 0.0, 1.0);
        });
    }

    [Fact]
    public async Task NullScore_SerializesAsAbsent_NeverAsZeroOrF()
    {
        var body = await Subjects();
        var insufficient = body.Subjects
            .SelectMany(s => s.Sets)
            .First(g => g.Status == AnalysisStatuses.InsufficientEvidence);

        var json = JsonSerializer.Serialize(insufficient, ApiJson);

        Assert.DoesNotContain("\"score\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"grade\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"F\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Units_ArePresent_AndCarryNoGradeField()
    {
        var body = await Subjects();
        var plan = body.Subjects.First(s => s.PlanKey == ScoredPlanKey);

        Assert.NotEmpty(plan.Units);
        Assert.Contains(plan.Units, u => u.UnitNumber == ScoredUnitNumber);

        var json = JsonSerializer.Serialize(plan.Units[0], ApiJson);
        Assert.DoesNotContain("\"grade\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"score\"", json, StringComparison.Ordinal);

        // Read-time only: no unit ever becomes a subject, and nothing about it is stored.
        Assert.DoesNotContain(await _db.Subjects.ToListAsync(), s => s.Id.Contains(ScoredUnitNumber, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImagelessPlan_HasNoUnitsSuppressed_ButStillNoSets()
    {
        var body = await Subjects();
        var imageless = body.Subjects.First(s => s.PlanKey == ImagelessPlanKey);

        // Unscored is about the grade, not about the plan: its numerology annotations still render.
        Assert.NotEmpty(imageless.Units);
        Assert.Empty(imageless.Sets);
    }

    [Fact]
    public async Task SetsFilter_NarrowsToTheRequestedTradition()
    {
        var body = await Subjects(sets: "fengshui");

        Assert.All(body.Subjects.SelectMany(s => s.Sets), g =>
            Assert.Equal(PrincipleSets.FengShui, g.PrincipleSet));
        Assert.All(body.Subjects.SelectMany(s => s.Units), u =>
            Assert.Equal(PrincipleSets.FengShui, u.PrincipleSet));
    }

    [Fact]
    public async Task UnknownProperty_Is404_AndCreatesNoSubjectRows()
    {
        var result = await _subjects.GetSubjects("not-a-property", null, null, default);

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Empty(await _db.Subjects.Where(s => s.PropertyKey == "not-a-property").ToListAsync());
    }

    [Fact]
    public async Task PinnedToAnotherEngineVersion_SeesNoRowsFromThisOne()
    {
        await Subjects(); // score everything under the current version

        var other = new EngineVersion
        {
            Version = "deadbeefcafe",
            RulesVersionFengshui = "fengshui-9.9",
            RulesVersionVastu = "vastu-9.9",
            PromptVersion = "p-9",
            ModelId = "model-9",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.EngineVersions.Add(other);
        await _db.SaveChangesAsync();

        var body = await Subjects(engineVersion: other.Version);

        Assert.Equal(other.Version, body.EngineVersion);
        Assert.All(body.Subjects, s => Assert.Empty(s.Sets));
    }

    [Fact]
    public async Task UnknownEngineVersion_Is404_NeverSilentlySubstituted()
    {
        var result = await _subjects.GetSubjects(MultiPlan, "no-such-version", null, default);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ---------------------------------------------------------------- report body

    [Fact]
    public async Task Report_404sForAnUnscoredSubject()
    {
        var body = await Subjects();
        var imageless = body.Subjects.First(s => s.PlanKey == ImagelessPlanKey);

        var result = await _subjects.GetReport(
            imageless.SubjectId, PrincipleSets.FengShui, body.EngineVersion, default);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Report_ReturnsGzippedBody_WithLongCacheControl()
    {
        var body = await Subjects();
        var scored = body.Subjects.First(s => s.PlanKey == ScoredPlanKey);
        _subjects.Request.Headers.AcceptEncoding = "gzip";

        var result = Assert.IsType<FileContentResult>(
            await _subjects.GetReport(scored.SubjectId, PrincipleSets.FengShui, body.EngineVersion, default));

        Assert.Equal("gzip", _subjects.Response.Headers.ContentEncoding);
        Assert.Contains("max-age=", _subjects.Response.Headers.CacheControl.ToString(), StringComparison.Ordinal);

        var report = Inflate(result.FileContents);
        Assert.Equal(scored.SubjectId, report.SubjectId);
        Assert.Equal(PrincipleSets.FengShui, report.PrincipleSet);
    }

    [Fact]
    public async Task Report_ForInsufficientEvidence_ExplainsItself_AndCarriesNoScore()
    {
        var body = await Subjects();
        var gated = body.Subjects.First(s =>
            s.Sets.Any(g => g.PrincipleSet == PrincipleSets.Vastu
                         && g.Status == AnalysisStatuses.InsufficientEvidence));

        var result = Assert.IsType<FileContentResult>(
            await _subjects.GetReport(gated.SubjectId, PrincipleSets.Vastu, body.EngineVersion, default));

        var report = Inflate(result.FileContents, gzipped: false);
        Assert.Equal(AnalysisStatuses.InsufficientEvidence, report.Status);
        Assert.Null(report.Score);
        Assert.Null(report.Grade);
        Assert.False(string.IsNullOrWhiteSpace(report.Summary));
    }

    [Fact]
    public async Task Report_RejectsAnUnknownPrincipleSet()
    {
        var body = await Subjects();
        var result = await _subjects.GetReport(body.Subjects[0].SubjectId, "both", body.EngineVersion, default);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ---------------------------------------------------------------- grades feed

    [Fact]
    public async Task Feed_409sOnAnUnpublishedVersion()
    {
        await Subjects();

        var result = await _feed.GetFeed(null, null, 5, includeUnpublished: false, default);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Feed_200sWithIncludeUnpublished_AndCarriesTheRequestedVersion()
    {
        var subjects = await Subjects();

        var result = Assert.IsType<OkObjectResult>(
            await _feed.GetFeed(null, null, 5, includeUnpublished: true, default));
        var page = Assert.IsType<GradesFeedPage>(result.Value);

        Assert.Equal(subjects.EngineVersion, page.EngineVersion);
        // Demo output is never published: the projection is empty, which is the correct answer,
        // not an error.
        Assert.Empty(page.Rows);
    }

    [Fact]
    public async Task Feed_ServesOnlyTheRequestedVersionsRows()
    {
        var subjects = await Subjects();
        var engine = await _db.EngineVersions.FirstAsync(e => e.Version == subjects.EngineVersion);
        engine.PublishedAt = DateTimeOffset.UtcNow;

        _db.ProjectionRows.Add(Row(subjects.EngineVersion, "a"));
        _db.ProjectionRows.Add(Row("some-other-version", "b"));
        await _db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(
            await _feed.GetFeed(subjects.EngineVersion, null, 50, includeUnpublished: false, default));
        var page = Assert.IsType<GradesFeedPage>(result.Value);

        Assert.All(page.Rows, r => Assert.Equal(subjects.EngineVersion, r.EngineVersion));
        Assert.Single(page.Rows);
    }

    [Fact]
    public async Task Feed_404sOnAnUnknownVersion()
    {
        var result = await _feed.GetFeed("no-such-version", null, 5, includeUnpublished: true, default);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ---------------------------------------------------------------- session-only refine

    [Fact]
    public async Task Refine_ReturnsPersistedFalse_AndWritesNothing()
    {
        var body = await Subjects();
        var subject = body.Subjects.First(s => s.PlanKey == UnorientedPlanKey);
        var before = await CountRowsAsync();

        var result = Assert.IsType<OkObjectResult>(await _analysis.Refine(
            new RefineRequest(subject.SubjectId, PrincipleSets.Vastu, "north"), default));
        var refined = Assert.IsType<RefineResponse>(result.Value);

        Assert.False(refined.Persisted);
        Assert.Equal(await CountRowsAsync(), before);
    }

    [Fact]
    public async Task Refine_WithRenterOrientation_ScoresVastuThatTheStoredGradeCannot()
    {
        var body = await Subjects();
        var subject = body.Subjects.First(s => s.PlanKey == UnorientedPlanKey);

        var stored = subject.Sets.Single(g => g.PrincipleSet == PrincipleSets.Vastu);
        Assert.Equal(AnalysisStatuses.InsufficientEvidence, stored.Status);

        var result = Assert.IsType<OkObjectResult>(await _analysis.Refine(
            new RefineRequest(subject.SubjectId, PrincipleSets.Vastu, "north"), default));
        var refined = Assert.IsType<RefineResponse>(result.Value);

        Assert.Equal(Cohort.With, refined.Score.Cohort.OrientationPath);
        Assert.Contains("not part of the published grade", refined.Notice, StringComparison.Ordinal);
        Assert.Contains("session-only", refined.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refine_NeverPublishesTheSessionScore_TheStoredRowIsUntouched()
    {
        var body = await Subjects();
        var subject = body.Subjects.First(s => s.PlanKey == UnorientedPlanKey);

        await _analysis.Refine(new RefineRequest(subject.SubjectId, PrincipleSets.Vastu, "north"), default);

        var after = await Subjects();
        var stored = after.Subjects.First(s => s.PlanKey == UnorientedPlanKey)
            .Sets.Single(g => g.PrincipleSet == PrincipleSets.Vastu);

        Assert.Equal(AnalysisStatuses.InsufficientEvidence, stored.Status);
        Assert.Null(stored.Score);
        Assert.Equal(Cohort.Without, stored.OrientationPath);
    }

    [Fact]
    public async Task Refine_IsDeterministic_SameInputSameScore()
    {
        var body = await Subjects();
        var subject = body.Subjects.First(s => s.PlanKey == UnorientedPlanKey);
        var request = new RefineRequest(subject.SubjectId, PrincipleSets.FengShui, "southeast");

        var first = Assert.IsType<RefineResponse>(
            Assert.IsType<OkObjectResult>(await _analysis.Refine(request, default)).Value);
        var second = Assert.IsType<RefineResponse>(
            Assert.IsType<OkObjectResult>(await _analysis.Refine(request, default)).Value);

        Assert.Equal(first.Score.Score, second.Score.Score);
        Assert.Equal(first.Score.Status, second.Score.Status);
    }

    [Fact]
    public async Task Refine_RejectsABlendedPrincipleSet_AndABadOrientation()
    {
        var body = await Subjects();
        var subjectId = body.Subjects[0].SubjectId;

        Assert.IsType<BadRequestObjectResult>(
            await _analysis.Refine(new RefineRequest(subjectId, "both"), default));
        Assert.IsType<BadRequestObjectResult>(
            await _analysis.Refine(new RefineRequest(subjectId, PrincipleSets.Vastu, "up"), default));
    }

    [Fact]
    public async Task Refine_404sForAnUnknownSubject()
    {
        var result = await _analysis.Refine(new RefineRequest("nope", PrincipleSets.FengShui), default);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ---------------------------------------------------------------- harness

    private static readonly JsonSerializerOptions ApiJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private async Task<SubjectsResponse> Subjects(string? engineVersion = null, string? sets = null)
    {
        var result = Assert.IsType<OkObjectResult>(
            await _subjects.GetSubjects(MultiPlan, engineVersion, sets, default));
        return Assert.IsType<SubjectsResponse>(result.Value);
    }

    private async Task<int> CountRowsAsync() =>
        await _db.Subjects.CountAsync()
        + await _db.InputSets.CountAsync()
        + await _db.Observations.CountAsync()
        + await _db.Analyses.CountAsync()
        + await _db.ScoringJobs.CountAsync()
        + await _db.ProjectionRows.CountAsync();

    private static ProjectionRow Row(string engineVersion, string id) => new()
    {
        Id = $"{engineVersion}:{id}",
        ListingId = MultiPlan,
        FloorPlanId = id,
        PrincipleSet = PrincipleSets.FengShui,
        Score = 80,
        Grade = "B+",
        Cohort = "floorplan/without",
        Confidence = 0.8,
        EngineVersion = engineVersion,
        ComputedAt = DateTimeOffset.UtcNow,
    };

    private static ReportBody Inflate(byte[] bytes, bool gzipped = true)
    {
        if (!gzipped)
        {
            return JsonSerializer.Deserialize<ReportBody>(bytes, HarmonIQ.Api.Models.Json.Options)!;
        }
        using var source = new MemoryStream(bytes);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var plain = new MemoryStream();
        gzip.CopyTo(plain);
        return JsonSerializer.Deserialize<ReportBody>(plain.ToArray(), HarmonIQ.Api.Models.Json.Options)!;
    }

    private static T WithHttpContext<T>(T controller) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    /// <summary>Walks up from the test binary to the repo root, then into the API project.</summary>
    private static string ApiContentRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "backend", "HarmonIQ.Api");
            if (Directory.Exists(Path.Combine(candidate, "Data"))) return candidate;
        }
        throw new DirectoryNotFoundException("Could not locate the HarmonIQ.Api content root from the test binary.");
    }

    private sealed class FakeEnv(string contentRoot) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.Combine(contentRoot, "wwwroot");
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "HarmonIQ.Api";
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRoot);
        public string ContentRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = "Development";
    }

    /// <summary>
    /// The real Enzo fixture with exactly one plan's image removed.
    ///
    /// <see cref="SubjectService"/> re-reads <c>PlanImageUrl</c> from the plan source on every
    /// materialization, so an imageless subject cannot be arranged by editing the database — it
    /// has to come from the source. Wrapping the provider keeps the fixture itself an honest
    /// capture of the LDP while still exercising the unscored-subject contract.
    /// </summary>
    private sealed class ImagelessPlanSource(IPlanSource inner, string planKey) : IPlanSource
    {
        public async Task<IReadOnlyList<ScrapedPlan>?> GetPlansAsync(string propertyKey, CancellationToken ct)
        {
            var plans = await inner.GetPlansAsync(propertyKey, ct);
            return plans?.Select(p => p.RentalKey == planKey
                ? p with { AttachmentId = null, PlanImageUrl = null }
                : p).ToList();
        }
    }

    private sealed class UnconfiguredClaudeClient : IClaudeClient
    {
        public bool IsConfigured => false;
        public string Model => "unconfigured";
        public Task<JsonElement> MessagesAsync(object payload, CancellationToken ct = default) =>
            throw new ClaudeUnavailableException("no key on this machine");
    }

    /// <summary>
    /// The offline listing surface: the single-listing fixture for "tk93cec", not-found for
    /// anything else (including the multi-plan property, whose environment resolves to unknown
    /// without a network call).
    /// </summary>
    private sealed class FixtureListingService(SampleListingProvider sample) : IListingService
    {
        public Task<ListingResponse> GetListingAsync(string listingId, CancellationToken ct) =>
            listingId == SampleListingProvider.ListingId
                ? Task.FromResult(sample.GetListing())
                : throw new ListingNotFoundException($"Listing '{listingId}' not found.");

        public Task<PhotoBytes?> GetPhotoAsync(string listingId, string photoId, int? width, CancellationToken ct)
        {
            var path = sample.GetPhotoPath(photoId);
            return Task.FromResult<PhotoBytes?>(
                path is null || !File.Exists(path) ? null : new PhotoBytes(File.ReadAllBytes(path), "image/jpeg"));
        }

        public Task<ListingEnvironment?> GetPropertyEnvironmentAsync(string propertyKey, CancellationToken ct) =>
            Task.FromResult<ListingEnvironment?>(
                propertyKey == SampleListingProvider.MultiplanPropertyKey
                    ? sample.GetMultiplanEnvironment()
                    : null);
    }
}
