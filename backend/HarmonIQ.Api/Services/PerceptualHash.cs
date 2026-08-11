using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace HarmonIQ.Api.Services;

/// <summary>
/// Perceptual (not byte) image hashing — an average-hash (aHash) over an 8x8 downscaled
/// grayscale render. Two images that look the same but differ byte-for-byte (re-encode,
/// re-compress, minor crop) hash identically or near-identically; a byte hash would not.
/// Used only for the content-signature fallback (design §5) when a plan card has no
/// <c>data-rentalkey</c>, combined with beds/baths — never as the primary plan identity.
/// </summary>
public static class PerceptualHash
{
    private const int HashSize = 8;

    /// <summary>Returns a 16-character lowercase hex string encoding the 64-bit aHash.</summary>
    public static string Compute(ReadOnlySpan<byte> imageBytes)
    {
        using var image = Image.Load<L8>(imageBytes);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(HashSize, HashSize),
            Sampler = KnownResamplers.Bicubic,
        }));

        var pixels = new byte[HashSize * HashSize];
        long sum = 0;
        var idx = 0;
        for (var y = 0; y < HashSize; y++)
        {
            for (var x = 0; x < HashSize; x++)
            {
                var v = image[x, y].PackedValue;
                pixels[idx++] = v;
                sum += v;
            }
        }

        var average = sum / (double)pixels.Length;
        ulong hash = 0;
        for (var i = 0; i < pixels.Length; i++)
        {
            if (pixels[i] >= average)
            {
                hash |= 1UL << i;
            }
        }

        return hash.ToString("x16");
    }
}
