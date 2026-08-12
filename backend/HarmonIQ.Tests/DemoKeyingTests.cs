using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;

namespace HarmonIQ.Tests;

/// <summary>
/// Demo-mode keying. The floor-plan path has <b>no PhotoId</b>: keying the demo jitter on a
/// constant would clone one grade across every plan of a property (twelve identical chips), so it
/// is keyed on the <b>SubjectId</b> there and on the <b>PhotoId</b> on the photo path. The hash is
/// hand-rolled because <c>string.GetHashCode()</c> is randomized per process — demo grades must not
/// change between runs.
/// </summary>
public class DemoKeyingTests
{
    private static readonly string[] FixturePlanSubjects =
    [
        "349246f:0xbkbx0",
        "349246f:gch7mgw",
        "349246f:1n992v6",
        "349246f:bmgrv28",
        "349246f:ry5b9z1",
    ];

    private static MockAnalysisService NewService() => new(AppContext.BaseDirectory);

    /// <summary>An independent reimplementation of the documented hash — no shared code path.</summary>
    private static int ExpectedKey(string value)
    {
        var h = 17;
        foreach (var c in value) h = unchecked(h * 31 + c);
        return h;
    }

    [Fact]
    public void DemoKey_IsTheHandRolledHash_NotStringGetHashCode()
    {
        foreach (var id in FixturePlanSubjects)
        {
            Assert.Equal(ExpectedKey(id), MockAnalysisService.DemoKey(id));
        }

        // A literal pin: a per-process randomized hash could not satisfy this across runs.
        Assert.Equal(ExpectedKey("349246f:0xbkbx0"), MockAnalysisService.DemoKey("349246f:0xbkbx0"));
        Assert.Equal(-2, MockAnalysisService.DemoJitter("photo-4"));
    }

    [Fact]
    public void FiveFixturePlans_ProduceFiveDifferentDemoScores()
    {
        var service = NewService();
        var scores = FixturePlanSubjects
            .Select(id => InteriorsScore(service.ObservePlan(id)))
            .ToList();

        Assert.Equal(5, scores.Count);
        Assert.Equal(5, scores.Distinct().Count());
    }

    [Fact]
    public void PlanObservations_AreStableAcrossServiceInstancesAndProcesses()
    {
        var first = NewService();
        var second = NewService();

        foreach (var id in FixturePlanSubjects)
        {
            var a = first.ObservePlan(id);
            var b = second.ObservePlan(id);
            Assert.Equal(a.Coverage, b.Coverage);
            Assert.Equal(a.BoundaryFullyDrawn, b.BoundaryFullyDrawn);
            Assert.Equal(a.Findings.Select(f => f.RuleId), b.Findings.Select(f => f.RuleId));
            Assert.Equal(InteriorsScore(a), InteriorsScore(b));
        }
    }

    [Fact]
    public void PlanKeying_IsOnSubjectId_SoPlansDoNotCloneOneGrade()
    {
        var service = NewService();
        var observations = FixturePlanSubjects.Select(service.ObservePlan).ToList();
        var signatures = observations
            .Select(o => string.Join(",", o.Findings.Select(f => f.RuleId)) + "|" + o.Coverage)
            .ToList();

        Assert.Equal(observations.Count, signatures.Distinct().Count());
    }

    [Fact]
    public void PlanObservations_NeverBreakTheClosedRuleSet_OrTheBrahmasthanBoundaryRule()
    {
        var service = NewService();
        foreach (var id in FixturePlanSubjects)
        {
            var obs = service.ObservePlan(id);
            Assert.All(obs.Findings, f => Assert.Contains(f.RuleId, FloorPlanRules.AllowedRuleIds));
            Assert.All(obs.Findings, f => Assert.InRange(f.Confidence, 0, 1));
            if (!obs.BoundaryFullyDrawn)
            {
                Assert.DoesNotContain(obs.Findings, f => f.RuleId == FloorPlanRules.CenterObstruction);
            }
        }
    }

    [Fact]
    public void PhotoKeying_IsOnPhotoId()
    {
        var service = NewService();
        var a = service.ObserveRoom(new PhotoSelection("photo-1", "bedroom"));
        var b = service.ObserveRoom(new PhotoSelection("photo-2", "bedroom"));

        Assert.Equal("bedroom", a.RoomType);
        Assert.NotEqual(
            string.Join(",", a.Findings.Select(f => f.RuleId)) + a.Coverage,
            string.Join(",", b.Findings.Select(f => f.RuleId)) + b.Coverage);

        // Stable for the same photo id.
        Assert.Equal(a.Coverage, service.ObserveRoom(new PhotoSelection("photo-1", "bedroom")).Coverage);
    }

    [Fact]
    public void RoomObservations_AreTraditionAgnostic_AndCarryConfidence()
    {
        var observation = NewService().ObserveRoom(new PhotoSelection("photo-1", "bedroom"));
        Assert.NotEmpty(observation.Findings);
        Assert.All(observation.Findings, f => Assert.Contains(f.Tradition, new[] { "fengshui", "vastu", "both" }));
        Assert.All(observation.Findings, f => Assert.InRange(f.Confidence, 0, 1));
        Assert.Contains(observation.Findings, f => f.Tradition != PrincipleSets.FengShui);
    }

    /// <summary>The derived interiors score for the Feng Shui set — what actually reaches a chip.</summary>
    private static int InteriorsScore(FloorPlanObservation plan)
    {
        var input = new DerivationInput(
            Cohort.FloorPlan,
            [ObservationPayload.ForPlan(plan)],
            ListingEnvironment.AllUnknown,
            null,
            new Dictionary<string, NumerologyResult>(),
            Calibration.Identity);
        var lens = AnalysisDerivation.InteriorsLens(PrincipleSets.FengShui, input)!;
        return (int)Math.Round(10000 * lens.Score01 * lens.Coverage);
    }
}
