using System.IO.Compression;
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Persistence;
using HarmonIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace HarmonIQ.Api.Controllers;

/// <summary>
/// The read path shared by every v2 endpoint: which engine version a request is pinned to,
/// which subjects a property has, and — in demo mode only — making sure those subjects have
/// something to read.
///
/// <b>Why a service and not controller code:</b> three controllers need the same version
/// resolution, and getting it wrong is the one failure that makes an SRP badge and an LDP chip
/// disagree. Resolution order is: the explicitly requested version (404 if unknown — never
/// silently substituted), else the published version, else the current version. The last
/// fallback is the local demo case, where nothing is ever published because publishing requires
/// <c>mode = live</c>.
/// </summary>
public class SubjectsReadService(
    HarmonIQDbContext db,
    ISubjectService subjects,
    IAnalysisPipeline pipeline,
    IEngineVersionService engineVersions,
    IPlanSource planSource,
    IListingService listings,
    NumerologyService numerology,
    IClaudeClient claude,
    ILogger<SubjectsReadService> log)
{
    public bool Live => claude.IsConfigured;

    public string Mode => Live ? "live" : "demo";

    /// <summary>
    /// Resolves the engine version a request is pinned to. Returns null when an explicitly
    /// requested version does not exist — the caller 404s rather than serving another version's
    /// rows under the requested name.
    /// </summary>
    public async Task<EngineVersion?> ResolveEngineAsync(string? requested, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return await engineVersions.GetAsync(requested.Trim(), ct);
        }
        return await engineVersions.GetPublishedAsync(ct)
            ?? await engineVersions.GetOrCreateCurrentAsync(ct);
    }

    /// <summary>Parses <c>?sets=fengshui,vastu</c>; unknown or empty means both, in canonical order.</summary>
    public static IReadOnlyList<string> ParseSets(string? sets)
    {
        if (string.IsNullOrWhiteSpace(sets)) return PrincipleSets.All;
        var requested = sets
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var chosen = PrincipleSets.All.Where(requested.Contains).ToList();
        return chosen.Count == 0 ? PrincipleSets.All : chosen;
    }

    /// <summary>
    /// The property's subjects, or null when the property key is unknown to every source. The
    /// null case matters: <c>MaterializeAsync</c> will happily mint a property subject for any
    /// string, and a GET must not create rows for a typo'd key.
    /// </summary>
    public async Task<IReadOnlyList<Subject>?> SubjectsForAsync(
        string propertyKey, EngineVersion engine, CancellationToken ct)
    {
        var known = await db.Subjects.AnyAsync(s => s.PropertyKey == propertyKey, ct);
        if (!known)
        {
            var plans = await planSource.GetPlansAsync(propertyKey, ct);
            if (plans is null or { Count: 0 } && !await ListingExistsAsync(propertyKey, ct))
            {
                return null;
            }
        }

        var materialized = await subjects.MaterializeAsync(propertyKey, ct);
        await EnsureDemoScoredAsync(materialized, engine, ct);
        return materialized;
    }

    /// <summary>
    /// Demo mode has no backfill run behind it, so the read path computes what is missing —
    /// deterministically, from the mock lens, writing <c>analyses</c> rows in <c>mode = demo</c>
    /// that the publisher will never pick up (publish requires <c>mode = live AND status = ok</c>).
    ///
    /// Three deliberate refusals:
    /// <list type="bullet">
    /// <item><description>Never in live mode — a GET must not spend money on model calls.</description></item>
    /// <item><description>Never for a pinned older version — a reader who asks for version X sees
    /// exactly version X's rows, including none at all.</description></item>
    /// <item><description>Never twice for a subject the pipeline already declined (the imageless
    /// plan): a recorded <c>skipped</c> job is remembered, so repeat requests stay cheap and the
    /// plan stays unscored.</description></item>
    /// </list>
    /// </summary>
    private async Task EnsureDemoScoredAsync(
        IReadOnlyList<Subject> materialized, EngineVersion engine, CancellationToken ct)
    {
        if (Live) return;

        var current = await engineVersions.GetOrCreateCurrentAsync(ct);
        if (current.Version != engine.Version) return;

        foreach (var subject in materialized)
        {
            if (await db.Analyses.AnyAsync(a => a.SubjectId == subject.Id && a.EngineVersion == engine.Version, ct))
            {
                continue;
            }
            var declined = await db.ScoringJobs.AnyAsync(
                j => j.SubjectId == subject.Id && j.EngineVersion == engine.Version && j.Status == "skipped", ct);
            if (declined) continue;

            var inputSet = await LatestInputSetAsync(subject.Id, ct) ?? await subjects.SnapshotAsync(subject, ct);
            try
            {
                await pipeline.RunAsync(subject, inputSet, engine, live: false, "new_listing", ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // A failure is never a grade, and never a 500 either: the subject just carries
                // no sets and the page renders nothing for it.
                log.LogWarning(e, "Demo scoring failed for subject {SubjectId}", subject.Id);
            }
        }
    }

    public async Task<InputSet?> LatestInputSetAsync(string subjectId, CancellationToken ct)
    {
        // DateTimeOffset ordering is not translatable on the SQLite provider; the per-subject
        // set is tiny, so order in memory.
        var rows = await db.InputSets.Where(i => i.SubjectId == subjectId).ToListAsync(ct);
        return rows.OrderByDescending(i => i.CreatedAt).FirstOrDefault();
    }

    /// <summary>
    /// The stored per-set grades for one subject under one engine version.
    /// <c>failed</c> and <c>pending</c> rows are omitted entirely — an in-flight or exhausted
    /// attempt is indistinguishable from "not scored", which is exactly how it must render.
    /// <c>insufficient_evidence</c> <b>is</b> returned, because it is a permanent, explainable
    /// state the drawer has copy for.
    /// </summary>
    public async Task<IReadOnlyList<SetGrade>> SetsForAsync(
        string subjectId, EngineVersion engine, IReadOnlyList<string> sets, CancellationToken ct)
    {
        var rows = await db.Analyses
            .Where(a => a.SubjectId == subjectId && a.EngineVersion == engine.Version)
            .ToListAsync(ct);

        return sets
            .Select(set => rows.FirstOrDefault(r => r.PrincipleSet == set))
            .Where(r => r is not null && r.Status is AnalysisStatuses.Ok or AnalysisStatuses.InsufficientEvidence)
            .Select(r => new SetGrade(
                r!.PrincipleSet,
                r.Status,
                r.Status == AnalysisStatuses.Ok ? r.Score : null,
                r.Status == AnalysisStatuses.Ok ? r.Grade : null,
                r.Confidence ?? 0.0,
                r.CohortEvidencePath ?? Cohort.FloorPlan,
                r.CohortOrientationPath ?? Cohort.Without))
            .ToList();
    }

    /// <summary>
    /// Read-time per-unit numerology. Never persisted, never a grade — one annotation per unit
    /// per requested tradition, carrying prose and a verdict word, no score.
    /// </summary>
    public async Task<IReadOnlyList<UnitNumerologyAnnotation>> UnitsForAsync(
        Subject subject, IReadOnlyList<string> sets, CancellationToken ct)
    {
        IReadOnlyList<ScrapedUnit> units = [];

        if (subject.SubjectType == "floorplan")
        {
            var plans = await planSource.GetPlansAsync(subject.PropertyKey, ct);
            var plan = plans?.FirstOrDefault(p =>
                !string.IsNullOrEmpty(subject.ExternalPlanKey) && p.RentalKey == subject.ExternalPlanKey);
            units = plan?.Units ?? [];
        }
        else
        {
            var listing = await SafeListingAsync(subject.PropertyKey, ct);
            if (listing?.Numbers.UnitNumber is { Length: > 0 } unitNumber)
            {
                units = [new ScrapedUnit(unitNumber, listing.Numbers.Floor, null, null)];
            }
        }

        if (units.Count == 0) return [];
        return sets.SelectMany(set => numerology.EvaluateUnits(units, set)).ToList();
    }

    private async Task<bool> ListingExistsAsync(string propertyKey, CancellationToken ct) =>
        await SafeListingAsync(propertyKey, ct) is not null;

    private async Task<ListingResponse?> SafeListingAsync(string propertyKey, CancellationToken ct)
    {
        try
        {
            return await listings.GetListingAsync(propertyKey, ct);
        }
        catch (ListingNotFoundException)
        {
            return null;
        }
        catch (ListingSourceException e)
        {
            log.LogWarning(e, "Listing source unavailable for {PropertyKey}", propertyKey);
            return null;
        }
    }
}

