using System.Text.Json;
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HarmonIQ.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────────────────────
//  Perception / judgment split (design §5)
//
//  PERCEPTION is expensive and cached, and runs in two stages:
//    1. one tradition-agnostic VISION call per evidence item, recording plain facts and taking no
//       view — so vision spend stays at 1x however many traditions are scored; then
//    2. one text INTERPRETATION call per tradition over the single assembled fact sheet, each
//       driven by that culture's own prompt (ITradition.InterpretPrompt).
//  Both are stored in `observations` keyed (SubjectId, EvidenceHash, PromptVersion, ModelId).
//  Because all traditions read identical evidence, a difference between two traditions' scores is
//  attributable to the traditions themselves, not to what one call happened to notice.
//  Stage 2 is live-only: the demo path's fixtures carry tagged findings and make no model call.
//
//  JUDGMENT is cheap and deterministic: `AnalysisDerivation` is a PURE function of
//  (stored observations + environment + orientation + numbers + calibration) → per-set SetScore.
//  It makes no I/O and no model call. An engine or rules bump therefore re-derives every row from
//  rows already on disk — that is what `AnalysisPipeline.RederiveAsync` does, and it cannot call
//  Claude because it has no lens dependency in its code path at all.
// ─────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The stored shape of one observation row's payload. A discriminated envelope so the photo path
/// and the floor-plan path share one table without either pretending to be the other.
/// </summary>
public record ObservationPayload(
    string Kind,
    RoomObservation? Room,
    FloorPlanObservation? Plan,
    TraditionInterpretation? Interpretation = null)
{
    public const string RoomKind = "room";
    public const string PlanKind = "floorplan";

    /// <summary>Stage-3: one tradition's reading of the shared fact sheet. One row per tradition.</summary>
    public const string InterpretationKind = "interpretation";

    public static ObservationPayload ForRoom(RoomObservation room) => new(RoomKind, room, null);
    public static ObservationPayload ForPlan(FloorPlanObservation plan) => new(PlanKind, null, plan);

    public static ObservationPayload ForInterpretation(TraditionInterpretation interpretation) =>
        new(InterpretationKind, null, null, interpretation);
}

/// <summary>One evidence item named by the immutable input set. <c>Kind</c> is "plan" or "photo".</summary>
public record EvidenceRef(string Hash, string Kind, string? Label, string? Source, string? RoomType)
{
    public const string Plan = "plan";
    public const string Photo = "photo";
}

/// <summary>
/// Reads the evidence list out of an <see cref="InputSet"/>. Deliberately tolerant of the exact
/// JSON shape ingestion writes (array of hashes, array of objects, or an object with
/// <c>plan</c>/<c>photos</c> keys) so the scoring half is not coupled to ingestion's serializer,
/// and falls back to the subject's own plan image when the snapshot carries no evidence list.
/// </summary>
public static class EvidenceManifest
{
    public static IReadOnlyList<EvidenceRef> Parse(Subject subject, InputSet inputSet)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(inputSet);

        var isPlanPath = inputSet.EvidencePath == Cohort.FloorPlan || subject.SubjectType == "floorplan";
        var refs = new List<EvidenceRef>();

        if (!string.IsNullOrWhiteSpace(inputSet.EvidenceHashesJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(inputSet.EvidenceHashesJson);
                Collect(doc.RootElement, isPlanPath ? EvidenceRef.Plan : EvidenceRef.Photo, refs);
            }
            catch (JsonException)
            {
                // A malformed snapshot is treated as "no evidence listed"; the fallback below applies.
            }
        }

        if (refs.Count == 0 && isPlanPath && !string.IsNullOrWhiteSpace(subject.PlanImageUrl))
        {
            refs.Add(new EvidenceRef(
                subject.PlanImageHash ?? subject.PlanImageUrl!, EvidenceRef.Plan,
                subject.ExternalPlanKey ?? subject.Id, subject.PlanImageUrl, null));
        }

        // A plan with no image is not scored at all (design Q3): no observation, no row, no chip.
        if (isPlanPath)
        {
            refs = refs.Where(r => r.Kind == EvidenceRef.Plan).Take(1).ToList();
            if (string.IsNullOrWhiteSpace(subject.PlanImageUrl) && subject.PlanImageHash is null)
            {
                return [];
            }
        }

