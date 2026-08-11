using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;
using HarmonIQ.Api.Services.Traditions;

namespace HarmonIQ.Tests;

/// <summary>
/// The point of scoring five traditions separately is that they genuinely disagree. If every
/// tradition read every fact the same way, one score would do and the other four would be
/// decoration. These tests pin the specific, documented divergences.
/// </summary>
public class TraditionDivergenceTests
{
    private readonly NumerologyService _numerology = new();

    // ---------------- numerology: the same digit, opposite readings ----------------

    /// <summary>
    /// 9 is 九 (jiǔ, "long-lasting") in Chinese practice and 九 (ku, 苦 "suffering") in Japanese.
    /// The same unit number is auspicious in one tradition and inauspicious in the other.
    /// </summary>
    [Fact]
    public void Nine_IsLuckyInChinesePractice_AndUnluckyInJapanese()
    {
        Assert.Equal("lucky", Read(PrincipleSets.FengShui, "9").Verdict);
        Assert.Equal("unlucky", Read(PrincipleSets.Kaso, "9").Verdict);
    }

    /// <summary>
    /// 7 is thất (≈ thất bại, loss) in Vietnamese practice but favourable in Korean and Japanese.
    /// </summary>
    [Fact]
    public void Seven_IsUnluckyInVietnamesePractice_AndLuckyInKoreanAndJapanese()
    {
        Assert.Equal("unlucky", Read(PrincipleSets.PhongThuy, "7").Verdict);
        Assert.Equal("lucky", Read(PrincipleSets.Pungsu, "7").Verdict);
        Assert.Equal("lucky", Read(PrincipleSets.Kaso, "7").Verdict);
    }

    /// <summary>Tetraphobia is the one thing all four Sinitic-influenced traditions agree on.</summary>
    [Fact]
    public void Four_IsInauspiciousAcrossEverySiniticTradition()
    {
        foreach (var id in new[] { PrincipleSets.FengShui, PrincipleSets.Pungsu, PrincipleSets.Kaso, PrincipleSets.PhongThuy })
        {
            Assert.Equal("unlucky", Read(id, "404").Verdict);
        }
    }

    /// <summary>
    /// Vastu reaches its verdict by digit-sum reduction rather than homophone, so it can disagree
    /// with all four: 404 reduces to 8 (Saturn) — inauspicious for a different reason — while 511
    /// reduces to 7 (Ketu, neutral) where the Sinitic traditions see nothing charged at all.
    /// </summary>
    [Fact]
    public void Vastu_ReadsByDigitSum_NotHomophone()
    {
        Assert.Equal("unlucky", Read(PrincipleSets.Vastu, "404").Verdict); // 4+0+4 = 8, Saturn
        Assert.Equal("neutral", Read(PrincipleSets.Vastu, "511").Verdict); // 5+1+1 = 7, Ketu
        Assert.Equal("lucky", Read(PrincipleSets.Vastu, "108").Verdict);   // 1+0+8 = 9, Mars
    }

    /// <summary>
    /// A single number must not produce one shared verdict. This is the guard against a future
    /// refactor collapsing five numerologies back into one.
    /// </summary>
    [Fact]
    public void OneNumberProducesGenuinelyDifferentVerdictsAcrossTraditions()
    {
        var verdicts = PrincipleSets.All.Select(id => Read(id, "479").Verdict).Distinct().ToList();
        Assert.True(verdicts.Count > 1, "All five traditions returned the same verdict for 479.");
    }

    private NumerologyCheck Read(string principleSet, string unitNumber)
    {
        var annotation = _numerology.EvaluateUnit(unitNumber, null, principleSet);
        Assert.Equal(principleSet, annotation.PrincipleSet);
        return new NumerologyCheck("unitNumber", unitNumber, annotation.Verdict, principleSet, annotation.Note, null);
    }

    // ---------------- site: the same environment, different scores ----------------

