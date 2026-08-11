using System.Text.Json;
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Persistence;
using HarmonIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HarmonIQ.Api.Controllers;

/// <summary>
/// The session-only refine path — the whole of what is left of v1's <c>/api/analyze</c>.
///
/// A reader who knows something the data does not (which way their unit actually faces, what is
/// across the street, their unit number) can ask what the same deterministic engine would say
/// with that input. The answer is <b>computed and returned, never stored</b>: no analysis row, no
/// observation, no projection, nothing filterable. That is not an implementation shortcut — a
/// renter-supplied Vastu facing is exactly the input design §2 forbids from reaching a published
/// grade, and the only way to keep that promise structurally is for this endpoint to have no
/// write path at all.
///
/// The re-grade is predictable because scoring is deterministic: the model produced the
/// observations once, and this endpoint only re-runs the rules over them.
/// </summary>
[ApiController]
public class AnalysisController(
    HarmonIQDbContext db,
    SubjectsReadService read,
    SiteAnalysisService siteService,
    NumerologyService numerology,
    ILogger<AnalysisController> log) : ControllerBase
{
    private static readonly string[] ValidOrientations =
        ["north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest"];

    [HttpPost("/api/refine")]
    public async Task<IActionResult> Refine([FromBody] RefineRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.SubjectId))
        {
            return BadRequest(new { error = "subjectId is required." });
        }
        if (!PrincipleSets.IsKnown(req.PrincipleSet))
        {
            return BadRequest(new { error = $"principleSet must be one of: {string.Join(", ", PrincipleSets.All)}." });
        }

        var orientationOverride = Normalize(req.Orientation);
        if (req.Orientation is { Length: > 0 } supplied
            && !string.Equals(supplied, "unknown", StringComparison.OrdinalIgnoreCase)
            && orientationOverride is null)
        {
            return BadRequest(new { error = "orientation is not a recognized compass direction." });
        }

        var subject = await db.Subjects.FirstOrDefaultAsync(s => s.Id == req.SubjectId, ct);
        if (subject is null)
        {
            return NotFound(new { error = $"Unknown subject '{req.SubjectId}'." });
        }

        var inputSet = await read.LatestInputSetAsync(subject.Id, ct);
        if (inputSet is null)
        {
            return NotFound(new { error = $"Subject '{subject.Id}' has no input snapshot to refine from." });
        }

        var engine = await read.ResolveEngineAsync(null, ct);
        if (engine is null)
        {
            return NotFound(new { error = "No engine version is available." });
        }

        // Perception is never re-run here: refine reads the observations already on disk. A
        // subject with none has nothing to re-grade, and inventing evidence would make the
        // "predictable re-grade" promise false.
        var refs = EvidenceManifest.Parse(subject, inputSet);
        var hashes = refs.Select(r => r.Hash).ToList();
        var observationRows = await db.Observations
            .Where(o => o.SubjectId == subject.Id && hashes.Contains(o.EvidenceHash))
            .ToListAsync(ct);

        var payloads = observationRows
            .Select(o => Deserialize<ObservationPayload>(o.PayloadJson))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

        var storedOrientation = Deserialize<SubjectOrientation>(inputSet.OrientationJson);
        var storedHasOrientation = SiteAnalysisService.HasResolvedOrientation(storedOrientation);

        var orientation = orientationOverride is null
            ? storedOrientation
            : new SubjectOrientation(subject.Id, null, orientationOverride, "annotation", 1.0, DateTimeOffset.UtcNow);

        var environment = req.Environment
            ?? Deserialize<ListingEnvironment>(inputSet.EnvironmentJson)
            ?? ListingEnvironment.AllUnknown;

        // A floor-plan snapshot stores its unit list here, not a ListingNumbers object; a failed
        // parse simply means "no subject-level numbers", which is the honest reading.
        var numbers = req.Numbers ?? Deserialize<ListingNumbers>(inputSet.NumbersJson);

        var input = new DerivationInput(
            AnalysisPipeline.EvidencePath(subject, inputSet),
            payloads,
            environment,
            orientation,
            PrincipleSets.All.ToDictionary(
                s => s, s => numerology.EvaluateSubject(numbers, s), StringComparer.Ordinal),
            Calibration.FromJson(engine.CalibrationJson));

        var derived = AnalysisDerivation.Derive(req.PrincipleSet, input, siteService);

        log.LogDebug(
            "Session-only refine for {SubjectId}/{PrincipleSet} (nothing persisted)",
            subject.Id, req.PrincipleSet);

        return Ok(new RefineResponse(
            derived.Score,
            Persisted: false,
            Notice(req.PrincipleSet, orientationOverride, storedHasOrientation)));
    }

    /// <summary>
    /// The copy that keeps a session-only number from reading as a published one. Tradition-framed,
    /// no negative superlative, and explicit that a renter-supplied facing never becomes a grade.
    /// </summary>
    private static string Notice(string principleSet, string? orientationOverride, bool storedHasOrientation)
    {
        var tradition = LocalSummary.TraditionName(principleSet);

        if (principleSet == PrincipleSets.Vastu && orientationOverride is not null && !storedHasOrientation)
        {
            return $"This {tradition} reading uses the facing you supplied ({orientationOverride}). "
                 + "It is a session-only estimate: it is not saved, it is not part of the published grade, "
                 + "and it is not used when filtering search results.";
        }

        if (orientationOverride is not null)
        {
            return $"This {tradition} reading reflects the facing you supplied ({orientationOverride}). "
                 + "It is a session-only recalculation — nothing was saved and the published grade is unchanged.";
        }

        return $"This {tradition} reading is a session-only recalculation from the details you entered. "
             + "Nothing was saved and the published grade is unchanged.";
    }

    private static string? Normalize(string? orientation)
    {
        if (string.IsNullOrWhiteSpace(orientation)) return null;
        var value = orientation.Trim().ToLowerInvariant();
        return ValidOrientations.Contains(value) ? value : null;
    }

    private static T? Deserialize<T>(string? json) where T : class
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
}
