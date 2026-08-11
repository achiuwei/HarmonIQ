using HarmonIQ.Api.Controllers;
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HarmonIQ.Api.Services;

/// <summary>Thrown when a caller pins <c>engineVersion</c> to a version that does not exist at all.</summary>
public class SearchEngineVersionNotFoundException(string version) : Exception($"Unknown engine version '{version}'.")
{
    public string Version { get; } = version;
}

/// <summary>
/// Thrown when a caller pins <c>engineVersion</c> to a version that exists but has never been
/// published. Search reads only published, stored grades (design §8) — there is no
/// <c>includeUnpublished</c> escape hatch here the way the internal grades feed has one; a vision
/// call, and by extension an unpublished mid-rollout row, must never sit in a search request path.
/// </summary>
public class SearchEngineVersionNotPublishedException(string version)
    : Exception($"Engine version '{version}' is not published.")
{
    public string Version { get; } = version;
}

public interface ISearchService
{
    /// <summary>The typeahead suggestion chip for a synonym query, or <c>null</c> when the query
    /// isn't a recognized spelling of a principle set.</summary>
    Task<SuggestResponse?> SuggestAsync(string? query, CancellationToken ct);

    /// <summary>
    /// The HarmonIQ filter (design §8). <paramref name="sets"/> is the raw <c>?sets=</c> querystring
    /// value (comma-separated; empty/unknown means the "HarmonIQ" parent checkbox with no
    /// sub-selection — every set, unioned); <paramref name="minGrade"/> defaults to <c>"B-"</c>;
    /// <paramref name="engineVersion"/> pins to a specific published version and otherwise resolves
    /// to the currently published one.
    /// </summary>
    Task<SearchResponse> SearchAsync(
        string? sets, string? minGrade, string? engineVersion, int? limit, CancellationToken ct);
}

/// <summary>
/// Grade-letter ordering for the "B- or better" threshold test. Mirrors <see cref="ScoreMath.Grade"/>'s
/// band boundaries exactly — a search threshold and the badge grade it's compared against must use
/// the same scale.
/// </summary>
public static class GradeScale
{
    public const string DefaultMinGrade = "B-";

    private static readonly IReadOnlyDictionary<string, int> Rank = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["A+"] = 12, ["A"] = 11, ["A-"] = 10,
        ["B+"] = 9, ["B"] = 8, ["B-"] = 7,
        ["C+"] = 6, ["C"] = 5, ["C-"] = 4,
        ["D+"] = 3, ["D"] = 2, ["D-"] = 1,
        ["F"] = 0,
    };

    /// <summary>An unrecognized/absent requested threshold falls back to the design default, never to "no floor".</summary>
    public static string Normalize(string? requested) =>
        requested is { Length: > 0 } r && Rank.ContainsKey(r.Trim().ToUpperInvariant())
            ? r.Trim().ToUpperInvariant()
            : DefaultMinGrade;

    /// <summary>True when <paramref name="grade"/> is at least as good as <paramref name="minGrade"/>. A null/unranked grade never meets a floor.</summary>
    public static bool Meets(string? grade, string minGrade) =>
        grade is not null
        && Rank.TryGetValue(grade, out var g)
        && Rank.TryGetValue(minGrade, out var m)
        && g >= m;
}

