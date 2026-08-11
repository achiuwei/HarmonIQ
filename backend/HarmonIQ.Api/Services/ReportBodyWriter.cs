using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Persistence;

namespace HarmonIQ.Api.Services;

/// <summary>One evaluated rule as the report renders it.</summary>
public record ReportRule(
    string RuleId, string Title, string Text, bool Applicable, bool Satisfied, int Severity, string PrincipleSet);

/// <summary>One room card on the photo path.</summary>
public record ReportRoomCard(
    string PhotoId, string RoomType, double Coverage,
    IReadOnlyList<ReportRule> Findings, ElementBalance? ElementBalance);

/// <summary>The single plan card on the floor-plan path. There is no per-room breakdown of a drawing.</summary>
public record ReportPlanCard(
    bool NotDeterminable, string? NotDeterminableReason, bool BoundaryFullyDrawn,
    double Coverage, IReadOnlyList<ReportRule> Findings);

/// <summary>
/// The full report body. Stored in the object store, not in the row — the row keeps only the URI
/// and the digest. <see cref="ElementBalance"/> is omitted entirely when null (the tradition does
/// not read wǔxíng, or a line drawing has no materials): the drawer omits the section rather than
/// showing zeros.
///
/// There is no <c>NumerologyAdjustment</c>: FR-20 dropped it from the contract. <see cref="Numerology"/>
/// carries the checks for the Numbers card (FR-19) and contributes to no score.
/// </summary>
public record ReportBody(
    string SubjectId,
    string PrincipleSet,
    string RulesVersion,
    string EngineVersion,
    string Status,
    string Mode,
    int? Score,
    string? Grade,
    double Confidence,
    double InteriorsCoverage,
    double SiteCoverage,
    string Cohort,
    int? InteriorsScore,
    int? SiteScore,
    string Summary,
    IReadOnlyList<ReportRule> Interiors,
    IReadOnlyList<ReportRule> Site,
    IReadOnlyList<Suggestion> Suggestions,
    IReadOnlyList<NumerologyCheck> Numerology,
    ElementBalance? ElementBalance,
    IReadOnlyList<ReportRoomCard>? Rooms,
    ReportPlanCard? Plan,
    DateTimeOffset ComputedAt);

/// <summary>
/// Serializes a report body, gzips it, and puts it at the fixed key
/// <c>reports/{engineVersion}/{subjectId}/{principleSet}.json.gz</c>. The returned digest is over
/// the <b>uncompressed</b> UTF-8 JSON so it is stable regardless of the gzip implementation.
/// </summary>
public class ReportBodyWriter(IObjectStore store)
{
    public static string KeyFor(string engineVersion, string subjectId, string principleSet) =>
        $"reports/{engineVersion}/{subjectId}/{principleSet}.json.gz";

    public async Task<(string Uri, string Sha256)> WriteAsync(ReportBody body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        var json = JsonSerializer.SerializeToUtf8Bytes(body, Json.Options);
        var sha = Convert.ToHexStringLower(SHA256.HashData(json));

        using var buffer = new MemoryStream();
        await using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            await gzip.WriteAsync(json, ct);
        }

        var key = KeyFor(body.EngineVersion, body.SubjectId, body.PrincipleSet);
        var uri = await store.PutAsync(key, buffer.ToArray(), ct);
        return (uri, sha);
    }

    /// <summary>Reads a body back (report drawer / tests).</summary>
    public async Task<ReportBody?> ReadAsync(
        string engineVersion, string subjectId, string principleSet, CancellationToken ct)
    {
        var bytes = await store.GetAsync(KeyFor(engineVersion, subjectId, principleSet), ct);
        if (bytes is null) return null;
        using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
        using var plain = new MemoryStream();
        await gzip.CopyToAsync(plain, ct);
        return JsonSerializer.Deserialize<ReportBody>(plain.ToArray(), Json.Options);
    }
}

