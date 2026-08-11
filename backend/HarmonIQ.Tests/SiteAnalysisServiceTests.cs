using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;

namespace HarmonIQ.Tests;

public class SiteAnalysisServiceTests
{
    private readonly SiteAnalysisService _svc = new();

    /// <summary>Copy of design §10's prohibition list; no emitted rule text may contain any of these.</summary>
    public static readonly string[] BannedSuperlatives =
    [
        "worst", "terrible", "cursed", "awful", "horrible", "disastrous", "catastrophic",
        "dangerous", "unsafe", "avoid this", "never live", "doomed", "toxic", "evil",
    ];

    private static SideEnvironment Side(
        string road = "unknown", string water = "unknown", string structures = "unknown", string slope = "unknown") =>
        new(road, water, structures, slope);

    private static ListingEnvironment Env(
        SideEnvironment? north = null, SideEnvironment? east = null,
        SideEnvironment? south = null, SideEnvironment? west = null) =>
        new(north ?? SideEnvironment.Unknown, east ?? SideEnvironment.Unknown,
            south ?? SideEnvironment.Unknown, west ?? SideEnvironment.Unknown);

    private static ListingEnvironment FullyKnown() => new(
        new("none", "river", "open", "falls"),
        new("quiet", "none", "open", "falls"),
        new("busy", "none", "taller-building", "rises"),
        new("none", "none", "similar", "rises"));

    private static SubjectOrientation Facing(string cardinal) =>
        new("s1", null, cardinal, "sightmap", 0.9, DateTimeOffset.UtcNow);

    private static SubjectOrientation Unresolved() =>
        new("s1", null, null, "none", null, DateTimeOffset.UtcNow);

    // ---------------- orientation gating of coverage ----------------

    [Fact]
    public void FengShui_OrientationDependentRulesAreNotApplicableWithoutOrientation()
    {
        var env = FullyKnown();
        var with = _svc.EvaluateSet(env, Facing("north"), PrincipleSets.FengShui);
        var without = _svc.EvaluateSet(env, null, PrincipleSets.FengShui);

        Assert.Equal(with.Outcomes.Count, without.Outcomes.Count); // same catalogue, same denominator
        Assert.True(without.Coverage < with.Coverage);

        var directional = SiteAnalysisService.OrientationDependentRuleIds;
        Assert.NotEmpty(directional);
        foreach (var id in directional)
        {
            Assert.True(with.Outcomes.Single(o => o.RuleId == id).Applicable, $"{id} should be applicable with orientation");
            Assert.False(without.Outcomes.Single(o => o.RuleId == id).Applicable, $"{id} should be n/a without orientation");
        }
    }

    [Fact]
    public void FengShui_SourceNoneIsTreatedAsNoOrientation()
    {
        var env = FullyKnown();
        var none = _svc.EvaluateSet(env, Unresolved(), PrincipleSets.FengShui);
        var missing = _svc.EvaluateSet(env, null, PrincipleSets.FengShui);
        Assert.Equal(missing.Coverage, none.Coverage, 10);
        Assert.Equal(missing.Score01, none.Score01, 10);
    }

    [Fact]
    public void FengShui_FacingDegreesResolveWhenCardinalIsAbsent()
    {
        var env = FullyKnown();
        var byDegrees = _svc.EvaluateSet(env, new SubjectOrientation("s1", 0, null, "sightmap", 0.9, DateTimeOffset.UtcNow), PrincipleSets.FengShui);
        var byCardinal = _svc.EvaluateSet(env, Facing("north"), PrincipleSets.FengShui);
        Assert.Equal(byCardinal.Coverage, byDegrees.Coverage, 10);
        Assert.Equal(byCardinal.Score01, byDegrees.Score01, 10);
    }

    // ---------------- unknowns are never violations ----------------

    [Fact]
    public void AllUnknownEnvironment_ProducesZeroCoverageAndNoViolations()
    {
        foreach (var set in PrincipleSets.All)
        {
            var lens = _svc.EvaluateSet(ListingEnvironment.AllUnknown, Facing("north"), set);
            Assert.NotEmpty(lens.Outcomes);
            Assert.All(lens.Outcomes, o => Assert.False(o.Applicable));
            Assert.All(lens.Outcomes, o => Assert.False(o.Satisfied));
            Assert.Equal(0.0, lens.Coverage, 10);
        }
    }

