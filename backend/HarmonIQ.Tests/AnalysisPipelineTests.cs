using System.Text.Json;
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Persistence;
using HarmonIQ.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HarmonIQ.Tests;

/// <summary>
/// The perception/judgment split end to end, on SQLite, in demo mode (no CLAUDE_API_KEY exists on
/// the verification machine and none is needed — that is the point of the split).
/// </summary>
public class AnalysisPipelineTests : IDisposable
{
    private readonly string _dbPath;

    public AnalysisPipelineTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"harmoniq-pipeline-{Guid.NewGuid():N}.db");
        using var ctx = NewContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }

    // ---------------------------------------------------------------- harness

    private HarmonIQDbContext NewContext() =>
        new(new DbContextOptionsBuilder<HarmonIQDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

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
        public Task<byte[]?> LoadAsync(EvidenceRef reference, CancellationToken ct) =>
            Task.FromResult<byte[]?>(null);
    }

    /// <summary>A lens that returns a canned observation, and counts how many times it was asked.</summary>
    private sealed class StubLens(FloorPlanObservation observation) : IFloorPlanLens
    {
        public int Calls { get; private set; }

        public Task<FloorPlanObservation> ReadAsync(Subject subject, byte[] planImage, bool live, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(FloorPlanLensService.Sanitize(observation));
        }
    }

    /// <summary>A lens that fails every time — the retries-exhausted / never-called guard.</summary>
    private sealed class ThrowingLens : IFloorPlanLens
    {
        public int Calls { get; private set; }

        public Task<FloorPlanObservation> ReadAsync(Subject subject, byte[] planImage, bool live, CancellationToken ct)
        {
            Calls++;
            throw new InvalidOperationException("the lens must not be called on this path");
        }
    }

    private sealed class NullClaudeClient : IClaudeClient
    {
        public bool IsConfigured => false;
        public string Model => "unconfigured";
        public Task<JsonElement> MessagesAsync(object payload, CancellationToken ct = default) =>
            throw new ClaudeUnavailableException("no key on this machine");
    }

    private (AnalysisPipeline Pipeline, HarmonIQDbContext Db, MemoryObjectStore Store) NewPipeline(
        IFloorPlanLens? lens = null)
    {
        var db = NewContext();
        var mock = new MockAnalysisService(AppContext.BaseDirectory);
        var store = new MemoryObjectStore();
        var pipeline = new AnalysisPipeline(
            db,
            lens ?? new FloorPlanLensService(new NullClaudeClient(), mock, NullLogger<FloorPlanLensService>.Instance),
            new ClaudeAnalysisService(new NullClaudeClient(), NullLogger<ClaudeAnalysisService>.Instance),
            mock,
            new SiteAnalysisService(),
            new NumerologyService(),
            new ReportBodyWriter(store),
            new NullEvidenceLoader(),
            NullLogger<AnalysisPipeline>.Instance)
        {
            RetryDelay = TimeSpan.Zero,
        };
        return (pipeline, db, store);
    }

    private static EngineVersion Engine(string version = "e1", string fengshui = "fengshui-2.0", string vastu = "vastu-2.0") => new()
    {
        Version = version,
        RulesVersionFengshui = fengshui,
        RulesVersionVastu = vastu,
        PromptVersion = Prompts.PromptVersion,
        ModelId = "claude-sonnet-5",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Subject PlanSubject(string rentalKey, bool imaged = true) => new()
    {
        Id = $"sample-multiplan:{rentalKey}",
        PropertyKey = "sample-multiplan",
        SubjectType = "floorplan",
        ExternalPlanKey = rentalKey,
        PlanName = rentalKey,
        Beds = 1,
        Baths = 1,
        PlanImageUrl = imaged ? $"sample-plans/plan-{rentalKey}.png" : null,
        PlanImageHash = imaged ? $"phash-{rentalKey}" : null,
        CreatedAt = DateTimeOffset.UtcNow,
        LastSeenAt = DateTimeOffset.UtcNow,
    };

    // A fully known environment, so the site lens has real coverage (an all-unknown environment
    // legitimately drops the whole subject below the confidence floor).
    private static readonly ListingEnvironment KnownEnvironment = new(
        North: new SideEnvironment("quiet", "none", "similar", "level"),
        East: new SideEnvironment("busy", "river", "open", "falls"),
        South: new SideEnvironment("none", "none", "taller-building", "rises"),
        West: new SideEnvironment("quiet", "none", "similar", "level"));

    private static InputSet PlanInputSet(Subject subject, bool withOrientation, string? numbersJson = null) => new()
    {
        Id = $"is-{subject.Id}-{(withOrientation ? "o" : "n")}",
        SubjectId = subject.Id,
        EvidencePath = Cohort.FloorPlan,
        EvidenceHashesJson = JsonSerializer.Serialize(new[]
        {
            new { hash = subject.PlanImageHash, kind = "plan", label = subject.ExternalPlanKey, source = subject.PlanImageUrl },
        }, Json.Options),
        EnvironmentJson = JsonSerializer.Serialize(KnownEnvironment, Json.Options),
        OrientationJson = withOrientation
            ? JsonSerializer.Serialize(new
            {
                subjectId = subject.Id,
                facingDegrees = 90.0,
                cardinal = "east",
                source = "sightmap",
                confidence = 0.9,
                resolvedAt = DateTimeOffset.UtcNow,
            }, Json.Options)
            : null,
        NumbersJson = numbersJson,
        InputFingerprint = $"fp-{subject.Id}-{(withOrientation ? "o" : "n")}",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private async Task<(Subject Subject, InputSet InputSet)> SeedAsync(
        HarmonIQDbContext db, string rentalKey, bool imaged = true, bool withOrientation = true)
    {
        var subject = PlanSubject(rentalKey, imaged);
        var inputSet = PlanInputSet(subject, withOrientation);
        db.Subjects.Add(subject);
        db.InputSets.Add(inputSet);
        await db.SaveChangesAsync();
        return (subject, inputSet);
    }

    // ---------------------------------------------------------------- tests

    [Fact]
    public async Task FloorPlanPath_WritesOneRowPerPrincipleSet_ForAnImagedPlan()
    {
        var (pipeline, db, store) = NewPipeline();
        var (subject, inputSet) = await SeedAsync(db, "rk-101");

        var analyses = await pipeline.RunAsync(subject, inputSet, Engine(), live: false, default);

        Assert.Equal(2, analyses.Count);
        Assert.Equal(
            new[] { PrincipleSets.FengShui, PrincipleSets.Vastu }.Order(),
            analyses.Select(a => a.PrincipleSet).Order());
        Assert.All(analyses, a => Assert.Equal("demo", a.Mode));
        Assert.All(analyses, a => Assert.Equal(Cohort.FloorPlan, a.CohortEvidencePath));
        Assert.All(analyses, a => Assert.Equal("fp-sample-multiplan:rk-101-o", a.InputFingerprint));

        // Report bodies were written under the fixed key convention.
        Assert.Equal(2, store.Items.Count);
        Assert.Contains("reports/e1/sample-multiplan:rk-101/fengshui.json.gz", store.Items.Keys);
        Assert.All(analyses, a => Assert.False(string.IsNullOrWhiteSpace(a.ReportSha256)));

        // ONE tradition-agnostic observation serves BOTH sets.
        Assert.Equal(1, await db.Observations.CountAsync());
        Assert.Equal("ok", await db.ScoringJobs.Where(j => j.SubjectId == subject.Id).Select(j => j.Status).SingleAsync());
    }

    [Fact]
    public async Task VastuIsUnscored_WithoutOrientation_AndScored_WhenTheFixtureResolvesOne()
    {
        var (pipeline, db, _) = NewPipeline();

        var without = PlanSubject("rk-101");
        var withOne = PlanSubject("rk-103");
        db.Subjects.AddRange(without, withOne);
        var withoutSet = PlanInputSet(without, withOrientation: false);
        var withSet = PlanInputSet(withOne, withOrientation: true);
        db.InputSets.AddRange(withoutSet, withSet);
        await db.SaveChangesAsync();

        var unoriented = await pipeline.RunAsync(without, withoutSet, Engine(), live: false, default);
        var oriented = await pipeline.RunAsync(withOne, withSet, Engine(), live: false, default);

        var unorientedVastu = unoriented.Single(a => a.PrincipleSet == PrincipleSets.Vastu);
        Assert.Equal(AnalysisStatuses.InsufficientEvidence, unorientedVastu.Status);
        Assert.Null(unorientedVastu.Score);
        Assert.Null(unorientedVastu.Grade);
        Assert.Equal(Cohort.Without, unorientedVastu.CohortOrientationPath);

        var orientedVastu = oriented.Single(a => a.PrincipleSet == PrincipleSets.Vastu);
        Assert.Equal(AnalysisStatuses.Ok, orientedVastu.Status);
        Assert.NotNull(orientedVastu.Score);
        Assert.NotNull(orientedVastu.Grade);
        Assert.Equal(Cohort.With, orientedVastu.CohortOrientationPath);
    }

    [Fact]
    public async Task PlanWithNoImage_IsNotScoredAtAll()
    {
        var (pipeline, db, store) = NewPipeline();
        var (subject, inputSet) = await SeedAsync(db, "rk-105", imaged: false);

        var analyses = await pipeline.RunAsync(subject, inputSet, Engine(), live: false, default);

        Assert.Empty(analyses);
        Assert.Empty(store.Items);
        Assert.Equal(0, await db.Analyses.CountAsync());
        Assert.Equal(0, await db.Observations.CountAsync());
        Assert.Equal("skipped", await db.ScoringJobs.Where(j => j.SubjectId == subject.Id).Select(j => j.Status).SingleAsync());
    }

    [Fact]
    public async Task NotDeterminableLens_YieldsInsufficientEvidence_NotALowScore()
    {
        var declined = new FloorPlanObservation(
            NotDeterminable: true,
            NotDeterminableReason: "The drawing is a marketing render without a readable unit boundary.",
            BoundaryFullyDrawn: false,
            Findings: [],
            Suggestions: [],
            Coverage: 0);

        var (pipeline, db, _) = NewPipeline(new StubLens(declined));
        var (subject, inputSet) = await SeedAsync(db, "rk-102");

        var analyses = await pipeline.RunAsync(subject, inputSet, Engine(), live: false, default);

        Assert.All(analyses, a =>
        {
            Assert.Equal(AnalysisStatuses.InsufficientEvidence, a.Status);
            Assert.Null(a.Score);
            Assert.Null(a.Grade);
        });
        Assert.All(analyses, a => Assert.Equal(0.0, a.InteriorsCoverage));
    }

    [Fact]
    public async Task Observations_AreReusedOnASecondRun_WithAnUnchangedFingerprint()
    {
        var lens = new StubLens(new FloorPlanObservation(
            false, null, true,
            [new LensFinding(FloorPlanRules.EntryToRearStraightLine, "Entry to Rear Sightline",
                "The entry lines up with the rear opening.", "fengshui", 0.9, "major")],
            [], 0.85));

        var (pipeline, db, _) = NewPipeline(lens);
        var (subject, inputSet) = await SeedAsync(db, "rk-104");

        await pipeline.RunAsync(subject, inputSet, Engine(), live: false, default);
        Assert.Equal(1, await db.Observations.CountAsync());
        Assert.Equal(1, lens.Calls);

        await pipeline.RunAsync(subject, inputSet, Engine(), live: false, default);

        Assert.Equal(1, await db.Observations.CountAsync());
        Assert.Equal(1, lens.Calls);
        Assert.Equal(2, await db.Analyses.CountAsync());
    }

    [Fact]
    public async Task EngineBump_RederivesEveryRowFromStoredObservations_WithNoModelCall()
    {
        var lens = new StubLens(new FloorPlanObservation(
            false, null, true,
            [new LensFinding(FloorPlanRules.BathAdjacentKitchen, "Water Room Beside the Cooking Zone",
                "The bathroom shares a wall with the kitchen.", "both", 0.8, "moderate")],
            [], 0.9));

        var (pipeline, db, _) = NewPipeline(lens);
        var (subject, inputSet) = await SeedAsync(db, "rk-103");

        await pipeline.RunAsync(subject, inputSet, Engine("e1"), live: false, default);
        var before = await db.Analyses.AsNoTracking().ToListAsync();
        Assert.Equal(2, before.Count);

        // Bump the engine AND both rules versions, then re-derive with a lens that throws if asked.
        var throwing = new ThrowingLens();
        var bumpedPipeline = new AnalysisPipeline(
            db, throwing,
            new ClaudeAnalysisService(new NullClaudeClient(), NullLogger<ClaudeAnalysisService>.Instance),
            new MockAnalysisService(AppContext.BaseDirectory),
            new SiteAnalysisService(), new NumerologyService(),
            new ReportBodyWriter(new MemoryObjectStore()), new NullEvidenceLoader(),
            NullLogger<AnalysisPipeline>.Instance);

        var bumped = Engine("e2", "fengshui-2.1", "vastu-2.1");
        var rederived = await bumpedPipeline.RederiveAsync(subject, inputSet, bumped, default);

        Assert.Equal(0, throwing.Calls);
        Assert.Equal(2, rederived.Count);
        Assert.All(rederived, a => Assert.Equal("e2", a.EngineVersion));
        Assert.Contains(rederived, a => a.RulesVersion == "fengshui-2.1");
        Assert.Contains(rederived, a => a.RulesVersion == "vastu-2.1");

        // New (subject, set, rulesVersion) rows alongside the originals; still one observation.
        Assert.Equal(4, await db.Analyses.CountAsync());
        Assert.Equal(1, await db.Observations.CountAsync());
    }

    [Fact]
    public async Task ElementBalanceJson_IsNullOnEveryRowOfTheFloorPlanPath()
    {
        var (pipeline, db, _) = NewPipeline();
        var (subject, inputSet) = await SeedAsync(db, "rk-101");

        var analyses = await pipeline.RunAsync(subject, inputSet, Engine(), live: false, default);

        // A line drawing has no materials: never five zero bars, and never a Vastu element balance.
        Assert.All(analyses, a => Assert.Null(a.ElementBalanceJson));
        Assert.Null(analyses.Single(a => a.PrincipleSet == PrincipleSets.Vastu).ElementBalanceJson);
    }

    [Fact]
    public async Task PhotoPath_ScoresRoomObservations_AndKeepsElementBalanceOffVastu()
    {
        var (pipeline, db, _) = NewPipeline();

        var subject = new Subject
        {
            Id = "sample",
            PropertyKey = "sample",
            SubjectType = "property",
            CreatedAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
        };
        var inputSet = new InputSet
        {
            Id = "is-sample",
            SubjectId = subject.Id,
            EvidencePath = Cohort.Photos,
            EvidenceHashesJson = JsonSerializer.Serialize(new[]
            {
                new { hash = "h-bed", kind = "photo", label = "photo-1", roomType = "bedroom" },
                new { hash = "h-kit", kind = "photo", label = "photo-2", roomType = "kitchen" },
            }, Json.Options),
            EnvironmentJson = JsonSerializer.Serialize(KnownEnvironment, Json.Options),
            OrientationJson = JsonSerializer.Serialize(new
            {
                subjectId = "sample", facingDegrees = 0.0, cardinal = "north",
                source = "annotation", confidence = 0.8, resolvedAt = DateTimeOffset.UtcNow,
            }, Json.Options),
            NumbersJson = JsonSerializer.Serialize(new ListingNumbers("404", 4, "900"), Json.Options),
            InputFingerprint = "fp-sample",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Subjects.Add(subject);
        db.InputSets.Add(inputSet);
        await db.SaveChangesAsync();

        var analyses = await pipeline.RunAsync(subject, inputSet, Engine(), live: false, default);

        Assert.Equal(2, analyses.Count);
        Assert.Equal(2, await db.Observations.CountAsync()); // one call per photo, not per set
        Assert.All(analyses, a => Assert.Equal(Cohort.Photos, a.CohortEvidencePath));

        var fengshui = analyses.Single(a => a.PrincipleSet == PrincipleSets.FengShui);
        var vastu = analyses.Single(a => a.PrincipleSet == PrincipleSets.Vastu);
        Assert.NotNull(fengshui.ElementBalanceJson);
        Assert.Null(vastu.ElementBalanceJson);

        // Numerology nudges, never drives: unit 404 reads as inauspicious in both traditions.
        Assert.NotNull(fengshui.NumerologyAdjustment);
        Assert.True(fengshui.NumerologyAdjustment < 0);
    }

    [Fact]
    public async Task RetriesExhausted_BecomeFailed_NeverAGrade()
    {
        var throwing = new ThrowingLens();
        var (pipeline, db, _) = NewPipeline(throwing);
        var (subject, inputSet) = await SeedAsync(db, "rk-101");

        var analyses = await pipeline.RunAsync(subject, inputSet, Engine(), live: false, default);

        Assert.Equal(AnalysisPipeline.MaxAttempts, throwing.Calls);
        Assert.All(analyses, a =>
        {
            Assert.Equal(AnalysisStatuses.Failed, a.Status);
            Assert.Null(a.Score);
            Assert.Null(a.Grade);
        });

        var job = await db.ScoringJobs.SingleAsync(j => j.SubjectId == subject.Id);
        Assert.Equal("failed", job.Status);
        Assert.Equal(AnalysisPipeline.MaxAttempts, job.Attempts);
        Assert.Equal(0, await db.Observations.CountAsync());
    }

    [Fact]
    public async Task DifferentPlansOfOneProperty_DoNotAllShareOneGrade()
    {
        var (pipeline, db, _) = NewPipeline();

        var scores = new List<int?>();
        foreach (var key in new[] { "rk-101", "rk-102", "rk-103", "rk-104" })
        {
            var (subject, inputSet) = await SeedAsync(db, key);
            var analyses = await pipeline.RunAsync(subject, inputSet, Engine(), live: false, default);
            scores.Add(analyses.Single(a => a.PrincipleSet == PrincipleSets.FengShui).Score);
        }

        Assert.True(scores.Distinct().Count() > 1,
            $"demo grades cloned across plans: {string.Join(",", scores)}");
    }

    [Fact]
    public async Task ReportBody_RoundTrips_AndOmitsElementBalanceOnThePlanPath()
    {
        var (pipeline, db, store) = NewPipeline();
        var (subject, inputSet) = await SeedAsync(db, "rk-101");
        await pipeline.RunAsync(subject, inputSet, Engine(), live: false, default);

        var writer = new ReportBodyWriter(store);
        var body = await writer.ReadAsync("e1", subject.Id, PrincipleSets.FengShui, default);

        Assert.NotNull(body);
        Assert.Equal(subject.Id, body!.SubjectId);
        Assert.Equal("demo", body.Mode);
        Assert.Null(body.ElementBalance);
        Assert.NotNull(body.Plan);
        Assert.Null(body.Rooms);
        Assert.NotEmpty(body.Site);
        Assert.NotEmpty(body.Summary);

        // Guardrail: no negative superlative anywhere in the stored copy.
        var text = string.Join(" ",
            new[] { body.Summary }
                .Concat(body.Interiors.Select(r => r.Text))
                .Concat(body.Site.Select(r => r.Text))
                .Concat(body.Suggestions.Select(s => $"{s.Title} {s.Detail}")));
        foreach (var banned in Prompts.BannedSuperlatives)
        {
            Assert.DoesNotContain(banned, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Derivation_IsPure_AndFiltersTraditionAtScoreTime()
    {
        var plan = new FloorPlanObservation(
            false, null, true,
            [
                new LensFinding(FloorPlanRules.CenterObstruction, "Open Centre",
                    "A column sits at the centre of the unit.", "vastu", 0.9, "moderate"),
                new LensFinding(FloorPlanRules.EntryToRearStraightLine, "Entry to Rear Sightline",
                    "The entry lines up with the rear window.", "fengshui", 0.9, "major"),
                new LensFinding(FloorPlanRules.BathAdjacentKitchen, "Water Room Beside the Cooking Zone",
                    "The bathroom shares a wall with the kitchen.", "both", 0.2, "moderate"),
            ],
            [], 1.0);

        var input = new DerivationInput(
            Cohort.FloorPlan, [ObservationPayload.ForPlan(plan)],
            ListingEnvironment.AllUnknown, null,
            new Dictionary<string, NumerologyResult>(), Calibration.Identity);

        var fengshui = AnalysisDerivation.InteriorsLens(PrincipleSets.FengShui, input)!;
        var vastu = AnalysisDerivation.InteriorsLens(PrincipleSets.Vastu, input)!;

        // The Vastu-tagged centre finding must not mark the Feng Shui rule unsatisfied, and vice versa.
        Assert.False(fengshui.Outcomes.Single(o => o.RuleId == FloorPlanRules.EntryToRearStraightLine).Satisfied);
        Assert.True(vastu.Outcomes.Single(o => o.RuleId == FloorPlanRules.EntryToRearStraightLine).Satisfied);
        Assert.False(vastu.Outcomes.Single(o => o.RuleId == FloorPlanRules.CenterObstruction).Satisfied);
        Assert.True(fengshui.Outcomes.Single(o => o.RuleId == FloorPlanRules.CenterObstruction).Satisfied);

        // A finding the model itself was unsure of does not move a grade.
        Assert.True(fengshui.Outcomes.Single(o => o.RuleId == FloorPlanRules.BathAdjacentKitchen).Satisfied);

        // Pure: identical input, identical output, no I/O.
        var again = AnalysisDerivation.InteriorsLens(PrincipleSets.FengShui, input)!;
        Assert.Equal(fengshui.Score01, again.Score01);
        Assert.Equal(fengshui.Coverage, again.Coverage);
    }

    [Fact]
    public void PartialBoundary_MakesTheBrahmasthanRuleNotApplicable_NotViolated()
    {
        var plan = FloorPlanLensService.Sanitize(new FloorPlanObservation(
            false, null, BoundaryFullyDrawn: false,
            [new LensFinding(FloorPlanRules.CenterObstruction, "Open Centre",
                "A column sits at the centre.", "vastu", 0.9, "moderate")],
            [], 0.8));

        Assert.Empty(plan.Findings); // dropped at the lens

        var input = new DerivationInput(
            Cohort.FloorPlan, [ObservationPayload.ForPlan(plan)],
            ListingEnvironment.AllUnknown, null,
            new Dictionary<string, NumerologyResult>(), Calibration.Identity);

        var lens = AnalysisDerivation.InteriorsLens(PrincipleSets.Vastu, input)!;
        var centre = lens.Outcomes.Single(o => o.RuleId == FloorPlanRules.CenterObstruction);
        Assert.False(centre.Applicable);
        Assert.False(centre.Satisfied);
    }

    [Fact]
    public void EvidenceManifest_TreatsAnImagelessPlanAsNoEvidence()
    {
        var imageless = PlanSubject("rk-105", imaged: false);
        var inputSet = PlanInputSet(imageless, withOrientation: true);
        Assert.Empty(EvidenceManifest.Parse(imageless, inputSet));

        var imaged = PlanSubject("rk-101");
        var refs = EvidenceManifest.Parse(imaged, PlanInputSet(imaged, withOrientation: true));
        var single = Assert.Single(refs);
        Assert.Equal(EvidenceRef.Plan, single.Kind);
        Assert.Equal("phash-rk-101", single.Hash);
    }
}
