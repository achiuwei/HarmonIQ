using System.Text.RegularExpressions;
using HarmonIQ.Api.Models;
using Microsoft.Extensions.Caching.Memory;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace HarmonIQ.Api.Services;

public interface IListingService
{
    Task<ListingResponse> GetListingAsync(string listingId, CancellationToken ct);
    Task<PhotoBytes?> GetPhotoAsync(string listingId, string photoId, int? width, CancellationToken ct);
}

public class ListingService(
    SampleListingProvider sample, IMemoryCache cache, IHttpClientFactory httpFactory,
    IServiceProvider services, ILogger<ListingService> log) : IListingService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);
    public const int MaxLongEdge = 1568;

    public async Task<ListingResponse> GetListingAsync(string listingId, CancellationToken ct)
    {
        if (listingId == SampleListingProvider.ListingId) return sample.GetListing();
        if (cache.TryGetValue<ListingResponse>($"listing:{listingId}", out var cached) && cached is not null)
            return cached;
        var listing = await ScrapeListingAsync(listingId, ct); // Task 8 implements
        cache.Set($"listing:{listingId}", listing, Ttl);
        return listing;
    }

    public async Task<PhotoBytes?> GetPhotoAsync(string listingId, string photoId, int? width, CancellationToken ct)
    {
        var key = $"photo:{listingId}/{photoId}";
        if (!cache.TryGetValue<byte[]>(key, out var jpeg) || jpeg is null)
        {
            byte[]? raw = null;
            if (listingId == SampleListingProvider.ListingId)
            {
                var path = sample.GetPhotoPath(photoId);
                if (path is not null && File.Exists(path)) raw = await File.ReadAllBytesAsync(path, ct);
            }
            else
            {
                raw = await FetchRemotePhotoAsync(listingId, photoId, ct); // Task 8 implements
            }
            if (raw is null) return null;
            jpeg = DownscaleToJpeg(raw, MaxLongEdge);
            cache.Set(key, jpeg, Ttl);
        }
        return width is { } w and > 0 and < MaxLongEdge
            ? new PhotoBytes(DownscaleToJpeg(jpeg, w), "image/jpeg")
            : new PhotoBytes(jpeg, "image/jpeg");
    }

    public static byte[] DownscaleToJpeg(byte[] input, int maxEdge)
    {
        using var img = Image.Load(input);
        var longEdge = Math.Max(img.Width, img.Height);
        if (longEdge > maxEdge)
        {
            var scale = (double)maxEdge / longEdge;
            img.Mutate(x => x.Resize((int)(img.Width * scale), (int)(img.Height * scale)));
        }
        using var ms = new MemoryStream();
        img.Save(ms, new JpegEncoder { Quality = 82 });
        return ms.ToArray();
    }

    private const string UserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) HarmonIQ-Hackathon/1.0 (contact: achiuwei@costar.com)";
    private static readonly string[] InteriorWords =
        ["bedroom", "living", "kitchen", "bath", "dining", "office", "den", "closet", "interior", "room"];
    private static readonly string[] NonInteriorWords =
        ["exterior", "building", "pool", "floor plan", "floorplan", "amenity", "gym", "fitness",
         "lobby", "courtyard", "aerial", "map", "community", "playground", "garage", "view"];

    private async Task<ListingResponse> ScrapeListingAsync(string listingId, CancellationToken ct)
    {
        var slug = listingId.Replace('~', '/').Trim('/');
        var url = $"https://www.apartments.com/{slug}/";
        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        string html;
        try
        {
            using var resp = await http.GetAsync(url, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new ListingNotFoundException($"Listing '{listingId}' not found at the source.");
            if (!resp.IsSuccessStatusCode)
                throw new ListingSourceException($"Listing source returned {(int)resp.StatusCode}.");
            html = await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new ListingSourceException("Listing source could not be reached.", e);
        }

        var title = Regex.Match(html, "<title>(.*?)</title>", RegexOptions.Singleline) is { Success: true } tm
            ? System.Net.WebUtility.HtmlDecode(tm.Groups[1].Value.Split('|')[0].Trim()) : slug;

        // Address: JSON-LD block first, og:title fallback.
        var address = "";
        var ld = Regex.Match(html,
            "\"streetAddress\"\\s*:\\s*\"([^\"]+)\"[\\s\\S]{0,300}?\"addressLocality\"\\s*:\\s*\"([^\"]+)\"[\\s\\S]{0,200}?\"addressRegion\"\\s*:\\s*\"([^\"]+)\"");
        if (ld.Success) address = $"{ld.Groups[1].Value}, {ld.Groups[2].Value}, {ld.Groups[3].Value}";

        // Photos: distinct CDN image URLs in listing-page markup, capped at 12 candidates.
        var photoUrls = Regex.Matches(html, @"https://images1?\.apartments\.com/[^\s""'\\]+?\.jpg")
            .Select(m => m.Value).Distinct().Take(12).ToList();
        if (photoUrls.Count == 0)
            throw new ListingNotFoundException($"Listing '{listingId}' has no photos we can read.");

        // Captions: alt text adjacent to each URL when present.
        string? CaptionFor(string photoUrl)
        {
            var m = Regex.Match(html,
                $@"alt=""([^""]{{3,60}})""[^>]{{0,200}}{Regex.Escape(photoUrl)}|{Regex.Escape(photoUrl)}[^>]{{0,200}}alt=""([^""]{{3,60}})""");
            var alt = m.Success ? (m.Groups[1].Value != "" ? m.Groups[1].Value : m.Groups[2].Value) : null;
            return string.IsNullOrWhiteSpace(alt) ? null : System.Net.WebUtility.HtmlDecode(alt);
        }

        var scraped = new List<(string PhotoId, string SourceUrl, string? Caption)>();
        for (var i = 0; i < photoUrls.Count; i++)
            scraped.Add(($"p{i + 1}", photoUrls[i], CaptionFor(photoUrls[i])));
        cache.Set($"photo-urls:{listingId}",
            scraped.ToDictionary(p => p.PhotoId, p => p.SourceUrl), Ttl);

        // Classify: caption keywords → else batched Claude thumbnail call → else permissive default.
        var photos = new List<ListingPhoto>();
        var needsModel = new List<int>();
        for (var i = 0; i < scraped.Count; i++)
        {
            var cap = scraped[i].Caption?.ToLowerInvariant() ?? "";
            bool? interior =
                NonInteriorWords.Any(cap.Contains) ? false :
                InteriorWords.Any(cap.Contains) ? true : null;
            if (interior is null) needsModel.Add(i);
            photos.Add(new ListingPhoto(
                scraped[i].PhotoId,
                $"/api/listing/{Uri.EscapeDataString(listingId)}/photos/{scraped[i].PhotoId}?w=300",
                scraped[i].Caption, interior ?? true, false,
                interior == true ? SuggestRoomType(cap) : null));
        }
        if (needsModel.Count > 0)
        {
            var verdicts = await TryClassifyWithClaudeAsync(listingId, needsModel.Select(i => photos[i]).ToList(), ct);
            if (verdicts is not null)
                foreach (var (idx, isInterior) in needsModel.Zip(verdicts))
                    photos[idx] = photos[idx] with { Interior = isInterior };
        }

        // Auto-select interiors up to 6 by listing photo order (FR-8).
        var selectedCount = 0;
        for (var i = 0; i < photos.Count; i++)
            if (photos[i].Interior && selectedCount < 6) { photos[i] = photos[i] with { Selected = true }; selectedCount++; }

        var numbers = ExtractNumbers(title, html, address);
        var environment = string.IsNullOrEmpty(address)
            ? ListingEnvironment.AllUnknown
            : await services.GetRequiredService<IGeoContextService>().GetEnvironmentAsync(listingId, address, ct);

        return new ListingResponse(listingId, title, address, url, photos, numbers, environment);
    }

    private async Task<byte[]?> FetchRemotePhotoAsync(string listingId, string photoId, CancellationToken ct)
    {
        if (!cache.TryGetValue<Dictionary<string, string>>($"photo-urls:{listingId}", out var map) || map is null)
        {
            await GetListingAsync(listingId, ct); // repopulate after cache expiry
            cache.TryGetValue($"photo-urls:{listingId}", out map);
        }
        if (map is null || !map.TryGetValue(photoId, out var src)) return null;
        var http = httpFactory.CreateClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        try { return await http.GetByteArrayAsync(src, ct); }
        catch (Exception e) { log.LogWarning(e, "Photo fetch failed: {Url}", src); return null; }
    }

    private static string? SuggestRoomType(string caption) => caption switch
    {
        var c when c.Contains("bed") => "Bedroom",
        var c when c.Contains("living") => "Living Room",
        var c when c.Contains("kitchen") => "Kitchen",
        var c when c.Contains("bath") => "Bathroom",
        var c when c.Contains("dining") => "Dining Room",
        var c when c.Contains("office") || c.Contains("den") => "Home Office",
        _ => null,
    };

    private static ListingNumbers ExtractNumbers(string title, string html, string address)
    {
        var street = Regex.Match(address, @"^(\d+)").Groups[1].Value;
        var unit = Regex.Match(title + " " + html[..Math.Min(html.Length, 20000)],
            @"(?:Unit|Apt|#)\s*([0-9]{1,5}[A-Z]?)", RegexOptions.IgnoreCase).Groups[1].Value;
        int? floor = null;
        var unitDigits = new string(unit.Where(char.IsDigit).ToArray());
        if (unitDigits.Length >= 3 && int.TryParse(unitDigits[..^2], out var f)) floor = f;
        else if (Regex.Match(html, @"(\d{1,2})(?:st|nd|rd|th)\s+[Ff]loor") is { Success: true } fm)
            floor = int.Parse(fm.Groups[1].Value);
        return new ListingNumbers(
            string.IsNullOrEmpty(unit) ? null : unit, floor,
            string.IsNullOrEmpty(street) ? null : street);
    }

    // Batched thumbnail classification; any failure returns null (callers keep the permissive default).
    private async Task<List<bool>?> TryClassifyWithClaudeAsync(
        string listingId, List<ListingPhoto> unclassified, CancellationToken ct)
    {
        try
        {
            var claude = services.GetService<IClaudeClient>();
            if (claude is null || !claude.IsConfigured) return null;
            var content = new List<object>();
            foreach (var p in unclassified)
            {
                var bytes = await GetPhotoAsync(listingId, p.PhotoId, 300, ct);
                if (bytes is null) return null;
                content.Add(new { type = "image", source = new { type = "base64", media_type = "image/jpeg", data = Convert.ToBase64String(bytes.Data) } });
            }
            content.Add(new { type = "text", text = $"Classify each of the {unclassified.Count} photos above, in order." });
            var resp = await claude.MessagesAsync(new
            {
                model = claude.Model, max_tokens = 1024,
                tools = new[] { Prompts.ClassifyTool },
                tool_choice = new { type = "tool", name = "classify_photos" },
                messages = new object[] { new { role = "user", content } },
            }, ct);
            var input = resp.GetProperty("content").EnumerateArray()
                .First(c => c.GetProperty("type").GetString() == "tool_use").GetProperty("input");
            return input.GetProperty("categories").EnumerateArray()
                .Select(c => c.GetString() == "interior").ToList();
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Photo classification degraded to permissive default");
            return null;
        }
    }
}
