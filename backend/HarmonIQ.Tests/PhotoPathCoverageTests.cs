using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;

namespace HarmonIQ.Tests;

/// <summary>
/// The <b>live</b> photo path — <see cref="AnalysisDerivation.InteriorsLens"/> resolving a stage-3
/// <see cref="TraditionInterpretation"/> rather than falling back to tradition-tagged room findings.
///
/// This branch had no coverage at all before these tests. Every other photo-path test runs the
/// fallback (demo/mock) branch, which computes coverage from perception alone — so the live
/// branch's behaviour was never exercised, and a real export could produce nothing but
/// <c>insufficient_evidence</c> on every photo subject while the suite stayed green.
///
/// See <c>docs/photo-path-coverage.md</c>.
/// </summary>
public class PhotoPathCoverageTests
{
    private const string Set = PrincipleSets.FengShui;

    /// <summary>A stage-1 perception fact: no tradition attached, so it never scores directly.</summary>
    private static LensFinding PerceptionFact(string ruleId) =>
        new(ruleId, "perception", "The photograph shows this.", "", 0.9, null);

    /// <summary>A stage-3 interpretation finding for one tradition, above the finding floor.</summary>
    private static LensFinding Interpreted(string ruleId, string? severity = null) =>
        new(ruleId, "principle", "Read through the tradition.", Set, 0.9, severity);

    private static RoomObservation Room(string photoId, double coverage) =>
        new(photoId, "living", [PerceptionFact("fact-1")], [], null, coverage);

    private static DerivationInput PhotoInput(
        double perceptionCoverage, double interpretationCoverage, params LensFinding[] findings) =>
        new(
            Cohort.Photos,
            [
                ObservationPayload.ForRoom(Room("photo-1", perceptionCoverage)),
                ObservationPayload.ForInterpretation(
                    new TraditionInterpretation(Set, findings, [], null, interpretationCoverage)),
            ],
            ListingEnvironment.AllUnknown,
            null,
            new Dictionary<string, NumerologyResult>(),
            Calibration.Identity);

    [Fact]
    public void LiveInterpretation_CoverageIsPerceptionAlone_NotCompoundedWithTheInterpretation()
    {
        // Perception saw 80% of what it needed; the tradition reported it could address 70% of its
        // own rule set. Coverage is a property of the EVIDENCE — how much the photographs showed —
        // so it is perception's number. Multiplying the two discounts the same evidence twice and
        // is what pushed every live photo subject under the confidence floor.
        var lens = AnalysisDerivation.InteriorsLens(Set, PhotoInput(0.8, 0.7, Interpreted("r1")));

        Assert.NotNull(lens);
        Assert.Equal(0.8, lens!.Coverage, 6);
    }

    [Fact]
    public void LiveInterpretation_WithRealisticCoverages_ClearsTheConfidenceFloor()
    {
        // The consequence, chosen so compounding is what decides it. Two model self-reports of 0.62
        // against a site lens covering 0.30:
        //   compounded   0.7*(0.62*0.62) + 0.3*0.30 = 0.359  → under the 0.50 floor, unscored
        //   perception   0.7*0.62        + 0.3*0.30 = 0.524  → scored
        // This is the shape of the real export: Enzo's property rows sat at 0.29-0.44 while its
        // floor-plan rows on the same building cleared the floor.
        var lens = AnalysisDerivation.InteriorsLens(Set, PhotoInput(0.62, 0.62, Interpreted("r1")));
        var site = new LensResult(LensResult.Site, 1.0, 0.30, []);

        var score = ScoreMath.Aggregate(
            Set, lens, site, new Cohort(Cohort.Photos, Cohort.Without),
            Calibration.Identity, null, "summary");

        Assert.Equal(AnalysisStatuses.Ok, score.Status);
        Assert.True(score.Confidence >= ScoreMath.ConfidenceFloor,
            $"confidence {score.Confidence} should clear the {ScoreMath.ConfidenceFloor} floor");
    }

    [Fact]
    public void LiveInterpretation_WithNoFindingsAboveTheFloor_IsZeroCoverage_NotAZeroScore()
    {
        // Unchanged by this fix and asserted so it stays that way: no readable evidence must remain
        // "not scored", never "scored badly". A 0.4-confidence finding is below FindingConfidenceFloor.
        var weak = new LensFinding("r1", "principle", "Barely visible.", Set, 0.4, null);
        var lens = AnalysisDerivation.InteriorsLens(Set, PhotoInput(0.8, 0.7, weak));

        Assert.NotNull(lens);
        Assert.Equal(0.0, lens!.Coverage, 6);
    }

