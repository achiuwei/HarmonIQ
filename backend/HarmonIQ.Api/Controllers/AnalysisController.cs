using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HarmonIQ.Api.Controllers;

[ApiController]
public class AnalysisController(
    IListingService listings, IClaudeClient claude, ClaudeAnalysisService live,
    MockAnalysisService mock, SiteAnalysisService siteSvc, NumerologyService numerologySvc,
    ILogger<AnalysisController> log) : ControllerBase
{
    private static readonly string[] ValidSystems = ["both", "fengshui", "vastu"];
    private static readonly string[] ValidOrientations =
        ["unknown", "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest"];

    [HttpPost("/api/analyze")]
    public async Task<IActionResult> Analyze([FromBody] AnalyzeRequest req, CancellationToken ct)
    {
        // --- Validation (FR-34) ---
        if (string.IsNullOrWhiteSpace(req.ListingId))
            return BadRequest(new { error = "listingId is required." });
        if (req.Photos is null || req.Photos.Count == 0)
            return BadRequest(new { error = "Select at least one photo to analyze." });
        if (req.Photos.Count > 6)
            return BadRequest(new { error = "At most 6 photos can be analyzed per report." });
        var systems = string.IsNullOrEmpty(req.Systems) ? "both" : req.Systems;
        if (!ValidSystems.Contains(systems))
            return BadRequest(new { error = $"systems must be one of: {string.Join(", ", ValidSystems)}." });
        var orientation = string.IsNullOrEmpty(req.Orientation) ? "unknown" : req.Orientation;
        if (!ValidOrientations.Contains(orientation))
            return BadRequest(new { error = "orientation is not a recognized compass direction." });

        ListingResponse listing;
        try { listing = await listings.GetListingAsync(req.ListingId, ct); }
        catch (ListingNotFoundException e) { return BadRequest(new { error = e.Message }); }
        catch (ListingSourceException e) { return StatusCode(502, new { error = e.Message }); }

        var known = listing.Photos.Select(p => p.PhotoId).ToHashSet();
        var unknown = req.Photos.FirstOrDefault(p => !known.Contains(p.PhotoId));
        if (unknown is not null)
            return BadRequest(new { error = $"Unknown photoId '{unknown.PhotoId}'." });

        // --- Deterministic lenses run concurrently with room analysis (NFR-1) ---
        var numbers = req.Numbers ?? listing.Numbers;
        var environment = req.Environment ?? listing.Environment;
        var numerology = numerologySvc.Evaluate(numbers, systems);
        var site = siteSvc.Analyze(environment, orientation, systems);

        // --- Room photos ---
        List<RoomAnalysis> rooms;
        string mode;
        string? modelId = null;
        string? notice = null;
        if (claude.IsConfigured)
        {
            try
            {
                var inputs = new List<RoomInput>();
                foreach (var p in req.Photos)
                {
                    var bytes = await listings.GetPhotoAsync(req.ListingId, p.PhotoId, null, ct);
                    if (bytes is null)
                        return StatusCode(502, new { error = $"Photo '{p.PhotoId}' could not be fetched from the listing source." });
                    inputs.Add(new RoomInput(p.PhotoId, p.RoomType, bytes.Data));
                }
                var siteTask = live.RephraseSiteAsync(site, ct);
                rooms = await live.AnalyzeRoomsAsync(inputs, systems, orientation, ct);
                site = await siteTask;
                mode = "live";
                modelId = claude.Model;
            }
            catch (ClaudeUnavailableException e)
            {
                log.LogWarning(e, "Claude unavailable; serving demo analysis");
                rooms = mock.AnalyzeRooms(req.Photos, systems);
                mode = "demo";
                notice = "The Claude endpoint was unavailable, so this is a built-in demonstration analysis.";
            }
        }
        else
        {
            rooms = mock.AnalyzeRooms(req.Photos, systems);
            mode = "demo";
            notice = "No Claude API key is configured, so this is a built-in demonstration analysis.";
        }

        // --- Merge (FR-25) ---
        var overall = ScoreMath.Overall(rooms, site, numerology.ScoreAdjustment);
        var summary = mode == "live"
            ? await live.SummarizeAsync(rooms, site, numerology, ct)
            : ScoreMath.LocalSummary(rooms, site, numerology);

        return Ok(new AnalyzeResponse(
            mode, modelId, notice,
            new ListingSummary(listing.ListingId, listing.Title, listing.Address, listing.Url),
            new AnalysisResult(
                overall, ScoreMath.Grade(overall), summary,
                ScoreMath.AverageElements(rooms), rooms, site, numerology)));
    }
}
