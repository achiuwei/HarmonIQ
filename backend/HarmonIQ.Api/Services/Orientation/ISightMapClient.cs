namespace HarmonIQ.Api.Services.Orientation;

/// <summary>
/// Thin client over the SightMap partner API. Returns the per-unit placement projection for a
/// property so <see cref="OrientationResolution"/> can apply the Q5 concentration rule.
/// </summary>
public interface ISightMapClient
{
    Task<IReadOnlyList<UnitPlacement>> GetPlacementsAsync(string propertyKey, CancellationToken ct);
}

/// <summary>
/// Thrown when SightMap cannot be reached or is not configured (no API key on this machine,
/// always, locally). Caught by <see cref="SightMapOrientationProvider"/> and converted to a
/// <c>null</c> resolution — never surfaced as an error to callers.
/// </summary>
public class SightMapUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
