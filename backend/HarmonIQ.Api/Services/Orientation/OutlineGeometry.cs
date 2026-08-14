namespace HarmonIQ.Api.Services.Orientation;

/// <summary>
/// A point in a local metric plane: metres east / north of a site anchor. Shape work happens
/// here rather than in lat/lon so that "rotate by θ" means what it says — a degree of longitude
/// and a degree of latitude are different distances, so rotating in lat/lon space shears the
/// outline instead of turning it.
/// </summary>
public readonly record struct PlanarPoint(double East, double North);

/// <summary>
/// One OSM building reduced to a planar ring plus the scalars the identifiability probe needs.
/// <see cref="Ring"/> is normalized: open (no repeated closing vertex), counter-clockwise, and
/// free of consecutive duplicates.
/// </summary>
public record BuildingOutline(
    string OsmType,
    long OsmId,
    IReadOnlyList<PlanarPoint> Ring,
    double AreaSquareMetres,
    PlanarPoint Centroid,
    IReadOnlyDictionary<string, string> Tags)
{
    public string? Tag(string key) => Tags.TryGetValue(key, out var v) ? v : null;

    /// <summary>Straight-line distance from the site anchor (the plane's origin) to the centroid.</summary>
    public double DistanceFromAnchorMetres => Math.Sqrt(
        Centroid.East * Centroid.East + Centroid.North * Centroid.North);

    /// <summary>
    /// Best-effort human label for reports: the OSM name, else the street address, else the id.
    /// </summary>
    public string Label
    {
        get
        {
            if (Tag("name") is { Length: > 0 } name) return name;
            var number = Tag("addr:housenumber");
            var street = Tag("addr:street");
            if (street is { Length: > 0 }) return $"{number} {street}".Trim();
            return $"{OsmType}/{OsmId}";
        }
    }
}

/// <summary>
/// Pure planar geometry for the outline identifiability probe: WGS84 → local metric plane,
/// ring normalization, area/centroid, uniform arclength resampling, and point-to-polyline
/// distance. No I/O, no configuration, no clock — every function here is deterministic.
/// </summary>
public static class OutlineGeometry
{
    /// <summary>Samples per ring used by the identifiability scan. Fixed so two rings are always
    /// compared at equal sample density and the scan's cost is bounded regardless of how finely
    /// OSM happens to have digitised a building.</summary>
    public const int ResampleCount = 512;

    private const double SemiMajorAxisMetres = 6378137.0;
    private const double EccentricitySquared = 0.00669437999014;

    /// <summary>
    /// Projects WGS84 degrees to metres east/north of an anchor, using the WGS84 meridional and
    /// prime-vertical radii at the anchor latitude. Accurate to well under a metre over the few
    /// hundred metres a single site spans, which is what matters here: a scale error is harmless
    /// to a rotation estimate, but an east/north <i>anisotropy</i> would distort the shape.
    /// </summary>
    public static PlanarPoint ToLocalPlane(double lat, double lon, double anchorLat, double anchorLon)
    {
        var phi0 = anchorLat * Math.PI / 180.0;
        var sinPhi = Math.Sin(phi0);
        var oneMinus = 1.0 - EccentricitySquared * sinPhi * sinPhi;

        // Metres per radian of latitude (meridional) and of longitude (prime vertical × cos φ).
        var metresPerRadianLat = SemiMajorAxisMetres * (1.0 - EccentricitySquared) / Math.Pow(oneMinus, 1.5);
        var metresPerRadianLon = SemiMajorAxisMetres / Math.Sqrt(oneMinus) * Math.Cos(phi0);

        var north = (lat - anchorLat) * Math.PI / 180.0 * metresPerRadianLat;
        var east = (lon - anchorLon) * Math.PI / 180.0 * metresPerRadianLon;
        return new PlanarPoint(east, north);
    }

    /// <summary>
    /// Drops the repeated closing vertex and any consecutive near-duplicates, then orients the
    /// ring counter-clockwise. Returns null when fewer than three distinct vertices survive, or
    /// when the ring encloses no meaningful area — a degenerate outline has no shape to measure,
    /// and a collinear one would otherwise sail through the vertex count and then score as
    /// perfectly symmetric, which is the most misleading answer available.
    /// </summary>
    public static IReadOnlyList<PlanarPoint>? NormalizeRing(
        IReadOnlyList<PlanarPoint> points, double toleranceMetres = 0.01)
    {
        var deduped = new List<PlanarPoint>(points.Count);
        foreach (var p in points)
        {
            if (deduped.Count > 0 && Near(deduped[^1], p, toleranceMetres)) continue;
            deduped.Add(p);
        }
        // The closing vertex is a duplicate of the first, which the pairwise pass above misses.
        while (deduped.Count > 1 && Near(deduped[0], deduped[^1], toleranceMetres))
        {
            deduped.RemoveAt(deduped.Count - 1);
        }
        if (deduped.Count < 3) return null;

        var area = SignedArea(deduped);
        if (Math.Abs(area) <= toleranceMetres * toleranceMetres) return null;

        if (area < 0) deduped.Reverse();
        return deduped;
    }

    private static bool Near(PlanarPoint a, PlanarPoint b, double tolerance) =>
        Math.Abs(a.East - b.East) <= tolerance && Math.Abs(a.North - b.North) <= tolerance;

    /// <summary>Shoelace signed area. Positive for a counter-clockwise ring.</summary>
    public static double SignedArea(IReadOnlyList<PlanarPoint> ring)
    {
        double sum = 0;
        for (var i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            sum += a.East * b.North - b.East * a.North;
        }
        return sum / 2.0;
    }

