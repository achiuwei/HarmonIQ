using System.Text.Json;
using System.Text.Json.Serialization;

namespace HarmonIQ.Api.Models;

/// <summary>
/// The principle-set wire ids — one per culture's tradition. There is no blended score: a
/// multi-tradition view is a UI union of the stored per-set rows, never an additional score.
///
/// These are consts so tradition implementations can use them without triggering
/// <c>TraditionRegistry</c>'s static initializer. <see cref="All"/> and <see cref="IsKnown"/>
/// delegate to that registry, which is the single source of truth for which traditions exist.
/// </summary>
public static class PrincipleSets
{
    public const string FengShui = "fengshui";
    public const string Vastu = "vastu";
    public const string Pungsu = "pungsu";
    public const string Kaso = "kaso";
    public const string PhongThuy = "phongthuy";

    public static IReadOnlyList<string> All => Services.Traditions.TraditionRegistry.Ids;

    public static bool IsKnown(string? principleSet) =>
        Services.Traditions.TraditionRegistry.IsKnown(principleSet);
}

/// <summary>Statuses an analysis row may carry. <c>insufficient_evidence</c> is permanent and non-retryable.</summary>
public static class AnalysisStatuses
{
    public const string Pending = "pending";
    public const string Ok = "ok";
    public const string Failed = "failed";
    public const string InsufficientEvidence = "insufficient_evidence";
}

/// <summary>
/// The result of evaluating one rule against one subject.
/// <paramref name="Applicable"/> is false whenever the evidence needed to judge the rule is
/// absent — an unknown value is never a violation. <paramref name="Severity"/> ∈ {1,2,3} is the
/// rule's weight in the normalized fraction. <paramref name="Text"/> is the tradition-framed
/// sentence shown to a reader; it names the tradition and carries no negative superlative.
/// </summary>
public record RuleOutcome(
    string RuleId, string PrincipleSet, bool Applicable, bool Satisfied, int Severity, string Text);

/// <summary>
/// One lens's contribution: a normalized [0,1] score over the rules it could actually judge,
/// plus the coverage fraction that determines how much weight the lens earns.
/// </summary>
public record LensResult(string LensId, double Score01, double Coverage, IReadOnlyList<RuleOutcome> Outcomes)
{
    public const string Interiors = "interiors";
    public const string Site = "site";
}

/// <summary>
/// The comparison bucket a score belongs to. Ranking and filtering happen within cohort.
/// The stored form is <c>"{evidencePath}/{orientationPath}"</c>, e.g. <c>"floorplan/without"</c>.
/// </summary>
public record Cohort(string EvidencePath, string OrientationPath)
{
    public const string Photos = "photos";
    public const string FloorPlan = "floorplan";
    public const string With = "with";
    public const string Without = "without";

    public override string ToString() => $"{EvidencePath}/{OrientationPath}";

    public static IReadOnlyList<Cohort> All { get; } =
    [
        new(Photos, With), new(Photos, Without),
        new(FloorPlan, With), new(FloorPlan, Without),
    ];

    public static Cohort Parse(string value)
    {
        var parts = (value ?? string.Empty).Split('/', 2);
        return parts.Length == 2 ? new Cohort(parts[0], parts[1]) : new Cohort(Photos, Without);
    }
}

/// <summary>Per-cohort linear calibration: <c>calibrated = Offset + Scale · raw</c>.</summary>
public record CalibrationConstants(
    [property: JsonPropertyName("offset")] double Offset,
    [property: JsonPropertyName("scale")] double Scale)
{
    public static readonly CalibrationConstants Identity = new(0.0, 1.0);

    public double Apply(double score100) => Offset + Scale * score100;
}

/// <summary>
/// Calibration constants keyed by <see cref="Cohort.ToString"/>. Loaded from
/// <c>EngineVersion.CalibrationJson</c> — derived offline by task zero, never computed live.
/// Absent cohorts fall back to identity.
/// </summary>
public record Calibration(IReadOnlyDictionary<string, CalibrationConstants> ByCohort)
{
    public static readonly Calibration Identity =
        new(new Dictionary<string, CalibrationConstants>(StringComparer.Ordinal));

    public CalibrationConstants For(Cohort cohort) =>
        ByCohort.TryGetValue(cohort.ToString(), out var c) ? c : CalibrationConstants.Identity;

    public static Calibration FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Identity;
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, CalibrationConstants>>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return map is null or { Count: 0 }
                ? Identity
                : new Calibration(new Dictionary<string, CalibrationConstants>(map, StringComparer.Ordinal));
        }
        catch (JsonException)
        {
            return Identity;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(ByCohort);
}

/// <summary>
/// The deterministic per-principle-set verdict. <c>Score</c> and <c>Grade</c> are null unless
/// <c>Status == "ok"</c> — an unscored subject is never rendered as a bad one.
///
/// <c>ElementBalance</c> carries wǔxíng and is present only for the traditions that use it
/// (<see cref="Services.Traditions.ITradition.UsesWuxing"/>); it is null — never five zeros —
/// otherwise, so the report omits the section rather than showing an empty one.
///
/// There is deliberately <b>no numerology adjustment</b>: per FR-20 numerology is cultural
/// annotation only and adjusts no stored score.
/// </summary>
public record SetScore(
    string PrincipleSet,
    string Status,
    int? Score,
    string? Grade,
    double Confidence,
    double InteriorsCoverage,
    double SiteCoverage,
    Cohort Cohort,
    int? InteriorsScore,
    int? SiteScore,
    ElementBalance? ElementBalance,
    string Summary,
    IReadOnlyList<RuleOutcome> Outcomes);