/// <summary>
/// The two LDP-facing reads: the one bulk grades call per page, and the report body behind a
/// drawer open. Neither ever emits a grade into server-rendered HTML, a <c>&lt;title&gt;</c> or a
/// <c>&lt;meta&gt;</c> tag — these are JSON endpoints consumed inside a shadow root.
/// </summary>
[ApiController]
public class SubjectsController(
    SubjectsReadService read, HarmonIQDbContext db, IObjectStore store) : ControllerBase
{
    /// <summary>Drawer-open report bodies are immutable per (engine version, subject, set), so they cache hard.</summary>
    private const int ReportCacheSeconds = 60 * 60 * 24 * 30;

    [HttpGet("/api/property/{propertyKey}/subjects")]
    public async Task<IActionResult> GetSubjects(
        string propertyKey,
        [FromQuery] string? engineVersion,
        [FromQuery] string? sets,
        CancellationToken ct)
    {
        var engine = await read.ResolveEngineAsync(engineVersion, ct);
        if (engine is null)
        {
            return NotFound(new { error = $"Unknown engine version '{engineVersion}'." });
        }

        var chosen = SubjectsReadService.ParseSets(sets);
        var subjects = await read.SubjectsForAsync(propertyKey, engine, ct);
        if (subjects is null)
        {
            return NotFound(new { error = $"Unknown property '{propertyKey}'." });
        }

        var grades = new List<SubjectGrade>(subjects.Count);
        foreach (var subject in subjects)
        {
            grades.Add(new SubjectGrade(
                subject.Id,
                subject.SubjectType,
                subject.ExternalPlanKey,
                subject.PlanName,
                subject.Beds,
                subject.Baths,
                await read.SetsForAsync(subject.Id, engine, chosen, ct),
                await read.UnitsForAsync(subject, chosen, ct)));
        }

        return Ok(new SubjectsResponse(propertyKey, engine.Version, read.Mode, grades));
    }

    /// <summary>
    /// Streams the stored gzipped report body. 404 when the subject has no analysis row for this
    /// engine version — an unscored subject has no report, rather than an empty one. A body for
    /// an <c>insufficient_evidence</c> analysis exists and carries the explanation, with no score.
    /// </summary>
    [HttpGet("/api/subject/{subjectId}/report/{principleSet}")]
    public async Task<IActionResult> GetReport(
        string subjectId, string principleSet, [FromQuery] string? engineVersion, CancellationToken ct)
    {
        if (!PrincipleSets.IsKnown(principleSet))
        {
            return BadRequest(new { error = $"principleSet must be one of: {string.Join(", ", PrincipleSets.All)}." });
        }

        var engine = await read.ResolveEngineAsync(engineVersion, ct);
        if (engine is null)
        {
            return NotFound(new { error = $"Unknown engine version '{engineVersion}'." });
        }

        var row = await db.Analyses.FirstOrDefaultAsync(
            a => a.SubjectId == subjectId && a.PrincipleSet == principleSet && a.EngineVersion == engine.Version, ct);

        if (row is null)
        {
            // Demo mode has no backfill behind it; a cold drawer-open on a subject whose property
            // was never fetched still gets a body, computed the same deterministic way.
            var subject = await db.Subjects.FirstOrDefaultAsync(s => s.Id == subjectId, ct);
            if (subject is not null)
            {
                await read.SubjectsForAsync(subject.PropertyKey, engine, ct);
                row = await db.Analyses.FirstOrDefaultAsync(
                    a => a.SubjectId == subjectId && a.PrincipleSet == principleSet && a.EngineVersion == engine.Version, ct);
            }
        }

        if (row is null || row.Status is AnalysisStatuses.Failed or AnalysisStatuses.Pending)
        {
            return NotFound(new { error = $"No {principleSet} report for subject '{subjectId}' at engine version '{engine.Version}'." });
        }

        var bytes = await store.GetAsync(ReportBodyWriter.KeyFor(engine.Version, subjectId, principleSet), ct);
        if (bytes is null)
        {
            return NotFound(new { error = $"Report body missing for subject '{subjectId}'." });
        }

        Response.Headers.CacheControl = $"public, max-age={ReportCacheSeconds}, immutable";
        Response.Headers.Vary = HeaderNames.AcceptEncoding;
        if (row.ReportSha256 is { Length: > 0 } digest)
        {
            Response.Headers.ETag = $"\"{digest}\"";
        }

        // The object store holds gzip. Hand it straight through when the caller accepts gzip
        // (that is the CDN-cacheable path); otherwise inflate rather than serve an encoding the
        // caller did not ask for.
        if (AcceptsGzip())
        {
            Response.Headers.ContentEncoding = "gzip";
            return File(bytes, "application/json");
        }

        using var source = new MemoryStream(bytes);
        await using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var plain = new MemoryStream();
        await gzip.CopyToAsync(plain, ct);
        return File(plain.ToArray(), "application/json");
    }

    private bool AcceptsGzip() =>
        Request.Headers.AcceptEncoding.Any(v => v is not null && v.Contains("gzip", StringComparison.OrdinalIgnoreCase));
}
