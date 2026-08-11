using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

/// <summary>
/// Deterministic, pure numerology engine. No DbContext, no I/O — safe as a singleton
/// and safe to call at read time with no persistence side effect.
///
/// Two distinct surfaces, per design Q1 / SPEC v2 FR-17..20:
/// <list type="bullet">
/// <item><description>
/// <see cref="EvaluateSubject"/> — the subject-level (building floor / street number)
/// ±3 that still nudges <c>ScoreMath.Aggregate</c>'s final score, now scoped per
/// principle set rather than the v1 tri-state <c>systems</c> string.
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
    /// The subject-level check that actually enters the score (via
    /// <c>ScoreMath.Aggregate</c>'s <c>numerologyAdjustment</c> parameter). Deliberately
    /// excludes the unit number — a subject (a floor plan or a property) may host many
    /// units, and no single unit's number may bias the subject's grade. Only the
    /// building floor and street number, which are properties of the subject itself,
    /// are considered here.
    /// </summary>
    public NumerologyResult EvaluateSubject(ListingNumbers? numbers, string principleSet)
    {
        RequireKnownSet(principleSet);
        var checks = new List<NumerologyCheck>();
        if (numbers is not null)
        {
            foreach (var (subject, value) in SubjectLevelNumbers(numbers))
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                checks.Add(principleSet == PrincipleSets.Vastu ? Vastu(subject, value) : Chinese(subject, value));
                if (Western(subject, value) is { } w) checks.Add(w); // only when triggered
            }
        }
        var adj = Math.Clamp(checks.Sum(c => Weight(c.Verdict)), -3, 3);
        return new NumerologyResult(adj, checks);
    }

    /// <summary>
    /// One unit's read-time annotation for a single principle set. Pure: same input
    /// always yields the same output, no DB access, nothing persisted. Never a
    /// competing grade — <see cref="UnitNumerologyAnnotation.Adjustment"/> exists only
    /// for internal ordering and must never be rendered as a score.
    /// </summary>
    public UnitNumerologyAnnotation EvaluateUnit(string unitNumber, int? floor, string principleSet)
    {
        RequireKnownSet(principleSet);
        var primary = principleSet == PrincipleSets.Vastu
            ? Vastu("unitNumber", unitNumber)
            : Chinese("unitNumber", unitNumber);
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
        RequireKnownSet(principleSet);
        return units.Select(u => EvaluateUnit(u.UnitNumber, u.Floor, principleSet)).ToList();
    }

    private static void RequireKnownSet(string principleSet)
    {
        if (!PrincipleSets.IsKnown(principleSet))
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

    private static NumerologyCheck Chinese(string subject, string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Contains('4'))
        {
            var combo = digits.Contains("14") ? " The pair 14 (yao sì) sounds like \"will die\" — considered especially inauspicious."
                      : digits.Contains("24") ? " The pair 24 (èr sì) sounds like \"easy to die\" — considered especially inauspicious." : "";
            return new(subject, value, "unlucky", "fengshui",
                $"Contains the digit 4 (sì), a homophone of death (sǐ) in Chinese numerology.{combo}",
                "Add a small interior plaque so the number read at the door sums to an auspicious digit, or place a red accent at the threshold.");
        }
        if (digits.Contains('8'))
            return new(subject, value, "lucky", "fengshui",
                "Contains 8 (bā), a homophone of prosperity (fā) — the most auspicious digit in Chinese numerology.", null);
        if (digits.Contains('9'))
            return new(subject, value, "lucky", "fengshui",
                "Contains 9 (jiǔ), a homophone of long-lasting — associated with longevity in Chinese numerology.", null);
        return new(subject, value, "neutral", "fengshui",
            "No strongly charged digits (4, 8, 9) in Chinese numerology.", null);
    }

    private static NumerologyCheck Vastu(string subject, string value)
    {
        var digits = value.Where(char.IsDigit).Select(c => c - '0').ToList();
        if (digits.Count == 0)
            return new(subject, value, "neutral", "vastu", "No digits to reduce.", null);
        var sum = digits.Sum();
        while (sum > 9) sum = sum.ToString().Sum(c => c - '0');
        var (verdict, meaning) = sum switch
        {
            1 => ("lucky", "1 (Sun) — leadership and new beginnings"),
            2 => ("neutral", "2 (Moon) — sensitivity and partnership; balanced, not charged"),
            3 => ("lucky", "3 (Jupiter) — growth, learning, and family expansion"),
            4 => ("unlucky", "4 (Rahu) — instability and sudden change"),
            5 => ("lucky", "5 (Mercury) — communication, adaptability, and movement"),
            6 => ("lucky", "6 (Venus) — harmony and domestic wellbeing"),
            7 => ("neutral", "7 (Ketu) — introspection and spirituality; suits quiet households"),
            8 => ("unlucky", "8 (Saturn) — heaviness and karmic lessons"),
            _ => ("lucky", "9 (Mars) — energy, courage, and completion"),
        };
        return new(subject, value, verdict, "vastu",
            $"Digit sum {sum} — in Indian numerology, {meaning}.",
            verdict == "unlucky"
                ? "Add an interior door plaque with an extra digit so the number as read reduces to 1, 3, 5, 6, or 9."
                : null);
    }

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
