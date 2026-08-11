using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;

namespace HarmonIQ.Tests;

public class ScoreMathTests
{
    private static readonly Cohort PhotosWith = new(Cohort.Photos, Cohort.With);
    private static readonly Cohort FloorPlanWithout = new(Cohort.FloorPlan, Cohort.Without);

    private static LensResult Lens(string id, double score01, double coverage) =>
        new(id, score01, coverage, [new RuleOutcome($"{id}.r1", PrincipleSets.FengShui, coverage > 0, score01 >= 0.5, 1, "text")]);

    private static LensResult Interiors(double score01, double coverage) => Lens(LensResult.Interiors, score01, coverage);
    private static LensResult Site(double score01, double coverage) => Lens(LensResult.Site, score01, coverage);

    private static SetScore Agg(
        LensResult? interiors, LensResult site, int numerology = 0, Cohort? cohort = null,
        Calibration? calibration = null, ElementBalance? elements = null,
        string principleSet = PrincipleSets.FengShui) =>
        ScoreMath.Aggregate(principleSet, interiors, site, numerology,
            cohort ?? PhotosWith, calibration ?? Calibration.Identity, elements, "summary");

    // ---------------- coverage-weighted aggregation ----------------

    [Fact]
    public void FullCoverage_AggregatesSeventyThirty()
    {
        // (0.7·1·0.8 + 0.3·1·0.6) / (0.7·1 + 0.3·1) = 0.74
        var s = Agg(Interiors(0.80, 1.0), Site(0.60, 1.0));
        Assert.Equal("ok", s.Status);
        Assert.Equal(1.0, s.Confidence, 6);
        Assert.Equal(74, s.Score);
        Assert.Equal("B-", s.Grade);
    }

    [Fact]
    public void PartialCoverage_RenormalizesAndLowersConfidenceOnly()
    {
        // interiors c=0.5 s=0.80 ; site c=1.0 s=0.60
        // num = .7*.5*.8 + .3*1*.6 = .28 + .18 = .46 ; den = .35 + .30 = .65
        var s = Agg(Interiors(0.80, 0.5), Site(0.60, 1.0));
        Assert.Equal(0.65, s.Confidence, 6);
        Assert.Equal(71, s.Score); // .46/.65 = .70769 -> 70.77 -> 71
        Assert.Equal("B-", s.Grade);
    }

    [Fact]
    public void ConfidenceIsTheSumOfWeightedCoverages()
    {
        Assert.Equal(0.7 * 0.4 + 0.3 * 0.9, Agg(Interiors(0.9, 0.4), Site(0.9, 0.9)).Confidence, 6);
    }

    // ---------------- the confidence floor ----------------

    [Fact]
    public void BelowConfidenceFloor_IsInsufficientEvidenceWithNoScoreAndNoGrade()
    {
        // den = .7*.2 + .3*.5 = .14 + .15 = .29
        var s = Agg(Interiors(0.9, 0.2), Site(0.9, 0.5));
        Assert.Equal("insufficient_evidence", s.Status);
        Assert.Null(s.Score);
        Assert.Null(s.Grade);
        Assert.Equal(0.29, s.Confidence, 6);
    }

    [Fact]
    public void SiteOnlyEvidence_CannotReachTheFloor_SoItIsNeverGraded()
    {
        var s = Agg(null, Site(0.95, 1.0));
        Assert.Equal("insufficient_evidence", s.Status);
        Assert.Null(s.Grade);
        Assert.Equal(0.30, s.Confidence, 6);
    }

    [Fact]
    public void ExactlyAtTheFloor_Scores()
    {
        // den = .7*(2/7) + .3*1 = .2 + .3 = .5
        var s = Agg(Interiors(0.9, 2d / 7d), Site(0.9, 1.0));
        Assert.Equal(0.5, s.Confidence, 6);
        Assert.Equal("ok", s.Status);
    }

    [Fact]
    public void ZeroCoverageEverywhere_IsInsufficientEvidence_NotAZero()
    {
        var s = Agg(Interiors(0.0, 0.0), Site(0.0, 0.0));
        Assert.Equal("insufficient_evidence", s.Status);
        Assert.Null(s.Score);
        Assert.Null(s.Grade);
        Assert.Equal(0.0, s.Confidence, 6);
    }

    // ---------------- missing evidence never lowers the score ----------------

