using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HarmonIQ.Api.Controllers;

/// <summary>
/// Listing metadata and the photo passthrough. The passthrough stays exactly as v1 left it —
/// listing media is proxied through this origin rather than hotlinked, and that is unrelated to
/// scoring. The only v2 change here is the removal of the <c>?brand=</c> parameter, which existed
/// solely so the retired <c>/api/analyze</c> contract could echo a brand back to the demo host.
/// </summary>
[ApiController]
public class ListingController(IListingService listings) : ControllerBase
{
    [HttpGet("/api/listing/{listingId}")]
    public async Task<IActionResult> GetListing(string listingId, CancellationToken ct)
    {
        try
        {
            return Ok(await listings.GetListingAsync(listingId, ct));
        }
        catch (ListingNotFoundException e) { return NotFound(new { error = e.Message }); }
        catch (ListingSourceException e) { return StatusCode(502, new { error = e.Message }); }
    }

    [HttpGet("/api/listing/{listingId}/photos/{photoId}")]
    public async Task<IActionResult> GetPhoto(string listingId, string photoId, [FromQuery] int? w, CancellationToken ct)
    {
        try
        {
            var photo = await listings.GetPhotoAsync(listingId, photoId, w, ct);
            return photo is null ? NotFound(new { error = $"Photo '{photoId}' not found." }) : File(photo.Data, photo.ContentType);
        }
        catch (ListingNotFoundException e) { return NotFound(new { error = e.Message }); }
        catch (ListingSourceException e) { return StatusCode(502, new { error = e.Message }); }
    }
}
