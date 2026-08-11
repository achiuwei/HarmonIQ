using System.Text.Json.Serialization;

namespace HarmonIQ.Api.Models;

public record PhotoSelection(string PhotoId, string? RoomType);

public record AnalyzeRequest(
    string ListingId,
    List<PhotoSelection> Photos,
    string Systems = "both",
    string Orientation = "unknown",
    ListingEnvironment? Environment = null,
    ListingNumbers? Numbers = null,
    string? Brand = null);

public record Finding(
    string Principle, string Observation,
    [property: JsonPropertyName("system")] string Tradition);

public record ViolationFinding(
    string Principle, string Observation, string Severity,
    [property: JsonPropertyName("system")] string Tradition);

public record Suggestion(string Title, string Detail, string Effort, string Impact);

public record ElementBalance(int Wood, int Fire, int Earth, int Metal, int Water)
{
    /// <summary>
    /// An all-zero balance means "nothing was reported", not "no elements present" — a line
    /// drawing has no materials to read. Callers drop these rather than rendering empty bars.
    /// </summary>
    public bool IsAllZero => Wood == 0 && Fire == 0 && Earth == 0 && Metal == 0 && Water == 0;
}

public record RoomAnalysis(
    string PhotoId, string RoomType, int Score, ElementBalance ElementBalance,
    List<Finding> Adhering, List<ViolationFinding> Violations, List<Suggestion> Suggestions);

public record SiteAnalysis(
    int Score, List<Finding> Adhering, List<ViolationFinding> Violations, List<Suggestion> Suggestions);

public record NumerologyCheck(
    string Subject, string Value, string Verdict, string Tradition, string Reason, string? Remedy);

public record NumerologyResult(int ScoreAdjustment, List<NumerologyCheck> Checks);

public record ListingSummary(string ListingId, string Title, string Address, string Url);

public record AnalysisResult(
    int OverallScore, string Grade, string Summary, ElementBalance ElementBalance,
    List<RoomAnalysis> Rooms, SiteAnalysis Site, NumerologyResult Numerology);

public record AnalyzeResponse(
    string Mode, string? ModelId, string? Notice, ListingSummary Listing, AnalysisResult Analysis);
