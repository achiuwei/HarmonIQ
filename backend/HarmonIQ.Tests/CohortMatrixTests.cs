using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;

namespace HarmonIQ.Tests;

/// <summary>
/// All four corners of (photos|floorplan) × (with|without) orientation, for both principle
/// sets. Proves the cohort travels with every result, that Vastu is gated on orientation,
/// and that the two evidence paths land on a comparable scale.
/// </summary>
public class CohortMatrixTests
{
    private readonly SiteAnalysisService _svc = new();

    private static SubjectOrientation Facing(string cardinal) =>
        new("s1", null, cardinal, "sightmap", 0.92, DateTimeOffset.UtcNow);

    private static readonly ListingEnvironment Env = new(
        new("none", "river", "open", "falls"),
        new("none", "none", "open", "falls"),
        new("none", "none", "taller-building", "rises"),
        new("none", "none", "similar", "rises"));

    /// <summary>photos give a 12-rule interiors lens; a floor plan gives a 3-rule one.</summary>
    private static LensResult Interiors(string evidencePath)
    {
        var (total, satisfied) = evidencePath == Cohort.Photos ? (12, 9) : (3, 2);
        var outcomes = Enumerable.Range(0, total)
            .Select(i => new RuleOutcome($"interiors.r{i}", PrincipleSets.FengShui, true, i < satisfied, 1, "text"))
            .ToList();
        return RuleEvaluation.ToLens(LensResult.Interiors, outcomes);
    }

    private SetScore Score(string principleSet, string evidencePath, string orientationPath)
    {
        var orientation = orientationPath == Cohort.With ? Facing("north") : null;
        var site = _svc.EvaluateSet(Env, orientation, principleSet);
        var cohort = VastuGate.CohortFor(evidencePath, orientation);
        return ScoreMath.Aggregate(principleSet, Interiors(evidencePath), site, 0,
            cohort, Calibration.Identity, null, "summary");
    }

    public static TheoryData<string, string> Corners() => new()
    {
        { Cohort.Photos, Cohort.With }, { Cohort.Photos, Cohort.Without },
        { Cohort.FloorPlan, Cohort.With }, { Cohort.FloorPlan, Cohort.Without },
    };

    [Theory]
    [MemberData(nameof(Corners))]
    public void EveryCornerProducesACoherentResultForBothSets(string evidence, string orientation)
    {
        foreach (var set in PrincipleSets.All)
        {
            var s = Score(set, evidence, orientation);

            Assert.Equal(set, s.PrincipleSet);
            Assert.Equal(evidence, s.Cohort.EvidencePath);
            Assert.Equal(orientation, s.Cohort.OrientationPath);
            Assert.Equal($"{evidence}/{orientation}", s.Cohort.ToString());
            Assert.InRange(s.Confidence, 0.0, 1.0);
            Assert.InRange(s.InteriorsCoverage, 0.0, 1.0);
            Assert.InRange(s.SiteCoverage, 0.0, 1.0);
            Assert.NotEmpty(s.Outcomes);

            if (s.Status == "ok")
            {
                Assert.NotNull(s.Score);
                Assert.NotNull(s.Grade);
                Assert.InRange(s.Score!.Value, 0, 100);
                Assert.True(s.Confidence >= ScoreMath.ConfidenceFloor);
            }
            else
            {
                Assert.Equal("insufficient_evidence", s.Status);
                Assert.Null(s.Score);
                Assert.Null(s.Grade);
            }
        }
    }

    [Theory]
    [InlineData(Cohort.Photos)]
    [InlineData(Cohort.FloorPlan)]
    public void VastuWithoutOrientationIsInsufficientEvidenceOnBothEvidencePaths(string evidence)
    {
        var s = Score(PrincipleSets.Vastu, evidence, Cohort.Without);
        Assert.Equal("insufficient_evidence", s.Status);
        Assert.Null(s.Score);
        Assert.Null(s.Grade);
        // it is gated, not starved: the evidence was actually there
        Assert.True(s.Confidence >= ScoreMath.ConfidenceFloor);
    }

    [Theory]
    [InlineData(Cohort.Photos)]
    [InlineData(Cohort.FloorPlan)]
    public void VastuWithOrientationScores(string evidence)
    {
        var s = Score(PrincipleSets.Vastu, evidence, Cohort.With);
        Assert.Equal("ok", s.Status);
        Assert.NotNull(s.Grade);
        Assert.Null(s.ElementBalance); // Feng Shui-only, never five zeros
    }

    [Theory]
    [InlineData(Cohort.Photos)]
    [InlineData(Cohort.FloorPlan)]
    public void FengShuiWithoutOrientationStillScores(string evidence)
    {
        var s = Score(PrincipleSets.FengShui, evidence, Cohort.Without);
        Assert.Equal("ok", s.Status);
        Assert.NotNull(s.Score);
        Assert.NotNull(s.Grade);
        Assert.True(s.Confidence < Score(PrincipleSets.FengShui, evidence, Cohort.With).Confidence);
    }

    [Fact]
    public void TheTwoEvidencePathsLandOnTheSameScale()
    {
        // a 3-rule floor-plan lens at 2/3 and a 12-rule photo lens at 8/12 must be identical
        var plan = RuleEvaluation.ToLens(LensResult.Interiors,
            Enumerable.Range(0, 3).Select(i => new RuleOutcome($"p{i}", PrincipleSets.FengShui, true, i < 2, 1, "t")).ToList());
        var photos = RuleEvaluation.ToLens(LensResult.Interiors,
            Enumerable.Range(0, 12).Select(i => new RuleOutcome($"h{i}", PrincipleSets.FengShui, true, i < 8, 1, "t")).ToList());

        var site = _svc.EvaluateSet(Env, Facing("north"), PrincipleSets.FengShui);
        var a = ScoreMath.Aggregate(PrincipleSets.FengShui, plan, site, 0,
            new Cohort(Cohort.FloorPlan, Cohort.With), Calibration.Identity, null, "s");
        var b = ScoreMath.Aggregate(PrincipleSets.FengShui, photos, site, 0,
            new Cohort(Cohort.Photos, Cohort.With), Calibration.Identity, null, "s");

        Assert.Equal(b.Score, a.Score);
        Assert.Equal(b.Confidence, a.Confidence, 10);
    }

    [Fact]
    public void CohortEnumeratesAllFourCorners()
    {
        Assert.Equal(4, Cohort.All.Count);
        Assert.Equal(
            new[] { "floorplan/with", "floorplan/without", "photos/with", "photos/without" },
            Cohort.All.Select(c => c.ToString()).OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void CohortRoundTripsThroughItsStoredString()
    {
        foreach (var c in Cohort.All)
            Assert.Equal(c, Cohort.Parse(c.ToString()));
    }
}
