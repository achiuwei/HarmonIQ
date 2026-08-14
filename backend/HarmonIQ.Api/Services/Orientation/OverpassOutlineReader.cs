using System.Text.Json;

namespace HarmonIQ.Api.Services.Orientation;

/// <summary>
/// What a fixture read produced, including what it could not use. The skipped counts are part of
/// the result rather than a log line because a silently dropped building changes the answer: the
/// probe would report on a smaller set than the one that exists and look just as confident.
/// </summary>
public record OverpassReadResult(
    IReadOnlyList<BuildingOutline> Outlines,
    int SkippedNoGeometry,
    int SkippedDegenerate,
    IReadOnlyList<string> SkippedLabels);

/// <summary>
/// Reads Overpass API JSON (<c>[out:json]</c> + <c>out geom</c>) into planar
/// <see cref="BuildingOutline"/>s about a site anchor.
///
/// <para><b>On <c>out geom tags</c>.</b> Overpass's <c>tags</c> output modifier prints tags but
/// suppresses the node/member lists, so <c>geom</c> has nothing to hang coordinates on for a
/// relation and emits only a bounding box. Ways still carry their coordinates, so the omission is
/// easy to miss — a fixture fetched that way silently loses every multipolygon building. Use plain
/// <c>out geom;</c>, and note that this reader reports such elements via
/// <see cref="OverpassReadResult.SkippedNoGeometry"/> rather than ignoring them.</para>
///
/// Relation members are read from the <c>members</c> array, taking <c>outer</c> ways only; inner
/// rings (courtyards, holes) are not part of the exterior outline being matched. Multi-part outer
/// rings are not stitched — a relation whose exterior arrives as several fragments is skipped as
/// degenerate rather than joined in an arbitrary order, which would invent a shape.
/// </summary>
public static class OverpassOutlineReader
{
    public static OverpassReadResult Read(string json, double anchorLat, double anchorLon)
    {
        using var doc = JsonDocument.Parse(json);
        var outlines = new List<BuildingOutline>();
        var skippedLabels = new List<string>();
        var noGeometry = 0;
        var degenerate = 0;

        if (!doc.RootElement.TryGetProperty("elements", out var elements))
        {
            return new OverpassReadResult(outlines, 0, 0, skippedLabels);
        }

        foreach (var element in elements.EnumerateArray())
        {
            var type = element.TryGetProperty("type", out var t) ? t.GetString() ?? "?" : "?";
            var id = element.TryGetProperty("id", out var i) && i.TryGetInt64(out var parsed) ? parsed : 0L;
            var tags = ReadTags(element);

            var raw = ReadGeometry(element, type, anchorLat, anchorLon);
            if (raw is null)
            {
                noGeometry++;
                skippedLabels.Add($"{type}/{id} (no geometry)");
                continue;
            }

            var ring = OutlineGeometry.NormalizeRing(raw);
            if (ring is null)
            {
                degenerate++;
                skippedLabels.Add($"{type}/{id} (degenerate ring)");
                continue;
            }

            outlines.Add(new BuildingOutline(
                type, id, ring,
                Math.Abs(OutlineGeometry.SignedArea(ring)),
                OutlineGeometry.Centroid(ring),
                tags));
        }

        return new OverpassReadResult(outlines, noGeometry, degenerate, skippedLabels);
    }

    private static Dictionary<string, string> ReadTags(JsonElement element)
    {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!element.TryGetProperty("tags", out var node) || node.ValueKind != JsonValueKind.Object)
        {
            return tags;
        }
        foreach (var tag in node.EnumerateObject())
        {
            if (tag.Value.ValueKind == JsonValueKind.String)
            {
                tags[tag.Name] = tag.Value.GetString() ?? string.Empty;
            }
        }
        return tags;
    }

    private static List<PlanarPoint>? ReadGeometry(
        JsonElement element, string type, double anchorLat, double anchorLon)
    {
        if (type == "way")
        {
            return ReadPointArray(element, "geometry", anchorLat, anchorLon);
        }

        if (type != "relation" ||
            !element.TryGetProperty("members", out var members) ||
            members.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var outerRings = new List<List<PlanarPoint>>();
        foreach (var member in members.EnumerateArray())
        {
            var role = member.TryGetProperty("role", out var r) ? r.GetString() : null;
            if (role != "outer") continue;

            var points = ReadPointArray(member, "geometry", anchorLat, anchorLon);
            if (points is { Count: >= 3 }) outerRings.Add(points);
        }

        // Exactly one closed outer way is the common multipolygon-building case. Several fragments
        // would need stitching by shared endpoints; refusing is honest, joining blindly is not.
        return outerRings.Count == 1 ? outerRings[0] : null;
    }

    private static List<PlanarPoint>? ReadPointArray(
        JsonElement owner, string property, double anchorLat, double anchorLon)
    {
        if (!owner.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var points = new List<PlanarPoint>();
        foreach (var node in array.EnumerateArray())
        {
            if (node.TryGetProperty("lat", out var lat) && node.TryGetProperty("lon", out var lon))
            {
                points.Add(OutlineGeometry.ToLocalPlane(
                    lat.GetDouble(), lon.GetDouble(), anchorLat, anchorLon));
            }
        }
        return points.Count == 0 ? null : points;
    }
}
