using System.Globalization;
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Persistence;
using HarmonIQ.Api.Services;
using HarmonIQ.Api.Services.Batch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HarmonIQ.Api.Commands;

/// <summary>The <c>scoring_jobs.reason</c> values the backfill command understands (design §6).</summary>
public static class BackfillReasons
{
    public const string Backfill = "backfill";
    public const string NewListing = "new_listing";
    public const string EvidenceChanged = "evidence_changed";
    public const string EngineUpgrade = "engine_upgrade";

    public static readonly IReadOnlyList<string> All = [Backfill, NewListing, EvidenceChanged, EngineUpgrade];
}

/// <summary>Parsed CLI arguments for <c>backfill</c>. See <see cref="BackfillCommand"/> for the flow.</summary>
public record BackfillOptions(
    IReadOnlyList<string> Properties, bool All, int? Limit, string Reason, bool Demo, bool Publish)
{
    public static BackfillOptions Parse(string[] args)
    {
        var properties = new List<string>();
        var all = false;
        int? limit = null;
        var reason = BackfillReasons.Backfill;
        var demo = false;
        var publish = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--property" when i + 1 < args.Length:
                    properties.Add(args[++i]);
                    break;
                case "--all":
                    all = true;
                    break;
                case "--limit" when i + 1 < args.Length:
                    limit = int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
                        ? l : limit;
                    break;
                case "--reason" when i + 1 < args.Length:
                    var candidate = args[++i];
                    reason = BackfillReasons.All.Contains(candidate, StringComparer.Ordinal) ? candidate : reason;
                    break;
                case "--demo":
                    demo = true;
                    break;
                case "--publish":
                    publish = true;
                    break;
            }
        }

        return new BackfillOptions(properties, all, limit, reason, demo, publish);
    }
}

/// <summary>
/// <c>backfill</c>: the scoring_jobs driver (design §6, Task 12). Flow, exactly as designed:
/// enumerate <c>subjects</c> (materializing them from the plan source if this is the first run)
/// → fingerprint check (the cheap idempotency win — see
/// <see cref="Services.Batch.InteractiveScoringDriver"/>) → run → record.
///
/// The Batch API path is a config-gated stub: reached only when
/// <c>Scoring:Mode == "batch" &amp;&amp; Scoring:BatchApiEnabled == true</c>, both of which default
/// off (Task 10's config), so this command always selects the interactive driver locally and the
/// batch client (<see cref="StubBatchScoringClient"/>) is never exercised in the demo.
///
/// <c>--demo</c> writes <c>analyses</c> rows with <c>Mode = "demo"</c>; <c>--publish</c> still runs
/// <see cref="IPublicationService.PublishVersionAsync"/> in that case, which correctly refuses to
/// project demo/non-ok rows — the command reports the resulting zero-row publish with an
/// explanation rather than pretending it succeeded.
/// </summary>
public class BackfillCommand(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<BackfillCommand> log) : IHarmonIQCommand
{
    public string Name => "backfill";

    public string Description => "Drive scoring_jobs over subjects: enumerate -> fingerprint check -> run.";

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        var options = BackfillOptions.Parse(args);

        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;

        var subjectService = services.GetRequiredService<ISubjectService>();
        var engineVersions = services.GetRequiredService<IEngineVersionService>();
        var publication = services.GetRequiredService<IPublicationService>();
        var driver = services.GetRequiredService<IScoringDriver>();
        var db = services.GetRequiredService<HarmonIQDbContext>();

        var engine = await engineVersions.GetOrCreateCurrentAsync(ct);

        var batchMode = string.Equals(config["Scoring:Mode"], "batch", StringComparison.OrdinalIgnoreCase)
            && bool.TryParse(config["Scoring:BatchApiEnabled"], out var batchEnabled) && batchEnabled;
        if (batchMode)
        {
            var batchClient = services.GetRequiredService<IBatchScoringClient>();
            try
            {
                await batchClient.SubmitAsync([], ct);
            }
            catch (NotSupportedException e)
            {
                Console.WriteLine($"backfill: batch scoring is config-gated off on this machine — {e.Message}");
                return 1;
            }
        }

        var hasClaudeKey = !string.IsNullOrEmpty(config["Claude:ApiKey"]) && !string.IsNullOrEmpty(config["Claude:BaseUrl"]);
        var live = !options.Demo && hasClaudeKey;

        var propertyKeys = await ResolvePropertiesAsync(options, db, ct);
        if (propertyKeys.Count == 0)
        {
            Console.WriteLine("backfill: no properties resolved — pass --property <key> (repeatable) or --all.");
            return 1;
        }

        var enqueued = 0;
        var ok = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var propertyKey in propertyKeys)
        {
            if (options.Limit is { } reached && enqueued >= reached)
            {
                break;
            }

            var subjectsForProperty = await subjectService.MaterializeAsync(propertyKey, ct);
            foreach (var subject in subjectsForProperty)
            {
                if (options.Limit is { } limit && enqueued >= limit)
                {
                    break;
                }

                enqueued++;
                var job = await driver.DriveAsync(subject, engine, options.Reason, live, ct);
                switch (job.Status)
                {
                    case "ok": ok++; break;
                    case "skipped": skipped++; break;
                    case "failed": failed++; break;
                }

                Console.WriteLine($"  {subject.Id,-40} {job.Status,-10} reason={job.Reason} attempts={job.Attempts}");
            }
        }

        Console.WriteLine(
            $"backfill: engine={engine.Version} mode={(live ? "live" : "demo")} reason={options.Reason} — " +
            $"{enqueued} enqueued, {ok} ok, {skipped} skipped, {failed} failed.");
        log.LogInformation(
            "backfill complete: engine={Engine} mode={Mode} enqueued={Enqueued} ok={Ok} skipped={Skipped} failed={Failed}",
            engine.Version, live ? "live" : "demo", enqueued, ok, skipped, failed);

        if (options.Publish)
        {
            var result = await publication.PublishVersionAsync(engine.Version, ct);
            if (result.RowsWritten == 0)
            {
                Console.WriteLine(
                    $"backfill: publish({engine.Version}) wrote 0 rows — publishing requires mode='live' AND " +
                    $"status='ok'; this was a {(live ? "live" : "demo")} run, so demo output is correctly never persisted.");
            }
            else
            {
                Console.WriteLine($"backfill: published {engine.Version} — {result.RowsWritten} rows.");
            }
        }

        return failed > 0 ? 1 : 0;
    }

    /// <summary>
    /// Local demo scope (design §12): no internal listing/geo source exists yet, so <c>--all</c>
    /// enumerates the fixtures this machine actually knows about (<see cref="SampleListingProvider"/>'s
    /// two property keys) plus anything already materialized in <c>subjects</c> from a prior run.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ResolvePropertiesAsync(
        BackfillOptions options, HarmonIQDbContext db, CancellationToken ct)
    {
        if (options.Properties.Count > 0)
        {
            return options.Properties;
        }
        if (!options.All)
        {
            return [];
        }

        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            SampleListingProvider.ListingId,
            SampleListingProvider.MultiplanPropertyKey,
        };
        var materialized = await db.Subjects.Select(s => s.PropertyKey).Distinct().ToListAsync(ct);
        foreach (var key in materialized)
        {
            known.Add(key);
        }
        return known.ToList();
    }
}
