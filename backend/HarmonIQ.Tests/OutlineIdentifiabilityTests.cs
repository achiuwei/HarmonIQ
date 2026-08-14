using HarmonIQ.Api.Services.Orientation;

namespace HarmonIQ.Tests;

/// <summary>
/// Controls for the outline identifiability probe, and the reason any measured site number can be
/// believed at all.
///
/// A scorer that reported "unidentifiable" for everything would be indistinguishable from the
/// result we half-expect at a real apartment building, so the scorer is pinned against shapes whose
/// answer follows from symmetry alone. The dividing line is <b>rotational</b> symmetry, not mirror
/// symmetry: a rectangle maps onto itself at 180° and scores an exactly zero-mismatch rival, while
/// a U maps onto itself only under reflection, which no footprint match can apply.
///
/// Assertions here deliberately test structure — "an exact symmetry yields a zero-mismatch rival",
/// "the answer is scale-free" — rather than specific magnitudes, so they pin the scorer's behaviour
/// without silently encoding whatever it happened to output first.
/// </summary>
public class OutlineIdentifiabilityTests
{
    /// <summary>Mismatch below this is a zero for our purposes: an exact symmetry maps the outline
    /// onto itself, leaving only floating-point and resampling residue.</summary>
    private const double ExactSymmetryToleranceMetres = 0.05;

    private static BuildingOutline Outline(params (double East, double North)[] points)
    {
        var raw = points.Select(p => new PlanarPoint(p.East, p.North)).ToList();
        var ring = OutlineGeometry.NormalizeRing(raw);
        Assert.NotNull(ring);
        return new BuildingOutline(
            "way", 1, ring!,
            Math.Abs(OutlineGeometry.SignedArea(ring!)),
            OutlineGeometry.Centroid(ring!),
            new Dictionary<string, string>());
    }

    /// <summary>40 × 20 bar — the commonest apartment building shape there is, and 2-fold
    /// rotationally symmetric, so its best rival is the 180° flip that inverts every direction.</summary>
    private static BuildingOutline Rectangle() =>
        Outline((0, 0), (40, 0), (40, 20), (0, 20));

    private static BuildingOutline Square() =>
        Outline((0, 0), (30, 0), (30, 30), (0, 30));

    /// <summary>H-shape: non-convex, yet still 2-fold rotationally symmetric.</summary>
    private static BuildingOutline HShape() =>
        Outline(
            (0, 0), (12, 0), (12, 16), (28, 16), (28, 0), (40, 0),
            (40, 40), (28, 40), (28, 24), (12, 24), (12, 40), (0, 40));

    /// <summary>L-shape: no rotational symmetry of any order.</summary>
    private static BuildingOutline LShape() =>
        Outline((0, 0), (60, 0), (60, 20), (20, 20), (20, 50), (0, 50));

    /// <summary>U-shape: mirror-symmetric but not rotationally symmetric.</summary>
    private static BuildingOutline UShape() =>
        Outline(
            (0, 0), (40, 0), (40, 40), (28, 40), (28, 12), (12, 12), (12, 40), (0, 40));

    /// <summary>A bar with one small bump — symmetry broken, but only barely.</summary>
    private static BuildingOutline NearlySymmetricBar() =>
        Outline((0, 0), (40, 0), (40, 20), (22, 20), (22, 22), (18, 22), (18, 20), (0, 20));

    [Fact]
    public void Rectangle_HasAZeroMismatchFlipRival()
    {
        var result = OutlineIdentifiability.Measure(Rectangle());

        Assert.NotNull(result.FlipRival);
        Assert.Equal(180.0, result.FlipRival!.AngleDegrees, precision: 6);
        Assert.True(result.FlipRival.MismatchMetres < ExactSymmetryToleranceMetres,
            $"an exact 2-fold symmetry should leave ~0m mismatch, got {result.FlipRival.MismatchMetres:F4}m");
    }

    [Fact]
    public void Square_HasAZeroMismatchRival_AtAQuarterTurn()
    {
        var result = OutlineIdentifiability.Measure(Square());

        Assert.NotNull(result.BestRival);
        Assert.True(result.BestRival!.MismatchMetres < ExactSymmetryToleranceMetres,
            $"expected ~0m mismatch, got {result.BestRival.MismatchMetres:F4}m");
        // 90, 180 and 270 are all exact symmetries of a square; which one wins is a tie-break.
        Assert.Contains(result.BestRival.AngleDegrees, new[] { 90.0, 180.0, 270.0 });
    }

    [Fact]
    public void HShape_HasAZeroMismatchFlipRival_DespiteBeingNonConvex()
    {
        var result = OutlineIdentifiability.Measure(HShape());

        Assert.NotNull(result.FlipRival);
        Assert.Equal(180.0, result.FlipRival!.AngleDegrees, precision: 6);
        Assert.True(result.FlipRival.MismatchMetres < ExactSymmetryToleranceMetres,
            $"expected ~0m mismatch, got {result.FlipRival.MismatchMetres:F4}m");
    }

    [Fact]
    public void SymmetricShapes_AreIndistinguishable_AtAnyRealisticTolerance()
    {
        foreach (var (name, outline) in new[]
                 {
                     ("rectangle", Rectangle()), ("square", Square()), ("H", HShape()),
                 })
        {
            var ratio = OutlineIdentifiability.Measure(outline).DiscriminationRatio(0.25);

            Assert.True(ratio < 1.0,
                $"{name}: an exact symmetry must be indistinguishable even at a 0.25m noise " +
                $"floor, got a ratio of {ratio:F3}");
        }
    }

