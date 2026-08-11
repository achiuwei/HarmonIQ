namespace HarmonIQ.Api.Services.Orientation;

/// <summary>
/// STUB. No partner API key exists on this machine — the hackathon has no SightMap
/// relationship yet. This class documents the intended HTTP shape (`{base}/v1/…`, API-key
/// header, unit↔floor↔building↔floor-plan linkage) so the real integration is a fill-in later,
/// not a rewrite, while guaranteeing zero network calls locally: <see cref="IsConfigured"/> is
/// false whenever <c>SightMap:ApiKey</c>/<c>SightMap:BaseUrl</c> (or the
/// <c>SIGHTMAP_API_KEY</c>/<c>SIGHTMAP_BASE_URL</c> env vars) are unset — always, locally — and
/// <see cref="GetPlacementsAsync"/> throws immediately in that case; it never attempts a
/// request. Tests must never exercise the configured branch against a live host.
/// </summary>
public class SightMapClient(HttpClient http, IConfiguration cfg) : ISightMapClient
{
    public bool IsConfigured =>
        !string.IsNullOrEmpty(cfg["SightMap:ApiKey"] ?? cfg["SIGHTMAP_API_KEY"]) &&
        !string.IsNullOrEmpty(cfg["SightMap:BaseUrl"] ?? cfg["SIGHTMAP_BASE_URL"]);

    public Task<IReadOnlyList<UnitPlacement>> GetPlacementsAsync(string propertyKey, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new SightMapUnavailableException(
                "SightMap API key / base URL not configured — no partner relationship exists locally.");

        // Unreachable locally (IsConfigured is always false above), but kept compiling and
        // written down so the partner integration is a fill-in later, not a rewrite, once a key
        // exists:
        //   GET {base}/v1/properties/{propertyKey}/units
        //   Header: X-SightMap-Api-Key: {apiKey}
        // Response links unit -> floor -> building -> floor-plan, with a per-unit exterior-wall
        // bearing (true-north degrees) when SightMap has resolved it for that building.
        using var req = BuildPlacementsRequest(propertyKey);
        throw new SightMapUnavailableException("SightMap client stub has no live implementation.");
    }

    /// <summary>
    /// Unused-but-compiling request builder — the documented HTTP shape for the real
    /// integration, referenced only from the unreachable branch above so it type-checks against
    /// <see cref="HttpClient"/>/config without ever sending a request.
    /// </summary>
    private HttpRequestMessage BuildPlacementsRequest(string propertyKey)
    {
        var baseUrl = (cfg["SightMap:BaseUrl"] ?? cfg["SIGHTMAP_BASE_URL"] ?? "https://api.sightmap.com").TrimEnd('/');
        var apiKey = cfg["SightMap:ApiKey"] ?? cfg["SIGHTMAP_API_KEY"] ?? string.Empty;
        var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/v1/properties/{Uri.EscapeDataString(propertyKey)}/units");
        req.Headers.Add("X-SightMap-Api-Key", apiKey);
        _ = http; // HttpClient is injected for the real call path; unused in the stub.
        return req;
    }
}