    [Fact]
    public void NullEnvironment_IsTreatedAsAllUnknown()
    {
        var lens = _svc.EvaluateSet(null, Facing("north"), PrincipleSets.FengShui);
        Assert.Equal(0.0, lens.Coverage, 10);
    }

    [Fact]
    public void KnowingOneMoreSide_RaisesCoverage()
    {
        var sparse = _svc.EvaluateSet(Env(north: Side(road: "highway")), null, PrincipleSets.FengShui);
        var richer = _svc.EvaluateSet(
            Env(north: Side(road: "highway"), south: Side(road: "none")), null, PrincipleSets.FengShui);
        Assert.True(richer.Coverage > sparse.Coverage);
    }

    // ---------------- catalogue independence ----------------

    [Fact]
    public void FengShuiAndVastuCataloguesAreDisjoint()
    {
        var fs = _svc.EvaluateSet(FullyKnown(), Facing("north"), PrincipleSets.FengShui);
        var va = _svc.EvaluateSet(FullyKnown(), Facing("north"), PrincipleSets.Vastu);

        Assert.All(fs.Outcomes, o => Assert.Equal(PrincipleSets.FengShui, o.PrincipleSet));
        Assert.All(va.Outcomes, o => Assert.Equal(PrincipleSets.Vastu, o.PrincipleSet));
        Assert.Empty(fs.Outcomes.Select(o => o.RuleId).Intersect(va.Outcomes.Select(o => o.RuleId)));
    }

    [Fact]
    public void VastuSiteRulesAreOrientationIndependent_SoAVastuChangeCannotMoveFengShui()
    {
        var env = FullyKnown();
        var withOrientation = _svc.EvaluateSet(env, Facing("south"), PrincipleSets.Vastu);
        var withoutOrientation = _svc.EvaluateSet(env, null, PrincipleSets.Vastu);
        Assert.Equal(withOrientation.Score01, withoutOrientation.Score01, 10);
        Assert.Equal(withOrientation.Coverage, withoutOrientation.Coverage, 10);
    }

    [Fact]
    public void FengShuiOutcomesDoNotChangeWhenTheVastuSetIsAlsoEvaluated()
    {
        var env = FullyKnown();
        var before = _svc.EvaluateSet(env, Facing("east"), PrincipleSets.FengShui);
        _ = _svc.EvaluateSet(env, Facing("east"), PrincipleSets.Vastu);
        var after = _svc.EvaluateSet(env, Facing("east"), PrincipleSets.FengShui);
        Assert.Equal(before.Score01, after.Score01, 10);
        Assert.Equal(
            before.Outcomes.Select(o => (o.RuleId, o.Applicable, o.Satisfied)),
            after.Outcomes.Select(o => (o.RuleId, o.Applicable, o.Satisfied)));
    }

    [Fact]
    public void RulesVersionsArePerPrincipleSet()
    {
        Assert.NotEqual(SiteAnalysisService.RulesVersionFengShui, SiteAnalysisService.RulesVersionVastu);
        Assert.False(string.IsNullOrWhiteSpace(SiteAnalysisService.RulesVersionFengShui));
        Assert.False(string.IsNullOrWhiteSpace(SiteAnalysisService.RulesVersionVastu));
        Assert.Equal(SiteAnalysisService.RulesVersionFengShui, SiteAnalysisService.RulesVersionFor(PrincipleSets.FengShui));
        Assert.Equal(SiteAnalysisService.RulesVersionVastu, SiteAnalysisService.RulesVersionFor(PrincipleSets.Vastu));
    }

    // ---------------- the scale is normalized ----------------

    [Fact]
    public void AFullySatisfiedSiteScoresOneAndAFullyUnsatisfiedSiteScoresZero()
    {
        // north-facing: open bright hall in front, solid backing behind, calm roads, no water behind
        var good = new ListingEnvironment(
            new("none", "river", "open", "unknown"),
            new("none", "unknown", "unknown", "unknown"),
            new("none", "none", "taller-building", "unknown"),
            new("none", "unknown", "unknown", "unknown"));
        var lens = _svc.EvaluateSet(good, Facing("north"), PrincipleSets.FengShui);
        Assert.Equal(1.0, lens.Score01, 10);

        var poor = new ListingEnvironment(
            new("t-junction", "none", "taller-building", "unknown"),
            new("t-junction", "unknown", "unknown", "unknown"),
            new("t-junction", "lake", "open", "unknown"),
            new("t-junction", "unknown", "unknown", "unknown"));
        var poorLens = _svc.EvaluateSet(poor, Facing("north"), PrincipleSets.FengShui);
        Assert.Equal(0.0, poorLens.Score01, 10);
    }

