namespace HarmonIQ.Api.Services.Orientation;

/// <summary>
/// Implements design §1 Q5 / §4: bucket a plan's placed units into the four cardinal sectors
/// (45° wide, centered on N/E/S/W). If 80% or more of the units that have a facing fall in one
/// sector, that sector is the plan's facing and <c>Confidence</c> equals the concentration ratio.
/// Otherwise the plan has no resolvable orientation (<c>Source = "none"</c>,
/// <c>FacingDegrees = null</c>) and callers must take the without-orientation path.
///
/// This is a pure deterministic function — no model call ever influences it (Global Constraints:
/// "no model call may influence a score").
/// </summary>
public static class OrientationResolution
{
    public const double ConcentrationThreshold = 0.8;

    /// <summary>
    /// Cardinal sector for a bearing in degrees true-north, clockwise (0/360 = north).
    /// Sectors are 90° wide, centered on the cardinal: N = [315, 45), E = [45, 135),
    /// S = [135, 225), W = [225, 315).
    /// </summary>
    public static string CardinalOf(double degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        if (normalized >= 315 || normalized < 45) return "north";
        if (normalized < 135) return "east";
        if (normalized < 225) return "south";
        return "west";
    }

    /// <summary>
    /// Resolves a plan's orientation from its placed units.
    /// - Units with no <see cref="UnitPlacement.FacingDegrees"/> are excluded from the
    ///   denominator entirely (they neither help nor hurt concentration).
    /// - Zero units with a facing (including an empty placement list) → <c>null</c>: no
    ///   resolvable data, same shape as "not covered" from the provider's perspective.
    /// - ≥80% concentration in one sector → resolved, <c>Source = "sightmap"</c>,
    ///   <c>Confidence</c> = concentration ratio, <c>FacingDegrees</c> = circular mean of the
    ///   winning sector's bearings.
    /// - Otherwise → <c>Source = "none"</c>, <c>FacingDegrees = null</c>, <c>Cardinal = null</c>,
    ///   <c>Confidence</c> = the (failed) concentration ratio for diagnostics.
    /// </summary>
    public static SubjectOrientation? Resolve(
        string subjectId, IReadOnlyList<UnitPlacement> placements, DateTimeOffset now)
    {
        var faced = placements.Where(p => p.FacingDegrees.HasValue).ToList();
        if (faced.Count == 0) return null;

        var bySector = faced
            .GroupBy(p => CardinalOf(p.FacingDegrees!.Value))
            .OrderByDescending(g => g.Count())
            .ToList();

        var top = bySector[0];
        var concentration = (double)top.Count() / faced.Count;

        if (concentration >= ConcentrationThreshold)
        {
            var facing = CircularMeanDegrees(top.Select(p => p.FacingDegrees!.Value));
            return new SubjectOrientation(subjectId, facing, top.Key, "sightmap", concentration, now);
        }

        return new SubjectOrientation(subjectId, null, null, "none", concentration, now);
    }

    /// <summary>
    /// Circular mean so bearings that straddle 0°/360° (e.g. 350° and 10°) average to 0°,
    /// not 180°, as a naive arithmetic mean would produce.
    /// </summary>
    private static double CircularMeanDegrees(IEnumerable<double> degrees)
    {
        double sin = 0, cos = 0;
        foreach (var d in degrees)
        {
            var rad = d * Math.PI / 180.0;
            sin += Math.Sin(rad);
            cos += Math.Cos(rad);
        }
        var angle = Math.Atan2(sin, cos) * 180.0 / Math.PI;
        return (angle + 360) % 360;
    }
}