/// <summary>
/// Backs <c>GET /api/search/suggest</c> and <c>GET /api/search</c> (design §8). Reads only stored,
/// published projection rows via <see cref="IPublicationService.GetFeedAsync"/> and
/// <see cref="IEngineVersionService.GetPublishedAsync"/> — the two seams Task 9 built precisely so
/// that no request path here can trigger a vision call. A reader who pins an engine version sees
/// only that version's rows, forever.
///
/// <b>Cohort-relative, not a global numeric sort:</b> by the time a score reaches a projection row,
/// <c>ScoreMath.Aggregate</c> has already applied that row's own cohort's calibration constants
/// (design §2) — the stored <c>Score</c>/<c>Grade</c> already sit on one comparable, calibrated
/// scale, which is what makes a flat "B- or better" threshold honest across cohorts in the first
/// place. This service does not re-derive or second-guess that calibration; it only compares the
/// stored grade to the threshold. It still refuses to let cohort be invisible in the result order:
/// hits are grouped by cohort (canonical <see cref="Cohort.All"/> order) before being ranked by
/// score within each group, rather than concatenated into one flat score-sorted list — so the
/// order itself never implies a photos/with-orientation B and a floorplan/without-orientation B+
/// were compared head-to-head.
/// </summary>
public class SearchService(
    HarmonIQDbContext db,
    IEngineVersionService engineVersions,
    IPublicationService publication,
    IListingService listings) : ISearchService
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    /// <summary>Page size used when draining <see cref="IPublicationService.GetFeedAsync"/> — demo/local scale (design §12); see that method's own comment for why materializing in memory is acceptable here.</summary>
    private const int FeedDrainPageSize = 500;

    private static readonly IReadOnlyDictionary<string, int> CohortOrder =
        Cohort.All.Select((c, i) => (c.ToString(), i)).ToDictionary(t => t.Item1, t => t.Item2, StringComparer.Ordinal);

    public Task<SuggestResponse?> SuggestAsync(string? query, CancellationToken ct)
    {
        var set = SynonymMap.Normalize(query);
        if (set is null)
        {
            return Task.FromResult<SuggestResponse?>(null);
        }

        var label = Traditions.TraditionRegistry.Find(set)?.DisplayName ?? set;
        var url = $"/mock-srp.html?harmoniqFilter=open&sets={set}";
        return Task.FromResult<SuggestResponse?>(new SuggestResponse(set, $"See {label} scores", url));
    }

    public async Task<SearchResponse> SearchAsync(
        string? sets, string? minGrade, string? engineVersion, int? limit, CancellationToken ct)
    {
        var chosenSets = SubjectsReadService.ParseSets(sets);
        var threshold = GradeScale.Normalize(minGrade);
        var cap = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

        var totalInArea = await TotalInAreaAsync(ct);
        var engine = await ResolveSearchableEngineAsync(engineVersion, ct);

        if (engine is null)
        {
            // Nothing has ever been published (the local-demo default state: publishing requires
            // mode='live' AND status='ok', and demo output is never persisted). A search with no
            // published engine behind it is not an error — it is zero scored properties, honestly
            // reported, exactly like any other "affirmative request for a signal we don't have yet".
            return new SearchResponse([], 0, totalInArea, BuildCaveat(0, totalInArea, chosenSets));
        }

        var rows = await AllPublishedRowsAsync(engine.Version, ct);
        var inScope = rows.Where(r => chosenSets.Contains(r.PrincipleSet)).ToList();
        var totalScoredInArea = inScope.Select(r => r.ListingId).Distinct().Count();

        // One group per subject (a floor plan, or the property itself): a property may contribute
        // more than one subject, and "any selected set at B- or better" is evaluated per subject,
        // never blended across a property's plans.
        var qualifyingSubjects = inScope
            .GroupBy(r => (r.ListingId, r.FloorPlanId))
            .Select(g => QualifySubject(g, threshold))
            .Where(t => t is not null)
            .Select(t => t!.Value)
            .ToList();

        var hitCandidates = new List<(SearchHit Hit, Cohort Cohort, int Score)>();
        foreach (var propertyGroup in qualifyingSubjects.GroupBy(t => t.Best.ListingId, StringComparer.Ordinal))
        {
            // Best-qualifying subject wins the property's one row in the result — never a blend of
            // that property's several floor plans.
            var winner = propertyGroup.OrderByDescending(t => t.Best.Score ?? 0).First();
            var title = await TitleForAsync(winner.Best.ListingId, ct);
            var subjectId = await ResolveSubjectIdAsync(winner.Best.ListingId, winner.Best.FloorPlanId, ct);
            var setGrades = winner.AllForSubject
                .OrderBy(r => r.PrincipleSet, StringComparer.Ordinal)
                .Select(ToSetGrade)
                .ToList();

            hitCandidates.Add((
                new SearchHit(winner.Best.ListingId, title, subjectId, setGrades),
                winner.Cohort,
                winner.Best.Score ?? 0));
        }

        var ordered = hitCandidates
            .OrderBy(c => CohortOrder.TryGetValue(c.Cohort.ToString(), out var r) ? r : int.MaxValue)
            .ThenByDescending(c => c.Score)
            .ThenBy(c => c.Hit.PropertyKey, StringComparer.Ordinal)
            .Select(c => c.Hit)
            .Take(cap)
            .ToList();

        return new SearchResponse(ordered, totalScoredInArea, totalInArea, BuildCaveat(totalScoredInArea, totalInArea, chosenSets));
    }

    private async Task<EngineVersion?> ResolveSearchableEngineAsync(string? requested, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return await engineVersions.GetPublishedAsync(ct);
        }

        var engine = await engineVersions.GetAsync(requested.Trim(), ct)
            ?? throw new SearchEngineVersionNotFoundException(requested.Trim());
        if (engine.PublishedAt is null)
        {
            throw new SearchEngineVersionNotPublishedException(requested.Trim());
        }
        return engine;
    }

    private async Task<List<ProjectionRow>> AllPublishedRowsAsync(string engineVersion, CancellationToken ct)
    {
        var all = new List<ProjectionRow>();
        string? cursor = null;
        do
        {
            var page = await publication.GetFeedAsync(engineVersion, cursor, FeedDrainPageSize, ct);
            all.AddRange(page.Rows);
            cursor = page.NextCursor;
        } while (cursor is not null);
        return all;
    }

    /// <summary>
    /// The local demo's known property corpus (design §12 scoping): the two fixtures
    /// <see cref="SampleListingProvider"/> always knows, plus anything already materialized into
    /// <c>subjects</c> by a prior ingestion/backfill/LDP-read. A production deployment would source
    /// "properties in this area" from apartments-web's own listing index (design §9's repo
    /// boundary) rather than from HarmonIQ's local subject table.
    /// </summary>
    private async Task<int> TotalInAreaAsync(CancellationToken ct)
    {
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
        return known.Count;
    }

    private async Task<string?> ResolveSubjectIdAsync(string listingId, string? floorPlanId, CancellationToken ct)
    {
        var subject = floorPlanId is null
            ? await db.Subjects.FirstOrDefaultAsync(s => s.PropertyKey == listingId && s.SubjectType == "property", ct)
            : await db.Subjects.FirstOrDefaultAsync(s => s.PropertyKey == listingId && s.ExternalPlanKey == floorPlanId, ct);
        return subject?.Id ?? (floorPlanId is null ? listingId : $"{listingId}:{floorPlanId}");
    }

    /// <summary>
    /// Search must never scrape a listing page at request time (the same "not in a request path"
    /// concern that keeps vision calls out of search) — titles come from the fast, local sample
    /// fixture for the one property key that offers one, and a humanized property key everywhere
    /// else. A production deployment sources titles from the consumer's own listing index, joined
    /// by property key (design §9) — HarmonIQ never owns listing metadata.
    /// </summary>
    private async Task<string> TitleForAsync(string propertyKey, CancellationToken ct)
    {
        if (propertyKey == SampleListingProvider.ListingId)
        {
            try
            {
                var listing = await listings.GetListingAsync(propertyKey, ct);
                if (!string.IsNullOrWhiteSpace(listing.Title))
                {
                    return listing.Title;
                }
            }
            catch (ListingNotFoundException)
            {
            }
            catch (ListingSourceException)
            {
            }
        }
        return Humanize(propertyKey);
    }

    private static string Humanize(string propertyKey)
    {
        var words = propertyKey.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0
            ? propertyKey
            : string.Join(' ', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    /// <summary>
    /// One subject qualifies when at least one of its in-scope rows meets the threshold. Returns
    /// null (never a default-valued tuple) when it doesn't, so the caller's filter is an ordinary
    /// null check rather than inspecting a sentinel field.
    /// </summary>
    private static (ProjectionRow Best, IReadOnlyList<ProjectionRow> AllForSubject, Cohort Cohort)? QualifySubject(
        IEnumerable<ProjectionRow> subjectRows, string threshold)
    {
        var all = subjectRows.ToList();
        var qualifying = all.Where(r => GradeScale.Meets(r.Grade, threshold)).ToList();
        if (qualifying.Count == 0)
        {
            return null;
        }
        var best = qualifying.OrderByDescending(r => r.Score ?? 0).First();
        return (best, all, Cohort.Parse(best.Cohort ?? ""));
    }

    private static SetGrade ToSetGrade(ProjectionRow row)
    {
        var cohort = Cohort.Parse(row.Cohort ?? "");
        return new SetGrade(
            row.PrincipleSet, AnalysisStatuses.Ok, row.Score, row.Grade,
            row.Confidence ?? 0.0, cohort.EvidencePath, cohort.OrientationPath);
    }

    /// <summary>
    /// Renter-facing wording for the omission (design §10): a filter is an affirmative request for
    /// a signal we have, so the exclusion of unscored inventory must be stated, never silent.
    /// </summary>
    private static string BuildCaveat(int scored, int total, IReadOnlyList<string> chosenSets)
    {
        var label = chosenSets.Count >= PrincipleSets.All.Count
            ? "HarmonIQ"
            : string.Join(" or ", chosenSets.Select(s => s == PrincipleSets.Vastu ? "Vastu" : "Feng Shui"));
        return $"{scored} of {total} properties have {label} scores in this area.";
    }
}
