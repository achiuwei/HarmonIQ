using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HarmonIQ.Api.Controllers;

/// <summary>
/// The versioned grades feed apartments-web consumes (design §9). HarmonIQ publishes; the
/// consumer owns its own tables, its own migration and its own feature flag — HarmonIQ never
/// writes into apartments-web's database.
///
/// The contract that makes an SRP badge and an LDP card agree is <b>version pinning</b>: a reader
/// that asks for version X gets exactly version X's projection rows, forever, even after a newer
/// version publishes. Consequently an unpublished version is a <c>409</c>, not a silent
/// substitution — a consumer must not accidentally ingest rows from a version that is still mid
/// roll-out. <c>?includeUnpublished=true</c> is the deliberate, explicit opt-in used by internal
/// tooling and by the local demo, where nothing is ever published (publishing requires
/// <c>mode = live</c>).
/// </summary>
[ApiController]
public class GradesFeedController(
    SubjectsReadService read, IPublicationService publication) : ControllerBase
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 500;

    [HttpGet("/api/feed/grades")]
    public async Task<IActionResult> GetFeed(
        [FromQuery] string? engineVersion,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        [FromQuery] bool includeUnpublished = false,
        CancellationToken ct = default)
    {
        var engine = await read.ResolveEngineAsync(engineVersion, ct);
        if (engine is null)
        {
            return NotFound(new { error = $"Unknown engine version '{engineVersion}'." });
        }

        if (engine.PublishedAt is null && !includeUnpublished)
        {
            return Conflict(new
            {
                error = $"Engine version '{engine.Version}' is not published.",
                engineVersion = engine.Version,
                hint = "Pass includeUnpublished=true to read an unpublished version's rows.",
            });
        }

        var page = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var feed = await publication.GetFeedAsync(engine.Version, cursor, page, ct);
        return Ok(feed);
    }
}
