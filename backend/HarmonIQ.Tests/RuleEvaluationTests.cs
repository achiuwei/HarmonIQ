using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;

namespace HarmonIQ.Tests;

public class RuleEvaluationTests
{
    private static RuleOutcome Rule(string id, bool applicable, bool satisfied, int severity = 1) =>
        new(id, PrincipleSets.FengShui, applicable, satisfied, severity, $"Rule {id} states a tradition's reading.");

    private static List<RuleOutcome> Uniform(int total, int satisfied, int severity = 1) =>
        Enumerable.Range(0, total).Select(i => Rule($"r{i}", true, i < satisfied, severity)).ToList();

    [Fact]
    public void ThreeRuleAndTwelveRuleEvaluations_WithSameSatisfiedFraction_ScoreIdentically()
    {
        var small = RuleEvaluation.NormalizedScore(Uniform(3, 2));   // 2/3
        var large = RuleEvaluation.NormalizedScore(Uniform(12, 8));  // 8/12
        Assert.Equal(small, large, 10);
        Assert.Equal(2d / 3d, small, 10);
    }

    [Fact]
    public void ScaleIsIndependentOfCatalogueSize_AcrossManyFractions()
    {
        foreach (var (n, k) in new[] { (4, 1), (4, 3), (5, 0), (5, 5) })
        {
            var expected = (double)k / n;
            Assert.Equal(expected, RuleEvaluation.NormalizedScore(Uniform(n, k)), 10);
            Assert.Equal(expected, RuleEvaluation.NormalizedScore(Uniform(n * 3, k * 3)), 10);
        }
    }

    [Fact]
    public void SeverityWeightsTheFraction()
    {
        // one severity-3 rule satisfied, one severity-1 rule unsatisfied => 3/4
        var outcomes = new[] { Rule("a", true, true, 3), Rule("b", true, false, 1) };
        Assert.Equal(0.75, RuleEvaluation.NormalizedScore(outcomes), 10);

        // inverted: severity-3 unsatisfied, severity-1 satisfied => 1/4
        var flipped = new[] { Rule("a", true, false, 3), Rule("b", true, true, 1) };
        Assert.Equal(0.25, RuleEvaluation.NormalizedScore(flipped), 10);
    }

    [Fact]
    public void NotApplicableOutcomesAreExcludedFromTheScore()
    {
        // the not-applicable, not-satisfied rule must not drag the score down
        var outcomes = new[] { Rule("a", true, true, 3), Rule("b", false, false, 3) };
        Assert.Equal(1.0, RuleEvaluation.NormalizedScore(outcomes), 10);
    }

    [Fact]
    public void ZeroApplicableRules_ScoreZeroAndCoverageZero()
    {
        var outcomes = new[] { Rule("a", false, false, 3), Rule("b", false, false, 1) };
        Assert.Equal(0.0, RuleEvaluation.NormalizedScore(outcomes), 10);
        Assert.Equal(0.0, RuleEvaluation.Coverage(outcomes), 10);
    }

    [Fact]
    public void EmptyOutcomeSet_IsZeroZero()
    {
        Assert.Equal(0.0, RuleEvaluation.NormalizedScore([]), 10);
        Assert.Equal(0.0, RuleEvaluation.Coverage([]), 10);
    }

    [Fact]
    public void CoverageIsApplicableOverTotal_AndIgnoresSeverity()
    {
        var outcomes = new[]
        {
            Rule("a", true, true, 3), Rule("b", true, false, 1),
            Rule("c", false, false, 3), Rule("d", false, false, 2),
        };
        Assert.Equal(0.5, RuleEvaluation.Coverage(outcomes), 10);
    }

    [Fact]
    public void ToLensPacksScoreCoverageAndOutcomes()
    {
        var outcomes = new List<RuleOutcome> { Rule("a", true, true, 1), Rule("b", true, false, 1), Rule("c", false, false, 1) };
        var lens = RuleEvaluation.ToLens(LensResult.Site, outcomes);
        Assert.Equal(LensResult.Site, lens.LensId);
        Assert.Equal(0.5, lens.Score01, 10);
        Assert.Equal(2d / 3d, lens.Coverage, 10);
        Assert.Equal(3, lens.Outcomes.Count);
    }
}
