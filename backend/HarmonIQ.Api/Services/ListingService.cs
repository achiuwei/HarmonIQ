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

    // --- Replaced with a real implementation in Task 8 ---
    private Task<ListingResponse> ScrapeListingAsync(string listingId, CancellationToken ct) =>
        throw new ListingNotFoundException($"Listing '{listingId}' not found (live listing fetch lands in Task 8).");
    private Task<byte[]?> FetchRemotePhotoAsync(string listingId, string photoId, CancellationToken ct) =>
        Task.FromResult<byte[]?>(null);
}
