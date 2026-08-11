using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

/// <summary>
/// Normalized rule scoring (design §2). Replaces v1's <c>70 + 5·adhering − penalties</c>,
/// which made missing evidence flattering and gave evidence-rich subjects higher variance.
///
/// A lens score is the severity-weighted fraction of the rules it could actually judge, so a
/// 3-rule evaluation and a 12-rule evaluation with the same satisfied fraction land on the
/// same scale. How *much* of the catalogue was judgeable is reported separately as coverage,
/// which becomes the lens's weight — missing evidence lowers weight, never score.
/// </summary>
public static class RuleEvaluation
{
    /// <summary>
    /// Σ(severityᵢ · satisfiedᵢ) / Σ(severityᵢ) over <b>applicable</b> outcomes only.
    /// Returns 0.0 when nothing is applicable; callers must read that together with
    /// <see cref="Coverage"/> == 0 and must not treat it as a bad score.
    /// </summary>
    public static double NormalizedScore(IEnumerable<RuleOutcome> outcomes)
    {
        double satisfiedWeight = 0, applicableWeight = 0;
        foreach (var o in outcomes)
        {
            if (!o.Applicable) continue;
            var w = Math.Clamp(o.Severity, 1, 3);
            applicableWeight += w;
            if (o.Satisfied) satisfiedWeight += w;
        }
        return applicableWeight <= 0 ? 0.0 : satisfiedWeight / applicableWeight;
    }

    /// <summary>Applicable rules ÷ evaluable rules for this lens and set. 0.0 for an empty catalogue.</summary>
    public static double Coverage(IEnumerable<RuleOutcome> outcomes)
    {
        int total = 0, applicable = 0;
        foreach (var o in outcomes)
        {
            total++;
            if (o.Applicable) applicable++;
        }
        return total == 0 ? 0.0 : (double)applicable / total;
    }

    /// <summary>Packs a rule catalogue's outcomes into a <see cref="LensResult"/>.</summary>
    public static LensResult ToLens(string lensId, IReadOnlyList<RuleOutcome> outcomes) =>
        new(lensId, NormalizedScore(outcomes), Coverage(outcomes), outcomes);
}
