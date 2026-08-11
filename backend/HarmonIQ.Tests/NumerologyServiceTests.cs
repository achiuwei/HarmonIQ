using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;

namespace HarmonIQ.Tests;

/// <summary>
/// v2 suite (design Q1 / SPEC v2 FR-17..20). Covers the two surfaces
/// <see cref="NumerologyService"/> now exposes: the subject-level check that still
/// nudges a score, and the per-unit read-time annotation that never does.
/// </summary>
public class NumerologyServiceTests
{
    private readonly NumerologyService _svc = new();

    private static readonly string[] BannedSuperlatives =
        ["worst", "terrible", "cursed", "avoid this unit", "avoid", "horrible", "disaster"];

    // ---------------------------------------------------------------- EvaluateSubject

    [Fact]
    public void EvaluateSubject_FengShui_Floor4_IsUnluckyWithReasonAndRemedy()
    {
        var r = _svc.EvaluateSubject(new ListingNumbers(null, 4, "123"), PrincipleSets.FengShui);
        var floor = r.Checks.Single(c => c.Subject == "floor" && c.Tradition == "fengshui");
        Assert.Equal("unlucky", floor.Verdict);
        Assert.Contains("4", floor.Reason);
        Assert.NotNull(floor.Remedy);
    }

    [Fact]
    public void EvaluateSubject_ExcludesUnitNumber_OnlyFloorAndStreetConsidered()
    {
        // Unit number "44" would read unlucky under Chinese rules, but the subject-level
        // check must never look at it — only floor and street number are the subject's own.
        var r = _svc.EvaluateSubject(new ListingNumbers("44", null, null), PrincipleSets.FengShui);
        Assert.Empty(r.Checks);
        Assert.Equal(0, r.ScoreAdjustment);
    }

    [Fact]
    public void EvaluateSubject_Vastu_Street123_DigitSum6_IsLucky()
    {
        var r = _svc.EvaluateSubject(new ListingNumbers(null, null, "123"), PrincipleSets.Vastu);
        var street = r.Checks.Single(c => c.Subject == "streetNumber" && c.Tradition == "vastu");
        Assert.Equal("lucky", street.Verdict);
        Assert.Contains("6", street.Reason);
    }

    [Fact]
    public void EvaluateSubject_VastuChangeDoesNotAlterFengShuiVerdict()
    {
        var numbers = new ListingNumbers(null, 4, "444");
        var fengshui = _svc.EvaluateSubject(numbers, PrincipleSets.FengShui);
        var vastu = _svc.EvaluateSubject(numbers, PrincipleSets.Vastu);

        // Same input, different principle sets: fengshui's reading is unaffected by
        // whatever the vastu digit-sum rule concludes, and vice versa.
        Assert.Contains(fengshui.Checks, c => c.Tradition == "fengshui" && c.Subject == "floor");
        Assert.DoesNotContain(fengshui.Checks, c => c.Tradition == "vastu");
        Assert.DoesNotContain(vastu.Checks, c => c.Tradition == "fengshui");
    }

    /// <summary>
    /// FR-20: numerology adjusts no stored score. Even the most inauspicious numbers available
    /// (floor 4, street 40 — both tetraphobic in Chinese practice) must yield an adjustment of
    /// exactly zero while still producing the checks the Numbers card renders.
    /// </summary>
    [Fact]
    public void EvaluateSubject_NeverProducesAScoreAdjustment()
    {
        var r = _svc.EvaluateSubject(new ListingNumbers(null, 4, "40"), PrincipleSets.FengShui);
        Assert.Equal(0, r.ScoreAdjustment);
        Assert.NotEmpty(r.Checks);
        Assert.Contains(r.Checks, c => c.Verdict == "unlucky");
    }

    /// <summary>Holds for every tradition, not just the one that used to drive the nudge.</summary>
    [Fact]
    public void EvaluateSubject_NeverAdjusts_ForAnyTradition()
    {
        foreach (var set in PrincipleSets.All)
        {
            var r = _svc.EvaluateSubject(new ListingNumbers(null, 4, "444"), set);
            Assert.Equal(0, r.ScoreAdjustment);
        }
    }

    [Fact]
    public void EvaluateSubject_NullNumbers_YieldsNoChecksAndZeroAdjustment()
    {
        var r = _svc.EvaluateSubject(null, PrincipleSets.FengShui);
        Assert.Empty(r.Checks);
        Assert.Equal(0, r.ScoreAdjustment);
    }

    [Fact]
    public void EvaluateSubject_UnknownPrincipleSet_Throws()
    {
        Assert.Throws<ArgumentException>(() => _svc.EvaluateSubject(new ListingNumbers(null, 4, "1"), "both"));
    }

