using System.Text.Json;
using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

public class SampleListingProvider
{
    public const string ListingId = "sample";
    private readonly ListingResponse _listing;
    private readonly Dictionary<string, string> _photoFiles = [];
    private readonly string _photoDir;

    public SampleListingProvider(IWebHostEnvironment env)
    {
        var dataDir = Path.Combine(env.ContentRootPath, "Data");
        _photoDir = Path.Combine(dataDir, "sample-photos");
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDir, "sample-listing.json")));
        var root = doc.RootElement;

        var photos = new List<ListingPhoto>();
        foreach (var p in root.GetProperty("photos").EnumerateArray())
        {
            var id = p.GetProperty("photoId").GetString()!;
            _photoFiles[id] = p.GetProperty("file").GetString()!;
            photos.Add(new ListingPhoto(
                id, $"/api/listing/{ListingId}/photos/{id}?w=300",
                p.GetProperty("caption").GetString(),
                p.GetProperty("interior").GetBoolean(),
                p.GetProperty("selected").GetBoolean(),
                p.GetProperty("suggestedRoomType").ValueKind == JsonValueKind.Null
                    ? null : p.GetProperty("suggestedRoomType").GetString()));
        }
        _listing = new ListingResponse(
            ListingId,
            root.GetProperty("title").GetString()!,
            root.GetProperty("address").GetString()!,
            root.GetProperty("url").GetString()!,
            photos,
            root.GetProperty("numbers").Deserialize<ListingNumbers>(Json.Options)!,
            root.GetProperty("environment").Deserialize<ListingEnvironment>(Json.Options)!);
    }

    public ListingResponse GetListing() => _listing;

    public string? GetPhotoPath(string photoId) =>
        _photoFiles.TryGetValue(photoId, out var f) ? Path.Combine(_photoDir, f) : null;
}