    /// <summary>
    /// Vastu places water in the north-east (the Jala zone) and reads it favourably; Kasō treats
    /// the north-east as the kimon and asks for it to be kept dry. One site fact, opposite
    /// readings — the clearest case for never blending the two scores.
    /// </summary>
    [Fact]
    public void WaterInTheNorthEast_IsFavourableInVastu_AndFlaggedInKaso()
    {
        var env = new ListingEnvironment(
            new("none", "pond", "open", "falls"),
            new("none", "pond", "open", "falls"),
            new("none", "none", "similar", "rises"),
            new("none", "none", "similar", "rises"));

        var vastuWater = Outcome(PrincipleSets.Vastu, env, "va.site.water_in_ne_quadrant");
        var kasoWater = Outcome(PrincipleSets.Kaso, env, "ks.site.kimon_dry.north");

        Assert.True(vastuWater.Applicable && vastuWater.Satisfied);
        Assert.True(kasoWater.Applicable);
        Assert.False(kasoWater.Satisfied);
    }

    /// <summary>
    /// The five traditions must not produce identical site scores on a fully-known environment.
    /// If they did, the catalogues would be five copies of one catalogue.
    /// </summary>
    [Fact]
    public void FiveTraditionsDoNotAllScoreTheSameSiteIdentically()
    {
        var svc = new SiteAnalysisService();
        var env = new ListingEnvironment(
            new("none", "river", "open", "falls"),
            new("quiet", "none", "open", "falls"),
            new("busy", "none", "taller-building", "rises"),
            new("t-junction", "pond", "similar", "rises"));

        var scores = PrincipleSets.All
            .Select(id => Math.Round(svc.EvaluateSet(env, Facing("north"), id).Score01, 6))
            .Distinct()
            .ToList();

        Assert.True(scores.Count > 1, "Every tradition scored the same site identically.");
    }

    /// <summary>Each tradition's rule ids must be namespaced to it, so reports never mis-attribute.</summary>
    [Theory]
    [InlineData(PrincipleSets.FengShui, "fs.site.")]
    [InlineData(PrincipleSets.Vastu, "va.site.")]
    [InlineData(PrincipleSets.Pungsu, "ps.site.")]
    [InlineData(PrincipleSets.Kaso, "ks.site.")]
    [InlineData(PrincipleSets.PhongThuy, "pt.site.")]
    public void RuleIdsAreNamespacedPerTradition(string id, string prefix)
    {
        var svc = new SiteAnalysisService();
        var outcomes = svc.EvaluateSet(FullyKnown(), Facing("north"), id).Outcomes;

        Assert.NotEmpty(outcomes);
        Assert.All(outcomes, o => Assert.StartsWith(prefix, o.RuleId, StringComparison.Ordinal));
        Assert.All(outcomes, o => Assert.Equal(id, o.PrincipleSet));
    }

    /// <summary>
    /// Pungsu names its flanking guardians Cheongnyong and Baekho, which is a left/right
    /// distinction. Floor plans are routinely mirrored between opposite building stacks, so the
    /// flank rule must read the same whichever way the site is handed.
    /// </summary>
    [Fact]
    public void PungsuFlankRuleIsChiralityFree()
    {
        var svc = new SiteAnalysisService();
        var env = new ListingEnvironment(
            new("none", "none", "open", "level"),
            new("none", "none", "similar", "level"),
            new("none", "none", "similar", "level"),
            new("none", "none", "taller-building", "level"));

        // Facing north and facing south swap which flank is "left"; the guardian rule must not move.
        var north = Outcome(PrincipleSets.Pungsu, env, "ps.site.embraced_flanks", "north");
        var south = Outcome(PrincipleSets.Pungsu, env, "ps.site.embraced_flanks", "south");

        Assert.Equal(north.Applicable, south.Applicable);
        Assert.Equal(north.Satisfied, south.Satisfied);
    }

    private static RuleOutcome Outcome(string set, ListingEnvironment env, string ruleId, string facing = "north") =>
        new SiteAnalysisService().EvaluateSet(env, Facing(facing), set).Outcomes.Single(o => o.RuleId == ruleId);

    private static SubjectOrientation Facing(string cardinal) =>
        new("s1", null, cardinal, "sightmap", 0.9, DateTimeOffset.UtcNow);

    private static ListingEnvironment FullyKnown() => new(
        new("none", "river", "open", "falls"),
        new("quiet", "none", "open", "falls"),
        new("busy", "none", "taller-building", "rises"),
        new("none", "none", "similar", "rises"));
}