    [Fact]
    public void PerceptionOnlyEvidence_StillYieldsNoInteriorsCoverage_ForATraditionThatReadNothing()
    {
        // A room carrying only untagged perception facts and no interpretation for this tradition:
        // the fallback branch finds nothing it can read, so coverage is zero rather than a score
        // built on facts no tradition actually interpreted.
        var input = new DerivationInput(
            Cohort.Photos,
            [ObservationPayload.ForRoom(Room("photo-1", 0.8))],
            ListingEnvironment.AllUnknown,
            null,
            new Dictionary<string, NumerologyResult>(),
            Calibration.Identity);

        var lens = AnalysisDerivation.InteriorsLens(Set, input);

        Assert.NotNull(lens);
        Assert.Equal(0.0, lens!.Coverage, 6);
    }

    // ---- polarity: `satisfied`, not inferred from `severity` ----

    /// <summary>A stage-3 finding that states its own polarity, as v3.1 requires.</summary>
    private static LensFinding Judged(string ruleId, bool satisfied, string? severity = null) =>
        new(ruleId, "principle", "Read through the tradition.", Set, 0.9, severity, Present: true,
            Satisfied: satisfied);

    [Fact]
    public void SatisfiedFindings_Score_EvenWhenEveryOneCarriesASeverity()
    {
        // The regression this fix exists for. A live model asked for findings attaches a severity
        // to all of them, so inferring "satisfied" from a null severity scored every live photo
        // subject at zero interiors - 101 Limone 33/F and 106 Tangerine 30/F, while the same
        // building's floor plans scored 55-93 through the lens that already had a polarity flag.
        var lens = AnalysisDerivation.InteriorsLens(
            Set,
            PhotoInput(0.8, 0.7,
                Judged("r1", satisfied: true, severity: "minor"),
                Judged("r2", satisfied: true, severity: "moderate")));

        Assert.NotNull(lens);
        Assert.Equal(1.0, lens!.Score01, 6);
    }

    [Fact]
    public void UnsatisfiedFindings_DoNotScore_EvenWhenTheyCarryNoSeverity()
    {
        // The inverse, which the old inference got wrong the other way: severity is optional, so a
        // violation reported without one used to read as a pass.
        var lens = AnalysisDerivation.InteriorsLens(
            Set, PhotoInput(0.8, 0.7, Judged("r1", satisfied: false), Judged("r2", satisfied: false)));

        Assert.NotNull(lens);
        Assert.Equal(0.0, lens!.Score01, 6);
    }

    [Fact]
    public void MixedPolarity_ScoresTheSatisfiedShare()
    {
        var lens = AnalysisDerivation.InteriorsLens(
            Set,
            PhotoInput(0.8, 0.7,
                Judged("r1", satisfied: true),
                Judged("r2", satisfied: false, severity: "minor")));

        Assert.NotNull(lens);
        // Severity weights the outcomes, so this is not a flat 0.5 - it is the satisfied share of
        // the severity-weighted total. What matters is that it sits strictly between the two poles.
        Assert.InRange(lens!.Score01, 0.01, 0.99);
    }

    [Fact]
    public void LegacyFindingsWithoutPolarity_KeepTheOldInference()
    {
        // Satisfied == null means a v3.0 finding, stored before the flag existed. Those keep the
        // old severity inference: wrong in a known direction, but the same answer already on disk,
        // rather than silently promoting stored history to satisfied.
        var legacyViolation = new LensFinding("r1", "principle", "Old.", Set, 0.9, "minor");
        var legacyClear = new LensFinding("r2", "principle", "Old.", Set, 0.9, null);

        Assert.Equal(
            0.0,
            AnalysisDerivation.InteriorsLens(Set, PhotoInput(0.8, 0.7, legacyViolation))!.Score01, 6);
        Assert.Equal(
            1.0,
            AnalysisDerivation.InteriorsLens(Set, PhotoInput(0.8, 0.7, legacyClear))!.Score01, 6);
    }
}
