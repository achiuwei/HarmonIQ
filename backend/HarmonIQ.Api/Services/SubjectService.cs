using System.Text.Json;
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Orientation = HarmonIQ.Api.Services.Orientation;

namespace HarmonIQ.Api.Services;

/// <summary>
/// Supplies the <see cref="ScrapedPlan"/>s for a property, or <c>null</c>/fewer than 2 to signal
/// the single-listing/property path (design §5's multi-plan discriminator is plan count, not
/// unit count). The demo path is <see cref="SampleListingProvider"/> reading the fixture
/// directly; a real backfill path would run <see cref="IPlanScraper"/> against fetched LDP HTML
/// (<c>LISTING_SOURCE=api</c> is the seam — out of local scope, design §6).
/// </summary>
public interface IPlanSource
{
    Task<IReadOnlyList<ScrapedPlan>?> GetPlansAsync(string propertyKey, CancellationToken ct);
}

/// <summary>Loads the raw bytes behind a plan image URL for perceptual hashing.</summary>
public interface IPlanImageLoader
{
    Task<byte[]?> LoadAsync(string? planImageUrl, CancellationToken ct);
}

/// <summary>
/// Local/offline implementation: resolves a relative plan-image path against the API's
/// <c>Data</c> directory (e.g. <c>sample-plans/plan-rk-101.png</c>). Absolute (remote) URLs
/// return <c>null</c> rather than making a network call — no network for fixtures locally.
/// </summary>
public class FilePlanImageLoader(IWebHostEnvironment env) : IPlanImageLoader
{
    public Task<byte[]?> LoadAsync(string? planImageUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(planImageUrl))
        {
            return Task.FromResult<byte[]?>(null);
        }
        if (Uri.TryCreate(planImageUrl, UriKind.Absolute, out _))
        {
            return Task.FromResult<byte[]?>(null);
        }

        var path = Path.Combine(env.ContentRootPath, "Data", planImageUrl.Replace('/', Path.DirectorySeparatorChar));
        return Task.FromResult<byte[]?>(File.Exists(path) ? File.ReadAllBytes(path) : null);
    }
}

/// <summary>
/// Materializes <see cref="Subject"/> rows from scraped listing/plan data and writes the
/// immutable <see cref="InputSet"/> snapshot each subject's scoring reads from (design §5/§6,
/// task 6). Plan identity keys on <c>data-rentalkey</c> (<see cref="ScrapedPlan.RentalKey"/>);
/// a card missing it falls back to a perceptual-image-hash + beds/baths content signature, and
/// an ambiguous fallback match writes no row — a wrong grade is worse than a null.
/// </summary>
public interface ISubjectService
{
    Task<IReadOnlyList<Subject>> MaterializeAsync(string propertyKey, CancellationToken ct);
    Task<InputSet> SnapshotAsync(Subject subject, CancellationToken ct);
}

