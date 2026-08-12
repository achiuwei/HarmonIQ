using HarmonIQ.Api.Commands;
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;
using Xunit;

namespace HarmonIQ.Tests;

/// <summary>
/// The mapper behind <c>export-fixture</c>: a stored <see cref="ReportBody"/> plus its
/// <see cref="Subject"/> becomes one row of apartments-web's <c>harmoniq-grades.json</c>.
///
/// The contract under test is the CONSUMER's, not HarmonIQ's: apartments-web drops score and
/// grade unless <c>status == "ok"</c>, so every field the LDP renders has to survive this hop
/// intact. The feed itself carries none of this (a <c>ProjectionRow</c> is eleven columns), which
/// is exactly why the export reads report bodies instead.
/// </summary>
public class FixtureExportTests
{
    private static Subject Plan(string propertyKey, string planKey) => new()
    {
        Id = $"{propertyKey}:{planKey}",
        PropertyKey = propertyKey,
        SubjectType = "floorplan",
        ExternalPlanKey = planKey,
        CreatedAt = DateTimeOffset.UnixEpoch,
        LastSeenAt = DateTimeOffset.UnixEpoch,
    };

    private static ReportBody Body(
        string status = "ok",
        int? score = 88,
        string? grade = "A-",
        int? interiorsScore = 90,
        int? siteScore = 84,
        double interiorsCoverage = 0.8,
        double siteCoverage = 0.5,
        string summary = "A clear entry sightline, and no direct door-to-window run.",
        ElementBalance? elementBalance = null,
        IReadOnlyList<NumerologyCheck>? numerology = null,
        IReadOnlyList<ReportRule>? interiors = null,
        IReadOnlyList<Suggestion>? suggestions = null) => new(
            SubjectId: "349246f:rk-1",
            PrincipleSet: PrincipleSets.FengShui,
            RulesVersion: "fengshui-2.0",
            EngineVersion: "eng-1",
            Status: status,
            Mode: "live",
            Score: score,
            Grade: grade,
            Confidence: 0.81,
            InteriorsCoverage: interiorsCoverage,
            SiteCoverage: siteCoverage,
            Cohort: "photos/without",
            InteriorsScore: interiorsScore,
            SiteScore: siteScore,
            Summary: summary,
            Interiors: interiors ?? [],
            Site: [],
            Suggestions: suggestions ?? [],
            Numerology: numerology ?? [],
            ElementBalance: elementBalance,
            Rooms: null,
            Plan: null,
            ComputedAt: DateTimeOffset.UnixEpoch);

    private static ReportRule Rule(string id, bool applicable, bool satisfied) =>
        new(id, $"title {id}", $"text {id}", applicable, satisfied, 3, PrincipleSets.FengShui);

    [Fact]
    public void CarriesThePropertyAndPlanKeysTheConsumerIndexesOn()
    {
        var row = FixtureRowMapper.Map(Plan("349246f", "rk-1"), Body());

        Assert.Equal("349246f", row.ListingId);
        Assert.Equal("rk-1", row.FloorPlanId);
    }

    [Fact]
    public void EmitsTheEnginesTwoLensesWithTheirRealWeights()
    {
        var row = FixtureRowMapper.Map(Plan("349246f", "rk-1"), Body(interiorsScore: 90, siteScore: 84));

        Assert.Collection(
            row.Lenses,
            lens =>
            {
                Assert.Equal("Interiors", lens.Name);
                Assert.Equal(90, lens.Score);
                Assert.Equal(ScoreMath.InteriorsWeight, lens.Weight);
            },
            lens =>
            {
                Assert.Equal("Site", lens.Name);
                Assert.Equal(84, lens.Score);
                Assert.Equal(ScoreMath.SiteWeight, lens.Weight);
            });
    }

    [Fact]
    public void NotesEachLensWithItsSatisfiedPrincipleCount()
    {
        var row = FixtureRowMapper.Map(
            Plan("349246f", "rk-1"),
            Body(interiors: [Rule("a", true, true), Rule("b", true, false), Rule("c", false, false)]));

        Assert.Equal("Satisfies 1 of the 2 interior principles this evidence supports.", row.Lenses[0].Notes);
    }