    // ---------------------------------------------------------------- EvaluateUnit

    [Theory]
    [InlineData("444", PrincipleSets.FengShui, "unlucky")]
    [InlineData("888", PrincipleSets.FengShui, "lucky")]
    [InlineData("1444", PrincipleSets.FengShui, "unlucky")]
    [InlineData("113", PrincipleSets.FengShui, "unlucky")] // no charged Chinese digit, but Western 13 fires
    public void EvaluateUnit_FixtureUnitNumbers_ProduceExpectedVerdicts(
        string unitNumber, string principleSet, string expectedVerdict)
    {
        var annotation = _svc.EvaluateUnit(unitNumber, floor: null, principleSet);
        Assert.Equal(expectedVerdict, annotation.Verdict);
        Assert.Equal(unitNumber, annotation.UnitNumber);
        Assert.Equal(principleSet, annotation.PrincipleSet);
    }

    [Fact]
    public void EvaluateUnit_113_NoteNamesWesternTradition()
    {
        var annotation = _svc.EvaluateUnit("113", floor: null, PrincipleSets.FengShui);
        Assert.Contains("Western tradition", annotation.Note);
    }

    [Fact]
    public void EvaluateUnit_444_NoteNamesChineseNumerology()
    {
        var annotation = _svc.EvaluateUnit("444", floor: null, PrincipleSets.FengShui);
        Assert.Contains("Chinese numerology", annotation.Note);
    }

    [Fact]
    public void EvaluateUnit_AdjustmentIsClampedToRangeInclusive()
    {
        var annotation = _svc.EvaluateUnit("413", floor: null, PrincipleSets.FengShui); // unlucky 4 + western 13
        Assert.InRange(annotation.Adjustment, -3, 3);
    }

    [Fact]
    public void EvaluateUnit_VastuAndFengShuiAreIndependentForTheSameUnit()
    {
        var fengshui = _svc.EvaluateUnit("444", floor: null, PrincipleSets.FengShui);
        var vastu = _svc.EvaluateUnit("444", floor: null, PrincipleSets.Vastu);

        // 444 reduces to 4+4+4=12 -> 1+2=3 (Jupiter, lucky) under Vastu, but is unlucky
        // under Chinese rules — the two traditions must not leak into each other.
        Assert.Equal("unlucky", fengshui.Verdict);
        Assert.Equal("lucky", vastu.Verdict);
    }

    [Fact]
    public void EvaluateUnit_CarriesFloorThrough()
    {
        var annotation = _svc.EvaluateUnit("888", floor: 8, PrincipleSets.FengShui);
        Assert.Equal(8, annotation.Floor);
    }

    [Fact]
    public void EvaluateUnit_UnknownPrincipleSet_Throws()
    {
        Assert.Throws<ArgumentException>(() => _svc.EvaluateUnit("444", null, "both"));
    }

    [Theory]
    [InlineData("444", PrincipleSets.FengShui)]
    [InlineData("888", PrincipleSets.FengShui)]
    [InlineData("1444", PrincipleSets.FengShui)]
    [InlineData("113", PrincipleSets.FengShui)]
    [InlineData("444", PrincipleSets.Vastu)]
    [InlineData("888", PrincipleSets.Vastu)]
    [InlineData("1444", PrincipleSets.Vastu)]
    [InlineData("113", PrincipleSets.Vastu)]
    public void EvaluateUnit_NoteNamesATradition(string unitNumber, string principleSet)
    {
        var annotation = _svc.EvaluateUnit(unitNumber, null, principleSet);
        Assert.True(
            annotation.Note.Contains("numerology", StringComparison.OrdinalIgnoreCase)
            || annotation.Note.Contains("tradition", StringComparison.OrdinalIgnoreCase),
            $"Note must name a tradition, got: '{annotation.Note}'");
    }

    [Theory]
    [InlineData("444", PrincipleSets.FengShui)]
    [InlineData("888", PrincipleSets.FengShui)]
    [InlineData("1444", PrincipleSets.FengShui)]
    [InlineData("113", PrincipleSets.FengShui)]
    [InlineData("444", PrincipleSets.Vastu)]
    [InlineData("888", PrincipleSets.Vastu)]
    [InlineData("1444", PrincipleSets.Vastu)]
    [InlineData("113", PrincipleSets.Vastu)]
    public void EvaluateUnit_NoteContainsNoBannedSuperlative(string unitNumber, string principleSet)
    {
        var annotation = _svc.EvaluateUnit(unitNumber, null, principleSet);
        foreach (var banned in BannedSuperlatives)
            Assert.DoesNotContain(banned, annotation.Note, StringComparison.OrdinalIgnoreCase);
    }
}
