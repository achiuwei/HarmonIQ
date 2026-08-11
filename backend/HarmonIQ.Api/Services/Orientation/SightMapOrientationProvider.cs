using Microsoft.Extensions.Logging;

namespace HarmonIQ.Api.Services.Orientation;

/// <summary>
/// Live orientation path: pulls unit placements from SightMap and applies the Q5 concentration
/// rule. Currently unreachable end-to-end since <see cref="SightMapClient"/> is a stub with no
/// partner key — <see cref="SightMapUnavailableException"/> is caught here and converted to
/// <c>null</c> so callers cannot distinguish "SightMap unreachable" from "not covered"; both are
/// the without-orientation path. A >45° disagreement between a resolved facing and any footprint
/// bearing is logged only (design §4/§1 Q4) — it is never fed back into the resolution.
/// </summary>
public class SightMapOrientationProvider(ISightMapClient client, ILogger<SightMapOrientationProvider> log)
    : IOrientationProvider
{
    public async Task<SubjectOrientation?> ResolveAsync(string propertyKey, string subjectId, CancellationToken ct)
    {
        try
        {
            var placements = await client.GetPlacementsAsync(propertyKey, ct);
            return OrientationResolution.Resolve(subjectId, placements, DateTimeOffset.UtcNow);
        }
        catch (SightMapUnavailableException ex)
        {
            log.LogInformation("SightMap unavailable for {PropertyKey}: {Message}", propertyKey, ex.Message);
            return null;
        }
    }
}
