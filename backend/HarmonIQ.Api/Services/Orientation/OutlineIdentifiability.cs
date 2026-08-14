namespace HarmonIQ.Api.Services.Orientation;

/// <summary>One candidate rotation and how badly the outline matches itself under it.</summary>
public record RotationScore(
    double AngleDegrees,
    double MismatchMetres,
    double NormalizedMismatch);

/// <summary>
/// A competing rotation hypothesis: a local minimum of the mismatch curve lying outside the basin
/// around the true rotation, i.e. a genuinely different answer an aligner could return.
/// </summary>
public record RivalRotation(
    double AngleDegrees,
    double MismatchMetres,
    double NormalizedMismatch);

/// <summary>
/// The rotational self-similarity of one footprint, split into the two questions that actually
/// matter and that a single "margin" number conflates.
///
/// <list type="number">
/// <item><b>Precision</b> — how tightly is the true rotation pinned? Read via
/// <see cref="PrecisionHalfWidthDegrees"/>. This must stay well inside 45°, because
/// <see cref="OrientationResolution"/> buckets a bearing into 90°-wide cardinal sectors, so an
/// error beyond 45° moves a unit into the wrong sector.</item>
/// <item><b>Ambiguity</b> — is there a <i>distant</i> rotation that fits about as well? Read via
/// <see cref="BestRival"/> and especially <see cref="FlipRival"/>. This is the dangerous one: a
/// 180° rival inverts the entire Vastu directional scheme and yields confidently backwards grades
/// rather than an honest absence.</item>
/// </list>
///
/// <para><b>Why there is no peak-versus-rival ratio here.</b> §5's ≥2.0 gate was defined on a
/// constellation scorer where the true match is itself noisy. This scan compares a footprint to
/// itself, so the truth scores exactly 0m by construction and any ratio against it is degenerate.
/// The honest substitute is <see cref="DiscriminationRatio"/>, which measures the best rival's
/// mismatch against an explicitly supplied estimate of how far a real Unit Map floor outline
/// departs from an OSM roof trace. That estimate is a stated assumption, not a measurement —
/// making it a parameter keeps it visible instead of burying it in a threshold.</para>
/// </summary>
public record IdentifiabilityResult(
    double CharacteristicRadiusMetres,
    double CentralBasinHalfWidthDegrees,
    IReadOnlyList<RivalRotation> DistinctRivals,
    double TurningRivalAngleDegrees,
    double TurningRivalResidualDegrees,
    IReadOnlyList<RotationScore> Curve)
{
    /// <summary>The most confusable distinct rotation, or null when the curve has no local minimum
    /// outside the central basin (a shape with one unambiguous fit).</summary>
    public RivalRotation? BestRival => DistinctRivals.Count > 0 ? DistinctRivals[0] : null;

    /// <summary>The rival nearest 180°, if any is within 20° of it — the grade-inverting flip.</summary>
    public RivalRotation? FlipRival => DistinctRivals
        .Where(r => Math.Abs(OutlineIdentifiability.AngularSeparationDegrees(r.AngleDegrees, 180.0)) <= 20.0)
        .OrderBy(r => r.MismatchMetres)
        .FirstOrDefault();

    /// <summary>
    /// How far the rotation can be wrong before mismatch exceeds
    /// <paramref name="toleranceMetres"/> — the angular precision the outline supports at that
    /// noise level. Returns the first crossing walking outward from 0°.
    /// </summary>
    public double PrecisionHalfWidthDegrees(double toleranceMetres)
    {
        foreach (var point in Curve.Where(p => p.AngleDegrees > 0).OrderBy(p => p.AngleDegrees))
        {
            if (point.MismatchMetres > toleranceMetres) return point.AngleDegrees;
        }
        return 180.0;
    }

    /// <summary>
    /// Best rival's mismatch relative to the assumed real-world outline disagreement. Below ~1 the
    /// rival is indistinguishable from noise; comfortably above it, the rival can be rejected.
    /// Returns <see cref="double.PositiveInfinity"/> when no distinct rival exists.
    /// </summary>
    public double DiscriminationRatio(double assumedOutlineDisagreementMetres)
    {
        if (assumedOutlineDisagreementMetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(assumedOutlineDisagreementMetres));
        return BestRival is null
            ? double.PositiveInfinity
            : BestRival.MismatchMetres / assumedOutlineDisagreementMetres;
    }
}

