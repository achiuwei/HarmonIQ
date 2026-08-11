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

    /// <summary>
    /// Aggregates one tradition's lenses into a stored verdict.
    ///
    /// Numerology is deliberately not a parameter: per FR-20 it adjusts no stored score and is
    /// cultural annotation only. v1's ±3 nudge is gone — do not reintroduce it.
    /// </summary>
    public static SetScore Aggregate(
        string principleSet,
        LensResult? interiors,
        LensResult site,
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
        // Wǔxíng belongs only to the traditions that use it; the report omits the section
        // rather than showing zeros for the ones that do not.
        var elementBalance = ElementsFor(principleSet, elements);

        var outcomes = new List<RuleOutcome>();
        if (interiors is not null) outcomes.AddRange(interiors.Outcomes);
        outcomes.AddRange(site.Outcomes);

        var interiorsScore = LensScore100(interiors);
        var siteScore = LensScore100(site);

        // An orientation-gated tradition with no facing is gated, not renormalized (design §2).
        var gated = !OrientationGate.CanScore(principleSet, cohort);
        if (gated || denominator <= 0 || confidence < ConfidenceFloor)
            return new SetScore(principleSet, AnalysisStatuses.InsufficientEvidence, null, null,
                confidence, interiorsCoverage, siteCoverage, cohort,
                interiorsScore, siteScore, elementBalance, summary, outcomes);

        var raw100 = 100.0 * numerator / denominator;
        var calibrated = calibration.For(cohort).Apply(raw100);
        var final = Math.Clamp((int)Math.Round(calibrated, MidpointRounding.AwayFromZero), 0, 100);

        return new SetScore(principleSet, AnalysisStatuses.Ok, final, Grade(final),
            confidence, interiorsCoverage, siteCoverage, cohort,
            interiorsScore, siteScore, elementBalance, summary, outcomes);
    }

    /// <summary>Null for any tradition that does not read wǔxíng — never five zeros.</summary>
    private static ElementBalance? ElementsFor(string principleSet, ElementBalance? elements) =>
        Traditions.TraditionRegistry.Find(principleSet)?.UsesWuxing == true ? elements : null;

    public static string Grade(int score) => score switch
    {
        >= 95 => "A+", >= 90 => "A", >= 85 => "A-",
        >= 80 => "B+", >= 75 => "B", >= 70 => "B-",
        >= 65 => "C+", >= 60 => "C", >= 55 => "C-",
        >= 50 => "D+", >= 45 => "D", >= 40 => "D-",
        _ => "F",
    };

    /// <summary>
    /// Mean element balance across the rooms that actually reported one. Returns <b>null</b> for
    /// traditions that do not read wǔxíng — Vastu's pancha bhuta are a different five and do not
    /// map onto this shape — and null when no room reported. Never five zeros.
    /// </summary>
    public static ElementBalance? AverageElements(
        IEnumerable<ElementBalance?> perRoom, string principleSet = PrincipleSets.FengShui)
    {
        if (Traditions.TraditionRegistry.Find(principleSet)?.UsesWuxing != true) return null;
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
}