/// <summary>
/// The deterministic per-set summary written on the scoring path. <b>No LLM summary in this path</b>
/// (design §6): narrative is lazy, on first report open, so an engine bump re-derives every row
/// with zero model calls. Copy is tradition-framed and carries no negative superlative (design §10).
/// </summary>
public static class LocalSummary
{
    public static string TraditionPhrase(string principleSet) =>
        Traditions.TraditionRegistry.Find(principleSet)?.TraditionPhrase ?? "in form-school Feng Shui terms";

    public static string TraditionName(string principleSet) =>
        Traditions.TraditionRegistry.Find(principleSet)?.DisplayName ?? "Feng Shui";

    /// <summary>
    /// The explanatory absence shown instead of a grade below the confidence floor. An
    /// orientation-gated tradition with no resolved facing gets its own explanation, authored by
    /// that tradition — Kasō explains the kimon, Vastu explains directional placement.
    /// </summary>
    public static string InsufficientEvidence(string principleSet, string evidencePath, bool orientationResolved)
    {
        var tradition = Traditions.TraditionRegistry.Find(principleSet);
        if (tradition is { RequiresOrientation: true } && !orientationResolved)
            return tradition.OrientationGateExplanation;
        return evidencePath == Cohort.FloorPlan
            ? $"The floor-plan drawing did not support enough of the {TraditionName(principleSet)} rule set to place a grade behind it, so this plan is left unscored."
            : $"The available evidence did not support enough of the {TraditionName(principleSet)} rule set to place a grade behind it, so this home is left unscored.";
    }

    public static string Build(
        string principleSet,
        LensResult? interiors,
        LensResult site,
        IReadOnlyList<Suggestion> suggestions,
        NumerologyResult numerology)
    {
        var parts = new List<string>();
        var phrase = TraditionPhrase(principleSet);

        var interiorOutcomes = interiors?.Outcomes ?? [];
        var (iSat, iApp) = Counts(interiorOutcomes);
        if (iApp > 0)
        {
            parts.Add($"{Capitalize(phrase)}, the layout reading satisfies {iSat} of the {iApp} interior principles this evidence supports.");
        }

        var (sSat, sApp) = Counts(site.Outcomes);
        if (sApp > 0)
        {
            parts.Add(iApp > 0
                ? $"The surroundings satisfy {sSat} of {sApp}."
                : $"{Capitalize(phrase)}, the surroundings satisfy {sSat} of the {sApp} site principles this location supports.");
        }

        var unmet = interiorOutcomes.Concat(site.Outcomes)
            .Where(o => o.Applicable && !o.Satisfied)
            .OrderByDescending(o => o.Severity)
            .ThenBy(o => o.RuleId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (unmet is not null) parts.Add(unmet.Text);

        var top = suggestions
            .OrderByDescending(s => Rank(s.Impact)).ThenBy(s => Rank(s.Effort))
            .ThenBy(s => s.Title, StringComparer.Ordinal)
            .FirstOrDefault();
        if (top is not null) parts.Add($"The highest-impact adjustment a renter can make: {top.Title} — {top.Detail.TrimEnd('.')}.");

        var flagged = numerology.Checks.Count(c => c.Verdict == "unlucky");
        if (flagged > 0)
            parts.Add($"{flagged} of the listing's numbers read as inauspicious in the traditions selected here, each with a simple remedy.");

        return parts.Count == 0
            ? $"{Capitalize(phrase)}, this subject's evidence did not support a reading."
            : string.Join(" ", parts);

        static int Rank(string level) => level switch { "high" => 3, "medium" => 2, _ => 1 };
    }

    private static (int Satisfied, int Applicable) Counts(IEnumerable<RuleOutcome> outcomes)
    {
        int sat = 0, app = 0;
        foreach (var o in outcomes)
        {
            if (!o.Applicable) continue;
            app++;
            if (o.Satisfied) sat++;
        }
        return (sat, app);
    }

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