public class SubjectService(
    HarmonIQDbContext db,
    IPlanSource planSource,
    IPlanImageLoader imageLoader,
    Orientation.IOrientationProvider orientationProvider,
    IListingService listingService,
    IConfiguration config,
    ILogger<SubjectService> log) : ISubjectService
{
    private const int DefaultGeoSnapshotTtlDays = 90;

    public async Task<IReadOnlyList<Subject>> MaterializeAsync(string propertyKey, CancellationToken ct)
    {
        var plans = await planSource.GetPlansAsync(propertyKey, ct);
        var now = DateTimeOffset.UtcNow;

        // 0 or 1 plan => the property itself is the subject (design §5's discriminator is the
        // property's *count of distinct floor plans*, not its unit count).
        if (plans is null || plans.Count < 2)
        {
            var subject = await db.Subjects.FirstOrDefaultAsync(s => s.Id == propertyKey, ct);
            if (subject is null)
            {
                subject = new Subject
                {
                    Id = propertyKey,
                    PropertyKey = propertyKey,
                    SubjectType = "property",
                    CreatedAt = now,
                    LastSeenAt = now,
                };
                db.Subjects.Add(subject);
            }
            else
            {
                subject.LastSeenAt = now;
            }

            if (plans is { Count: 1 } single)
            {
                ApplyPlanMetadata(subject, single[0]);
                subject.PlanImageHash = await TryHashImageAsync(single[0].PlanImageUrl, ct) ?? subject.PlanImageHash;
            }

            await db.SaveChangesAsync(ct);
            return [subject];
        }

        // >=2 plans => one floorplan subject per plan, no property subject. A plan with a single
        // available unit next to plans with many units still takes this path.
        var existing = await db.Subjects
            .Where(s => s.PropertyKey == propertyKey && s.SubjectType == "floorplan")
            .ToListAsync(ct);

        var byRentalKey = existing
            .Where(s => !string.IsNullOrEmpty(s.ExternalPlanKey))
            .ToDictionary(s => s.ExternalPlanKey!, StringComparer.Ordinal);

        var bySignature = existing
            .Where(s => string.IsNullOrEmpty(s.ExternalPlanKey) && s.ContentSignature is not null)
            .GroupBy(s => s.ContentSignature!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var result = new List<Subject>();

        foreach (var plan in plans)
        {
            Subject? subject;

            if (!string.IsNullOrEmpty(plan.RentalKey))
            {
                if (!byRentalKey.TryGetValue(plan.RentalKey, out subject))
                {
                    subject = new Subject
                    {
                        Id = $"{propertyKey}:{plan.RentalKey}",
                        PropertyKey = propertyKey,
                        SubjectType = "floorplan",
                        ExternalPlanKey = plan.RentalKey,
                        CreatedAt = now,
                        LastSeenAt = now,
                    };
                    db.Subjects.Add(subject);
                }
                else
                {
                    subject.LastSeenAt = now;
                }
            }
            else
            {
                // Content-signature fallback: no data-rentalkey, so identity comes from the
                // perceptual plan-image hash + beds/baths. No hashable image and no key means no
                // reliable identity at all — skip (write no row).
                var signature = await ComputeContentSignatureAsync(plan, ct);
                if (signature is null)
                {
                    log.LogInformation(
                        "Plan with no rental key and no hashable image skipped for {PropertyKey}", propertyKey);
                    continue;
                }

                if (bySignature.TryGetValue(signature, out var matches))
                {
                    if (matches.Count > 1)
                    {
                        log.LogWarning(
                            "Ambiguous content-signature match ({Count} candidates) for {PropertyKey}; writing no row",
                            matches.Count, propertyKey);
                        continue;
                    }
                    subject = matches[0];
                    subject.LastSeenAt = now;
                }
                else
                {
                    subject = new Subject
                    {
                        Id = $"{propertyKey}:cs:{signature}",
                        PropertyKey = propertyKey,
                        SubjectType = "floorplan",
                        ContentSignature = signature,
                        CreatedAt = now,
                        LastSeenAt = now,
                    };
                    db.Subjects.Add(subject);
                }
            }

            ApplyPlanMetadata(subject, plan);
            subject.PlanImageHash = await TryHashImageAsync(plan.PlanImageUrl, ct) ?? subject.PlanImageHash;
            result.Add(subject);
        }

        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<InputSet> SnapshotAsync(Subject subject, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var isFloorPlan = subject.SubjectType == "floorplan";

        string evidenceHashesJson;
        string? numbersJson;

        if (isFloorPlan)
        {
            // Listing photos are never attached to a plan subject's input set (design §5) — they
            // are property-level marketing shots, not this plan's interior.
            var hashes = subject.PlanImageHash is null ? Array.Empty<string>() : new[] { subject.PlanImageHash };
            evidenceHashesJson = JsonSerializer.Serialize(hashes, Json.Options);
            numbersJson = await ResolveFloorPlanUnitNumbersJsonAsync(subject, ct);
        }
        else
        {
            var listing = await SafeGetListingAsync(subject.PropertyKey, ct);
            var hashes = new List<string>();
            if (listing is not null)
            {
                foreach (var photo in listing.Photos.Where(p => p.Selected))
                {
                    var bytes = await listingService.GetPhotoAsync(subject.PropertyKey, photo.PhotoId, null, ct);
                    if (bytes is not null) hashes.Add(PerceptualHash.Compute(bytes.Data));
                }
            }
            evidenceHashesJson = JsonSerializer.Serialize(hashes, Json.Options);
            numbersJson = listing is null ? null : JsonSerializer.Serialize(listing.Numbers, Json.Options);
        }

        var environmentJson = await ResolveEnvironmentJsonAsync(subject, now, ct);
        var orientationJson = await ResolveOrientationJsonAsync(subject, ct);

        var inputSet = new InputSet
        {
            Id = Guid.NewGuid().ToString("n"),
            SubjectId = subject.Id,
            EvidencePath = isFloorPlan ? "floorplan" : "photos",
            EvidenceHashesJson = evidenceHashesJson,
            EnvironmentJson = environmentJson,
            OrientationJson = orientationJson,
            NumbersJson = numbersJson,
            CreatedAt = now,
        };
        // Neither principle set nor rules version is known at ingestion time; this fingerprint
        // only needs to detect evidence drift, so both are passed empty. Scoring calls
        // InputFingerprint.Compute again per (principle_set, rules_version) when deriving.
        inputSet.InputFingerprint = InputFingerprint.Compute(inputSet, string.Empty, string.Empty);

        db.InputSets.Add(inputSet);
        await db.SaveChangesAsync(ct);
        return inputSet;
    }

    private static void ApplyPlanMetadata(Subject subject, ScrapedPlan plan)
    {
        subject.PlanName = plan.ModelName;
        subject.Beds = plan.Beds;
        subject.Baths = plan.Baths;
        subject.SqftMin = plan.SqftMin;
        subject.SqftMax = plan.SqftMax;
        subject.PlanImageUrl = plan.PlanImageUrl;
    }

    private async Task<string?> TryHashImageAsync(string? planImageUrl, CancellationToken ct)
    {
        var bytes = await imageLoader.LoadAsync(planImageUrl, ct);
        return bytes is null ? null : PerceptualHash.Compute(bytes);
    }

    private async Task<string?> ComputeContentSignatureAsync(ScrapedPlan plan, CancellationToken ct)
    {
        var hash = await TryHashImageAsync(plan.PlanImageUrl, ct);
        if (hash is null) return null;
        return $"{hash}|{plan.Beds}|{plan.Baths}";
    }

    private async Task<string?> ResolveFloorPlanUnitNumbersJsonAsync(Subject subject, CancellationToken ct)
    {
        var plans = await planSource.GetPlansAsync(subject.PropertyKey, ct);
        if (plans is null) return null;

        ScrapedPlan? plan = null;
        foreach (var candidate in plans)
        {
            if (!string.IsNullOrEmpty(subject.ExternalPlanKey))
            {
                if (candidate.RentalKey == subject.ExternalPlanKey) { plan = candidate; break; }
                continue;
            }
            if (subject.ContentSignature is null) continue;
            var signature = await ComputeContentSignatureAsync(candidate, ct);
            if (string.Equals(signature, subject.ContentSignature, StringComparison.Ordinal)) { plan = candidate; break; }
        }

        if (plan is null) return null;
        return JsonSerializer.Serialize(plan.Units.Select(u => u.UnitNumber), Json.Options);
    }

    private async Task<string> ResolveEnvironmentJsonAsync(Subject subject, DateTimeOffset now, CancellationToken ct)
    {
        var ttlDays = int.TryParse(
            config["GEO_SNAPSHOT_TTL_DAYS"] ?? Environment.GetEnvironmentVariable("GEO_SNAPSHOT_TTL_DAYS"), out var parsed)
            ? parsed : DefaultGeoSnapshotTtlDays;

        // SQLite can't ORDER BY DateTimeOffset server-side; the per-subject input-set history is
        // small, so order client-side after materializing the (already-filtered) rows.
        var priorCandidates = await db.InputSets.Where(i => i.SubjectId == subject.Id).ToListAsync(ct);
        var prior = priorCandidates.OrderByDescending(i => i.CreatedAt).FirstOrDefault();

        // Environment snapshots are pinned with a re-resolve cadence: existing grades must not
        // drift under a frozen engine when OSM/geo data changes. Reuse the prior snapshot's
        // environment verbatim while it's within the TTL; only re-resolve once it's stale.
        if (prior is not null && (now - prior.CreatedAt).TotalDays < ttlDays)
        {
            return prior.EnvironmentJson;
        }

        var env = await listingService.GetPropertyEnvironmentAsync(subject.PropertyKey, ct) ?? ListingEnvironment.AllUnknown;
        return JsonSerializer.Serialize(env, Json.Options);
    }

    private async Task<string?> ResolveOrientationJsonAsync(Subject subject, CancellationToken ct)
    {
        var orientationSubjectId = subject.SubjectType == "floorplan"
            ? subject.ExternalPlanKey ?? subject.Id
            : subject.PropertyKey;

        var resolved = await orientationProvider.ResolveAsync(subject.PropertyKey, orientationSubjectId, ct);
        if (resolved is null)
        {
            return null;
        }

        // Reconcile the two SubjectOrientation shapes (design §4/§5): the provider seam
        // (Services.Orientation.SubjectOrientation, Task 4's contract) is mapped here — the
        // consumer — onto the persisted entity (Models.SubjectOrientation, Task 1's table),
        // which this task upserts. The frozen copy also goes into the immutable InputSet as
        // OrientationJson so scoring never re-resolves it.
        var entity = await db.SubjectOrientations.FindAsync([subject.Id], ct);
        if (entity is null)
        {
            db.SubjectOrientations.Add(new SubjectOrientation(
                subject.Id, resolved.FacingDegrees, resolved.Cardinal, resolved.Source, resolved.Confidence, resolved.ResolvedAt));
        }
        else
        {
            entity.FacingDegrees = resolved.FacingDegrees;
            entity.Cardinal = resolved.Cardinal;
            entity.Source = resolved.Source;
            entity.Confidence = resolved.Confidence;
            entity.ResolvedAt = resolved.ResolvedAt;
        }

        return JsonSerializer.Serialize(resolved, Json.Options);
    }

    private async Task<ListingResponse?> SafeGetListingAsync(string propertyKey, CancellationToken ct)
    {
        try
        {
            return await listingService.GetListingAsync(propertyKey, ct);
        }
        catch (ListingNotFoundException)
        {
            return null;
        }
        catch (ListingSourceException)
        {
            return null;
        }
    }
}
