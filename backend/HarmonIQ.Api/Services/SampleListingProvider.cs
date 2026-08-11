using System.Text.Json;
using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

/// <summary>
/// Local demo listing/plan source. <c>sample</c> (a single listing, unchanged from v1 — byte-for
/// -byte identical behaviour) and <c>sample-multiplan</c> (Task 5's five-plan fixture, used to
/// exercise the multi-plan ingestion path in <see cref="SubjectService"/>) are the only two
/// property keys this provider knows.
/// </summary>
public class SampleListingProvider : IPlanSource
{
    public const string ListingId = "sample";
    public const string MultiplanPropertyKey = "sample-multiplan";

    private readonly ListingResponse _listing;
    private readonly Dictionary<string, string> _photoFiles = [];
    private readonly string _photoDir;
    private readonly IReadOnlyList<ScrapedPlan> _multiplanPlans;

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

        _multiplanPlans = LoadMultiplanPlans(Path.Combine(dataDir, "sample-multiplan-listing.json"));
    }

    public ListingResponse GetListing() => _listing;

    public string? GetPhotoPath(string photoId) =>
        _photoFiles.TryGetValue(photoId, out var f) ? Path.Combine(_photoDir, f) : null;

    /// <summary>
    /// The multi-plan property's environment. No network geocoding happens for the local demo
    /// fixture (no network for fixtures) — surroundings stay unknown until a real ingestion run
    /// resolves them, same as any other unresolved-environment case downstream.
    /// </summary>
    public ListingEnvironment GetMultiplanEnvironment() => ListingEnvironment.AllUnknown;

    public Task<IReadOnlyList<ScrapedPlan>?> GetPlansAsync(string propertyKey, CancellationToken ct) =>
        Task.FromResult(propertyKey == MultiplanPropertyKey ? _multiplanPlans : null);

    private static IReadOnlyList<ScrapedPlan> LoadMultiplanPlans(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var plans = new List<ScrapedPlan>();
        foreach (var p in doc.RootElement.GetProperty("plans").EnumerateArray())
        {
            var units = new List<ScrapedUnit>();
            foreach (var u in p.GetProperty("units").EnumerateArray())
            {
                units.Add(new ScrapedUnit(
                    u.GetProperty("unitNumber").GetString()!,
                    u.TryGetProperty("floor", out var f) && f.ValueKind != JsonValueKind.Null ? f.GetInt32() : null,
                    u.TryGetProperty("sqft", out var s) && s.ValueKind != JsonValueKind.Null ? s.GetInt32() : null,
                    u.TryGetProperty("price", out var pr) && pr.ValueKind != JsonValueKind.Null ? pr.GetDecimal() : null));
            }

            plans.Add(new ScrapedPlan(
                p.GetProperty("rentalKey").GetString()!,
                p.GetProperty("modelName").GetString()!,
                p.TryGetProperty("attachmentId", out var a) && a.ValueKind != JsonValueKind.Null ? a.GetString() : null,
                p.TryGetProperty("planImageUrl", out var img) && img.ValueKind != JsonValueKind.Null ? img.GetString() : null,
                p.TryGetProperty("beds", out var b) && b.ValueKind != JsonValueKind.Null ? b.GetInt32() : null,
                p.TryGetProperty("baths", out var ba) && ba.ValueKind != JsonValueKind.Null ? ba.GetDouble() : null,
                p.TryGetProperty("sqftMin", out var smin) && smin.ValueKind != JsonValueKind.Null ? smin.GetInt32() : null,
                p.TryGetProperty("sqftMax", out var smax) && smax.ValueKind != JsonValueKind.Null ? smax.GetInt32() : null,
                units));
        }
        return plans;
    }
}
