using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

/// <summary>
/// Coverage-weighted per-set aggregation (design §2):
/// <code>
/// score(set)      = Σ(wᵢ·cᵢ·sᵢ) / Σ(wᵢ·cᵢ)      wᵢ: interiors .70, site .30
/// confidence(set) = Σ(wᵢ·cᵢ)                     cᵢ: lens rule-coverage ∈ [0,1]
/// confidence &lt; 0.5 → status = insufficient_evidence, no grade
/// </code>
/// Missing evidence reduces a lens's <b>weight</b>, never its score: a lens with coverage 0
/// contributes nothing to either numerator or denominator, so a thinly-evidenced subject is
/// never scored worse than a well-evidenced one of the same quality — it is scored less
/// confidently, and below the floor it is not scored at all.
/// </summary>
public static class ScoreMath
{
    public const double InteriorsWeight = 0.70;
    public const double SiteWeight = 0.30;
    public const double ConfidenceFloor = 0.5;

    /// <summary>Numerology may nudge a score, never drive it.</summary>
    public const int MaxNumerologyAdjustment = 3;

    public static SetScore Aggregate(
        string principleSet,
        LensResult? interiors,
        LensResult site,
        int numerologyAdjustment,
        Cohort cohort,
        Calibration calibration,
        ElementBalance? elements,
        string summary)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(cohort);
        calibration ??= Calibration.Identity;

        var interiorsCoverage = Clamp01(interiors?.Coverage ?? 0.0);
        var siteCoverage = Clamp01(site.Coverage);

        var numerator = InteriorsWeight * interiorsCoverage * Clamp01(interiors?.Score01 ?? 0.0)
                      + SiteWeight * siteCoverage * Clamp01(site.Score01);
        var denominator = InteriorsWeight * interiorsCoverage + SiteWeight * siteCoverage;

        var confidence = Math.Round(denominator, 10);
        var adjustment = Math.Clamp(numerologyAdjustment, -MaxNumerologyAdjustment, MaxNumerologyAdjustment);
        // ElementBalance is Feng Shui-only; the report omits the section rather than showing zeros.
        var elementBalance = principleSet == PrincipleSets.Vastu ? null : elements;

        var outcomes = new List<RuleOutcome>();
        if (interiors is not null) outcomes.AddRange(interiors.Outcomes);
        outcomes.AddRange(site.Outcomes);

        var interiorsScore = LensScore100(interiors);
        var siteScore = LensScore100(site);

        // Vastu without a resolved facing is gated, not renormalized (design §2).
        var gated = !VastuGate.CanScore(principleSet, cohort);
        if (gated || denominator <= 0 || confidence < ConfidenceFloor)
            return new SetScore(principleSet, AnalysisStatuses.InsufficientEvidence, null, null,
                confidence, interiorsCoverage, siteCoverage, cohort,
                interiorsScore, siteScore, adjustment, elementBalance, summary, outcomes);

        var raw100 = 100.0 * numerator / denominator;
        var calibrated = calibration.For(cohort).Apply(raw100);
        var final = Math.Clamp(
            (int)Math.Round(calibrated, MidpointRounding.AwayFromZero) + adjustment, 0, 100);

        return new SetScore(principleSet, AnalysisStatuses.Ok, final, Grade(final),
            confidence, interiorsCoverage, siteCoverage, cohort,
            interiorsScore, siteScore, adjustment, elementBalance, summary, outcomes);
    }

    public static string Grade(int score) => score switch
    {
        >= 95 => "A+", >= 90 => "A", >= 85 => "A-",
        >= 80 => "B+", >= 75 => "B", >= 70 => "B-",
        >= 65 => "C+", >= 60 => "C", >= 55 => "C-",
        >= 50 => "D+", >= 45 => "D", >= 40 => "D-",
        _ => "F",
    };

    /// <summary>
    /// Mean element balance across the rooms that actually reported one. Returns <b>null</b>
    /// for Vastu (the concept is Feng Shui's) and null when no room reported — never five zeros.
    /// </summary>
    public static ElementBalance? AverageElements(
        IEnumerable<ElementBalance?> perRoom, string principleSet = PrincipleSets.FengShui)
    {
        if (principleSet == PrincipleSets.Vastu) return null;
        var reported = perRoom.Where(e => e is not null && !e.IsAllZero).Select(e => e!).ToList();
        if (reported.Count == 0) return null;
        return new ElementBalance(
            (int)Math.Round(reported.Average(e => e.Wood)),
            (int)Math.Round(reported.Average(e => e.Fire)),
            (int)Math.Round(reported.Average(e => e.Earth)),
            (int)Math.Round(reported.Average(e => e.Metal)),
            (int)Math.Round(reported.Average(e => e.Water)));
    }

    private static int? LensScore100(LensResult? lens) =>
        lens is null || lens.Coverage <= 0 ? null : (int)Math.Round(100 * Clamp01(lens.Score01), MidpointRounding.AwayFromZero);

    private static double Clamp01(double v) => double.IsNaN(v) ? 0.0 : Math.Clamp(v, 0.0, 1.0);

    // ---------------------------------------------------------------- v1 shims (removed in Tier 2)

    [Obsolete("Blended scoring is replaced by per-set Aggregate(...). Removed in Tier 2 (Task 11).")]
    public static int Overall(IReadOnlyList<RoomAnalysis> rooms, SiteAnalysis site, int numerologyAdjustment)
    {
        var baseScore = rooms.Count == 0
            ? site.Score
            : 0.7 * rooms.Average(r => r.Score) + 0.3 * site.Score;
        return Math.Clamp((int)Math.Round(baseScore) + numerologyAdjustment, 0, 100);
    }

    [Obsolete("Use AverageElements(IEnumerable<ElementBalance?>, principleSet). Removed in Tier 2 (Task 11).")]
    public static ElementBalance AverageElements(IReadOnlyList<RoomAnalysis> rooms) =>
        AverageElements(rooms.Select(r => (ElementBalance?)r.ElementBalance)) ?? new ElementBalance(0, 0, 0, 0, 0);

    [Obsolete("Summaries move to the report writer in Tier 2 (Task 7).")]
    public static string LocalSummary(
        IReadOnlyList<RoomAnalysis> rooms, SiteAnalysis site, NumerologyResult numerology)
    {
        var bestRoom = rooms.OrderByDescending(r => r.Score).FirstOrDefault();
        var strongest = bestRoom?.Adhering.FirstOrDefault();
        var allSuggestions = rooms.SelectMany(r => r.Suggestions.Select(s => (room: r.RoomType, s)))
            .Concat(site.Suggestions.Select(s => (room: "the site", s)))
            .OrderByDescending(x => Rank(x.s.Impact)).ThenBy(x => Rank(x.s.Effort))
            .ToList();
        var parts = new List<string>();
        parts.Add(strongest is not null && bestRoom is not null
            ? $"The strongest asset is the {bestRoom.RoomType} — {strongest.Observation.TrimEnd('.')}."
            : "This home shows a mixed harmony profile across its rooms and site.");
        if (allSuggestions.Count > 0)
        {
            var top = allSuggestions[0];
            parts.Add($"The highest-impact fix: {top.s.Title} ({top.room}) — {top.s.Detail.TrimEnd('.')}.");
        }
        var unlucky = numerology.Checks.Count(c => c.Verdict == "unlucky");
        if (unlucky > 0)
            parts.Add($"Note: {unlucky} of the listing's numbers read as inauspicious in the selected traditions — easy to soften with the suggested remedies.");
        return string.Join(" ", parts);

        static int Rank(string level) => level switch { "high" => 3, "medium" => 2, _ => 1 };
    }
}