/// <summary>
/// Measures the <b>ceiling</b> on recovering true north by matching a floor outline to a building
/// footprint: how rotationally distinct is the footprint from itself?
///
/// The achievable margin is a property of the ground-truth footprint alone. Rotate the real
/// footprint by θ and score it against the unrotated original; θ=0 matches perfectly by
/// construction, and any other rotation that also scores low is a hypothesis no aligner could rule
/// out <i>even with a perfect input</i>. A real Engrain outline can only do worse, so a failure here
/// is decisive while a pass is only a precondition.
///
/// Two independent metrics, because they fail differently and their agreement is the finding:
/// <list type="bullet">
/// <item><b>Mean symmetric mismatch</b> (metres) — what an aligner actually minimizes when fitting
/// an outline onto a footprint.</item>
/// <item><b>Turning-function residual</b> (degrees) — pure shape, invariant to scale and position,
/// found by seeking an arclength shift under which the tangent-angle profile reproduces itself.
/// Detects n-fold rotational symmetry without reference to any distance.</item>
/// </list>
///
/// <para><b>Two ways this overstates identifiability, both deliberate.</b> Translation is fixed by
/// making the centroids coincide rather than re-optimized per θ; letting an aligner also slide the
/// outline could only find a <i>better</i> rival fit. And the scan compares OSM to itself, whereas a
/// real Unit Map floor outline differs from an OSM roof trace (overhangs, balconies, podium versus
/// tower), which adds mismatch at the true rotation only. Both biases run the same direction, so
/// every number here is an upper bound.</para>
///
/// Pure and deterministic — no model call, no clock, no I/O, consistent with the Global Constraint
/// that no model call may influence a score.
/// </summary>
public static class OutlineIdentifiability
{
    public const double DefaultStepDegrees = 1.0;

    /// <summary>
    /// Rotational symmetry is inherently a large-angle phenomenon, so the turning-function scan
    /// ignores shifts implying a rotation smaller than this. Unlike the mismatch curve — whose
    /// central basin is found from the data — this one has no shape-derived boundary to use.
    /// </summary>
    public const double MinimumDistinctRotationDegrees = 15.0;

    /// <summary>
    /// Signed separation between two bearings, in (-180, 180]. Wraps, so 350° and 10° are 20° apart
    /// rather than 340°.
    /// </summary>
    public static double AngularSeparationDegrees(double a, double b) =>
        ((a - b) % 360.0 + 540.0) % 360.0 - 180.0;

    public static IdentifiabilityResult Measure(
        BuildingOutline outline, double stepDegrees = DefaultStepDegrees)
    {
        if (stepDegrees <= 0) throw new ArgumentOutOfRangeException(nameof(stepDegrees));

        var ring = outline.Ring;
        var pivot = outline.Centroid;
        var samples = OutlineGeometry.ResampleUniform(ring);
        var radius = OutlineGeometry.CharacteristicRadiusMetres(samples, pivot);

        var curve = new List<RotationScore>();
        for (var angle = 0.0; angle < 360.0; angle += stepDegrees)
        {
            var rotatedSamples = samples.Select(p => OutlineGeometry.Rotate(p, pivot, angle)).ToList();
            var rotatedRing = ring.Select(p => OutlineGeometry.Rotate(p, pivot, angle)).ToList();

            // Symmetrized: a rotation is only a real rival if the match holds in both directions.
            var forward = OutlineGeometry.MeanDistanceToRing(rotatedSamples, ring);
            var backward = OutlineGeometry.MeanDistanceToRing(samples, rotatedRing);
            var mismatch = (forward + backward) / 2.0;

            curve.Add(new RotationScore(angle, mismatch, radius <= 0 ? 0 : mismatch / radius));
        }

        var basin = CentralBasinHalfWidth(curve);
        var rivals = FindDistinctRivals(curve, basin, radius);
        var (turningAngle, turningResidual) = TurningFunctionRival(samples);

        return new IdentifiabilityResult(
            CharacteristicRadiusMetres: radius,
            CentralBasinHalfWidthDegrees: basin,
            DistinctRivals: rivals,
            TurningRivalAngleDegrees: turningAngle,
            TurningRivalResidualDegrees: turningResidual,
            Curve: curve);
    }

    /// <summary>
    /// Walks outward from 0° until the mismatch stops rising. That turn-over is the edge of the
    /// basin containing the true rotation, and everything inside it is the same answer measured
    /// imprecisely rather than a competing answer. Deriving it from the curve avoids a magic
    /// deadband: for a compact shape the nearest low-mismatch rotation is always adjacent to the
    /// truth, so a fixed cutoff would just report wherever the cutoff was placed.
    /// </summary>
    private static double CentralBasinHalfWidth(IReadOnlyList<RotationScore> curve)
    {
        for (var i = 1; i < curve.Count - 1; i++)
        {
            if (curve[i + 1].MismatchMetres < curve[i].MismatchMetres)
            {
                return curve[i].AngleDegrees;
            }
        }
        return 180.0;
    }

