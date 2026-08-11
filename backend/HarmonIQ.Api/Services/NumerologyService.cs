using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

public class NumerologyService
{
    public NumerologyResult Evaluate(ListingNumbers? numbers, string systems)
    {
        var checks = new List<NumerologyCheck>();
        if (numbers is not null)
        {
            foreach (var (subject, value) in Subjects(numbers))
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (systems is "both" or "fengshui") checks.Add(Chinese(subject, value));
                if (systems is "both" or "vastu") checks.Add(Vastu(subject, value));
                if (Western(subject, value) is { } w) checks.Add(w); // only when triggered
            }
        }
        var adj = Math.Clamp(
            checks.Sum(c => c.Verdict switch { "lucky" => 1, "unlucky" => -2, _ => 0 }), -3, 3);
        return new NumerologyResult(adj, checks);
    }

    private static IEnumerable<(string, string?)> Subjects(ListingNumbers n) =>
    [
        ("unitNumber", n.UnitNumber),
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
        if (digits == "13")
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
