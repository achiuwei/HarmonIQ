using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services.Traditions;

namespace HarmonIQ.Api.Services;

/// <summary>
/// The deterministic site lens. Emits one <see cref="RuleOutcome"/> per rule in the requested
/// tradition's catalogue. An unknown environment value makes a rule <b>not applicable</b>, never
/// violated; orientation-dependent rules drop to not-applicable when no facing has resolved, which
/// lowers coverage (and therefore the site lens's weight) rather than the score.
///
/// The catalogues themselves live with their traditions in <c>Services/Traditions/</c> — this class
/// is now the dispatcher and the shared orientation maths, not the rule content.
/// </summary>
public class SiteAnalysisService
{
    /// <summary>Rules versions are per tradition: a Vastu change must not invalidate Feng Shui.</summary>
    public const string RulesVersionFengShui = "fengshui-2.0";
    public const string RulesVersionVastu = "vastu-2.0";

    /// <summary>
    /// The tradition's own rules version. Unknown ids fall back to Feng Shui's so a stray
    /// principle set can never silently share another tradition's version key.
    /// </summary>
    public static string RulesVersionFor(string principleSet) =>
        TraditionRegistry.Find(principleSet)?.RulesVersion ?? RulesVersionFengShui;

    /// <summary>The Feng Shui rules that can only be judged once a facing has resolved.</summary>
    public static IReadOnlyList<string> OrientationDependentRuleIds =>
        FengShuiTradition.OrientationDependentRuleIds;

    // ---------------------------------------------------------------- public API

    public LensResult EvaluateSet(ListingEnvironment? env, SubjectOrientation? orientation, string principleSet)
    {
        env ??= ListingEnvironment.AllUnknown;
        var tradition = TraditionRegistry.Require(principleSet);
        var outcomes = tradition.SiteCatalogue(env, ResolvedCardinal(orientation));
        return RuleEvaluation.ToLens(LensResult.Site, outcomes);
    }

    /// <summary>The resolved cardinal/intercardinal facing, or null when orientation is absent or unresolved.</summary>
    public static string? ResolvedCardinal(SubjectOrientation? o)
    {
        if (o is null || string.IsNullOrWhiteSpace(o.Source) || o.Source == "none") return null;
        if (!string.IsNullOrWhiteSpace(o.Cardinal))
        {
            var c = o.Cardinal.Trim().ToLowerInvariant();
            return SiteRules.Compass.Contains(c) ? c : null;
        }
        if (o.FacingDegrees is double d) return SiteRules.Compass[(int)Math.Round((((d % 360) + 360) % 360) / 45.0) % 8];
        return null;
    }

    public static bool HasResolvedOrientation(SubjectOrientation? o) => ResolvedCardinal(o) is not null;

    /// <summary>
    /// Short human title for a rule id, for report rendering. Resolves through whichever tradition
    /// owns the id's prefix; falls back to the raw id so an unknown rule still renders.
    /// </summary>
    public static string RuleTitle(string ruleId)
    {
        foreach (var tradition in TraditionRegistry.Ordered)
        {
            if (tradition.RuleTitle(ruleId) is { } title) return title;
        }
        return ruleId;
    }

    /// <summary>Renter-feasible remedy for a rule id, or null when the owning tradition has none.</summary>
    public static Suggestion? RuleRemedy(string ruleId)
    {
        foreach (var tradition in TraditionRegistry.Ordered)
        {
            if (tradition.RuleRemedy(ruleId) is { } remedy) return remedy;
        }
        return null;
    }
}

/// <summary>
/// Some traditions' cores are absolute-directional and cannot run without a facing — Vastu's
/// directional room placement and sleep orientation, Kasō's kimon axis. The leftovers would be
/// "the tradition with the tradition removed", so this gate <b>overrides renormalization</b>
/// (design §2): a gated set with no resolved orientation is <c>insufficient_evidence</c>, not a
/// renormalized score. Ungated traditions degrade through coverage instead.
///
/// Which traditions are gated is declared by each tradition
/// (<see cref="ITradition.RequiresOrientation"/>), not listed here.
/// </summary>
public static class OrientationGate
{
    public static bool CanScore(string principleSet, SubjectOrientation? orientation) =>
        !RequiresOrientation(principleSet) || SiteAnalysisService.HasResolvedOrientation(orientation);

    public static bool CanScore(string principleSet, Cohort cohort) =>
        !RequiresOrientation(principleSet) || cohort.OrientationPath == Cohort.With;

    /// <summary>Builds the cohort a score will be ranked within.</summary>
    public static Cohort CohortFor(string evidencePath, SubjectOrientation? orientation) =>
        new(evidencePath, SiteAnalysisService.HasResolvedOrientation(orientation) ? Cohort.With : Cohort.Without);

    private static bool RequiresOrientation(string principleSet) =>
        TraditionRegistry.Find(principleSet)?.RequiresOrientation == true;
}
