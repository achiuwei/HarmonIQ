using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;
using HarmonIQ.Api.Services.Sampling;

namespace HarmonIQ.Tests;

/// <summary>
/// <see cref="CalibrationDeriver"/> is the pure offline half of task zero's decision gate (c):
/// per-cohort calibration constants derived from a dual-scored subsample, never computed live.
/// </summary>
public class CalibrationDeriverTests
{
    private static Analysis Row(
        string subjectId, string principleSet, string evidencePath, string orientationPath,
        int score, DateTimeOffset? computedAt = null) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            SubjectId = subjectId,
            PrincipleSet = principleSet,
            RulesVersion = "v1",
            EngineVersion = "e1",
            Status = AnalysisStatuses.Ok,
            Score = score,
            Grade = ScoreMath.Grade(score),
            CohortEvidencePath = evidencePath,
            CohortOrientationPath = orientationPath,
            ComputedAt = computedAt ?? DateTimeOffset.UtcNow,
        };

    [Fact]
    public void FixedInput_ProducesDeterministicOffsets()
    {
        // subject-1 dual-scored photos vs floorplan at 80 vs 60 -> reference 70, deltas +10 / -10.
        var rows = new List<Analysis>
        {
            Row("subject-1", PrincipleSets.FengShui, Cohort.Photos, Cohort.With, 80),
            Row("subject-1", PrincipleSets.FengShui, Cohort.FloorPlan, Cohort.With, 60),
        };

        var result = CalibrationDeriver.Derive(rows);

        var photosWith = new Cohort(Cohort.Photos, Cohort.With).ToString();
        var floorplanWith = new Cohort(Cohort.FloorPlan, Cohort.With).ToString();

        Assert.Equal(-10.0, result[photosWith].Offset, 4);
        Assert.Equal(1.0, result[photosWith].Scale, 4);
        Assert.Equal(10.0, result[floorplanWith].Offset, 4);
        Assert.Equal(1.0, result[floorplanWith].Scale, 4);
    }

    [Fact]
    public void ResultIsIndependentOfInputOrdering()
    {
        var rows = new List<Analysis>
        {
            Row("subject-1", PrincipleSets.FengShui, Cohort.Photos, Cohort.With, 80),
            Row("subject-1", PrincipleSets.FengShui, Cohort.FloorPlan, Cohort.With, 60),
            Row("subject-2", PrincipleSets.Vastu, Cohort.Photos, Cohort.Without, 50),
            Row("subject-2", PrincipleSets.Vastu, Cohort.FloorPlan, Cohort.Without, 70),
        };

        var forward = CalibrationDeriver.Derive(rows);
        var reversed = CalibrationDeriver.Derive(Enumerable.Reverse(rows).ToList());
        var shuffled = CalibrationDeriver.Derive(new[] { rows[2], rows[0], rows[3], rows[1] });

        foreach (var cohort in Cohort.All)
        {
            var key = cohort.ToString();
            Assert.Equal(forward[key].Offset, reversed[key].Offset, 6);
            Assert.Equal(forward[key].Offset, shuffled[key].Offset, 6);
        }
    }

    [Fact]
    public void CohortWithNoDualScoredSubjectsYieldsIdentity_NeverAnExtrapolation()
    {
        // Only photos/with and floorplan/with are dual-scored. photos/without and
        // floorplan/without were never compared against anything and must stay Identity — not
        // some value inferred from the other cohorts' offsets.
        var rows = new List<Analysis>
        {
            Row("subject-1", PrincipleSets.FengShui, Cohort.Photos, Cohort.With, 90),
            Row("subject-1", PrincipleSets.FengShui, Cohort.FloorPlan, Cohort.With, 70),
        };

        var result = CalibrationDeriver.Derive(rows);

        var photosWithout = new Cohort(Cohort.Photos, Cohort.Without).ToString();
        var floorplanWithout = new Cohort(Cohort.FloorPlan, Cohort.Without).ToString();

        Assert.Equal(CalibrationConstants.Identity, result[photosWithout]);
        Assert.Equal(CalibrationConstants.Identity, result[floorplanWithout]);
    }

    [Fact]
    public void EmptyInput_YieldsIdentityForEveryCohort()
    {
        var result = CalibrationDeriver.Derive([]);

        Assert.Equal(Cohort.All.Count, result.Count);
        foreach (var cohort in Cohort.All)
        {
            Assert.Equal(CalibrationConstants.Identity, result[cohort.ToString()]);
        }
    }

    [Fact]
    public void SubjectScoredUnderOnlyOneCohort_DoesNotCountAsDualScored()
    {
        // subject-2 only ever has a photos/with row for this principle set — a single
        // observation of one cohort proves nothing about calibrating it against another, so it
        // must not contribute a delta anywhere.
        var rows = new List<Analysis>
        {
            Row("subject-1", PrincipleSets.FengShui, Cohort.Photos, Cohort.With, 80),
            Row("subject-1", PrincipleSets.FengShui, Cohort.FloorPlan, Cohort.With, 60),
            Row("subject-2", PrincipleSets.FengShui, Cohort.Photos, Cohort.With, 40),
        };

        var withExtra = CalibrationDeriver.Derive(rows);
        var withoutExtra = CalibrationDeriver.Derive(rows.Take(2).ToList());

        var photosWith = new Cohort(Cohort.Photos, Cohort.With).ToString();
        Assert.Equal(withoutExtra[photosWith].Offset, withExtra[photosWith].Offset, 6);
    }

    [Fact]
    public void NonOkOrUnscoredRows_AreIgnored()
    {
        var rows = new List<Analysis>
        {
            Row("subject-1", PrincipleSets.FengShui, Cohort.Photos, Cohort.With, 80),
            Row("subject-1", PrincipleSets.FengShui, Cohort.FloorPlan, Cohort.With, 60),
            new()
            {
                Id = Guid.NewGuid().ToString("N"), SubjectId = "subject-1", PrincipleSet = PrincipleSets.FengShui,
                RulesVersion = "v1", EngineVersion = "e1", Status = AnalysisStatuses.InsufficientEvidence,
                Score = null, CohortEvidencePath = Cohort.FloorPlan, CohortOrientationPath = Cohort.Without,
                ComputedAt = DateTimeOffset.UtcNow,
            },
        };

        var result = CalibrationDeriver.Derive(rows);

        // The insufficient_evidence row (null score, different cohort) must not manufacture a
        // third cohort's calibration or otherwise perturb the pair that IS validly dual-scored.
        Assert.Equal(CalibrationConstants.Identity, result[new Cohort(Cohort.FloorPlan, Cohort.Without).ToString()]);
        Assert.Equal(-10.0, result[new Cohort(Cohort.Photos, Cohort.With).ToString()].Offset, 4);
    }

    [Fact]
    public void DerivedOffset_IsComputedButDoesNotBypassTheConfidenceFloor()
    {
        // A large offset for a cohort must not rescue a subject the coverage gate already
        // rejected: ScoreMath.Aggregate decides `gated`/confidence from lens coverage BEFORE
        // calibration.Apply ever runs, so an aggressive calibration constant is inert against
        // an insufficient-evidence subject.
        var rows = new List<Analysis>
        {
            Row("subject-1", PrincipleSets.FengShui, Cohort.Photos, Cohort.With, 95),
            Row("subject-1", PrincipleSets.FengShui, Cohort.FloorPlan, Cohort.With, 5),
        };
        var derived = CalibrationDeriver.Derive(rows);
        var calibration = new Calibration(derived);

        // Offset for floorplan/with should be a large positive push (would-be grade rescue).
        var floorplanWith = new Cohort(Cohort.FloorPlan, Cohort.With);
        Assert.True(calibration.For(floorplanWith).Offset > 30, "expected a large derived offset to exercise the floor guarantee");

        // Thin coverage on both lenses => confidence well under the 0.5 floor regardless of the
        // huge offset sitting in `calibration`.
        var thinInteriors = new LensResult(LensResult.Interiors, 0.9, 0.1, []);
        var thinSite = new LensResult(LensResult.Site, 0.9, 0.1, []);

        var score = ScoreMath.Aggregate(
            PrincipleSets.FengShui, thinInteriors, thinSite, floorplanWith, calibration, null, "summary");

        Assert.Equal(AnalysisStatuses.InsufficientEvidence, score.Status);
        Assert.Null(score.Score);
    }
}
