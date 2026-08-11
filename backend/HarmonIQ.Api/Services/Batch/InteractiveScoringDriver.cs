using HarmonIQ.Api.Commands;
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HarmonIQ.Api.Services.Batch;

/// <summary>
/// Drives one <see cref="ScoringJob"/> per subject through <see cref="IAnalysisPipeline"/>,
/// selected by <see cref="BackfillCommand"/> whenever the batch path is not config-gated on
/// (which is always, on this machine — <c>Scoring:BatchApiEnabled</c> defaults false).
///
/// Two branches, matching the two kinds of "re-score" the design distinguishes (§6):
/// <list type="bullet">
/// <item><b><see cref="BackfillReasons.EngineUpgrade"/></b> — an engine/rules bump. Re-derives
/// from whatever observations are already on disk via <see cref="IAnalysisPipeline.RederiveAsync"/>,
/// which makes <b>zero</b> model calls. Getting this branch wrong turns a SQL-cheap rules change
/// into a full re-perception bill.</item>
/// <item><b>Everything else</b> (<c>backfill</c>, <c>new_listing</c>, <c>evidence_changed</c>) —
/// genuinely new or possibly-changed evidence. The cheap idempotency win happens here: a fresh
/// immutable snapshot is taken and its evidence-level <see cref="InputFingerprint"/> is compared
/// against the subject's most recent prior snapshot. An unchanged fingerprint short-circuits
/// before <see cref="IAnalysisPipeline.RunAsync"/> is ever called — no perception, no
/// observations written, just a <c>"skipped"</c> job row.</item>
/// </list>
/// </summary>
public interface IScoringDriver
{
    Task<ScoringJob> DriveAsync(
        Subject subject, EngineVersion engine, string reason, bool live, CancellationToken ct);
}

