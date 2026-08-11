using System.IO.Compression;
using System.Text;
using HarmonIQ.Api.Infrastructure;
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HarmonIQ.Tests;

public class PersistenceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteContextFactory _factory;

    public PersistenceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"harmoniq-test-{Guid.NewGuid():N}.db");
        _factory = new SqliteContextFactory(_dbPath);
        using var ctx = _factory.Create();
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnectionClearPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private static void SqliteConnectionClearPools() => Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

    private static Subject MakeSubject(string id = "prop1") => new()
    {
        Id = id,
        PropertyKey = "prop1",
        SubjectType = "property",
        CreatedAt = DateTimeOffset.UtcNow,
        LastSeenAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void Analysis_UniqueConstraint_RejectsDuplicateSubjectPrincipleSetRulesVersion()
    {
        using var ctx = _factory.Create();
        ctx.Subjects.Add(MakeSubject());
        ctx.SaveChanges();

        ctx.Analyses.Add(new Analysis
        {
            Id = "a1",
            SubjectId = "prop1",
            PrincipleSet = "fengshui",
            RulesVersion = "v1",
            EngineVersion = "e1",
            Status = "ok",
            Mode = "demo",
            ComputedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();

        ctx.Analyses.Add(new Analysis
        {
            Id = "a2",
            SubjectId = "prop1",
            PrincipleSet = "fengshui",
            RulesVersion = "v1",
            EngineVersion = "e1",
            Status = "ok",
            Mode = "demo",
            ComputedAt = DateTimeOffset.UtcNow,
        });

        Assert.Throws<DbUpdateException>(() => ctx.SaveChanges());
    }

    [Fact]
    public void Analysis_DifferentRulesVersion_IsAllowed()
    {
        using var ctx = _factory.Create();
        ctx.Subjects.Add(MakeSubject());
        ctx.SaveChanges();

        ctx.Analyses.Add(new Analysis
        {
            Id = "a1",
            SubjectId = "prop1",
            PrincipleSet = "fengshui",
            RulesVersion = "v1",
            EngineVersion = "e1",
            Status = "ok",
            Mode = "demo",
            ComputedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();

        ctx.Analyses.Add(new Analysis
        {
            Id = "a2",
            SubjectId = "prop1",
            PrincipleSet = "fengshui",
            RulesVersion = "v2",
            EngineVersion = "e1",
            Status = "ok",
            Mode = "demo",
            ComputedAt = DateTimeOffset.UtcNow,
        });

        ctx.SaveChanges(); // should not throw
    }

    [Fact]
    public void Observation_UniqueConstraint_RejectsDuplicateKey()
    {
        using var ctx = _factory.Create();
        ctx.Subjects.Add(MakeSubject());
        ctx.SaveChanges();

        ctx.Observations.Add(new Observation
        {
            Id = "o1",
            SubjectId = "prop1",
            InputSetId = "is1",
            EvidenceHash = "hash1",
            PromptVersion = "p1",
            ModelId = "m1",
            Mode = "demo",
            PayloadJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();

        ctx.Observations.Add(new Observation
        {
            Id = "o2",
            SubjectId = "prop1",
            InputSetId = "is1",
            EvidenceHash = "hash1",
            PromptVersion = "p1",
            ModelId = "m1",
            Mode = "demo",
            PayloadJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        Assert.Throws<DbUpdateException>(() => ctx.SaveChanges());
    }

    [Fact]
    public async Task FileSystemObjectStore_RoundTripsGzippedBody_AndReturnsStableUri()
    {
        var root = Path.Combine(Path.GetTempPath(), $"harmoniq-store-{Guid.NewGuid():N}");
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["HARMONIQ_OBJECT_STORE"] = root })
                .Build();
            var store = new FileSystemObjectStore(config);

            var key = "reports/e1/prop1/fengshui.json.gz";
            var payload = "{\"grade\":\"A-\"}"u8.ToArray();
            byte[] gzipped;
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                {
                    gz.Write(payload, 0, payload.Length);
                }
                gzipped = ms.ToArray();
            }

            var uri1 = await store.PutAsync(key, gzipped, CancellationToken.None);
            var uri2 = store.UriFor(key);
            Assert.Equal(uri1, uri2);

            var roundTripped = await store.GetAsync(key, CancellationToken.None);
            Assert.NotNull(roundTripped);

            using var readStream = new MemoryStream(roundTripped!);
            using var gzRead = new GZipStream(readStream, CompressionMode.Decompress);
            using var outStream = new MemoryStream();
            await gzRead.CopyToAsync(outStream);
            var roundTrippedPayload = Encoding.UTF8.GetString(outStream.ToArray());

            Assert.Equal("{\"grade\":\"A-\"}", roundTrippedPayload);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FileSystemObjectStore_GetAsync_ReturnsNull_WhenKeyMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"harmoniq-store-{Guid.NewGuid():N}");
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["HARMONIQ_OBJECT_STORE"] = root })
                .Build();
            var store = new FileSystemObjectStore(config);

            var result = await store.GetAsync("reports/nope/nope/fengshui.json.gz", CancellationToken.None);
            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void InputSet_Write_ThenMutation_IsRejected()
    {
        using var ctx = _factory.Create();
        ctx.Subjects.Add(MakeSubject());
        ctx.SaveChanges();

        var inputSet = new InputSet
        {
            Id = "is1",
            SubjectId = "prop1",
            EvidencePath = "photos",
            EvidenceHashesJson = "[]",
            EnvironmentJson = "{}",
            InputFingerprint = "fp1",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        ctx.InputSets.Add(inputSet);
        ctx.SaveChanges();

        inputSet.EvidenceHashesJson = "[\"changed\"]";

        Assert.Throws<InvalidOperationException>(() => ctx.SaveChanges());
    }

    [Fact]
    public void ServiceModuleRegistration_DiscoversPersistenceModule()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        services.AddHarmonIQModules(config);

        Assert.Contains(services, d => d.ServiceType == typeof(HarmonIQDbContext) || d.ServiceType == typeof(DbContextOptions<HarmonIQDbContext>));
        Assert.Contains(services, d => d.ServiceType == typeof(IObjectStore));
    }

    private sealed class SqliteContextFactory
    {
        private readonly string _connectionString;

        public SqliteContextFactory(string dbPath)
        {
            _connectionString = $"Data Source={dbPath}";
        }

        public HarmonIQDbContext Create()
        {
            var options = new DbContextOptionsBuilder<HarmonIQDbContext>()
                .UseSqlite(_connectionString)
                .Options;
            return new HarmonIQDbContext(options);
        }
    }
}