        return refs;
    }

    private static void Collect(JsonElement element, string defaultKind, List<EvidenceRef> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Collect(item, defaultKind, into);
                break;

            case JsonValueKind.String:
                if (element.GetString() is { Length: > 0 } hash)
                    into.Add(new EvidenceRef(hash, defaultKind, null, null, null));
                break;

            case JsonValueKind.Object:
                var direct = Str(element, "hash") ?? Str(element, "evidenceHash") ?? Str(element, "sha256");
                if (direct is not null)
                {
                    into.Add(new EvidenceRef(
                        direct,
                        Str(element, "kind") ?? defaultKind,
                        Str(element, "label") ?? Str(element, "photoId") ?? Str(element, "id"),
                        Str(element, "source") ?? Str(element, "path") ?? Str(element, "url"),
                        Str(element, "roomType")));
                    break;
                }
                foreach (var prop in element.EnumerateObject())
                {
                    var kind = prop.Name is "plan" or "planImage" or "floorplan" ? EvidenceRef.Plan
                        : prop.Name is "photos" or "photo" ? EvidenceRef.Photo
                        : defaultKind;
                    Collect(prop.Value, kind, into);
                }
                break;
        }

        static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() is { Length: > 0 } s ? s : null
                : null;
    }
}

/// <summary>Fetches the bytes behind an evidence item. Only the <b>live</b> path needs them.</summary>
public interface IEvidenceLoader
{
    /// <param name="subject">
    /// Needed for photo evidence: photos are addressed by (propertyKey, photoId) through the
    /// listing cache, not by a file path or URL.
    /// </param>
    Task<byte[]?> LoadAsync(Subject subject, EvidenceRef reference, CancellationToken ct);
}

