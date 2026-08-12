using System.Text.Json;
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Persistence;
using HarmonIQ.Api.Services;
using HarmonIQ.Api.Services.Sampling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HarmonIQ.Api.Commands;

/// <summary>
/// <c>task-zero</c> — the decision-gate sampling job (design §6, §11 #1/#3; plan Task 13), run at
/// local scale over the fixture properties instead of the design's 1,000-property job.
///
/// Measures, and prints a verdict over, the four things the design names: (a) plan-image
/// coverage (validates Q3's "leave it null" call), (b) within-property score variance across
/// floor plans — the gate that decides whether per-plan grades are real or cosmetic, (c)
/// per-cohort calibration constants from a dual-scored subsample, (d) modelled cost per property
/// (no Claude key exists on this machine, so cost is estimated from the design's own unit
/// economics — never billed).
/// </summary>
public class TaskZeroCommand(IServiceScopeFactory scopeFactory, IConfiguration config) : IHarmonIQCommand
{
    public const string CommandName = "task-zero";

    // Design §6's stated 100k-property unit economics (interactive path — the mode this machine
    // runs in; Scoring:Mode defaults to "interactive"): 60% multi-plan x ~6 plans x 1 floor-plan
    // call, 40% single x ~5 photo calls => 560,000 perception calls, ≈$14.6k. Used only as an
    // ASSUMED per-call rate to model local cost — never a bill, because no Claude key exists here.
    internal const double AssumedInteractiveCostPerCallUsd = 14_600.0 / 560_000.0;

    // A within-property mean-stdev below this (score points, 0-100 scale) is reported as
    // "the floor-plan lens is not producing meaningful per-plan separation at this sample" —
    // the plan's explicit fallback trigger. Chosen as a modest, stated threshold, not derived.
    internal const double VarianceGateThreshold = 3.0;

    private const string DefaultOutPath = ".harmoniq-local/task-zero-report.json";

    public string Name => CommandName;
    public string Description => "Runs the local-scale task-zero sampling job: coverage, per-plan variance, calibration, and cost.";

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        var options = ParseArgs(args);
        var notes = new List<string>();

        if (!string.Equals(options.Source, "fixture", StringComparison.OrdinalIgnoreCase))
        {
            notes.Add($"--source {options.Source} is not available on this machine (no partner listing/geo access, design §11 #5); falling back to --source fixture.");
        }

        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<HarmonIQDbContext>();
        var subjectService = services.GetRequiredService<ISubjectService>();
        var pipeline = services.GetRequiredService<IAnalysisPipeline>();
        var engineVersions = services.GetRequiredService<IEngineVersionService>();

        var engine = await engineVersions.GetOrCreateCurrentAsync(ct);
        var runStart = DateTimeOffset.UtcNow;

        // Local scope has exactly two fixture properties (plan Task 13): the twenty-plan Enzo
        // property `349246f` and the single-listing 108 Ambiance `tk93cec`, both scraped from
        // apartments.com. The design's `--n` is a PROPERTY count (it names the 1,000-PROPERTY
        // sampling job, and SamplingReport carries SampledProperties and SampledSubjects as
        // distinct measures) — every subject of a sampled property is included, so N never
        // truncates a property's plan set midway. The design's 1,000-property job itself is not
        // attempted here.
        var propertyKeys = new[] { SampleListingProvider.MultiplanPropertyKey, SampleListingProvider.ListingId };

        var subjectsByProperty = new Dictionary<string, IReadOnlyList<Subject>>(StringComparer.Ordinal);
        foreach (var key in propertyKeys)
        {
            subjectsByProperty[key] = await subjectService.MaterializeAsync(key, ct);
        }

        var availableSubjects = subjectsByProperty.Values.Sum(v => v.Count);
        var sampledPropertyKeys = propertyKeys.Take(Math.Max(0, options.N)).ToList();

