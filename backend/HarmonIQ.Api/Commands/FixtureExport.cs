using HarmonIQ.Api.Models;
using HarmonIQ.Api.Persistence;
using HarmonIQ.Api.Services;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarmonIQ.Api.Commands;

/// <summary>One lens of a row, as apartments-web's <c>LensScore</c> reads it.</summary>
public record FixtureLens(string Name, int? Score, double Weight, string? Notes);

/// <summary>One element's share, as apartments-web's <c>ElementShare</c> reads it.</summary>
public record FixtureElementShare(string Element, double Share);

/// <summary>One annotated number, as apartments-web's <c>UnitNumerology</c> reads it.</summary>
public record FixtureUnitNumerology(string Unit, string Note);

/// <summary>
/// One row of apartments-web's <c>harmoniq-grades.json</c>. Property names serialize camelCase
/// under <see cref="Json.Options"/>, matching that repo's <c>[JsonPropertyName]</c> attributes;
/// nulls are omitted, so an unscored row carries no <c>score</c>/<c>grade</c> keys at all.
/// </summary>
public record FixtureGradeRow(
    string SubjectId,
    string ListingId,
    string? FloorPlanId,
    string PrincipleSet,
    string EngineVersion,
    string Status,
    int? Score,
    string? Grade,
    string Cohort,
    double Confidence,
    double Coverage,
    string? Summary,
    string? Explanation,
    IReadOnlyList<FixtureLens> Lenses,
    IReadOnlyList<string> Suggestions,
    IReadOnlyList<FixtureElementShare>? ElementBalance,
    IReadOnlyList<FixtureUnitNumerology> Numerology);

/// <summary>The file's envelope — the same shape as <c>GradesFeedPage</c>.</summary>
public record FixtureGradesFile(IReadOnlyList<FixtureGradeRow> Rows, string? NextCursor);

/// <summary>Parsed CLI arguments for <c>export-fixture</c>.</summary>
public record ExportFixtureOptions(IReadOnlyList<string> Properties, string? EngineVersion, string Out)
{
    public const string DefaultOut = "harmoniq-grades.json";

    public static ExportFixtureOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var properties = new List<string>();
        string? engineVersion = null;
        var output = DefaultOut;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--property" when i + 1 < args.Length:
                    properties.Add(args[++i]);
                    break;
                case "--engine" when i + 1 < args.Length:
                    engineVersion = args[++i];
                    break;
                case "--out" when i + 1 < args.Length:
                    output = args[++i];
                    break;
            }
        }

        return new ExportFixtureOptions(properties, engineVersion, output);
    }
}

/// <summary>
/// <c>export-fixture</c>: writes apartments-web's <c>harmoniq-grades.json</c> from the report
/// bodies HarmonIQ actually produced, so that repo's fixture source stops being hand-authored.
///
/// The grades feed is deliberately NOT the source. A <c>ProjectionRow</c> carries eleven columns —
/// no status, summary, lenses, element balance or numerology — and the consumer drops score and
/// grade for any row whose status is not <c>"ok"</c>, so a feed dump would render as no grades at
/// all. The report bodies carry the whole card. They also exist for demo-mode runs, which never
/// publish, which is the only reason a local export is possible before the roll-out.
///
/// Writing the file is where this command stops. Copying it into apartments-web is a human step,
/// on that repo's local <c>harmoniq-demo</c> branch (SPEC §7 / FR-6b).
/// </summary>
public class ExportFixtureCommand(IServiceScopeFactory scopeFactory) : IHarmonIQCommand
{
    public string Name => "export-fixture";

    public string Description => "Write apartments-web's harmoniq-grades.json from stored report bodies.";

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        var options = ExportFixtureOptions.Parse(args);
        if (options.Properties.Count == 0)
        {
            Console.WriteLine("export-fixture: pass --property <key> (repeatable), optionally --engine <version> and --out <path>.");
            return 1;
        }

        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<HarmonIQDbContext>();
        var reports = services.GetRequiredService<ReportBodyWriter>();

        var engineVersion = options.EngineVersion
            ?? (await services.GetRequiredService<IEngineVersionService>().GetOrCreateCurrentAsync(ct)).Version;

        var file = await new FixtureExporter(db, reports).ExportAsync(options.Properties, engineVersion, ct);

        // Per-property counts, so a property that scored nothing is visible rather than absorbed
        // into a total that looks healthy.
        foreach (var property in options.Properties)
        {
            var rows = file.Rows.Where(r => r.ListingId == property).ToList();
            var scored = rows.Count(r => r.Status == AnalysisStatuses.Ok);
            Console.WriteLine(rows.Count == 0
                ? $"  {property,-24} no rows — nothing scored for engine {engineVersion}."
                : $"  {property,-24} {rows.Count} rows ({scored} scored, {rows.Count - scored} unscored).");
        }

        await File.WriteAllTextAsync(options.Out, JsonSerializer.Serialize(file, Json.Options), ct);
        Console.WriteLine($"export-fixture: engine={engineVersion} — {file.Rows.Count} rows written to {options.Out}.");

        return file.Rows.Count == 0 ? 1 : 0;
    }
}

