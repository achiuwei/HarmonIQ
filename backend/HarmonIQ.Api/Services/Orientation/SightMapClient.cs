namespace HarmonIQ.Api.Services.Orientation;

/// <summary>
/// STUB, and — as of 2026-08-11 — a stub against the wrong product. See
/// <c>docs/orientation-data-sources.md</c> for the full finding; the short version:
///
/// <b>SightMap's REST API carries no unit geometry at all.</b> Its published OpenAPI spec gives a
/// unit <c>id, asset_id, building_id, floor_id, floor_plan_id, unit_number, area</c> and postal
/// address fields — no polygon, no coordinate, no bearing. The per-unit exterior-wall bearing this
/// class was written to consume does not exist on this API. Unit geometry lives in <b>Unit Map</b>
/// (<c>api.unitmap.com</c>), a separate Engrain product, and arrives in map space (pixels) rather
/// than true north. So a key alone would not make <see cref="SightMapOrientationProvider"/> work.
///
/// The HTTP shape below is nonetheless corrected to the real published one, so that whoever picks
/// this up starts from fact rather than from the guess this file used to carry:
/// <c>API-Key</c> header (not <c>X-SightMap-Api-Key</c>) and
/// <c>/v1/assets/{asset}/multifamily/units</c> (not <c>/v1/properties/{key}/units</c>).
///
/// Zero network calls locally regardless: <see cref="IsConfigured"/> is false whenever
/// <c>SightMap:ApiKey</c>/<c>SightMap:BaseUrl</c> (or the <c>SIGHTMAP_API_KEY</c>/
/// <c>SIGHTMAP_BASE_URL</c> env vars) are unset — always, locally — and
/// <see cref="GetPlacementsAsync"/> throws immediately in that case; it never attempts a request.
/// Tests must never exercise the configured branch against a live host.
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
        // written down so the integration is a fill-in later, not a rewrite:
        //   GET {base}/v1/assets/{asset}/multifamily/units
        //   Header: API-Key: {apiKey}
        // Response links unit -> floor -> building -> floor-plan. It carries NO bearing and no
        // geometry, so this call alone cannot fill UnitPlacement.FacingDegrees — every placement
        // would come back with a null facing, which OrientationResolution reads as "no data".
        // Note also that {asset} is an Engrain asset id, not an apartments.com property key;
        // apartments-web maps between the two via Marketplace's vendor/engrain/get/mapped-unit.
        using var req = BuildPlacementsRequest(propertyKey);
        throw new SightMapUnavailableException("SightMap client stub has no live implementation.");
    }

    /// <summary>
    /// Unused-but-compiling request builder — the real published HTTP shape, referenced only from
    /// the unreachable branch above so it type-checks against <see cref="HttpClient"/>/config
    /// without ever sending a request.
    ///
    /// <c>BaseUrl</c> is the host root and must NOT carry the version segment: this method appends
    /// <c>/v1</c> itself. Configuring <c>https://api.sightmap.com/v1</c> here produced
    /// <c>/v1/v1/...</c>, which is what appsettings.json used to hold.
    /// </summary>
    private HttpRequestMessage BuildPlacementsRequest(string assetId)
    {
        var baseUrl = (cfg["SightMap:BaseUrl"] ?? cfg["SIGHTMAP_BASE_URL"] ?? "https://api.sightmap.com").TrimEnd('/');
        var apiKey = cfg["SightMap:ApiKey"] ?? cfg["SIGHTMAP_API_KEY"] ?? string.Empty;
        var req = new HttpRequestMessage(
            HttpMethod.Get, $"{baseUrl}/v1/assets/{Uri.EscapeDataString(assetId)}/multifamily/units");
        req.Headers.Add("API-Key", apiKey);
        _ = http; // HttpClient is injected for the real call path; unused in the stub.
        return req;
    }
}