public class InteractiveScoringDriver(
    HarmonIQDbContext db,
    ISubjectService subjects,
    IAnalysisPipeline pipeline) : IScoringDriver
{
    public async Task<ScoringJob> DriveAsync(
        Subject subject, EngineVersion engine, string reason, bool live, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(engine);

        if (reason == BackfillReasons.EngineUpgrade)
        {
            return await DriveEngineUpgradeAsync(subject, engine, ct);
        }

        return await DriveNewPerceptionAsync(subject, engine, reason, live, ct);
    }

    /// <summary>
    /// The engine-bump path. Re-derives from the latest stored <see cref="InputSet"/> without
    /// touching a lens. A subject with no snapshot at all (never ingested) has nothing to
    /// re-derive from and is recorded as skipped, not failed — there is no evidence to blame.
    /// </summary>
    private async Task<ScoringJob> DriveEngineUpgradeAsync(Subject subject, EngineVersion engine, CancellationToken ct)
    {
        var latest = await LatestInputSetAsync(subject.Id, ct);
        if (latest is null)
        {
            return await RecordSyntheticJobAsync(subject, engine, BackfillReasons.EngineUpgrade, "skipped", ct);
        }

        var rederived = await pipeline.RederiveAsync(subject, latest, engine, ct);
        var status = rederived.Count > 0 ? "ok" : "skipped";
        return await RecordSyntheticJobAsync(subject, engine, BackfillReasons.EngineUpgrade, status, ct);
    }

    /// <summary>
    /// The genuinely-new-perception path: fingerprint check first, then
    /// <see cref="IAnalysisPipeline.RunAsync(Subject, InputSet, EngineVersion, bool, string, CancellationToken)"/>
    /// (which writes its own <c>scoring_jobs</c> row, including the ok/failed/skipped-for-no-evidence
    /// outcome and the retry bookkeeping) only when the evidence actually changed.
    /// </summary>
    private async Task<ScoringJob> DriveNewPerceptionAsync(
        Subject subject, EngineVersion engine, string reason, bool live, CancellationToken ct)
    {
        var prior = await LatestInputSetAsync(subject.Id, ct);
        var hasPriorAnalysis = await db.Analyses.AnyAsync(a => a.SubjectId == subject.Id, ct);

        // The default "backfill" sweep carries no signal that evidence moved: a subject already
        // run through the pipeline at least once is exactly the cheap-idempotency case the
        // design calls out, and it is decided WITHOUT taking a new snapshot at all.
        //
        // That matters beyond skipping a few hash/DB calls: SubjectService.SnapshotAsync
        // re-resolves orientation on every call, and the orientation provider (the local fixture
        // stand-in here; a live SightMap client would do the same) stamps a fresh `resolvedAt`
        // each time it resolves one. Comparing two *freshly taken* snapshots would then look
        // "changed" on every single run for any subject whose orientation actually resolves —
        // permanently defeating the skip for exactly the subjects most worth skipping. Reusing
        // the already-stored snapshot's fingerprint sidesteps that: "backfill" only re-derives
        // when there is a concrete reason to (see "new_listing" / "evidence_changed" below, or
        // the engine_upgrade branch in <see cref="DriveEngineUpgradeAsync"/>).
        if (reason == BackfillReasons.Backfill && prior is not null && hasPriorAnalysis)
        {
            return await RecordSyntheticJobAsync(subject, engine, reason, "skipped", ct);
        }

        // "new_listing" (nothing stored yet) and "evidence_changed" (an explicit signal that
        // evidence may have moved) are the two reasons that warrant taking a fresh, immutable
        // snapshot and comparing its fingerprint against whatever is already on file — the cheap
        // idempotency win for those reasons specifically.
        var freshInputSet = await subjects.SnapshotAsync(subject, ct);

        var unchanged = prior is not null && hasPriorAnalysis
            && string.Equals(prior.InputFingerprint, freshInputSet.InputFingerprint, StringComparison.Ordinal);

        if (unchanged)
        {
            return await RecordSyntheticJobAsync(subject, engine, reason, "skipped", ct);
        }

        var beforeIds = await db.ScoringJobs.Select(j => j.Id).ToListAsync(ct);
        await pipeline.RunAsync(subject, freshInputSet, engine, live, reason, ct);

        var after = await db.ScoringJobs.Where(j => j.SubjectId == subject.Id).ToListAsync(ct);
        var created = after.FirstOrDefault(j => !beforeIds.Contains(j.Id)) ?? after[^1];

        // Demo mode makes no real model call, so its token/cost fields are honestly zero rather
        // than left null (which would read as "unknown" instead of "there was no bill"). A live
        // run's real usage is Task 7's pipeline to report; this driver never fabricates it.
        if (!live)
        {
            created.InputTokens ??= 0;
            created.OutputTokens ??= 0;
            created.CostUsd ??= 0.0;
            await db.SaveChangesAsync(ct);
        }

        return created;
    }

    private async Task<InputSet?> LatestInputSetAsync(string subjectId, CancellationToken ct)
    {
        // SQLite cannot translate ORDER BY DateTimeOffset server-side (same constraint noted in
        // EngineVersionService/PublicationService); the per-subject snapshot history is small, so
        // order client-side after materializing.
        var snapshots = await db.InputSets.Where(i => i.SubjectId == subjectId).ToListAsync(ct);
        return snapshots.OrderByDescending(i => i.CreatedAt).FirstOrDefault();
    }

    private async Task<ScoringJob> RecordSyntheticJobAsync(
        Subject subject, EngineVersion engine, string reason, string status, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var job = new ScoringJob
        {
            Id = Guid.NewGuid().ToString("N"),
            SubjectId = subject.Id,
            EngineVersion = engine.Version,
            Reason = reason,
            Status = status,
            Attempts = 0,
            QueuedAt = now,
            StartedAt = now,
            CompletedAt = now,
            InputTokens = 0,
            OutputTokens = 0,
            CostUsd = 0.0,
        };
        db.ScoringJobs.Add(job);
        await db.SaveChangesAsync(ct);
        return job;
    }
}
