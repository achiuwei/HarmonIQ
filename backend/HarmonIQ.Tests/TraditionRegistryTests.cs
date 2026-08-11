using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;
using HarmonIQ.Api.Services.Traditions;

namespace HarmonIQ.Tests;

/// <summary>
/// The registry is the single place that knows which traditions exist. These tests pin the
/// invariants every tradition must satisfy, so a sixth is held to the same contract automatically.
/// </summary>
public class TraditionRegistryTests
{
    [Fact]
    public void AllFiveCulturesArePresent_InDisplayOrder()
    {
        Assert.Equal(
            new[] { "fengshui", "vastu", "pungsu", "kaso", "phongthuy" },
            TraditionRegistry.Ids);
    }

    [Fact]
    public void PrincipleSetsDelegatesToTheRegistry()
    {
        Assert.Equal(TraditionRegistry.Ids, PrincipleSets.All);
        Assert.All(TraditionRegistry.Ids, id => Assert.True(PrincipleSets.IsKnown(id)));
        Assert.False(PrincipleSets.IsKnown("both"));
        Assert.False(PrincipleSets.IsKnown(null));
    }

    [Fact]
    public void EveryTraditionIsFullyDescribed()
    {
        foreach (var t in TraditionRegistry.Ordered)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Id));
            Assert.False(string.IsNullOrWhiteSpace(t.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(t.Culture));
            Assert.False(string.IsNullOrWhiteSpace(t.RulesVersion));
            Assert.False(string.IsNullOrWhiteSpace(t.TraditionPhrase));
            Assert.NotEmpty(t.SearchSynonyms);
        }
    }

    /// <summary>A shared rules version would let one tradition's change invalidate another's analyses (FR-41).</summary>
    [Fact]
    public void RulesVersionsAreUniquePerTradition()
    {
        var versions = TraditionRegistry.Ordered.Select(t => t.RulesVersion).ToList();
        Assert.Equal(versions.Count, versions.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void OrderIsUnique_AndNeverScoreDerived()
    {
        var orders = TraditionRegistry.Ordered.Select(t => t.Order).ToList();
        Assert.Equal(orders.Count, orders.Distinct().Count());
        Assert.Equal(orders.OrderBy(o => o), orders);
    }

    // ---------------- orientation gating ----------------

    /// <summary>
    /// Vastu is gated on directional room placement; Kasō on the kimon, which is an absolute
    /// compass position. The other three lead with landform and degrade through coverage instead.
    /// </summary>
    [Fact]
    public void OnlyVastuAndKasoRequireOrientation()
    {
        Assert.Equal(
            new[] { PrincipleSets.Vastu, PrincipleSets.Kaso }.Order(),
            TraditionRegistry.OrientationGatedIds.Order());
    }

    [Fact]
    public void GatedTraditionsCannotScoreWithoutAFacing()
    {
        var without = new Cohort(Cohort.Photos, Cohort.Without);
        var with = new Cohort(Cohort.Photos, Cohort.With);

        foreach (var t in TraditionRegistry.Ordered)
        {
            Assert.True(OrientationGate.CanScore(t.Id, with));
            Assert.Equal(!t.RequiresOrientation, OrientationGate.CanScore(t.Id, without));
        }
    }

    [Fact]
    public void EveryGatedTraditionExplainsItsOwnGate()
    {
        foreach (var t in TraditionRegistry.Ordered.Where(x => x.RequiresOrientation))
        {
            var text = LocalSummary.InsufficientEvidence(t.Id, Cohort.Photos, orientationResolved: false);
            Assert.Equal(t.OrientationGateExplanation, text);
            Assert.Contains(t.DisplayName, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---------------- element balance ----------------

    /// <summary>
    /// Wǔxíng is shared by the four Sinitic traditions. Vastu's pancha bhuta are a different five
    /// (earth/water/fire/air/space) and cannot ride in the same shape, so it gets null — never
    /// five zeros.
    /// </summary>
    [Fact]
    public void OnlyVastuOmitsWuxing()
    {
        foreach (var t in TraditionRegistry.Ordered)
        {
            Assert.Equal(t.Id != PrincipleSets.Vastu, t.UsesWuxing);
        }
    }

    [Fact]
    public void AverageElements_IsNullForVastu_AndPresentForTheSiniticTraditions()
    {
        ElementBalance?[] rooms = [new ElementBalance(40, 20, 20, 10, 10)];

        Assert.Null(ScoreMath.AverageElements(rooms, PrincipleSets.Vastu));
        foreach (var t in TraditionRegistry.Ordered.Where(x => x.UsesWuxing))
        {
            Assert.NotNull(ScoreMath.AverageElements(rooms, t.Id));
        }
    }

    // ---------------- search synonyms ----------------

    [Fact]
    public void EverySynonymResolvesToItsOwnTradition()
    {
        foreach (var t in TraditionRegistry.Ordered)
        {
            foreach (var synonym in t.SearchSynonyms)
            {
                Assert.Equal(t.Id, SynonymMap.Normalize(synonym));
            }
        }
    }

    /// <summary>
    /// 風水 is written identically in Chinese and Japanese. It must resolve to Feng Shui rather
    /// than being claimed by both — an ambiguous query cannot route to a set.
    /// </summary>
    [Fact]
    public void SharedHanziResolvesToFengShui_NotKaso()
    {
        Assert.Equal(PrincipleSets.FengShui, SynonymMap.Normalize("風水"));
        Assert.Equal(PrincipleSets.Kaso, SynonymMap.Normalize("家相"));
    }

    [Fact]
    public void NewTraditionsAreSearchable_IncludingNativeScript()
    {
        Assert.Equal(PrincipleSets.Pungsu, SynonymMap.Normalize("풍수지리"));
        Assert.Equal(PrincipleSets.Pungsu, SynonymMap.Normalize("Pungsu-jiri"));
        Assert.Equal(PrincipleSets.Kaso, SynonymMap.Normalize("fusui"));
        Assert.Equal(PrincipleSets.PhongThuy, SynonymMap.Normalize("phong thuy"));
        Assert.Equal(PrincipleSets.PhongThuy, SynonymMap.Normalize("Phong Thủy"));
    }

    [Fact]
    public void UnrelatedQueriesStillDoNotMatch()
    {
        Assert.Null(SynonymMap.Normalize("vast open floor plan"));
        Assert.Null(SynonymMap.Normalize("kasoline"));
        Assert.Null(SynonymMap.Normalize(null));
        Assert.Null(SynonymMap.Normalize("   "));
    }

    // ---------------- the prompts are genuinely per-culture ----------------

    [Fact]
    public void EveryTraditionHasADistinctInterpretationPrompt()
    {
        var prompts = TraditionRegistry.Ordered
            .Select(t => t.InterpretPrompt("{}", "north"))
            .ToList();

        Assert.Equal(prompts.Count, prompts.Distinct(StringComparer.Ordinal).Count());
        foreach (var (t, prompt) in TraditionRegistry.Ordered.Zip(prompts))
        {
            Assert.Contains(t.DisplayName, prompt, StringComparison.Ordinal);
            Assert.Contains(t.Culture, prompt, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Each prompt must carry doctrine the others do not — otherwise five prompts would be one
    /// prompt with the name swapped, and the five scores would be theatre.
    /// </summary>
    [Theory]
    [InlineData(PrincipleSets.FengShui, "commanding position")]
    [InlineData(PrincipleSets.Vastu, "Brahmasthan")]
    [InlineData(PrincipleSets.Pungsu, "baesan-imsu")]
    [InlineData(PrincipleSets.Kaso, "kimon")]
    [InlineData(PrincipleSets.PhongThuy, "minh đường")]
    public void EachPromptCarriesItsOwnDoctrine(string id, string signature)
    {
        var mine = TraditionRegistry.Require(id).InterpretPrompt("{}", null);
        Assert.Contains(signature, mine, StringComparison.OrdinalIgnoreCase);

        foreach (var other in TraditionRegistry.Ordered.Where(t => t.Id != id))
        {
            // A related tradition may *mention* another by name to mark a divergence, but must not
            // adopt its signature doctrine wholesale.
            var theirs = other.InterpretPrompt("{}", null);
            if (theirs.Contains(signature, StringComparison.OrdinalIgnoreCase))
            {
                Assert.Fail($"'{other.Id}' prompt contains '{id}' signature doctrine '{signature}'.");
            }
        }
    }

    [Fact]
    public void PromptsWithoutAFacingForbidGuessingDirections()
    {
        foreach (var t in TraditionRegistry.Ordered)
        {
            var prompt = t.InterpretPrompt("{}", null);
            Assert.Contains("No compass facing has resolved", prompt, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NoInterpretationPromptContainsABannedSuperlative()
    {
        foreach (var t in TraditionRegistry.Ordered)
        {
            var prompt = t.InterpretPrompt("{}", "north");
            foreach (var banned in Prompts.BannedSuperlatives)
            {
                Assert.DoesNotContain(banned, prompt, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
