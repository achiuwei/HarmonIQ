using System.Text.Json;
using HarmonIQ.Api.Models;
using Microsoft.Extensions.Caching.Memory;

namespace HarmonIQ.Api.Services;

public interface IGeoContextService
{
    Task<ListingEnvironment> GetEnvironmentAsync(string listingId, string address, CancellationToken ct);
}

public class GeoContextService(
    IHttpClientFactory httpFactory, IConfiguration cfg, IMemoryCache cache,
    ILogger<GeoContextService> log) : IGeoContextService
{
    private const string UserAgent = "HarmonIQ-Hackathon-Demo/1.0 (contact: achiuwei@costar.com)";

    public async Task<ListingEnvironment> GetEnvironmentAsync(string listingId, string address, CancellationToken ct)
    {
        return await cache.GetOrCreateAsync($"geo:{listingId}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            try { return await BuildAsync(address, ct); }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Geo prefill failed for {Address}; returning unknowns", address);
                return ListingEnvironment.AllUnknown;
            }
        }) ?? ListingEnvironment.AllUnknown;
    }

    private async Task<ListingEnvironment> BuildAsync(string address, CancellationToken ct)
    {
        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        // 1) Geocode (Nominatim requires a UA identifying the app).
        var geoUrl = $"{cfg["Geo:GeocoderUrl"]}?q={Uri.EscapeDataString(address)}&format=json&limit=1";
        using var geoDoc = JsonDocument.Parse(await http.GetStringAsync(geoUrl, ct));
        var first = geoDoc.RootElement.EnumerateArray().FirstOrDefault();
        if (first.ValueKind != JsonValueKind.Object) return ListingEnvironment.AllUnknown;
        var lat = double.Parse(first.GetProperty("lat").GetString()!);
        var lon = double.Parse(first.GetProperty("lon").GetString()!);

        // 2) Overpass: roads/water/buildings near the point. Failures → sides stay unknown.
        var sides = new Dictionary<string, (string road, string water, string structures, string slope)>
        {
            ["north"] = ("unknown", "unknown", "unknown", "unknown"),
            ["east"] = ("unknown", "unknown", "unknown", "unknown"),
            ["south"] = ("unknown", "unknown", "unknown", "unknown"),
            ["west"] = ("unknown", "unknown", "unknown", "unknown"),
        };
        try
        {
            var q = $@"[out:json][timeout:10];
(
  way(around:120,{lat},{lon})[highway];
  way(around:250,{lat},{lon})[natural=water];
  way(around:250,{lat},{lon})[waterway~""river|stream""];
  way(around:120,{lat},{lon})[building];
);
out center tags;";
            using var resp = await http.PostAsync(cfg["Geo:OverpassUrl"],
                new FormUrlEncodedContent([new KeyValuePair<string, string>("data", q)]), ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

            // Start from "none"/"open" once Overpass answered: absence of features is information.
            foreach (var k in sides.Keys.ToList())
                sides[k] = ("none", "none", "open", sides[k].slope);

            foreach (var el in doc.RootElement.GetProperty("elements").EnumerateArray())
            {
                if (!el.TryGetProperty("center", out var c)) continue;
                var side = BearingSide(lat, lon, c.GetProperty("lat").GetDouble(), c.GetProperty("lon").GetDouble());
                var cur = sides[side];
                var tags = el.TryGetProperty("tags", out var t) ? t : default;
                string? Tag(string name) =>
                    tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty(name, out var v) ? v.GetString() : null;

                if (Tag("highway") is { } hw)
                {
                    var kind = hw switch
                    {
                        "motorway" or "trunk" => "highway",
                        "primary" or "secondary" => "busy",
                        "tertiary" or "residential" or "unclassified" or "living_street" => "quiet",
                        _ => (string?)null,
                    };
                    // T-junction detection needs topology we don't fetch — leave that value to the Refine drawer.
                    if (kind is not null && Rank(kind) > Rank(cur.road)) cur.road = kind;
                }
                if (Tag("natural") == "water" || Tag("waterway") is not null)
                {
                    var w = Tag("waterway") is not null ? "river"
                          : Tag("water") == "lake" ? "lake" : "pond";
                    if (cur.water == "none") cur.water = w;
                }
                if (Tag("building") is not null)
                {
                    var levels = int.TryParse(Tag("building:levels"), out var l) ? l : 0;
                    var s = levels >= 8 ? "taller-building" : "similar";
                    if (cur.structures == "open" || (s == "taller-building" && cur.structures == "similar"))
                        cur.structures = s;
                }
                sides[side] = cur;
            }
        }
        catch (Exception ex) { log.LogWarning(ex, "Overpass lookup failed"); }

        // 3) Elevation: center + 4 points ~150 m out → slope per side. Failure → slope unknown.
        try
        {
            const double dLat = 0.00135; // ≈150 m
            var dLon = dLat / Math.Cos(lat * Math.PI / 180);
            double[] lats = [lat, lat + dLat, lat, lat - dLat, lat];
            double[] lons = [lon, lon, lon + dLon, lon, lon - dLon]; // center, N, E, S, W
            var url = $"{cfg["Geo:ElevationUrl"]}?latitude={string.Join(',', lats)}&longitude={string.Join(',', lons)}";
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
            var el = doc.RootElement.GetProperty("elevation").EnumerateArray().Select(x => x.GetDouble()).ToArray();
            string[] order = ["north", "east", "south", "west"];
            for (var i = 0; i < 4; i++)
            {
                var diff = el[i + 1] - el[0];
                var slope = diff > 2 ? "rises" : diff < -2 ? "falls" : "level";
                var cur = sides[order[i]];
                sides[order[i]] = (cur.road, cur.water, cur.structures, slope);
            }
        }
        catch (Exception ex) { log.LogWarning(ex, "Elevation lookup failed"); }

        SideEnvironment S(string k) => new(sides[k].road, sides[k].water, sides[k].structures, sides[k].slope);
        return new ListingEnvironment(S("north"), S("east"), S("south"), S("west"));
    }

    private static int Rank(string road) => road switch
    { "highway" => 4, "busy" => 3, "quiet" => 2, "none" => 1, _ => 0 };

    private static string BearingSide(double lat0, double lon0, double lat1, double lon1)
    {
        var dy = lat1 - lat0;
        var dx = (lon1 - lon0) * Math.Cos(lat0 * Math.PI / 180);
        var bearing = Math.Atan2(dx, dy) * 180 / Math.PI; // 0 = north, 90 = east
        return bearing switch
        {
            >= -45 and < 45 => "north",
            >= 45 and < 135 => "east",
            >= -135 and < -45 => "west",
            _ => "south",
        };
    }
}
