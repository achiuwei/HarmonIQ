using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

/// <summary>
/// Deterministic, pure numerology engine. No DbContext, no I/O — safe as a singleton
/// and safe to call at read time with no persistence side effect.
///
/// Each tradition owns its own reading of a number (FR-18) — see
/// <c>ITradition.Numerology</c>. This service supplies the two surfaces and the
/// tradition-independent Western check; it holds no per-culture rules of its own.
///
/// <b>Numerology adjusts no score (FR-20).</b> v1's ±3 mechanism is removed: both surfaces
/// below are annotation only.
///
/// Two distinct surfaces, per design Q1 / SPEC v2 FR-17..20:
/// <list type="bullet">
/// <item><description>
/// <see cref="EvaluateSubject"/> — the subject-level (building floor / street number)
/// checks rendered on the report's Numbers card, scoped per principle set rather than
/// the v1 tri-state <c>systems</c> string.
/// </description></item>
/// <item><description>
/// <see cref="EvaluateUnit"/> / <see cref="EvaluateUnits"/> — per-unit annotations,
/// computed at read time, never persisted, never entering any score. They render as
/// an availability-table annotation only.
/// </description></item>
/// </list>
/// There is no blended entry point: the v1 <c>Evaluate(numbers, systems)</c> — which mixed both
/// traditions' verdicts into one adjustment, and folded the unit number into the subject's own
/// score — went out with the v1 <c>AnalysisController</c>. A tradition's reading of a number is
/// never averaged with another tradition's.
/// </summary>
public class NumerologyService
{
    /// <summary>
    /// The subject-level checks rendered on the report's Numbers card (FR-19). Deliberately
    /// excludes the unit number — a subject (a floor plan or a property) may host many units, and
    /// no single unit's number speaks for the subject. Only the building floor and street number,
    /// which are properties of the subject itself, are considered here.
    ///
    /// <b>Display only.</b> Per FR-20 the returned <see cref="NumerologyResult.ScoreAdjustment"/> is
    /// always 0 and no caller may feed it into a score; v1's ±3 mechanism is removed.
    /// </summary>
    public NumerologyResult EvaluateSubject(ListingNumbers? numbers, string principleSet)
    {
        var tradition = RequireTradition(principleSet);
        var checks = new List<NumerologyCheck>();
        if (numbers is not null)
        {
            foreach (var (subject, value) in SubjectLevelNumbers(numbers))
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                checks.Add(tradition.Numerology(subject, value));
                if (Western(subject, value) is { } w) checks.Add(w); // only when triggered
            }
        }
        return new NumerologyResult(0, checks);
    }

    /// <summary>
    /// One unit's read-time annotation for a single principle set. Pure: same input
    /// always yields the same output, no DB access, nothing persisted. Never a
    /// competing grade — <see cref="UnitNumerologyAnnotation.Adjustment"/> exists only
    /// for internal ordering and must never be rendered as a score.
    /// </summary>
    public UnitNumerologyAnnotation EvaluateUnit(string unitNumber, int? floor, string principleSet)
    {
        var tradition = RequireTradition(principleSet);
        var primary = tradition.Numerology("unitNumber", unitNumber);
        var western = Western("unitNumber", unitNumber);

        var weight = Weight(primary.Verdict) + (western is null ? 0 : Weight(western.Verdict));
        var adjustment = Math.Clamp(weight, -3, 3);
        var verdict = adjustment switch { > 0 => "lucky", < 0 => "unlucky", _ => "neutral" };
        var note = western is null ? primary.Reason : $"{primary.Reason} {western.Reason}";

        return new UnitNumerologyAnnotation(unitNumber, floor, principleSet, adjustment, verdict, note);
    }

    /// <summary>
    /// Annotates every unit on a plan for one principle set. Pure and read-time only —
    /// callers may construct <see cref="NumerologyService"/> with no dependencies at all
    /// and call this with no database in play.
    /// </summary>
    public IReadOnlyList<UnitNumerologyAnnotation> EvaluateUnits(
        IEnumerable<ScrapedUnit> units, string principleSet)
    {
        RequireTradition(principleSet);
        return units.Select(u => EvaluateUnit(u.UnitNumber, u.Floor, principleSet)).ToList();
    }

    /// <summary>Each tradition owns its own reading of a number (FR-18); this resolves whose to apply.</summary>
    private static Traditions.ITradition RequireTradition(string principleSet)
    {
        if (Traditions.TraditionRegistry.Find(principleSet) is { } tradition) return tradition;
        throw new ArgumentException(
            $"principleSet must be one of: {string.Join(", ", PrincipleSets.All)}.", nameof(principleSet));
    }

    private static int Weight(string verdict) => verdict switch { "lucky" => 1, "unlucky" => -2, _ => 0 };

    private static IEnumerable<(string, string?)> Subjects(ListingNumbers n) =>
    [
        ("unitNumber", n.UnitNumber),
        ("floor", n.Floor?.ToString()),
        ("streetNumber", n.StreetNumber),
    ];

    private static IEnumerable<(string, string?)> SubjectLevelNumbers(ListingNumbers n) =>
    [
        ("floor", n.Floor?.ToString()),
        ("streetNumber", n.StreetNumber),
    ];

    private static NumerologyCheck? Western(string subject, string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Contains("13"))
            return new(subject, value, "unlucky", "western",
                "13 is widely considered unlucky in Western tradition (triskaidekaphobia).",
                "A door wreath or plant at the entry is a common softening touch; many buildings simply relabel.");
        if (digits.Contains("666"))
            return new(subject, value, "unlucky", "western",
                "666 carries strong negative connotations in Western culture — flagged as culturally sensitive.",
                "An interior plaque adding a digit changes the number as read at the door.");
        return null;
    }
}