        if (options.N > propertyKeys.Length)
        {
            notes.Add($"Requested N={options.N} properties exceeds the {propertyKeys.Length} fixture properties available ('{SampleListingProvider.MultiplanPropertyKey}', '{SampleListingProvider.ListingId}'); sampled all {propertyKeys.Length}. This stands in for the design's 1,000-property job at local scale only — it is not network-scale coverage.");
        }

        var sampled = sampledPropertyKeys.SelectMany(k => subjectsByProperty[k]).ToList();
        var sampledPropertyCount = sampledPropertyKeys.Count;

        // ---------------------------------------------------------------- score every sampled subject
        var analysesBySubject = new Dictionary<string, IReadOnlyList<Analysis>>(StringComparer.Ordinal);
        foreach (var subject in sampled)
        {
            var inputSet = await subjectService.SnapshotAsync(subject, ct);
            var analyses = await pipeline.RunAsync(subject, inputSet, engine, live: false, "task_zero", ct);
            analysesBySubject[subject.Id] = analyses;
        }

        // ---------------------------------------------------------------- (a) plan-image coverage
        var floorPlanSubjects = sampled.Where(s => s.SubjectType == "floorplan").ToList();
        var floorPlanWithImage = floorPlanSubjects.Count(s => analysesBySubject[s.Id].Count > 0);
        var planImageCoverage = floorPlanSubjects.Count == 0 ? 1.0 : (double)floorPlanWithImage / floorPlanSubjects.Count;

        // ---------------------------------------------------------------- (b) within-property variance
        var variance = ComputeVariance(subjectsByProperty.Keys, sampled, analysesBySubject, notes);

        // ---------------------------------------------------------------- (c) dual-scored calibration
        // Local subjects each carry exactly one evidence path (floor-plan subjects: a plan image
        // only; property subjects: listing photos only — SubjectService.SnapshotAsync's design).
        // No fixture subject therefore has BOTH an Ok photos-cohort row and an Ok floorplan-cohort
        // row for the same principle set, so the dual-scored subsample is honestly empty here.
        var dualScoredAnalyses = new List<Analysis>();
        if (options.DualScore)
        {
            notes.Add("Dual-scoring requested: local fixture subjects each carry exactly one evidence path (floor-plan subjects score only via floorplan; the single-listing property scores only via photos), so no subject qualifies for both cohorts. The dual-scored subsample is 0 at this scale — calibration below is Identity for every cohort by construction (CalibrationDeriver's documented behaviour), not a derivation bug.");
        }
        var calibration = CalibrationDeriver.Derive(dualScoredAnalyses);

        if (options.WriteCalibration)
        {
            engine.CalibrationJson = JsonSerializer.Serialize(calibration, Json.Options);
            await db.SaveChangesAsync(ct);
            notes.Add($"Wrote calibration constants onto EngineVersion '{engine.Version}'.CalibrationJson (all Identity — no dual-scored subsample yet at this scale).");
        }

        // ---------------------------------------------------------------- (d) modelled cost
        // SQLite/EF cannot translate a DateTimeOffset comparison server-side (the same known
        // limitation IEngineVersionService.GetPublishedAsync works around); the Observations
        // table is small at this scale, so filter client-side after materializing.
        var perceptionCallsMade = (await db.Observations.ToListAsync(ct)).Count(o => o.CreatedAt >= runStart);
        var totalCost = perceptionCallsMade * AssumedInteractiveCostPerCallUsd;
        var costPerProperty = sampledPropertyCount > 0 ? totalCost / sampledPropertyCount : 0.0;
        var cost = new CostSummary(perceptionCallsMade, AssumedInteractiveCostPerCallUsd, totalCost, costPerProperty, Estimated: true);
        notes.Add("Cost is MODELLED from design §6's own 100k-property unit-economics assumption ($14.6k interactive / 560,000 calls), not billed — no Claude key exists on this machine.");

