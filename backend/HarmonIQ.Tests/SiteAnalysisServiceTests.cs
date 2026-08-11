using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;

namespace HarmonIQ.Tests;

public class SiteAnalysisServiceTests
{
    private readonly SiteAnalysisService _svc = new();
    private static SideEnvironment U => SideEnvironment.Unknown;
    private static SideEnvironment Side(string road = "unknown", string water = "unknown",
        string structures = "unknown", string slope = "unknown") => new(road, water, structures, slope);

    [Fact]
    public void TJunctionNorth_UnknownOrientation_ProducesMajorShaChiViolation()
    {
        var env = new ListingEnvironment(Side(road: "t-junction"), U, U, U);
        var r = _svc.Analyze(env, "unknown", "both");
        var v = Assert.Single(r.Violations, v => v.Principle.Contains("T-Junction"));
        Assert.Equal("major", v.Severity);
        Assert.Equal("fengshui", v.Tradition);
        Assert.Contains(r.Suggestions, s => s.Impact == "high"); // screening remedy
    }

    [Fact]
    public void PondEast_Vastu_IsAdhering()
    {
        var env = new ListingEnvironment(U, Side(water: "pond"), U, U);
        var r = _svc.Analyze(env, "unknown", "vastu");
        Assert.Contains(r.Adhering, a => a.Tradition == "vastu" && a.Observation.Contains("pond"));
    }

    [Fact]
    public void WaterSouth_Vastu_IsViolation()
    {
        var env = new ListingEnvironment(U, U, Side(water: "lake"), U);
        var r = _svc.Analyze(env, "unknown", "vastu");
        Assert.Contains(r.Violations, v => v.Tradition == "vastu" && v.Severity == "moderate");
    }

    [Fact]
    public void SlopeFallsNorth_Vastu_Adhering_And_FallsSouth_Violation()
    {
        var falls = _svc.Analyze(new ListingEnvironment(Side(slope: "falls"), U, U, U), "unknown", "vastu");
        Assert.Contains(falls.Adhering, a => a.Principle.Contains("Slope"));
        var bad = _svc.Analyze(new ListingEnvironment(U, U, Side(slope: "falls"), U), "unknown", "vastu");
        Assert.Contains(bad.Violations, v => v.Principle.Contains("Slope"));
    }

    [Fact]
    public void ArmchairPosition_TallerBehindNorthEntrance_Adhering()
    {
        // Entrance faces north → back is south; taller structure behind = support.
        var env = new ListingEnvironment(U, U, Side(structures: "taller-building"), U);
        var r = _svc.Analyze(env, "north", "fengshui");
        Assert.Contains(r.Adhering, a => a.Principle == "Armchair Position");
    }

    [Fact]
    public void OpenFront_NorthEntrance_BrightHallAdhering()
    {
        var env = new ListingEnvironment(Side(structures: "open"), U, U, U);
        var r = _svc.Analyze(env, "north", "fengshui");
        Assert.Contains(r.Adhering, a => a.Principle == "Bright Hall");
    }

    [Fact]
    public void TallerBuildingInFront_ModerateViolation()
    {
        var env = new ListingEnvironment(Side(structures: "taller-building"), U, U, U);
        var r = _svc.Analyze(env, "north", "fengshui");
        Assert.Contains(r.Violations, v => v.Principle == "Overshadowed Facing" && v.Severity == "moderate");
    }

    [Fact]
    public void SystemsFengshui_ExcludesVastuRules()
    {
        var env = new ListingEnvironment(U, Side(water: "pond"), U, U);
        var r = _svc.Analyze(env, "unknown", "fengshui");
        Assert.DoesNotContain(r.Adhering, a => a.Tradition == "vastu");
        Assert.DoesNotContain(r.Violations, v => v.Tradition == "vastu");
    }

    [Fact]
    public void AllUnknown_NoFindings_Score70()
    {
        var r = _svc.Analyze(ListingEnvironment.AllUnknown, "unknown", "both");
        Assert.Empty(r.Adhering);
        Assert.Empty(r.Violations);
        Assert.Equal(70, r.Score);
    }

    [Fact]
    public void NullEnvironment_BehavesLikeAllUnknown()
    {
        var r = _svc.Analyze(null, "unknown", "both");
        Assert.Empty(r.Adhering);
        Assert.Equal(70, r.Score);
    }
}