    [Fact]
    public void SeverityIsAlwaysOneTwoOrThree()
    {
        foreach (var set in PrincipleSets.All)
            Assert.All(_svc.EvaluateSet(FullyKnown(), Facing("northeast"), set).Outcomes,
                o => Assert.InRange(o.Severity, 1, 3));
    }

    [Fact]
    public void RuleIdsAreUniqueWithinASet()
    {
        foreach (var set in PrincipleSets.All)
        {
            var ids = _svc.EvaluateSet(FullyKnown(), Facing("southwest"), set).Outcomes.Select(o => o.RuleId).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }
    }

    [Fact]
    public void LensIdIsSite()
    {
        Assert.Equal(LensResult.Site, _svc.EvaluateSet(FullyKnown(), null, PrincipleSets.FengShui).LensId);
    }

    // ---------------- guardrails on rule text ----------------

    [Fact]
    public void NoRuleTextUsesANegativeSuperlative()
    {
        foreach (var text in AllRuleText())
            foreach (var banned in BannedSuperlatives)
                Assert.DoesNotContain(banned, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryRuleTextNamesItsTradition()
    {
        foreach (var set in PrincipleSets.All)
        {
            var needle = set == PrincipleSets.Vastu ? "vastu" : "feng shui";
            foreach (var o in AllOutcomes(set))
            {
                Assert.False(string.IsNullOrWhiteSpace(o.Text));
                Assert.Contains(needle, o.Text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private IEnumerable<RuleOutcome> AllOutcomes(string set)
    {
        string?[] orientations = [null, "north", "east", "south", "west", "northeast", "southeast", "southwest", "northwest"];
        foreach (var o in orientations)
            foreach (var outcome in _svc.EvaluateSet(FullyKnown(), o is null ? null : Facing(o), set).Outcomes)
                yield return outcome;
    }

    private IEnumerable<string> AllRuleText() =>
        PrincipleSets.All.SelectMany(AllOutcomes).Select(o => o.Text);

    // ---------------- the Vastu gate ----------------

    [Fact]
    public void VastuGate_RequiresResolvedOrientation()
    {
        Assert.False(VastuGate.CanScore(PrincipleSets.Vastu, (SubjectOrientation?)null));
        Assert.False(VastuGate.CanScore(PrincipleSets.Vastu, Unresolved()));
        Assert.True(VastuGate.CanScore(PrincipleSets.Vastu, Facing("north")));
    }

    [Fact]
    public void VastuGate_DoesNotGateFengShui()
    {
        Assert.True(VastuGate.CanScore(PrincipleSets.FengShui, (SubjectOrientation?)null));
        Assert.True(VastuGate.CanScore(PrincipleSets.FengShui, Unresolved()));
    }

    [Fact]
    public void VastuGate_AlsoReadsTheCohortOrientationPath()
    {
        Assert.False(VastuGate.CanScore(PrincipleSets.Vastu, new Cohort(Cohort.Photos, Cohort.Without)));
        Assert.True(VastuGate.CanScore(PrincipleSets.Vastu, new Cohort(Cohort.Photos, Cohort.With)));
        Assert.True(VastuGate.CanScore(PrincipleSets.FengShui, new Cohort(Cohort.FloorPlan, Cohort.Without)));
    }

    [Fact]
    public void CohortForRecordsTheOrientationPath()
    {
        Assert.Equal("floorplan/without", VastuGate.CohortFor(Cohort.FloorPlan, null).ToString());
        Assert.Equal("photos/with", VastuGate.CohortFor(Cohort.Photos, Facing("west")).ToString());
        Assert.Equal("photos/without", VastuGate.CohortFor(Cohort.Photos, Unresolved()).ToString());
    }
}
