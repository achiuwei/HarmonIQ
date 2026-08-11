using HarmonIQ.Api.Models;
using HarmonIQ.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HarmonIQ.Api.Services;

/// <summary>
/// Publishes an engine version's eligible analyses as a set of <see cref="ProjectionRow"/>s,
/// then flips <see cref="EngineVersion.PublishedAt"/> — atomically, in one transaction, so a
/// reader never observes a half-written version. Eligible = <c>Mode == "live" &amp;&amp;
/// Status == "ok"</c>; demo output and non-ok statuses (<c>failed</c>, <c>insufficient_evidence</c>)
/// never reach the projection (design §6 / global constraints: NULL means "not scored yet",
/// never "scored badly").
/// </summary>
public interface IPublicationService
{
    /// <summary>
    /// Writes projection rows for every eligible analysis of <paramref name="engineVersion"/>
    /// and flips the version's <c>PublishedAt</c> in one transaction. Idempotent: if the
    /// version is already published, this is a no-op that returns the existing result.
    /// </summary>
    Task<PublishResult> PublishVersionAsync(string engineVersion, CancellationToken ct);

    /// <summary>
    /// Reads a page of the published projection for exactly the requested
    /// <paramref name="engineVersion"/> — never the latest version, so a caller who pins a
    /// version keeps getting that version's rows even after a newer one publishes.
    /// </summary>
    Task<GradesFeedPage> GetFeedAsync(string engineVersion, string? cursor, int limit, CancellationToken ct);
}

public class PublicationService(HarmonIQDbContext db) : IPublicationService
{
    public async Task<PublishResult> PublishVersionAsync(string engineVersion, CancellationToken ct)
    {
        var version = await db.EngineVersions.FirstOrDefaultAsync(e => e.Version == engineVersion, ct)
            ?? throw new InvalidOperationException($"Unknown engine version '{engineVersion}'.");

        if (version.PublishedAt is { } alreadyPublishedAt)
        {
            var existingCount = await db.ProjectionRows.CountAsync(r => r.EngineVersion == engineVersion, ct);
            return new PublishResult(engineVersion, existingCount, alreadyPublishedAt);
        }

        var eligible = await db.Analyses
            .Where(a => a.EngineVersion == engineVersion && a.Mode == "live" && a.Status == "ok")
            .ToListAsync(ct);

        var subjectIds = eligible.Select(a => a.SubjectId).Distinct().ToList();
        var subjectsById = await db.Subjects
            .Where(s => subjectIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            foreach (var analysis in eligible)
            {
                if (!subjectsById.TryGetValue(analysis.SubjectId, out var subject))
                {
                    // No materialized subject for this analysis — never fabricate a listing/plan
                    // identity; skip rather than publish an orphaned row.
                    continue;
                }

                var row = new ProjectionRow
                {
                    Id = ProjectionRowId(engineVersion, analysis.SubjectId, analysis.PrincipleSet),
                    ListingId = subject.PropertyKey,
                    FloorPlanId = subject.SubjectType == "floorplan"
                        ? subject.ExternalPlanKey ?? subject.Id
                        : null,
                    SubjectId = analysis.SubjectId,
                    PrincipleSet = analysis.PrincipleSet,
                    Score = analysis.Score,
                    Grade = analysis.Grade,
                    Cohort = analysis.CohortEvidencePath is not null && analysis.CohortOrientationPath is not null
                        ? $"{analysis.CohortEvidencePath}/{analysis.CohortOrientationPath}"
                        : null,
                    Confidence = analysis.Confidence,
                    EngineVersion = engineVersion,
                    ComputedAt = analysis.ComputedAt,
                };
                db.ProjectionRows.Add(row);
            }

            await db.SaveChangesAsync(ct);

            version.PublishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }

        var rowsWritten = await db.ProjectionRows.CountAsync(r => r.EngineVersion == engineVersion, ct);
        return new PublishResult(engineVersion, rowsWritten, version.PublishedAt!.Value);
    }

    public async Task<GradesFeedPage> GetFeedAsync(string engineVersion, string? cursor, int limit, CancellationToken ct)
    {
        // Local/demo scale (design §12): materialize the version's rows and paginate in
        // memory with an ordinal string comparison, rather than relying on provider-specific
        // translation of string comparisons for cursoring.
        var rows = await db.ProjectionRows
            .Where(r => r.EngineVersion == engineVersion)
            .ToListAsync(ct);
        var ordered = rows.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();

        var start = 0;
        if (!string.IsNullOrEmpty(cursor))
        {
            start = ordered.FindIndex(r => string.CompareOrdinal(r.Id, cursor) > 0);
            if (start < 0)
            {
                start = ordered.Count;
            }
        }

        var page = ordered.Skip(start).Take(limit).ToList();
        var nextCursor = start + page.Count < ordered.Count ? page[^1].Id : null;

        return new GradesFeedPage(engineVersion, page, nextCursor);
    }

    private static string ProjectionRowId(string engineVersion, string subjectId, string principleSet) =>
        $"{engineVersion}:{subjectId}:{principleSet}";
}