    [Fact]
    public void LeavesALensUnannotatedWhenNoPrincipleApplied()
    {
        var row = FixtureRowMapper.Map(Plan("349246f", "rk-1"), Body(interiors: []));

        Assert.Null(row.Lenses[0].Notes);
    }

    [Fact]
    public void MovesTheSummaryToExplanationWhenEvidenceWasInsufficient()
    {
        var row = FixtureRowMapper.Map(
            Plan("349246f", "rk-1"),
            Body(status: "insufficient_evidence", score: null, grade: null, summary: "No compass bearing."));

        Assert.Equal("No compass bearing.", row.Explanation);
        Assert.Null(row.Summary);
    }

    [Fact]
    public void BlendsBothCoveragesUsingTheEngineWeights()
    {
        var row = FixtureRowMapper.Map(
            Plan("349246f", "rk-1"),
            Body(interiorsCoverage: 0.8, siteCoverage: 0.5));

        Assert.Equal((0.70 * 0.8) + (0.30 * 0.5), row.Coverage, 6);
    }

    [Fact]
    public void NormalizesElementSharesToFractionsOfTheReportedTotal()
    {
        var row = FixtureRowMapper.Map(
            Plan("349246f", "rk-1"),
            Body(elementBalance: new ElementBalance(Wood: 30, Fire: 20, Earth: 20, Metal: 20, Water: 10)));

        Assert.NotNull(row.ElementBalance);
        Assert.Equal(5, row.ElementBalance!.Count);
        Assert.Equal("Wood", row.ElementBalance[0].Element);
        Assert.Equal(0.30, row.ElementBalance[0].Share, 6);
        Assert.Equal(1.0, row.ElementBalance.Sum(e => e.Share), 6);
    }

    [Fact]
    public void OmitsAnAllZeroElementBalanceRatherThanRenderingEmptyBars()
    {
        var row = FixtureRowMapper.Map(
            Plan("349246f", "rk-1"),
            Body(elementBalance: new ElementBalance(0, 0, 0, 0, 0)));

        Assert.Null(row.ElementBalance);
    }

    [Fact]
    public void AnnotatesOnlyTheNumbersThatReadAsInauspicious()
    {
        var row = FixtureRowMapper.Map(
            Plan("349246f", "rk-1"),
            Body(numerology:
            [
                new NumerologyCheck("unit", "414", "unlucky", "fengshui", "Contains the digit 4.", "Add a screen."),
                new NumerologyCheck("unit", "512", "auspicious", "fengshui", "Reads as growth.", null),
            ]));

        var only = Assert.Single(row.Numerology);
        Assert.Equal("414", only.Unit);
        Assert.Contains("Contains the digit 4.", only.Note);
        Assert.Contains("Add a screen.", only.Note);
    }

    [Fact]
    public void FlattensASuggestionToItsRenterFacingSentence()
    {
        var row = FixtureRowMapper.Map(
            Plan("349246f", "rk-1"),
            Body(suggestions: [new Suggestion("Reposition the sofa", "Angle it away from the entry line.", "low", "high")]));

        Assert.Equal("Reposition the sofa — Angle it away from the entry line.", Assert.Single(row.Suggestions));
    }

    [Fact]
    public void LeavesTheFloorPlanKeyNullForAWholePropertySubject()
    {
        var property = new Subject
        {
            Id = "349246f",
            PropertyKey = "349246f",
            SubjectType = "property",
            CreatedAt = DateTimeOffset.UnixEpoch,
            LastSeenAt = DateTimeOffset.UnixEpoch,
        };

        Assert.Null(FixtureRowMapper.Map(property, Body()).FloorPlanId);
    }

    [Fact]
    public void ParsesEveryRepeatedPropertyFlag()
    {
        var options = ExportFixtureOptions.Parse(["--property", "349246f", "--property", "tk93cec"]);

        Assert.Equal(["349246f", "tk93cec"], options.Properties);
    }

    [Fact]
    public void DefaultsTheOutputPathSoAnExportNeverLandsInAnotherRepoByAccident()
    {
        var options = ExportFixtureOptions.Parse(["--property", "349246f"]);

        Assert.Equal("harmoniq-grades.json", options.Out);
    }
}