/// <summary>
/// Assembles the fixture file from what HarmonIQ actually scored.
/// </summary>
public class FixtureExporter(HarmonIQDbContext db, ReportBodyWriter reports)
{
    /// <summary>
    /// <c>analyses</c> is the index of what was scored; the object store holds what the consumer
    /// renders. An analysis whose body is missing is skipped rather than emitted hollow — a row
    /// with no summary or lenses would render as a grade with no reasoning behind it.
    /// </summary>
    public async Task<FixtureGradesFile> ExportAsync(
        IReadOnlyList<string> propertyKeys, string engineVersion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(propertyKeys);

        var keys = propertyKeys.ToList();
        var pairs = await (
            from analysis in db.Analyses
            join subject in db.Subjects on analysis.SubjectId equals subject.Id
            where analysis.EngineVersion == engineVersion && keys.Contains(subject.PropertyKey)
            select new { subject, analysis.PrincipleSet }).ToListAsync(ct);

        // Ordered so re-exporting the same data produces the same file, and a diff of two exports
        // shows what the engine changed rather than what the query planner did.
        var ordered = pairs
            .OrderBy(p => p.subject.PropertyKey, StringComparer.Ordinal)
            .ThenBy(p => p.subject.Id, StringComparer.Ordinal)
            .ThenBy(p => p.PrincipleSet, StringComparer.Ordinal);

        var rows = new List<FixtureGradeRow>();
        foreach (var pair in ordered)
        {
            var body = await reports.ReadAsync(engineVersion, pair.subject.Id, pair.PrincipleSet, ct);
            if (body is null)
            {
                continue;
            }

            rows.Add(FixtureRowMapper.Map(pair.subject, body));
        }

        // The consumer paginates the real feed; a fixture is always the whole set.
        return new FixtureGradesFile(rows, null);
    }
}

/// <summary>
/// Maps a stored report body onto the consumer's row shape.
/// </summary>
public static class FixtureRowMapper
{
    public static FixtureGradeRow Map(Subject subject, ReportBody body)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(body);

        // apartments-web drops score and grade unless status is exactly "ok", and shows the
        // explanation in their place — so the narrative has to move fields, not just tag along.
        var scored = body.Status == AnalysisStatuses.Ok;

        return new FixtureGradeRow(
            SubjectId: body.SubjectId,
            ListingId: subject.PropertyKey,
            FloorPlanId: subject.ExternalPlanKey,
            PrincipleSet: body.PrincipleSet,
            EngineVersion: body.EngineVersion,
            Status: body.Status,
            Score: body.Score,
            Grade: body.Grade,
            Cohort: body.Cohort,
            Confidence: body.Confidence,
            Coverage: (ScoreMath.InteriorsWeight * body.InteriorsCoverage)
                + (ScoreMath.SiteWeight * body.SiteCoverage),
            Summary: scored ? body.Summary : null,
            Explanation: scored ? null : body.Summary,
            Lenses: Lenses(body),
            Suggestions: [.. body.Suggestions.Select(s => $"{s.Title} — {s.Detail}")],
            ElementBalance: Elements(body.ElementBalance),
            Numerology: [.. body.Numerology
                .Where(c => c.Verdict == "unlucky")
                .Select(c => new FixtureUnitNumerology(
                    c.Value,
                    string.IsNullOrWhiteSpace(c.Remedy) ? c.Reason : $"{c.Reason} {c.Remedy}"))]);
    }

    /// <summary>
    /// The engine reads exactly two lenses, at <see cref="ScoreMath.InteriorsWeight"/> and
    /// <see cref="ScoreMath.SiteWeight"/>. Their weights are reported as the engine defines them,
    /// not as the coverage-adjusted effective weights of one particular subject.
    /// </summary>
    private static IReadOnlyList<FixtureLens> Lenses(ReportBody body) =>
    [
        new("Interiors", body.InteriorsScore, ScoreMath.InteriorsWeight,
            Note(body.Interiors, "interior principles this evidence supports")),
        new("Site", body.SiteScore, ScoreMath.SiteWeight,
            Note(body.Site, "site principles this location supports")),
    ];

    /// <summary>
    /// A lens note counts only the principles the evidence could actually judge — an inapplicable
    /// rule is absent evidence, never a violation, so it belongs in neither total. No applicable
    /// rule at all means no note: an empty count reads as a zero score, which it is not.
    /// </summary>
    private static string? Note(IReadOnlyList<ReportRule> rules, string noun)
    {
        var applicable = rules.Count(r => r.Applicable);
        if (applicable == 0)
        {
            return null;
        }

        var satisfied = rules.Count(r => r.Applicable && r.Satisfied);
        return $"Satisfies {satisfied} of the {applicable} {noun}.";
    }

    /// <summary>
    /// Shares are reported as fractions of what the reading actually found, so the bars sum to 1
    /// whatever scale the model returned. An all-zero balance is dropped rather than normalized:
    /// it means "nothing was reported" (a line drawing has no materials), and dividing by its zero
    /// total would be the difference between an omitted section and five NaN bars.
    /// </summary>
    private static IReadOnlyList<FixtureElementShare>? Elements(ElementBalance? balance)
    {
        if (balance is null || balance.IsAllZero)
        {
            return null;
        }

        double total = balance.Wood + balance.Fire + balance.Earth + balance.Metal + balance.Water;
        return
        [
            new("Wood", balance.Wood / total),
            new("Fire", balance.Fire / total),
            new("Earth", balance.Earth / total),
            new("Metal", balance.Metal / total),
            new("Water", balance.Water / total),
        ];
    }
}