    /// <summary>
    /// Area-weighted polygon centroid — not the mean of the vertices, which would be pulled
    /// toward whichever wall OSM happened to digitise most finely.
    /// </summary>
    public static PlanarPoint Centroid(IReadOnlyList<PlanarPoint> ring)
    {
        var area = SignedArea(ring);
        if (Math.Abs(area) < 1e-9)
        {
            // Degenerate (collinear) ring: fall back to the vertex mean rather than dividing by ~0.
            return new PlanarPoint(ring.Average(p => p.East), ring.Average(p => p.North));
        }

        double cx = 0, cy = 0;
        for (var i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            var cross = a.East * b.North - b.East * a.North;
            cx += (a.East + b.East) * cross;
            cy += (a.North + b.North) * cross;
        }
        return new PlanarPoint(cx / (6.0 * area), cy / (6.0 * area));
    }

    /// <summary>
    /// Resamples the ring at uniform arclength into <paramref name="count"/> points. Uniform
    /// spacing matters: measuring at OSM's own vertices would weight a finely-traced facade far
    /// more heavily than a long plain wall, so a shape's score would depend on its mapper's
    /// diligence rather than on its geometry.
    /// </summary>
    public static IReadOnlyList<PlanarPoint> ResampleUniform(
        IReadOnlyList<PlanarPoint> ring, int count = ResampleCount)
    {
        var perimeter = 0.0;
        var edgeLengths = new double[ring.Count];
        for (var i = 0; i < ring.Count; i++)
        {
            edgeLengths[i] = Distance(ring[i], ring[(i + 1) % ring.Count]);
            perimeter += edgeLengths[i];
        }
        if (perimeter <= 0) return ring;

        var step = perimeter / count;
        var samples = new List<PlanarPoint>(count);
        var edge = 0;
        var consumed = 0.0;

        for (var k = 0; k < count; k++)
        {
            var target = k * step;
            while (edge < ring.Count - 1 && consumed + edgeLengths[edge] < target)
            {
                consumed += edgeLengths[edge];
                edge++;
            }
            var along = edgeLengths[edge] <= 0 ? 0 : (target - consumed) / edgeLengths[edge];
            var a = ring[edge];
            var b = ring[(edge + 1) % ring.Count];
            samples.Add(new PlanarPoint(
                a.East + (b.East - a.East) * along,
                a.North + (b.North - a.North) * along));
        }
        return samples;
    }

    public static double Distance(PlanarPoint a, PlanarPoint b)
    {
        var de = a.East - b.East;
        var dn = a.North - b.North;
        return Math.Sqrt(de * de + dn * dn);
    }

    /// <summary>Rotates a point clockwise by <paramref name="degrees"/> about <paramref name="pivot"/>,
    /// in the same sense as a compass bearing (0 = north, increasing eastward), so a reported rival
    /// angle reads directly as the bearing error an aligner would make.</summary>
    public static PlanarPoint Rotate(PlanarPoint p, PlanarPoint pivot, double degrees)
    {
        var rad = degrees * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var e = p.East - pivot.East;
        var n = p.North - pivot.North;
        // Clockwise in an east/north frame.
        return new PlanarPoint(
            pivot.East + e * cos + n * sin,
            pivot.North - e * sin + n * cos);
    }

    /// <summary>Shortest distance from a point to a line segment.</summary>
    public static double PointToSegmentDistance(PlanarPoint p, PlanarPoint a, PlanarPoint b)
    {
        var de = b.East - a.East;
        var dn = b.North - a.North;
        var lengthSquared = de * de + dn * dn;
        if (lengthSquared <= 0) return Distance(p, a);

        var t = ((p.East - a.East) * de + (p.North - a.North) * dn) / lengthSquared;
        t = Math.Clamp(t, 0.0, 1.0);
        return Distance(p, new PlanarPoint(a.East + t * de, a.North + t * dn));
    }

    /// <summary>
    /// Mean distance from each sample to the nearest point on the closed polyline
    /// <paramref name="ring"/> — nearest point on an <i>edge</i>, not the nearest vertex, so a
    /// sample sitting mid-wall scores zero instead of scoring the half-wall gap to a corner.
    /// </summary>
    public static double MeanDistanceToRing(
        IReadOnlyList<PlanarPoint> samples, IReadOnlyList<PlanarPoint> ring)
    {
        double total = 0;
        foreach (var s in samples)
        {
            var best = double.MaxValue;
            for (var i = 0; i < ring.Count; i++)
            {
                var d = PointToSegmentDistance(s, ring[i], ring[(i + 1) % ring.Count]);
                if (d < best) best = d;
            }
            total += best;
        }
        return samples.Count == 0 ? 0 : total / samples.Count;
    }

    /// <summary>
    /// Root-mean-square distance of the samples from the centroid — the outline's characteristic
    /// radius, used to turn an absolute mismatch in metres into a scale-free margin so that a
    /// large building is not judged more identifiable than a small one of the same shape.
    /// </summary>
    public static double CharacteristicRadiusMetres(
        IReadOnlyList<PlanarPoint> samples, PlanarPoint centroid)
    {
        if (samples.Count == 0) return 0;
        double sum = 0;
        foreach (var s in samples)
        {
            var d = Distance(s, centroid);
            sum += d * d;
        }
        return Math.Sqrt(sum / samples.Count);
    }
}