    [Fact]
    public void AsymmetricShapes_HaveNoZeroMismatchRival()
    {
        foreach (var (name, outline) in new[] { ("L", LShape()), ("U", UShape()) })
        {
            var result = OutlineIdentifiability.Measure(outline);

            Assert.NotNull(result.BestRival);
            Assert.True(result.BestRival!.MismatchMetres > 10 * ExactSymmetryToleranceMetres,
                $"{name} has no rotational symmetry, so no rival should match nearly exactly; " +
                $"got {result.BestRival.MismatchMetres:F4}m at {result.BestRival.AngleDegrees:F0}°");
        }
    }

    /// <summary>
    /// The realistic hazard, and the reason this probe exists. A bar with one small bump is not
    /// exactly symmetric, so a test for perfect symmetry would clear it — yet its 180° rival still
    /// sits within a fraction of a metre, which is far inside any plausible disagreement between an
    /// OSM roof trace and a real floor outline.
    /// </summary>
    [Fact]
    public void NearlySymmetricBar_StillHasADangerousFlipRival()
    {
        var result = OutlineIdentifiability.Measure(NearlySymmetricBar());

        Assert.NotNull(result.FlipRival);
        Assert.True(result.FlipRival!.MismatchMetres > 0,
            "a broken symmetry should not score as exactly symmetric");
        Assert.True(result.FlipRival.MismatchMetres < 1.0,
            $"one small bump should not rescue a bar; got {result.FlipRival.MismatchMetres:F4}m");
        Assert.True(result.DiscriminationRatio(1.0) < 1.0,
            "a near-symmetric bar must not be declared distinguishable at a 1m noise floor");
    }

    /// <summary>
    /// The two metrics are computed from different things — one from distances in metres, the other
    /// from a scale-free tangent-angle profile. What matters is that they agree on <i>which</i>
    /// rotation is confusable, not on tie-breaking between equally valid symmetries, so this checks
    /// that the turning function's nominee also scores ~0m under the distance metric.
    /// </summary>
    [Fact]
    public void TurningFunction_NominatesAnAngleTheDistanceMetricAlsoCallsSymmetric()
    {
        foreach (var (name, outline) in new[]
                 {
                     ("rectangle", Rectangle()), ("square", Square()), ("H", HShape()),
                 })
        {
            var result = OutlineIdentifiability.Measure(outline);

            Assert.True(result.TurningRivalResidualDegrees < 2.0,
                $"{name}: expected a near-zero turning residual under an exact symmetry, got " +
                $"{result.TurningRivalResidualDegrees:F2}°");

            var atNominee = result.Curve
                .OrderBy(p => Math.Abs(OutlineIdentifiability.AngularSeparationDegrees(
                    p.AngleDegrees, result.TurningRivalAngleDegrees)))
                .First();

            Assert.True(atNominee.MismatchMetres < ExactSymmetryToleranceMetres,
                $"{name}: turning function nominated {result.TurningRivalAngleDegrees:F1}°, but the " +
                $"distance metric scores {atNominee.MismatchMetres:F4}m there — the metrics disagree");
        }
    }

    [Fact]
    public void TurningFunction_ReportsALargeResidual_WhenNoSymmetryExists()
    {
        var result = OutlineIdentifiability.Measure(LShape());

        Assert.True(result.TurningRivalResidualDegrees > 10.0,
            $"an L should admit no low-residual shift, got {result.TurningRivalResidualDegrees:F2}°");
    }

    [Fact]
    public void NormalizedMismatch_IsScaleFree()
    {
        var small = OutlineIdentifiability.Measure(LShape());
        var large = OutlineIdentifiability.Measure(
            Outline((0, 0), (600, 0), (600, 200), (200, 200), (200, 500), (0, 500)));

        Assert.NotNull(small.BestRival);
        Assert.NotNull(large.BestRival);
        // Same shape at 10× the size: identifiability is a property of shape, not of area.
        Assert.Equal(small.BestRival!.NormalizedMismatch, large.BestRival!.NormalizedMismatch, precision: 3);
        Assert.Equal(10.0, large.BestRival.MismatchMetres / small.BestRival.MismatchMetres, precision: 2);
    }

    /// <summary>
    /// Precision is the other half of the answer: <see cref="OrientationResolution"/> buckets a
    /// bearing into 90°-wide cardinal sectors, so a rotation pinned only to ±45° is useless even
    /// with no flip rival at all.
    /// </summary>
    [Fact]
    public void PrecisionHalfWidth_TightensAsToleranceFalls()
    {
        var result = OutlineIdentifiability.Measure(LShape());

        var tight = result.PrecisionHalfWidthDegrees(0.5);
        var loose = result.PrecisionHalfWidthDegrees(5.0);

        Assert.True(tight < loose,
            $"a smaller mismatch budget must pin the rotation more tightly: {tight:F0}° vs {loose:F0}°");
        Assert.True(tight < 45.0,
            $"at a 0.5m budget the rotation should land well inside one cardinal sector, got {tight:F0}°");
    }

    [Fact]
    public void DegenerateRing_IsRejectedRatherThanScoredAsSymmetric()
    {
        // Three collinear points enclose no area — there is no shape here to measure.
        var collinear = new List<PlanarPoint>
        {
            new(0, 0), new(10, 0), new(20, 0), new(0, 0),
        };

        Assert.Null(OutlineGeometry.NormalizeRing(collinear));
    }
}