    [Fact]
    public void MissingEvidenceLowersWeightNotScore()
    {
        var known = Agg(Interiors(0.90, 1.0), Site(0.90, 1.0));
        var unknownHeavy = Agg(Interiors(0.90, 0.4), Site(0.90, 1.0));

        Assert.Equal(known.Score, unknownHeavy.Score);
        Assert.True(unknownHeavy.Confidence < known.Confidence);
    }

    [Fact]
    public void AnUnknownHeavySubjectNeverScoresWorseThanAKnownGoodOne()
    {
        var knownGood = Agg(Interiors(0.85, 1.0), Site(0.85, 1.0));
        // same underlying quality, far less evidence: site nearly blind
        var thin = Agg(Interiors(0.85, 1.0), Site(0.85, 0.1));
        Assert.True(thin.Score >= knownGood.Score);

        // and a lens whose evidence is absent must not be read as a zero-scoring lens
        var absentSite = Agg(Interiors(0.85, 1.0), Site(0.0, 0.0));
        Assert.Equal(85, absentSite.Score);
    }

    // ---------------- numerology ----------------

    [Theory]
    [InlineData(0, 74)]
    [InlineData(2, 76)]
    [InlineData(-2, 72)]
    [InlineData(9, 77)]   // clamped to +3
    [InlineData(-9, 71)]  // clamped to -3
    public void NumerologyAdjustmentIsClampedToPlusMinusThree(int adjustment, int expected)
    {
        var s = Agg(Interiors(0.80, 1.0), Site(0.60, 1.0), adjustment);
        Assert.Equal(expected, s.Score);
        Assert.InRange(s.NumerologyAdjustment, -3, 3);
    }

    [Fact]
    public void NumerologyDoesNotPushOutsideZeroToHundred()
    {
        Assert.Equal(100, Agg(Interiors(1.0, 1.0), Site(1.0, 1.0), 3).Score);
        Assert.Equal(0, Agg(Interiors(0.0, 1.0), Site(0.0, 1.0), -3).Score);
    }

    // ---------------- calibration ----------------

    [Fact]
    public void CalibrationIsAppliedPerCohortBeforeBanding()
    {
        var cal = new Calibration(new Dictionary<string, CalibrationConstants>
        {
            ["floorplan/without"] = new(6.0, 1.0),
        });

        var calibrated = Agg(Interiors(0.80, 1.0), Site(0.60, 1.0), 0, FloorPlanWithout, cal);
        Assert.Equal(80, calibrated.Score);      // 74 + 6
        Assert.Equal("B+", calibrated.Grade);    // band moved because calibration ran first

        var otherCohort = Agg(Interiors(0.80, 1.0), Site(0.60, 1.0), 0, PhotosWith, cal);
        Assert.Equal(74, otherCohort.Score);     // untouched cohort keeps identity constants
    }

    [Fact]
    public void CalibrationScaleApplies()
    {
        var cal = new Calibration(new Dictionary<string, CalibrationConstants> { ["photos/with"] = new(0.0, 0.5) });
        Assert.Equal(37, Agg(Interiors(0.80, 1.0), Site(0.60, 1.0), 0, PhotosWith, cal).Score);
    }

    [Fact]
    public void MissingCalibrationIsIdentity()
    {
        Assert.Equal(CalibrationConstants.Identity, Calibration.Identity.For(PhotosWith));
        Assert.Equal(CalibrationConstants.Identity, Calibration.FromJson(null).For(PhotosWith));
        Assert.Equal(CalibrationConstants.Identity, Calibration.FromJson("   ").For(PhotosWith));
    }

    [Fact]
    public void CalibrationRoundTripsFromEngineVersionJson()
    {
        var cal = Calibration.FromJson("""{"floorplan/without":{"offset":6,"scale":1.1}}""");
        Assert.Equal(new CalibrationConstants(6, 1.1), cal.For(FloorPlanWithout));
        Assert.Equal(CalibrationConstants.Identity, cal.For(PhotosWith));
    }

    // ---------------- element balance ----------------

    [Fact]
    public void AverageElements_ReturnsNullRatherThanFiveZeros()
    {
        // Explicitly typed: a bare [] binds to the v1 compat shim, which returns zeros by design.
        Assert.Null(ScoreMath.AverageElements(Array.Empty<ElementBalance?>()));
        Assert.Null(ScoreMath.AverageElements(new ElementBalance?[] { null, null }));
        Assert.Null(ScoreMath.AverageElements([new ElementBalance(0, 0, 0, 0, 0)]));
    }