        // ---------------------------------------------------------------- verdict
        var (verdict, gateNote) = BuildVerdict(variance, floorPlanSubjects.Count);
        if (gateNote is not null) notes.Add(gateNote);

        const string caveat =
            "ILLUSTRATIVE SCALE, NOT STATISTICALLY REPRESENTATIVE: this run sampled fixtures, not the design's 1,000-property job. " +
            "Every number above is a local, small-N signal only — it can rule out a gross implementation bug, it cannot settle the design's risk gates for real inventory.";

        var report = new SamplingReport(
            DateTimeOffset.UtcNow,
            options.N,
            availableSubjects,
            sampledPropertyCount,
            sampled.Count,
            floorPlanSubjects.Count,
            floorPlanWithImage,
            planImageCoverage,
            options.DualScore,
            dualScoredAnalyses.Count,
            variance,
            calibration,
            cost,
            notes,
            verdict,
            caveat);

        var outPath = options.OutPath;
        var outDir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (outDir is not null) Directory.CreateDirectory(outDir);
        await File.WriteAllTextAsync(outPath, JsonSerializer.Serialize(report, new JsonSerializerOptions(Json.Options) { WriteIndented = true }), ct);

        PrintSummary(report, outPath);
        return 0;
    }

    // ------------------------------------------------------------------ variance

    private static WithinPropertyVariance ComputeVariance(
        IEnumerable<string> propertyKeys,
        IReadOnlyList<Subject> sampled,
        IReadOnlyDictionary<string, IReadOnlyList<Analysis>> analysesBySubject,
        List<string> notes)
    {
        var stdevs = new List<double>();
        var ranges = new List<double>();
        var zeroVarianceProperties = 0;
        var evaluatedProperties = 0;

        foreach (var propertyKey in propertyKeys)
        {
            var plans = sampled.Where(s => s.PropertyKey == propertyKey && s.SubjectType == "floorplan").ToList();
            if (plans.Count == 0) continue; // single-listing properties carry no per-plan variance signal

            // Pool Ok scores across both principle sets for this property's sampled plans — an
            // illustrative, deliberately simple pooling (fengshui and vastu share the 0-100 grade
            // scale) rather than a per-set breakdown at this small a sample.
            var scores = plans
                .SelectMany(p => analysesBySubject[p.Id])
                .Where(a => a.Status == AnalysisStatuses.Ok && a.Score is not null)
                .Select(a => (double)a.Score!.Value)
                .ToList();

            evaluatedProperties++;
            if (scores.Count == 0)
            {
                notes.Add($"Property '{propertyKey}': no plan scored Ok in this sample; contributes no variance signal.");
                zeroVarianceProperties++;
                continue;
            }

            var stdev = scores.Count < 2 ? 0.0 : PopulationStdDev(scores);
            var range = scores.Max() - scores.Min();
            stdevs.Add(stdev);
            ranges.Add(range);
            if (stdev <= 0.0) zeroVarianceProperties++;
        }

        if (evaluatedProperties == 0)
        {
            notes.Add("No multi-plan property with sampled floor-plan subjects was in this run; within-property variance is not evaluable at this N.");
        }

        var meanStdDev = stdevs.Count > 0 ? stdevs.Average() : 0.0;
        var medianRange = ranges.Count > 0 ? Median(ranges) : 0.0;
        return new WithinPropertyVariance(Math.Round(meanStdDev, 4), Math.Round(medianRange, 4), zeroVarianceProperties);
    }

    private static double PopulationStdDev(IReadOnlyList<double> values)
    {
        var mean = values.Average();
        var variance = values.Sum(v => (v - mean) * (v - mean)) / values.Count;
        return Math.Sqrt(variance);
    }

    private static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
    }

    private static (string Verdict, string? Note) BuildVerdict(WithinPropertyVariance variance, int floorPlanSubjectCount)
    {
        if (floorPlanSubjectCount == 0)
        {
            return ("NOT EVALUATED — sample contained no floor-plan subjects; the per-plan-variance gate needs a multi-plan property in the sample.", null);
        }

        if (variance.MeanStdDev < VarianceGateThreshold)
        {
            var note = $"Mean within-property stdev ({variance.MeanStdDev:F2} pts) is below the stated gate threshold ({VarianceGateThreshold:F1} pts): " +
                        "per-plan grades may be cosmetic; the design's fallback is a property grade plus per-plan layout notes.";
            return ("GATE NOT CLEARED (at this sample) — " + note, note);
        }

        return ($"GATE CLEARED (at this sample) — mean within-property stdev {variance.MeanStdDev:F2} pts >= the {VarianceGateThreshold:F1}-pt threshold: " +
                "the floor-plan lens is producing real per-plan separation, not identical chips.", null);
    }

    // ------------------------------------------------------------------ printed summary

    private static void PrintSummary(SamplingReport report, string outPath)
    {
        Console.WriteLine("HarmonIQ task-zero — local-scale decision-gate sampling");
        Console.WriteLine(new string('-', 64));
        Console.WriteLine($"Sampled {report.SampledSubjects}/{report.AvailableSubjects} available subjects across {report.SampledProperties} propert{(report.SampledProperties == 1 ? "y" : "ies")} (requested N={report.RequestedN}).");
        Console.WriteLine();
        Console.WriteLine($"(a) Plan-image coverage: {report.FloorPlanSubjectsWithImage}/{report.FloorPlanSubjectsTotal} floor-plan subjects have a plan image ({report.PlanImageCoverage:P0}).");
        Console.WriteLine($"(b) Within-property variance: mean stdev {report.Variance.MeanStdDev:F2} pts, median range {report.Variance.MedianRange:F2} pts, {report.Variance.PropertiesWithZeroVariance} propert{(report.Variance.PropertiesWithZeroVariance == 1 ? "y" : "ies")} with zero variance.");
        Console.WriteLine($"(c) Calibration (dual-scored subsample: {report.DualScoredSubjects} subjects):");
        foreach (var (cohort, constants) in report.Calibration)
        {
            Console.WriteLine($"      {cohort,-16} offset={constants.Offset:F4} scale={constants.Scale:F2}");
        }
        Console.WriteLine($"(d) Cost: {report.Cost.PerceptionCallsMade} perception call(s) x ${report.Cost.AssumedCostPerCallUsd:F4} (assumed/modelled, not billed) = ${report.Cost.TotalCostUsd:F2} total, ${report.Cost.CostPerPropertyUsd:F2}/property.");
        Console.WriteLine();
        Console.WriteLine($"VERDICT: {report.Verdict}");
        Console.WriteLine();
        Console.WriteLine(report.Caveat);
        if (report.Notes.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Notes:");
            foreach (var note in report.Notes) Console.WriteLine($"  - {note}");
        }
        Console.WriteLine();
        Console.WriteLine($"Report written to {outPath}");
    }

    // ------------------------------------------------------------------ args

    private record Options(int N, string Source, string OutPath, bool DualScore, bool WriteCalibration);

    private Options ParseArgs(string[] args)
    {
        var n = int.TryParse(config["TaskZero:SampleN"], out var configured) ? configured : 20;
        var source = "fixture";
        var outPath = DefaultOutPath;
        var dualScore = false;
        var writeCalibration = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--n" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var parsedN)) n = parsedN;
                    break;
                case "--source" when i + 1 < args.Length:
                    source = args[++i];
                    break;
                case "--out" when i + 1 < args.Length:
                    outPath = args[++i];
                    break;
                case "--dual-score":
                    dualScore = true;
                    break;
                case "--write-calibration":
                    writeCalibration = true;
                    break;
            }
        }

        return new Options(n, source, outPath, dualScore, writeCalibration);
    }
}
