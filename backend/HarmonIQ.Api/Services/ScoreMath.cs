using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

public static class ScoreMath
{
    public static int Overall(IReadOnlyList<RoomAnalysis> rooms, SiteAnalysis site, int numerologyAdjustment)
    {
        var baseScore = rooms.Count == 0
            ? site.Score
            : 0.7 * rooms.Average(r => r.Score) + 0.3 * site.Score;
        return Math.Clamp((int)Math.Round(baseScore) + numerologyAdjustment, 0, 100);
    }

    public static string Grade(int score) => score switch
    {
        >= 95 => "A+", >= 90 => "A", >= 85 => "A-",
        >= 80 => "B+", >= 75 => "B", >= 70 => "B-",
        >= 65 => "C+", >= 60 => "C", >= 55 => "C-",
        >= 50 => "D+", >= 45 => "D", >= 40 => "D-",
        _ => "F",
    };

    public static ElementBalance AverageElements(IReadOnlyList<RoomAnalysis> rooms)
    {
        if (rooms.Count == 0) return new ElementBalance(0, 0, 0, 0, 0);
        return new ElementBalance(
            (int)rooms.Average(r => r.ElementBalance.Wood),
            (int)rooms.Average(r => r.ElementBalance.Fire),
            (int)rooms.Average(r => r.ElementBalance.Earth),
            (int)rooms.Average(r => r.ElementBalance.Metal),
            (int)rooms.Average(r => r.ElementBalance.Water));
    }

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