    /// <summary>
    /// Local minima of the mismatch curve outside the central basin, lowest mismatch first. Runs of
    /// equal values collapse to their first index so a flat trough yields one rival, not dozens.
    /// </summary>
    private static IReadOnlyList<RivalRotation> FindDistinctRivals(
        IReadOnlyList<RotationScore> curve, double basinHalfWidth, double radius)
    {
        var rivals = new List<RivalRotation>();
        var n = curve.Count;

        for (var i = 0; i < n; i++)
        {
            var angle = curve[i].AngleDegrees;
            // Exclude the basin on both sides of 0°/360°.
            if (angle <= basinHalfWidth || angle >= 360.0 - basinHalfWidth) continue;

            var previous = curve[(i - 1 + n) % n].MismatchMetres;
            var next = curve[(i + 1) % n].MismatchMetres;
            var here = curve[i].MismatchMetres;

            if (here <= previous && here < next)
            {
                rivals.Add(new RivalRotation(angle, here, radius <= 0 ? 0 : here / radius));
            }
        }

        return rivals.OrderBy(r => r.MismatchMetres).ToList();
    }

    /// <summary>
    /// Independent shape check. The outline is walked at uniform arclength and its cumulative
    /// tangent angle recorded, giving a turning function Θ. If shifting along the outline by k
    /// samples reproduces the same profile offset by a constant, the shape maps onto itself under a
    /// rotation of that constant — which is rotational symmetry.
    ///
    /// Returns the implied rotation of the lowest-residual qualifying shift and that residual in
    /// degrees; a near-zero residual means a genuinely symmetric shape.
    /// </summary>
    private static (double AngleDegrees, double ResidualDegrees) TurningFunctionRival(
        IReadOnlyList<PlanarPoint> samples)
    {
        var n = samples.Count;
        if (n < 8) return (double.NaN, double.NaN);

        var tangent = new double[n];
        for (var i = 0; i < n; i++)
        {
            var a = samples[i];
            var b = samples[(i + 1) % n];
            tangent[i] = Math.Atan2(b.North - a.North, b.East - a.East);
        }

        var turning = new double[n];
        turning[0] = tangent[0];
        for (var i = 1; i < n; i++)
        {
            turning[i] = turning[i - 1] + WrapToPi(tangent[i] - tangent[i - 1]);
        }
        // Total turning over one circuit (±2π). Used to unwrap the seam when a shift wraps past
        // the end of the array.
        var total = turning[n - 1] + WrapToPi(tangent[0] - tangent[n - 1]) - turning[0];

        var bestResidual = double.MaxValue;
        var bestAngle = double.NaN;

        for (var k = 1; k < n; k++)
        {
            double sin = 0, cos = 0;
            for (var i = 0; i < n; i++)
            {
                var j = i + k;
                var shifted = turning[j % n] + (j >= n ? total : 0.0);
                var delta = shifted - turning[i];
                sin += Math.Sin(delta);
                cos += Math.Cos(delta);
            }
            var mean = Math.Atan2(sin, cos);
            // Circular spread: the resultant length is 1 when every delta agrees, 0 when they cancel.
            var resultant = Math.Sqrt(sin * sin + cos * cos) / n;
            var residual = Math.Sqrt(Math.Max(0.0, -2.0 * Math.Log(Math.Max(resultant, 1e-12))));

            // Turning is measured counter-clockwise; report a clockwise bearing to match the curve.
            var angle = NormalizeDegrees(-mean * 180.0 / Math.PI);
            if (angle < MinimumDistinctRotationDegrees ||
                angle > 360.0 - MinimumDistinctRotationDegrees)
            {
                continue;
            }

            if (residual < bestResidual)
            {
                bestResidual = residual;
                bestAngle = angle;
            }
        }

        return (bestAngle, bestResidual * 180.0 / Math.PI);
    }

    private static double WrapToPi(double radians)
    {
        var x = (radians + Math.PI) % (2.0 * Math.PI);
        if (x < 0) x += 2.0 * Math.PI;
        return x - Math.PI;
    }

    private static double NormalizeDegrees(double degrees) => ((degrees % 360.0) + 360.0) % 360.0;
}
