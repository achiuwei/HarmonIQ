using HarmonIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HarmonIQ.Api.Controllers;

/// <summary>
/// The mock-SRP-facing search surface (design §8, R4/R7): a synonym-aware typeahead suggestion
/// and the HarmonIQ filter itself. Both endpoints read only stored, published grades — see
/// <see cref="SearchService"/> for why neither can ever trigger a vision call.
/// </summary>
[ApiController]
public class SearchController(ISearchService search) : ControllerBase
{
    /// <summary>404 (not 200 with a null body) when the query isn't a recognized synonym — there is no chip to show.</summary>
    [HttpGet("/api/search/suggest")]
    public async Task<IActionResult> Suggest([FromQuery] string? q, CancellationToken ct)
    {
        var suggestion = await search.SuggestAsync(q, ct);
        return suggestion is null
            ? NotFound(new { error = $"No HarmonIQ synonym match for '{q}'." })
            : Ok(suggestion);
    }

    [HttpGet("/api/search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? sets,
        [FromQuery] string? min,
        [FromQuery] string? engineVersion,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        try
        {
            var result = await search.SearchAsync(sets, min, engineVersion, limit, ct);
            return Ok(result);
        }
        catch (SearchEngineVersionNotFoundException e)
        {
            return NotFound(new { error = e.Message });
        }
        catch (SearchEngineVersionNotPublishedException e)
        {
            return Conflict(new
            {
                error = e.Message,
                hint = "Search reads only published grades; there is no includeUnpublished escape hatch here.",
            });
        }
    }
}