    [Fact]
    public void AverageElements_IsNullForVastu()
    {
        Assert.Null(ScoreMath.AverageElements(
            [new ElementBalance(40, 20, 20, 10, 10)], PrincipleSets.Vastu));
    }

    [Fact]
    public void AverageElements_AveragesOnlyRoomsThatReported()
    {
        var avg = ScoreMath.AverageElements(
            [new ElementBalance(40, 20, 20, 10, 10), null, new ElementBalance(20, 40, 20, 10, 10)]);
        Assert.NotNull(avg);
        Assert.Equal(30, avg!.Wood);
        Assert.Equal(30, avg.Fire);
        Assert.Equal(20, avg.Earth);
    }

    [Fact]
    public void SetScore_DropsElementBalanceForVastu()
    {
        var eb = new ElementBalance(40, 20, 20, 10, 10);
        Assert.Null(Agg(Interiors(0.8, 1.0), Site(0.6, 1.0), 0, PhotosWith, null, eb, PrincipleSets.Vastu).ElementBalance);
        Assert.NotNull(Agg(Interiors(0.8, 1.0), Site(0.6, 1.0), 0, PhotosWith, null, eb).ElementBalance);
    }

    // ---------------- grade banding ----------------

    [Theory]
    [InlineData(100, "A+")] [InlineData(95, "A+")] [InlineData(94, "A")] [InlineData(90, "A")]
    [InlineData(89, "A-")] [InlineData(85, "A-")] [InlineData(84, "B+")] [InlineData(80, "B+")]
    [InlineData(79, "B")] [InlineData(75, "B")] [InlineData(74, "B-")] [InlineData(70, "B-")]
    [InlineData(69, "C+")] [InlineData(65, "C+")] [InlineData(64, "C")] [InlineData(60, "C")]
    [InlineData(59, "C-")] [InlineData(55, "C-")] [InlineData(54, "D+")] [InlineData(50, "D+")]
    [InlineData(49, "D")] [InlineData(45, "D")] [InlineData(44, "D-")] [InlineData(40, "D-")]
    [InlineData(39, "F")] [InlineData(0, "F")]
    public void GradeBands(int score, string grade) => Assert.Equal(grade, ScoreMath.Grade(score));

    // ---------------- bookkeeping carried on every result ----------------

    [Fact]
    public void CohortAndCoveragesAreRecordedOnEveryResult()
    {
        var ok = Agg(Interiors(0.8, 0.9), Site(0.6, 0.5));
        Assert.Equal(PhotosWith, ok.Cohort);
        Assert.Equal(0.9, ok.InteriorsCoverage, 6);
        Assert.Equal(0.5, ok.SiteCoverage, 6);
        Assert.Equal(80, ok.InteriorsScore);
        Assert.Equal(60, ok.SiteScore);

        var thin = Agg(Interiors(0.8, 0.1), Site(0.6, 0.1));
        Assert.Equal("insufficient_evidence", thin.Status);
        Assert.Equal(PhotosWith, thin.Cohort);
        Assert.Equal(0.1, thin.InteriorsCoverage, 6);
    }

    [Fact]
    public void LensScoresAreNullWhenTheLensHasNoCoverage()
    {
        var s = Agg(Interiors(0.9, 1.0), Site(0.0, 0.0));
        Assert.Equal(90, s.InteriorsScore);
        Assert.Null(s.SiteScore);
    }

    [Fact]
    public void OutcomesFromBothLensesAreCarriedThrough()
    {
        var s = Agg(Interiors(0.8, 1.0), Site(0.6, 1.0));
        Assert.Equal(2, s.Outcomes.Count);
        Assert.Contains(s.Outcomes, o => o.RuleId.StartsWith("interiors."));
        Assert.Contains(s.Outcomes, o => o.RuleId.StartsWith("site."));
    }

    [Fact]
    public void WeightsAndFloorAreTheDesignConstants()
    {
        Assert.Equal(0.70, ScoreMath.InteriorsWeight, 6);
        Assert.Equal(0.30, ScoreMath.SiteWeight, 6);
        Assert.Equal(0.50, ScoreMath.ConfidenceFloor, 6);
    }
}
