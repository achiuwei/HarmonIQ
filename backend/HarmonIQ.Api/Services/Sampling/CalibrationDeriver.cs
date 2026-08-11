using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services.Sampling;

/// <summary>
/// Derives per-cohort linear calibration constants from a <b>dual-scored</b> subsample —
/// subjects whose same (SubjectId, PrincipleSet) has an <c>Ok</c> analysis row under more than
/// one <see cref="Cohort"/> (design §2's "cohorts, not disclaimers": ranking and filtering
/// happen within cohort, using constants derived offline, never computed live).
///
/// Method: for every subject/principle-set pair scored under 2+ cohorts, the reference point is
/// the mean raw score across that pair's cohorts (order-independent, so the result is
/// deterministic regardless of how the caller orders <paramref name="dualScored"/> — a Dictionary
/// enumeration or an EF query would otherwise make ordering unstable). Each cohort's constant is
/// the average <c>(reference − thisCohortScore)</c> over every pair it appeared in, i.e. an
/// additive offset that would recenter that cohort's scores onto the shared reference; scale is
/// held at 1.0 (no local evidence supports fitting a slope from a handful of fixture pairs).
///
/// A cohort with <b>no</b> dual-scored subjects at all yields <see cref="CalibrationConstants.Identity"/>
/// — never an extrapolation from cohorts it was never compared against.
///
/// This is pure and offline: it never reads <see cref="ScoreMath.ConfidenceFloor"/> or touches
/// confidence. Confidence is decided by <see cref="ScoreMath.Aggregate"/> from lens coverage
/// <b>before</b> calibration is applied, so a derived offset can move a calibrated score but can
/// never itself pull a subject across the floor into a grade.
/// </summary>
public static class CalibrationDeriver
{
    public static IReadOnlyDictionary<string, CalibrationConstants> Derive(IEnumerable<Analysis> dualScored)
    {
        ArgumentNullException.ThrowIfNull(dualScored);

        var scored = dualScored
            .Where(a => a.Status == AnalysisStatuses.Ok && a.Score is not null)
            .ToList();

        // deltas[cohort] accumulates (referenceMean - thisCohortScore) for every dual-scored pair
        // that included this cohort.
        var deltas = new Dictionary<string, List<double>>(StringComparer.Ordinal);

        foreach (var group in scored.GroupBy(a => (a.SubjectId, a.PrincipleSet)))
        {
            // One row per cohort per subject/set — the latest computation wins if the same
            // cohort somehow appears twice (re-derivation under an unchanged engine).
            var byCohort = group
                .GroupBy(a => CohortKey(a))
                .Select(g => g.OrderByDescending(a => a.ComputedAt).First())
                .ToList();

            if (byCohort.Count < 2)
            {
                continue; // not actually dual-scored: only one cohort present for this subject/set
            }

            var referenceMean = byCohort.Average(a => (double)a.Score!.Value);

            foreach (var a in byCohort)
            {
                var key = CohortKey(a);
                if (!deltas.TryGetValue(key, out var list))
                {
                    list = [];
                    deltas[key] = list;
                }
                list.Add(referenceMean - a.Score!.Value);
            }
        }

        var result = new Dictionary<string, CalibrationConstants>(StringComparer.Ordinal);
        foreach (var cohort in Cohort.All)
        {
            var key = cohort.ToString();
            result[key] = deltas.TryGetValue(key, out var ds) && ds.Count > 0
                ? new CalibrationConstants(Math.Round(ds.Average(), 4), 1.0)
                : CalibrationConstants.Identity;
        }
        return result;
    }

    private static string CohortKey(Analysis a) =>
        new Cohort(a.CohortEvidencePath ?? Cohort.Photos, a.CohortOrientationPath ?? Cohort.Without).ToString();
}
