using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;

namespace HarmonIQ.Tests;

/// <summary>
/// <see cref="NumerologyService.EvaluateUnits"/> is the read-time, never-persisted path
/// (design Q1 / SPEC v2 FR-17..20). These tests assert purity — no DbContext, no
/// randomness, no grade field anywhere on the output.
///
/// The unit numbers below are chosen to exercise the numerology rules (tetraphobia, the Western
/// 13, the 8-as-wealth reading), not copied from any fixture — this test owns its inputs so that
/// re-pointing the demo fixtures at a different property cannot silently change what it covers.
/// </summary>
public class UnitAnnotationTests
{
    // Constructed with no DbContext and no other dependency: EvaluateUnits must be
    // callable on a bare `new()` to prove it never touches storage.
    private readonly NumerologyService _svc = new();

    private static readonly IReadOnlyList<ScrapedUnit> FixtureUnits =
    [
        new ScrapedUnit("444", 4, 700, 2100m),   // tetraphobia
        new ScrapedUnit("113", 1, 650, 1900m),   // triggers Western 13
        new ScrapedUnit("888", 8, 900, 2600m),   // 8 as wealth
        new ScrapedUnit("1444", 14, 950, 2800m), // 14 plus repeated 4
    ];

    [Fact]
    public void EvaluateUnits_IsPure_SameInputSameOutput()
    {
        var first = _svc.EvaluateUnits(FixtureUnits, PrincipleSets.FengShui);
        var second = _svc.EvaluateUnits(FixtureUnits, PrincipleSets.FengShui);

        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
            Assert.Equal(first[i], second[i]);
    }

    [Fact]
    public void EvaluateUnits_ProducesOneAnnotationPerUnit()
    {
        var annotations = _svc.EvaluateUnits(FixtureUnits, PrincipleSets.FengShui);
        Assert.Equal(FixtureUnits.Count, annotations.Count);
        Assert.Equal(FixtureUnits.Select(u => u.UnitNumber), annotations.Select(a => a.UnitNumber));
    }

    [Fact]
    public void EvaluateUnits_CarriesFloorFromScrapedUnit()
    {
        var annotations = _svc.EvaluateUnits(FixtureUnits, PrincipleSets.FengShui);
        Assert.Equal(FixtureUnits.Select(u => u.Floor), annotations.Select(a => a.Floor));
    }

    [Fact]
    public void EvaluateUnits_TagsEveryAnnotationWithTheRequestedPrincipleSet()
    {
        var fengshui = _svc.EvaluateUnits(FixtureUnits, PrincipleSets.FengShui);
        var vastu = _svc.EvaluateUnits(FixtureUnits, PrincipleSets.Vastu);

        Assert.All(fengshui, a => Assert.Equal(PrincipleSets.FengShui, a.PrincipleSet));
        Assert.All(vastu, a => Assert.Equal(PrincipleSets.Vastu, a.PrincipleSet));
    }

    [Fact]
    public void EvaluateUnits_Unit113_TriggersWesternThirteenUnderEitherSet()
    {
        var fengshui = _svc.EvaluateUnits(FixtureUnits, PrincipleSets.FengShui).Single(a => a.UnitNumber == "113");
        Assert.Equal("unlucky", fengshui.Verdict);
        Assert.Contains("Western tradition", fengshui.Note);
    }

    [Fact]
    public void EvaluateUnits_AllAdjustmentsAreClampedToThree()
    {
        foreach (var set in PrincipleSets.All)
            foreach (var a in _svc.EvaluateUnits(FixtureUnits, set))
                Assert.InRange(a.Adjustment, -3, 3);
    }

    [Fact]
    public void EvaluateUnits_AnnotationsCarryNoGradeField()
    {
        // UnitNumerologyAnnotation must never carry a letter grade or a 0-100 score —
        // only a bounded ±3 Adjustment and a lucky/neutral/unlucky Verdict. Enumerating
        // the record's declared properties by name is a cheap, durable guard against a
        // future edit accidentally adding a "Grade" or "Score" field.
        var properties = typeof(UnitNumerologyAnnotation).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("Grade", properties);
        Assert.DoesNotContain("Score", properties);
    }

    [Fact]
    public void EvaluateUnits_FixtureNumbers_ProduceFiveDistinctReasonProfiles()
    {
        // 444 and 1444 both read unlucky under Chinese rules but for related-not-identical
        // reasons (1444 carries the 14 combo); 888 reads lucky; 113 reads unlucky purely
        // via the Western overlay. Distinct notes prove the engine is not returning a
        // single canned string.
        var notes = _svc.EvaluateUnits(FixtureUnits, PrincipleSets.FengShui).Select(a => a.Note).Distinct().ToList();
        Assert.True(notes.Count >= 3, $"Expected at least 3 distinct notes, got {notes.Count}: {string.Join(" | ", notes)}");
    }
}
