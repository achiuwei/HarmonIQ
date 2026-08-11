using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HarmonIQ.Api.Controllers;

[ApiController]
public class ListingController(IListingService listings) : ControllerBase
{
    [HttpGet("/api/listing/{listingId}")]
    public async Task<IActionResult> GetListing(string listingId, [FromQuery] string? brand, CancellationToken ct)
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
