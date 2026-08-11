using HarmonIQ.Api.Models;
using HarmonIQ.Api.Persistence;
using HarmonIQ.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Orientation = HarmonIQ.Api.Services.Orientation;

namespace HarmonIQ.Tests;

public class SubjectServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly HarmonIQDbContext _db;

    public SubjectServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"harmoniq-subj-test-{Guid.NewGuid():N}.db");
        _db = CreateContext(_dbPath);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private static HarmonIQDbContext CreateContext(string dbPath) => new(
        new DbContextOptionsBuilder<HarmonIQDbContext>().UseSqlite($"Data Source={dbPath}").Options);

    private SubjectService MakeService(
        FakePlanSource? planSource = null, FakePlanImageLoader? imageLoader = null,
        FakeOrientationProvider? orientation = null, FakeListingService? listing = null) =>
        new(_db,
            planSource ?? new FakePlanSource(),
            imageLoader ?? new FakePlanImageLoader(),
            orientation ?? new FakeOrientationProvider(),
            listing ?? new FakeListingService(),
            new ConfigurationBuilder().Build(),
            NullLogger<SubjectService>.Instance);

    private static byte[] MakePng(byte value)
    {
        using var img = new Image<L8>(8, 8);
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
                img[x, y] = new L8(value);
        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    private static ScrapedPlan Plan(string rentalKey, string model, int beds, double baths, int units = 1, string? imageUrl = "img.png") =>
        new(rentalKey, model, $"att-{rentalKey}", imageUrl, beds, baths, 500, 600,
            Enumerable.Range(1, units).Select(i => new ScrapedUnit($"{rentalKey}-{i}", i, 500, 1500m)).ToList());

    [Fact]
    public async Task FivePlans_ProduceFiveFloorplanSubjects_NoPropertySubject()
    {
        var plans = new[]
        {
            Plan("rk-1", "A", 1, 1), Plan("rk-2", "B", 1, 1), Plan("rk-3", "C", 2, 2),
            Plan("rk-4", "D", 0, 1), Plan("rk-5", "E", 2, 1),
        };
        var svc = MakeService(new FakePlanSource { ["prop"] = plans });

        var subjects = await svc.MaterializeAsync("prop", CancellationToken.None);

        Assert.Equal(5, subjects.Count);
        Assert.All(subjects, s => Assert.Equal("floorplan", s.SubjectType));
        Assert.DoesNotContain(await _db.Subjects.ToListAsync(), s => s.SubjectType == "property");
    }

    [Fact]
    public async Task OnePlan_ProducesOnePropertySubject()
    {
        var svc = MakeService(new FakePlanSource { ["prop"] = [Plan("rk-1", "A", 1, 1)] });

        var subjects = await svc.MaterializeAsync("prop", CancellationToken.None);

        Assert.Single(subjects);
        Assert.Equal("property", subjects[0].SubjectType);
        Assert.Equal("prop", subjects[0].Id);
    }

    [Fact]
    public async Task ZeroPlans_ProducesOnePropertySubject()
    {
        var svc = MakeService(new FakePlanSource { ["prop"] = null });

        var subjects = await svc.MaterializeAsync("prop", CancellationToken.None);

        Assert.Single(subjects);
        Assert.Equal("property", subjects[0].SubjectType);
    }

    [Fact]
    public async Task PlanWithOneUnitNextToPlanWithTen_StillTakesFloorPlanPath()
    {
        var plans = new[] { Plan("rk-1", "A", 1, 1, units: 1), Plan("rk-2", "B", 2, 2, units: 10) };
        var svc = MakeService(new FakePlanSource { ["prop"] = plans });

        var subjects = await svc.MaterializeAsync("prop", CancellationToken.None);

        Assert.Equal(2, subjects.Count);
        Assert.All(subjects, s => Assert.Equal("floorplan", s.SubjectType));
    }

    [Fact]
    public async Task Rematerializing_IsIdempotent_LastSeenAtMoves_IdStays()
    {
        var svc = MakeService(new FakePlanSource { ["prop"] = [Plan("rk-1", "A", 1, 1)] });

        var first = await svc.MaterializeAsync("prop", CancellationToken.None);
        var firstId = first[0].Id;
        var firstSeen = first[0].LastSeenAt;

        await Task.Delay(10);
        var second = await svc.MaterializeAsync("prop", CancellationToken.None);

        Assert.Equal(firstId, second[0].Id);
        Assert.True(second[0].LastSeenAt > firstSeen);

        var all = await _db.Subjects.Where(s => s.PropertyKey == "prop").ToListAsync();
        Assert.Single(all); // no duplicate row was created
    }

    [Fact]
    public async Task MultiPlan_Rematerializing_IsIdempotent_PerPlanIdStays()
    {
        var plans = new[] { Plan("rk-1", "A", 1, 1), Plan("rk-2", "B", 1, 1) };
        var svc = MakeService(new FakePlanSource { ["prop"] = plans });

        var first = await svc.MaterializeAsync("prop", CancellationToken.None);
        await Task.Delay(10);
        var second = await svc.MaterializeAsync("prop", CancellationToken.None);

        Assert.Equal(first.Select(s => s.Id).OrderBy(x => x), second.Select(s => s.Id).OrderBy(x => x));
        var all = await _db.Subjects.Where(s => s.PropertyKey == "prop").ToListAsync();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task MissingRentalKey_UnambiguousContentSignatureMatch_ReusesSubject()
    {
        var imageBytes = MakePng(120);
        var loader = new FakePlanImageLoader { ["shared.png"] = imageBytes };

        var keyedPlan = Plan("rk-1", "Keyed", 1, 1);
        var unkeyedPlan = new ScrapedPlan("", "NoKey", "att", "shared.png", 2, 1, 500, 600,
            [new ScrapedUnit("u1", 1, 500, 1500m)]);

        var svc = MakeService(new FakePlanSource { ["prop"] = [keyedPlan, unkeyedPlan] }, loader);

        var first = await svc.MaterializeAsync("prop", CancellationToken.None);
        Assert.Equal(2, first.Count);
        var unkeyedSubject = first.Single(s => s.ExternalPlanKey is null);
        var unkeyedId = unkeyedSubject.Id;
        var unkeyedFirstSeenAt = unkeyedSubject.LastSeenAt; // struct copy, taken before the second call

        await Task.Delay(10);
        var second = await svc.MaterializeAsync("prop", CancellationToken.None);
        var reused = second.Single(s => s.ExternalPlanKey is null);

        Assert.Equal(unkeyedId, reused.Id);
        Assert.True(reused.LastSeenAt > unkeyedFirstSeenAt);
        var allFloorplans = await _db.Subjects.Where(s => s.PropertyKey == "prop" && s.SubjectType == "floorplan").ToListAsync();
        Assert.Equal(2, allFloorplans.Count); // still no duplicate row
    }

    [Fact]
    public async Task MissingRentalKey_AmbiguousContentSignatureMatch_WritesNoRow()
    {
        var imageBytes = MakePng(90);
        var loader = new FakePlanImageLoader { ["shared.png"] = imageBytes };

        // Seed two existing unkeyed floorplan subjects that already share the same content
        // signature (as if a prior run created two independently, e.g. via distinct sources).
        var now = DateTimeOffset.UtcNow;
        var signature = $"{PerceptualHash.Compute(imageBytes)}|2|1";
        _db.Subjects.Add(new Subject
        {
            Id = "prop:cs:existing-1", PropertyKey = "prop", SubjectType = "floorplan",
            ContentSignature = signature, CreatedAt = now, LastSeenAt = now,
        });
        _db.Subjects.Add(new Subject
        {
            Id = "prop:cs:existing-2", PropertyKey = "prop", SubjectType = "floorplan",
            ContentSignature = signature, CreatedAt = now, LastSeenAt = now,
        });
        await _db.SaveChangesAsync();

        var keyedPlan = Plan("rk-1", "Keyed", 1, 1);
        var ambiguousPlan = new ScrapedPlan("", "NoKey", "att", "shared.png", 2, 1, 500, 600,
            [new ScrapedUnit("u1", 1, 500, 1500m)]);

        var svc = MakeService(new FakePlanSource { ["prop"] = [keyedPlan, ambiguousPlan] }, loader);
        var subjects = await svc.MaterializeAsync("prop", CancellationToken.None);

        // Only the keyed plan produced a subject; the ambiguous unkeyed plan wrote no row.
        Assert.Single(subjects);
        Assert.Equal("rk-1", subjects[0].ExternalPlanKey);
        var allFloorplans = await _db.Subjects.Where(s => s.PropertyKey == "prop" && s.SubjectType == "floorplan").ToListAsync();
        Assert.Equal(3, allFloorplans.Count); // the two pre-seeded rows + the keyed plan; nothing new for the ambiguous plan
    }

    [Fact]
    public async Task MissingRentalKey_NoImage_WritesNoRow()
    {
        var keyedPlan = Plan("rk-1", "Keyed", 1, 1);
        var noKeyNoImage = new ScrapedPlan("", "NoKey", "att", null, 2, 1, 500, 600,
            [new ScrapedUnit("u1", 1, 500, 1500m)]);

        var svc = MakeService(new FakePlanSource { ["prop"] = [keyedPlan, noKeyNoImage] });
        var subjects = await svc.MaterializeAsync("prop", CancellationToken.None);

        Assert.Single(subjects);
    }

    [Fact]
    public async Task SnapshotAsync_FloorPlan_NeverAttachesListingPhotos()
    {
        var svc = MakeService();
        var floorplanSubject = new Subject
        {
            Id = "prop:rk-1", PropertyKey = "prop", SubjectType = "floorplan", ExternalPlanKey = "rk-1",
            PlanImageHash = "abc123", CreatedAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow,
        };
        _db.Subjects.Add(floorplanSubject);
        await _db.SaveChangesAsync();

        var inputSet = await svc.SnapshotAsync(floorplanSubject, CancellationToken.None);

        Assert.Equal("floorplan", inputSet.EvidencePath);
        Assert.Contains("abc123", inputSet.EvidenceHashesJson);
    }

    [Fact]
    public async Task SnapshotAsync_WritesImmutableInputSet()
    {
        var svc = MakeService();
        var subject = new Subject
        {
            Id = "prop", PropertyKey = "prop", SubjectType = "property",
            CreatedAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow,
        };
        _db.Subjects.Add(subject);
        await _db.SaveChangesAsync();

        var inputSet = await svc.SnapshotAsync(subject, CancellationToken.None);

        var tracked = await _db.InputSets.FindAsync(inputSet.Id);
        tracked!.EvidenceHashesJson = "[\"tampered\"]";
        Assert.Throws<InvalidOperationException>(() => _db.SaveChanges());
    }

    [Fact]
    public void Fingerprints_AreStableAcrossIdenticalSnapshots_AndDifferWhenInputChanges()
    {
        var setA = new InputSet
        {
            Id = "1", SubjectId = "s1", EvidencePath = "floorplan",
            EvidenceHashesJson = "[\"h1\"]", EnvironmentJson = "{}", CreatedAt = DateTimeOffset.UtcNow,
        };
        var setB = new InputSet
        {
            Id = "2", SubjectId = "s1", EvidencePath = "floorplan",
            EvidenceHashesJson = "[\"h1\"]", EnvironmentJson = "{}", CreatedAt = DateTimeOffset.UtcNow,
        };
        var setC = new InputSet
        {
            Id = "3", SubjectId = "s1", EvidencePath = "floorplan",
            EvidenceHashesJson = "[\"h2\"]", EnvironmentJson = "{}", CreatedAt = DateTimeOffset.UtcNow,
        };

        var fpA = InputFingerprint.Compute(setA, "fengshui", "v1");
        var fpB = InputFingerprint.Compute(setB, "fengshui", "v1");
        var fpC = InputFingerprint.Compute(setC, "fengshui", "v1");

        Assert.Equal(fpA, fpB); // Id/CreatedAt don't participate; identical snapshot fields => identical fingerprint
        Assert.NotEqual(fpA, fpC);
    }

    private class FakePlanSource : Dictionary<string, IReadOnlyList<ScrapedPlan>?>, IPlanSource
    {
        public Task<IReadOnlyList<ScrapedPlan>?> GetPlansAsync(string propertyKey, CancellationToken ct) =>
            Task.FromResult(TryGetValue(propertyKey, out var plans) ? plans : null);
    }

    private class FakePlanImageLoader : Dictionary<string, byte[]?>, IPlanImageLoader
    {
        public Task<byte[]?> LoadAsync(string? planImageUrl, CancellationToken ct) =>
            Task.FromResult(planImageUrl is not null && TryGetValue(planImageUrl, out var bytes) ? bytes : null);
    }

    private class FakeOrientationProvider : Orientation.IOrientationProvider
    {
        public Task<Orientation.SubjectOrientation?> ResolveAsync(string propertyKey, string subjectId, CancellationToken ct) =>
            Task.FromResult<Orientation.SubjectOrientation?>(null);
    }

    private class FakeListingService : IListingService
    {
        public Task<ListingResponse> GetListingAsync(string listingId, CancellationToken ct) =>
            throw new ListingNotFoundException("not in fake");

        public Task<PhotoBytes?> GetPhotoAsync(string listingId, string photoId, int? width, CancellationToken ct) =>
            Task.FromResult<PhotoBytes?>(null);

        public Task<ListingEnvironment?> GetPropertyEnvironmentAsync(string propertyKey, CancellationToken ct) =>
            Task.FromResult<ListingEnvironment?>(null);
    }
}