/// <summary>
/// Local-file / HTTP evidence loader. Relative sources resolve under the API content root and
/// under <c>Data/</c> (where the sample plan images live).
/// </summary>
public class FileEvidenceLoader(
    IWebHostEnvironment env,
    IListingService? listings = null,
    IHttpClientFactory? httpFactory = null) : IEvidenceLoader
{
    public async Task<byte[]?> LoadAsync(Subject subject, EvidenceRef reference, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(reference);

        // Photos live in the listing cache (downscaled, in memory, never on disk) and are
        // addressed by photoId — which the manifest carries as the ref's label.
        if (reference.Kind == EvidenceRef.Photo && listings is not null
            && reference.Label is { Length: > 0 } photoId)
        {
            var cached = await listings.GetPhotoAsync(subject.PropertyKey, photoId, null, ct);
            if (cached is not null) return cached.Data;
        }

        if (string.IsNullOrWhiteSpace(reference.Source)) return null;

        if (Uri.TryCreate(reference.Source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            if (httpFactory is null) return null;
            using var http = httpFactory.CreateClient();
            using var resp = await http.GetAsync(uri, ct);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadAsByteArrayAsync(ct) : null;
        }

        foreach (var candidate in Candidates(env.ContentRootPath, reference.Source))
        {
            if (File.Exists(candidate)) return await File.ReadAllBytesAsync(candidate, ct);
        }
        return null;
    }

    private static IEnumerable<string> Candidates(string contentRoot, string source)
    {
        yield return source;
        yield return Path.Combine(contentRoot, source);
        yield return Path.Combine(contentRoot, "Data", source);
    }
}

// ─────────────────────────────────────────────────────────────────────────── judgment (pure)

/// <summary>Everything the deterministic derivation needs. No services, no I/O, no model.</summary>
public record DerivationInput(
    string EvidencePath,
    IReadOnlyList<ObservationPayload> Observations,
    ListingEnvironment Environment,
    SubjectOrientation? Orientation,
    IReadOnlyDictionary<string, NumerologyResult> NumerologyBySet,
    Calibration Calibration);

/// <summary>The derived verdict for one principle set, with the parts the report body needs.</summary>
public record DerivedSet(
    SetScore Score,
    LensResult? Interiors,
    LensResult Site,
    NumerologyResult Numerology,
    IReadOnlyList<Suggestion> Suggestions);

/// <summary>
/// <b>The judgment half.</b> A pure function from persisted observations (plus site, orientation and
/// numbers) to per-set scores. Tradition FILTERING happens here, at score time — one observation
/// serves both principle sets, which is what halves the model bill against per-set prompting.
/// </summary>
public static class AnalysisDerivation
{
    /// <summary>
    /// Findings the model itself was unsure of do not move a grade. They stay in the report body
    /// as recorded observations; they just do not enter the arithmetic.
    /// </summary>
    public const double FindingConfidenceFloor = 0.5;

    public static IReadOnlyList<DerivedSet> DeriveAll(DerivationInput input, SiteAnalysisService? site = null) =>
        PrincipleSets.All.Select(s => Derive(s, input, site)).ToList();

    public static DerivedSet Derive(string principleSet, DerivationInput input, SiteAnalysisService? site = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        site ??= new SiteAnalysisService();

        var interiors = InteriorsLens(principleSet, input);
        var siteLens = site.EvaluateSet(input.Environment, input.Orientation, principleSet);
        var numerology = input.NumerologyBySet.TryGetValue(principleSet, out var n) ? n : new NumerologyResult(0, []);
        var suggestions = Suggestions(input, principleSet);

        // ElementBalance is materials-only: a line drawing has no materials, so the floor-plan path
        // never reports one. Never five zeros — the section is omitted instead. Prefer this
        // tradition's own reading (each wǔxíng tradition derives its own from the shared
        // materials list); fall back to the demo path's precomputed per-room balances.
        var interpretation = Interpretation(input, principleSet);
        var elements = input.EvidencePath == Cohort.FloorPlan
            ? null
            : interpretation?.ElementBalance
              ?? ScoreMath.AverageElements(Rooms(input).Select(r => r.ElementBalance), principleSet);

        var cohort = OrientationGate.CohortFor(input.EvidencePath, input.Orientation);
        var summary = LocalSummary.Build(principleSet, interiors, siteLens, suggestions, numerology);

        // Numerology is deliberately absent from the aggregation: per FR-20 it is cultural
        // annotation only and adjusts no stored score. It still reaches the report body's
        // Numbers card (FR-19) via derived.Numerology — display, never grade.
        var score = ScoreMath.Aggregate(
            principleSet, interiors, siteLens, cohort, input.Calibration, elements, summary);

        if (score.Status != AnalysisStatuses.Ok)
        {
            score = score with
            {
                Summary = LocalSummary.InsufficientEvidence(
                    principleSet, input.EvidencePath, cohort.OrientationPath == Cohort.With),
            };
        }

        return new DerivedSet(score, interiors, siteLens, numerology, suggestions);
    }

    /// <summary>
    /// The interiors lens for this principle set: the floor-plan catalogue on the plan path, the
    /// per-photo findings on the photo path. Returns null when there is no interior evidence at all.
    /// </summary>
    public static LensResult? InteriorsLens(string principleSet, DerivationInput input) =>
        input.EvidencePath == Cohort.FloorPlan
            ? PlanLens(principleSet, input)
            : RoomLens(principleSet, input);

    private static LensResult? PlanLens(string principleSet, DerivationInput input)
    {
        var plan = input.Observations.FirstOrDefault(o => o.Kind == ObservationPayload.PlanKind)?.Plan;
        if (plan is null) return null;

        // A declined read is zero coverage, not a low score: it flows through the confidence floor
        // to insufficient_evidence.
        if (plan.NotDeterminable || plan.Coverage <= 0)
            return new LensResult(LensResult.Interiors, 0.0, 0.0, []);

        var relevant = plan.Findings
            .Where(f => Matches(f.Tradition, principleSet))
            .Where(f => f.Confidence >= FindingConfidenceFloor)
            .Where(f => FloorPlanRules.AllowedRuleIds.Contains(f.RuleId, StringComparer.Ordinal))
            .Where(f => FloorPlanRuleCatalogue.IsApplicable(f.RuleId, plan.BoundaryFullyDrawn))
            .ToList();

        var outcomes = new List<RuleOutcome>(FloorPlanRules.AllowedRuleIds.Count);
        foreach (var ruleId in FloorPlanRules.AllowedRuleIds)
        {
            var entry = FloorPlanRuleCatalogue.For(ruleId);
            var adverse = relevant.Where(f => f.RuleId == ruleId && f.Severity is not null).ToList();
            var favourable = relevant.FirstOrDefault(f => f.RuleId == ruleId && f.Severity is null);

            var applicable = FloorPlanRuleCatalogue.IsApplicable(ruleId, plan.BoundaryFullyDrawn)
                && (!entry.PositiveEvidence || favourable is not null || adverse.Count > 0);

            var satisfied = adverse.Count == 0 && (!entry.PositiveEvidence || favourable is not null);
            var severity = adverse.Count > 0 ? adverse.Max(a => Weight(a.Severity)) : entry.Severity;
            var text = adverse.Count > 0 ? adverse[0].Observation
                : favourable?.Observation ?? entry.SatisfiedText;

            outcomes.Add(new RuleOutcome(ruleId, principleSet, applicable, applicable && satisfied, severity, text));
        }

        // Catalogue applicability, discounted by how much of the catalogue the drawing itself let
        // the lens evaluate. Missing evidence lowers the lens's weight, never its score.
        var coverage = Math.Clamp(RuleEvaluation.Coverage(outcomes) * plan.Coverage, 0.0, 1.0);
        return new LensResult(LensResult.Interiors, RuleEvaluation.NormalizedScore(outcomes), coverage, outcomes);
    }

    /// <summary>
    /// The interiors lens for one tradition.
    ///
    /// Prefers this tradition's own stage-3 interpretation, which is what makes five traditions
    /// genuinely differ: each read the same shared fact sheet through its own prompt. Falls back
    /// to tradition-tagged findings recorded on the room observations themselves — the demo/mock
    /// path and any observation predating the interpretation stage.
    /// </summary>
    private static LensResult? RoomLens(string principleSet, DerivationInput input)
    {
        var rooms = Rooms(input);
        if (rooms.Count == 0) return null;

        // Coverage comes from perception either way: it measures how much the photographs showed,
        // which is a property of the evidence, not of the tradition reading it.
        var perceptionCoverage = Math.Clamp(rooms.Average(r => r.Coverage), 0.0, 1.0);

        if (Interpretation(input, principleSet) is { } interpretation)
        {
            var interpreted = interpretation.Findings
                .Where(f => f.Confidence >= FindingConfidenceFloor)
                .Select(f => new RuleOutcome(
                    f.RuleId, principleSet, true, f.Severity is null, Weight(f.Severity), f.Observation))
                .ToList();

            var covered = Math.Clamp(perceptionCoverage * Math.Clamp(interpretation.Coverage, 0.0, 1.0), 0.0, 1.0);
            return new LensResult(
                LensResult.Interiors,
                RuleEvaluation.NormalizedScore(interpreted),
                interpreted.Count == 0 ? 0.0 : covered,
                interpreted);
        }

        var outcomes = new List<RuleOutcome>();
        foreach (var room in rooms)
        {
            foreach (var finding in room.Findings)
            {
                // A blank tradition is an untagged perception fact, not a reading. It renders on
                // the room card but must not score, or every tradition would inherit the same
                // undifferentiated findings.
                if (string.IsNullOrWhiteSpace(finding.Tradition)) continue;
                if (!Matches(finding.Tradition, principleSet)) continue;
                if (finding.Confidence < FindingConfidenceFloor) continue;
                outcomes.Add(new RuleOutcome(
                    $"{room.PhotoId}:{finding.RuleId}", principleSet, true,
                    finding.Severity is null, Weight(finding.Severity), finding.Observation));
            }
        }

        // No findings this tradition can read ⇒ no coverage, not a zero score.
        var fallbackCoverage = outcomes.Count == 0 ? 0.0 : perceptionCoverage;
        return new LensResult(
            LensResult.Interiors, RuleEvaluation.NormalizedScore(outcomes), fallbackCoverage, outcomes);
    }

    /// <summary>This tradition's stage-3 interpretation, or null when none was recorded.</summary>
    public static TraditionInterpretation? Interpretation(DerivationInput input, string principleSet) =>
        input.Observations
            .Where(o => o.Kind == ObservationPayload.InterpretationKind)
            .Select(o => o.Interpretation)
            .FirstOrDefault(i => i is not null && i.PrincipleSet == principleSet);

    public static IReadOnlyList<RoomObservation> Rooms(DerivationInput input) =>
        input.Observations.Where(o => o.Kind == ObservationPayload.RoomKind && o.Room is not null)
            .Select(o => o.Room!).ToList();

    public static FloorPlanObservation? Plan(DerivationInput input) =>
        input.Observations.FirstOrDefault(o => o.Kind == ObservationPayload.PlanKind)?.Plan;

    /// <summary>
    /// Suggestions for one tradition. A remedy is a reading, not an observation, so this tradition's
    /// own interpretation comes first; the plan/room suggestions behind it are the demo path and
    /// the tradition-neutral adjacency remedies.
    /// </summary>
    private static IReadOnlyList<Suggestion> Suggestions(DerivationInput input, string principleSet)
    {
        var mine = Interpretation(input, principleSet)?.Suggestions ?? [];
        return mine
            .Concat(input.Observations.SelectMany(o => o.Plan?.Suggestions ?? o.Room?.Suggestions ?? []))
            .DistinctBy(s => s.Title, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Tradition filtering for tagged findings — the floor-plan path and any observation recorded
    /// under the two-tradition contract.
    ///
    /// <c>"both"</c> predates the five-tradition model and means "shared by every tradition"; on
    /// the floor-plan path that is accurate, because adjacency relationships are tradition-neutral
    /// facts. A blank tag matches too, for the same legacy reason — but note that
    /// <see cref="RoomLens"/> deliberately excludes blanks, since on the photo path a blank marks
    /// an untagged stage-1 perception fact rather than a reading.
    /// </summary>
    public static bool Matches(string? tradition, string principleSet) =>
        string.IsNullOrWhiteSpace(tradition) || tradition == "both" || tradition == principleSet;

    private static int Weight(string? severity) =>
        severity switch { "major" => 3, "moderate" => 2, _ => 1 };
}

// ─────────────────────────────────────────────────────────────────────────── the pipeline

/// <summary>
/// Perception (expensive, cached) then judgment (cheap, deterministic), in one place.
/// </summary>
public interface IAnalysisPipeline
{
    /// <summary>
    /// Scores one subject: reuses cached observations, perceives only what is missing, then derives
    /// and upserts one <c>analyses</c> row per principle set. Returns an <b>empty list</b> for a plan
    /// with no image (design Q3) and records a <c>scoring_jobs</c> outcome of "skipped".
    /// </summary>
    Task<IReadOnlyList<Analysis>> RunAsync(
        Subject subject, InputSet inputSet, EngineVersion engine, bool live, CancellationToken ct);

    /// <inheritdoc cref="RunAsync(Subject, InputSet, EngineVersion, bool, CancellationToken)"/>
    Task<IReadOnlyList<Analysis>> RunAsync(
        Subject subject, InputSet inputSet, EngineVersion engine, bool live, string reason, CancellationToken ct);

    /// <summary>
    /// Re-derives every principle set for a subject from the observations <b>already on disk</b>.
    /// This is the engine-bump path: it never touches a lens, so a rules or engine version change
    /// costs no Claude call. Returns an empty list when the subject has no stored observations.
    /// </summary>
    Task<IReadOnlyList<Analysis>> RederiveAsync(
        Subject subject, InputSet inputSet, EngineVersion engine, CancellationToken ct);
}

public class AnalysisPipeline(
    HarmonIQDbContext db,
    IFloorPlanLens floorPlanLens,
    ClaudeAnalysisService liveRooms,
    MockAnalysisService mockRooms,
    SiteAnalysisService siteService,
    NumerologyService numerologyService,
    ReportBodyWriter reports,
    IEvidenceLoader evidence,
    ILogger<AnalysisPipeline> log) : IAnalysisPipeline
{
    public const int MaxAttempts = 3;

    /// <summary>Backoff between perception attempts. Settable so tests do not sleep.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    public Task<IReadOnlyList<Analysis>> RunAsync(
        Subject subject, InputSet inputSet, EngineVersion engine, bool live, CancellationToken ct) =>
        RunAsync(subject, inputSet, engine, live, "new_listing", ct);

    public async Task<IReadOnlyList<Analysis>> RunAsync(
        Subject subject, InputSet inputSet, EngineVersion engine, bool live, string reason, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(inputSet);
        ArgumentNullException.ThrowIfNull(engine);

        var now = DateTimeOffset.UtcNow;
        var job = new ScoringJob
        {
            Id = Guid.NewGuid().ToString("N"),
            SubjectId = subject.Id,
            EngineVersion = engine.Version,
            Reason = reason,
            Status = "running",
            QueuedAt = now,
            StartedAt = now,
        };
        db.ScoringJobs.Add(job);

        var refs = EvidenceManifest.Parse(subject, inputSet);
        if (refs.Count == 0)
        {
            // A plan with no image is not scored at all: no observation, no analyses row, no chip.
            job.Status = "skipped";
            job.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return [];
        }

        List<ObservationPayload> payloads;
        try
        {
            payloads = await PerceiveAsync(subject, inputSet, engine, live, refs, job, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A failure is never a grade: the row goes to `failed` with a null score, and the
            // projection stays NULL.
            log.LogWarning(e, "Perception failed for subject {SubjectId} after {Attempts} attempts", subject.Id, job.Attempts);
            job.Status = "failed";
            job.LastError = e.Message;
            job.CompletedAt = DateTimeOffset.UtcNow;
            var failed = await UpsertFailedAsync(subject, engine, live, ct);
            await db.SaveChangesAsync(ct);
            return failed;
        }

        var analyses = await DeriveAndPersistAsync(subject, inputSet, engine, live, payloads, ct);
        job.Status = "ok";
        job.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return analyses;
    }

    public async Task<IReadOnlyList<Analysis>> RederiveAsync(
        Subject subject, InputSet inputSet, EngineVersion engine, CancellationToken ct)
    {
        var refs = EvidenceManifest.Parse(subject, inputSet);
        if (refs.Count == 0) return [];

        var hashes = refs.Select(r => r.Hash).ToList();
        var rows = await db.Observations
            .Where(o => o.SubjectId == subject.Id && hashes.Contains(o.EvidenceHash))
            .ToListAsync(ct);
        if (rows.Count == 0) return [];

        var live = rows.All(r => r.Mode == "live");
        var payloads = rows.Select(r => Deserialize(r.PayloadJson)).Where(p => p is not null).Select(p => p!).ToList();

        var analyses = await DeriveAndPersistAsync(subject, inputSet, engine, live, payloads, ct);
        await db.SaveChangesAsync(ct);
        return analyses;
    }

    // ------------------------------------------------------------------ perception (cached)

    /// <summary>
    /// Observations are keyed on the evidence itself, not on the rules: a rules bump reuses them,
    /// an evidence / prompt / model change invalidates them. Demo observations carry the mock
    /// model id so switching to live re-perceives rather than blessing mock output as real.
    /// </summary>
    public static string ObservationModelId(EngineVersion engine, bool live) =>
        live ? engine.ModelId : "mock";

    private async Task<List<ObservationPayload>> PerceiveAsync(
        Subject subject, InputSet inputSet, EngineVersion engine, bool live,
        IReadOnlyList<EvidenceRef> refs, ScoringJob job, CancellationToken ct)
    {
        var modelId = ObservationModelId(engine, live);
        var orientationHint = SiteAnalysisService.ResolvedCardinal(await OrientationAsync(subject, inputSet, ct));
        var payloads = new List<ObservationPayload>(refs.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var reference in refs)
        {
            if (!seen.Add(reference.Hash)) continue;

            var existing = await db.Observations.FirstOrDefaultAsync(
                o => o.SubjectId == subject.Id
                    && o.EvidenceHash == reference.Hash
                    && o.PromptVersion == Prompts.PromptVersion
                    && o.ModelId == modelId,
                ct);
            if (existing is not null && Deserialize(existing.PayloadJson) is { } cached)
            {
                payloads.Add(cached);
                continue;
            }

            var fresh = await WithRetriesAsync(
                () => PerceiveOneAsync(subject, reference, live, orientationHint, ct), job, ct);

            db.Observations.Add(new Observation
            {
                Id = Guid.NewGuid().ToString("N"),
                SubjectId = subject.Id,
                InputSetId = inputSet.Id,
                EvidenceHash = reference.Hash,
                PromptVersion = Prompts.PromptVersion,
                ModelId = modelId,
                Mode = live ? "live" : "demo",
                PayloadJson = JsonSerializer.Serialize(fresh, Json.Options),
                CreatedAt = DateTimeOffset.UtcNow,
            });
            payloads.Add(fresh);
        }

        // Stage 3 — every tradition reads the same assembled fact sheet through its own prompt.
        // Live only: the demo path's fixtures carry tradition-tagged findings already, and demo
        // must stay fully functional with no key and no model call.
        if (live)
        {
            payloads.AddRange(await InterpretAsync(
                subject, inputSet, payloads, orientationHint, modelId, job, ct));
        }

        return payloads;
    }

    /// <summary>
    /// Runs one interpretation per tradition over the shared fact sheet, caching each the same way
    /// perception is cached. The cache key folds in a digest of the fact sheet, so a change to any
    /// upstream observation invalidates every tradition's reading of it — and, critically, adding a
    /// sixth tradition re-runs only that tradition rather than the whole subject.
    /// </summary>
    private async Task<List<ObservationPayload>> InterpretAsync(
        Subject subject, InputSet inputSet, IReadOnlyList<ObservationPayload> perceptions,
        string? orientationHint, string modelId, ScoringJob job, CancellationToken ct)
    {
        var rooms = perceptions.Where(p => p.Room is not null).Select(p => p.Room!).ToList();
        var plan = perceptions.FirstOrDefault(p => p.Kind == ObservationPayload.PlanKind)?.Plan;
        if (rooms.Count == 0 && plan is null) return [];

        var factSheet = ClaudeAnalysisService.BuildFactSheet(rooms, plan, orientationHint);
        var digest = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(factSheet)))[..16];

        var results = new List<ObservationPayload>(Traditions.TraditionRegistry.Ordered.Count);
        foreach (var tradition in Traditions.TraditionRegistry.Ordered)
        {
            var key = $"interpretation:{tradition.Id}:{digest}";

            var existing = await db.Observations.FirstOrDefaultAsync(
                o => o.SubjectId == subject.Id
                    && o.EvidenceHash == key
                    && o.PromptVersion == Prompts.PromptVersion
                    && o.ModelId == modelId,
                ct);
            if (existing is not null && Deserialize(existing.PayloadJson) is { } cached)
            {
                results.Add(cached);
                continue;
            }

            var interpretation = await WithRetriesAsync(
                async () => ObservationPayload.ForInterpretation(
                    await liveRooms.InterpretAsync(tradition, factSheet, orientationHint, ct)),
                job, ct);

            db.Observations.Add(new Observation
            {
                Id = Guid.NewGuid().ToString("N"),
                SubjectId = subject.Id,
                InputSetId = inputSet.Id,
                EvidenceHash = key,
                PromptVersion = Prompts.PromptVersion,
                ModelId = modelId,
                Mode = "live",
                PayloadJson = JsonSerializer.Serialize(interpretation, Json.Options),
                CreatedAt = DateTimeOffset.UtcNow,
            });
            results.Add(interpretation);
        }

        return results;
    }

    private async Task<ObservationPayload> PerceiveOneAsync(
        Subject subject, EvidenceRef reference, bool live, string? orientationHint, CancellationToken ct)
    {
        var bytes = live ? await evidence.LoadAsync(subject, reference, ct) : null;

        if (reference.Kind == EvidenceRef.Plan)
        {
            // ONE tradition-agnostic call over the plan image, serving BOTH principle sets.
            var plan = await floorPlanLens.ReadAsync(subject, bytes ?? [], live, ct);
            return ObservationPayload.ForPlan(plan);
        }

        var photoId = reference.Label ?? reference.Hash;
        if (!live)
        {
            return ObservationPayload.ForRoom(mockRooms.ObserveRoom(new PhotoSelection(photoId, reference.RoomType)));
        }

        if (bytes is null || bytes.Length == 0)
            throw new InvalidOperationException($"No image bytes for photo evidence '{photoId}' of subject '{subject.Id}'.");

        var room = await liveRooms.ObserveRoomAsync(
            new RoomInput(photoId, reference.RoomType, bytes), orientationHint, ct);
        return ObservationPayload.ForRoom(room);
    }

    private async Task<T> WithRetriesAsync<T>(Func<Task<T>> action, ScoringJob job, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            job.Attempts = attempt;
            try
            {
                return await action();
            }
            catch (Exception e) when (e is not OperationCanceledException && attempt < MaxAttempts)
            {
                log.LogWarning(e, "Perception attempt {Attempt}/{Max} failed; retrying", attempt, MaxAttempts);
                if (RetryDelay > TimeSpan.Zero) await Task.Delay(RetryDelay * attempt, ct);
            }
        }
    }

    private static ObservationPayload? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ObservationPayload>(json, Json.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ judgment (deterministic)

    private async Task<IReadOnlyList<Analysis>> DeriveAndPersistAsync(
        Subject subject, InputSet inputSet, EngineVersion engine, bool live,
        IReadOnlyList<ObservationPayload> payloads, CancellationToken ct)
    {
        var orientation = await OrientationAsync(subject, inputSet, ct);
        var numbers = Read<ListingNumbers>(inputSet.NumbersJson);
        var environment = Read<ListingEnvironment>(inputSet.EnvironmentJson) ?? ListingEnvironment.AllUnknown;

        var input = new DerivationInput(
            EvidencePath(subject, inputSet),
            payloads,
            environment,
            orientation,
            PrincipleSets.All.ToDictionary(s => s, s => numerologyService.EvaluateSubject(numbers, s), StringComparer.Ordinal),
            Calibration.FromJson(engine.CalibrationJson));

        var mode = live ? "live" : "demo";
        var results = new List<Analysis>(PrincipleSets.All.Count);

        foreach (var principleSet in PrincipleSets.All)
        {
            var derived = AnalysisDerivation.Derive(principleSet, input, siteService);
            var rulesVersion = RulesVersionFor(engine, principleSet);
            var score = derived.Score;

            var body = BuildBody(subject, engine, rulesVersion, mode, input, derived);
            var (uri, sha) = await reports.WriteAsync(body, ct);

            var row = await UpsertAsync(subject.Id, principleSet, rulesVersion, ct);
            row.EngineVersion = engine.Version;
            row.Status = score.Status;
            row.Score = score.Score;
            row.Grade = score.Grade;
            row.InteriorsScore = score.InteriorsScore;
            row.SiteScore = score.SiteScore;
            // NumerologyAdjustment is intentionally not written: FR-20 removed the mechanism.
            // The column stays nullable-and-null pending its migration-out.
            row.NumerologyAdjustment = null;
            row.InteriorsCoverage = score.InteriorsCoverage;
            row.SiteCoverage = score.SiteCoverage;
            row.Confidence = score.Confidence;
            row.CohortEvidencePath = score.Cohort.EvidencePath;
            row.CohortOrientationPath = score.Cohort.OrientationPath;
            // Never five zeros, and never present on Vastu: the section is omitted, not zeroed.
            row.ElementBalanceJson = score.ElementBalance is null
                ? null
                : JsonSerializer.Serialize(score.ElementBalance, Json.Options);
            row.SummaryText = score.Summary;
            row.Mode = mode;
            row.ModelId = ObservationModelId(engine, live);
            row.InputFingerprint = inputSet.InputFingerprint;
            row.ReportUri = uri;
            row.ReportSha256 = sha;
            row.ComputedAt = DateTimeOffset.UtcNow;

            results.Add(row);
        }

        return results;
    }

    private async Task<IReadOnlyList<Analysis>> UpsertFailedAsync(
        Subject subject, EngineVersion engine, bool live, CancellationToken ct)
    {
        var rows = new List<Analysis>();
        foreach (var principleSet in PrincipleSets.All)
        {
            var row = await UpsertAsync(subject.Id, principleSet, RulesVersionFor(engine, principleSet), ct);
            row.EngineVersion = engine.Version;
            row.Status = AnalysisStatuses.Failed;
            row.Score = null;
            row.Grade = null;
            row.ElementBalanceJson = null;
            row.Mode = live ? "live" : "demo";
            row.ComputedAt = DateTimeOffset.UtcNow;
            rows.Add(row);
        }
        return rows;
    }

    private async Task<Analysis> UpsertAsync(
        string subjectId, string principleSet, string rulesVersion, CancellationToken ct)
    {
        var row = db.ChangeTracker.Entries<Analysis>()
            .Select(e => e.Entity)
            .FirstOrDefault(a => a.SubjectId == subjectId && a.PrincipleSet == principleSet && a.RulesVersion == rulesVersion)
            ?? await db.Analyses.FirstOrDefaultAsync(
                a => a.SubjectId == subjectId && a.PrincipleSet == principleSet && a.RulesVersion == rulesVersion, ct);

        if (row is null)
        {
            row = new Analysis
            {
                Id = Guid.NewGuid().ToString("N"),
                SubjectId = subjectId,
                PrincipleSet = principleSet,
                RulesVersion = rulesVersion,
            };
            db.Analyses.Add(row);
        }
        return row;
    }

    /// <summary>Rules versions are per principle set: a Vastu change must not invalidate Feng Shui.</summary>
    public static string RulesVersionFor(EngineVersion engine, string principleSet)
    {
        var fromEngine = principleSet == PrincipleSets.Vastu ? engine.RulesVersionVastu : engine.RulesVersionFengshui;
        return string.IsNullOrWhiteSpace(fromEngine) ? SiteAnalysisService.RulesVersionFor(principleSet) : fromEngine;
    }

    public static string EvidencePath(Subject subject, InputSet inputSet) =>
        inputSet.EvidencePath is Cohort.FloorPlan or Cohort.Photos
            ? inputSet.EvidencePath
            : subject.SubjectType == "floorplan" ? Cohort.FloorPlan : Cohort.Photos;

    /// <summary>
    /// Scoring reads the orientation the <b>immutable snapshot</b> pinned; the live
    /// <c>subject_orientation</c> row is only a fallback for snapshots written without one.
    /// </summary>
    private async Task<SubjectOrientation?> OrientationAsync(Subject subject, InputSet inputSet, CancellationToken ct)
    {
        var snapshot = Read<SubjectOrientation>(inputSet.OrientationJson);
        if (snapshot is not null)
        {
            if (string.IsNullOrWhiteSpace(snapshot.Source))
            {
                snapshot.Source = snapshot.Cardinal is not null || snapshot.FacingDegrees is not null ? "annotation" : "none";
            }
            return snapshot;
        }
        return await db.SubjectOrientations.FirstOrDefaultAsync(o => o.SubjectId == subject.Id, ct);
    }

    private static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ report body

    private static ReportBody BuildBody(
        Subject subject, EngineVersion engine, string rulesVersion, string mode,
        DerivationInput input, DerivedSet derived)
    {
        var score = derived.Score;
        var plan = AnalysisDerivation.Plan(input);
        var rooms = AnalysisDerivation.Rooms(input);
        var interiorRules = (derived.Interiors?.Outcomes ?? []).Select(Rule).ToList();

        List<ReportRoomCard>? roomCards = null;
        if (rooms.Count > 0)
        {
            roomCards = [];
            foreach (var room in rooms)
            {
                var cardRules = new List<ReportRule>();
                foreach (var finding in room.Findings)
                {
                    if (!AnalysisDerivation.Matches(finding.Tradition, score.PrincipleSet)) continue;
                    var weight = finding.Severity switch { "major" => 3, "moderate" => 2, _ => 1 };
                    cardRules.Add(new ReportRule(
                        finding.RuleId, finding.Principle, finding.Observation,
                        true, finding.Severity is null, weight, score.PrincipleSet));
                }
                roomCards.Add(new ReportRoomCard(
                    room.PhotoId, room.RoomType, room.Coverage, cardRules,
                    score.PrincipleSet == PrincipleSets.Vastu ? null : room.ElementBalance));
            }
        }

        var planCard = plan is null
            ? null
            : new ReportPlanCard(
                plan.NotDeterminable, plan.NotDeterminableReason, plan.BoundaryFullyDrawn,
                plan.Coverage, interiorRules);

        return new ReportBody(
            subject.Id,
            score.PrincipleSet,
            rulesVersion,
            engine.Version,
            score.Status,
            mode,
            score.Score,
            score.Grade,
            score.Confidence,
            score.InteriorsCoverage,
            score.SiteCoverage,
            score.Cohort.ToString(),
            score.InteriorsScore,
            score.SiteScore,
            score.Summary,
            interiorRules,
            derived.Site.Outcomes.Select(Rule).ToList(),
            derived.Suggestions,
            derived.Numerology.Checks,
            score.ElementBalance,
            roomCards,
            planCard,
            DateTimeOffset.UtcNow);

        static ReportRule Rule(RuleOutcome o) =>
            new(o.RuleId, Title(o.RuleId), o.Text, o.Applicable, o.Satisfied, o.Severity, o.PrincipleSet);

        static string Title(string ruleId) =>
            FloorPlanRules.AllowedRuleIds.Contains(ruleId, StringComparer.Ordinal)
                ? FloorPlanRuleCatalogue.For(ruleId).Title
                : SiteAnalysisService.RuleTitle(ruleId);
    }
}
