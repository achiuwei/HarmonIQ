using HarmonIQ.Api.Commands;
using HarmonIQ.Api.Infrastructure;
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Persistence;
using HarmonIQ.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5080");

LoadDotEnv();
// Map flat env vars (from real environment or .env) onto config keys.
builder.Configuration.AddInMemoryCollection(EnvOverrides());

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.JsonSerializerOptions.DefaultIgnoreCondition =
        System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddMemoryCache();
builder.Services.AddCors();

// v1-legacy registrations with no v2 module owner yet. Every other hand-listed service
// (NumerologyService, SiteAnalysisService, the Claude client, MockAnalysisService,
// ClaudeAnalysisService, ...) moved into Infrastructure/AnalysisModule.cs /
// NumerologyModule.cs below; these three remain here only because IngestionModule.cs
// (Task 6) still depends on SampleListingProvider/IListingService/IGeoContextService
// being registered by someone, and no v2 module claims that v1 surface. Remove once the
// v1 AnalysisController is retired (Task 11) and nothing needs them.
builder.Services.AddHttpClient();
// The listing scraper's own client: identical to the default except that it accepts a
// self-signed certificate on loopback, so `Listing:BaseUrl` can point at a locally-served LDP.
// Public hosts still get full validation — see ListingSource.AllowsDevCertificate.
builder.Services.AddHttpClient(ListingService.HttpClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
            errors == System.Net.Security.SslPolicyErrors.None
            || (request.RequestUri is { } uri && ListingSource.AllowsDevCertificate(uri)),
    });
builder.Services.AddSingleton<SampleListingProvider>();
builder.Services.AddSingleton<IListingService, ListingService>();
builder.Services.AddSingleton<IGeoContextService, GeoContextService>();

// Every other service registration (persistence, ingestion, orientation, analysis,
// numerology, publishing, the API surface, commands, ...) lives in an
// Infrastructure/<Area>Module.cs implementing IServiceModule, discovered here by assembly
// scan. Program.cs never names a sibling task's module directly.
builder.Services.AddHarmonIQModules(builder.Configuration);

var app = builder.Build();

// Migrate-on-start: bring the SQLite DB up to the latest EF migration before anything
// (web host or a CLI command) touches it.
using (var migrationScope = app.Services.CreateScope())
{
    migrationScope.ServiceProvider.GetRequiredService<HarmonIQDbContext>().Database.Migrate();
}

// CLI seam: if args[0] names a registered IHarmonIQCommand, run it and exit instead of
// starting the web host. Later tasks add commands via their own module — never here.
var commandExitCode = await CommandRunner.TryRunAsync(args, app.Services, default);
if (commandExitCode is not null)
{
    return commandExitCode.Value;
}

// The real-LDP demo host (FR-6b) embeds the module from another local origin.
app.UseCors(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());

app.MapGet("/api/health", async (IConfiguration cfg, IEngineVersionService engineVersions, CancellationToken ct) =>
{
    var current = await engineVersions.GetOrCreateCurrentAsync(ct);
    // GetPublishedAsync orders by DateTimeOffset, which the Sqlite EF provider cannot
    // translate server-side (known EngineVersionService bug, not this task's file to fix —
    // reported separately). Demo mode never publishes anyway (Global Constraints: publish
    // writes require mode=live AND status=ok), so a null published version here is both the
    // correct fallback and the expected demo-mode value.
    EngineVersion? published = null;
    try
    {
        published = await engineVersions.GetPublishedAsync(ct);
    }
    catch (NotSupportedException)
    {
    }
    return Results.Ok(new
    {
        ok = true,
        live = !string.IsNullOrEmpty(cfg["Claude:ApiKey"]) && !string.IsNullOrEmpty(cfg["Claude:BaseUrl"]),
        engineVersion = current.Version,
        publishedVersion = published?.Version,
    });
});

// [FromServices] is explicit here (rather than relying on inference) because this
// v1 debug endpoint's service may not be registered yet while sibling Tier-2 tasks are
// still landing their Infrastructure/*Module.cs files; inference would otherwise crash
// endpoint construction at host startup instead of failing this one request at 500.
app.MapGet("/api/debug/geo", (
        string address,
        [Microsoft.AspNetCore.Mvc.FromServices] HarmonIQ.Api.Services.IGeoContextService geo,
        CancellationToken ct) =>
    geo.GetEnvironmentAsync($"debug:{address}", address, point: null, ct));

app.UseDefaultFiles(new DefaultFilesOptions { DefaultFileNames = { "mock-ldp.html" } });
app.UseStaticFiles();
app.MapControllers();

app.MapGet("/harmoniq", (IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.WebRootPath, "harmoniq.html"), "text/html"));
app.Run();
return 0;

static void LoadDotEnv()
{
    // Walk up from the binary until a .env is found (repo root when run via --project).
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
    {
        var f = Path.Combine(d.FullName, ".env");
        if (!File.Exists(f)) continue;
        foreach (var line in File.ReadAllLines(f))
        {
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith('#') || !t.Contains('=')) continue;
            var (k, v) = (t[..t.IndexOf('=')].Trim(), t[(t.IndexOf('=') + 1)..].Trim());
            var hash = v.IndexOf(" #", StringComparison.Ordinal);
            if (hash >= 0) v = v[..hash].Trim();
            if (Environment.GetEnvironmentVariable(k) is null)
                Environment.SetEnvironmentVariable(k, v);
        }
        return;
    }
}

static Dictionary<string, string?> EnvOverrides()
{
    var map = new Dictionary<string, string> {
        ["CLAUDE_API_KEY"] = "Claude:ApiKey", ["CLAUDE_BASE_URL"] = "Claude:BaseUrl",
        ["CLAUDE_MODEL"] = "Claude:Model", ["LISTING_SOURCE"] = "Listing:Source",
        // Repoints the scraper at a locally-served LDP; the production site 403s an automated client.
        ["LISTING_BASE_URL"] = "Listing:BaseUrl", ["LISTING_PATH_TEMPLATE"] = "Listing:PathTemplate",
        ["LISTING_TIMEOUT_SECONDS"] = "Listing:TimeoutSeconds",
        ["GEO_GEOCODER_URL"] = "Geo:GeocoderUrl", ["GEO_OVERPASS_URL"] = "Geo:OverpassUrl",
        ["GEO_ELEVATION_URL"] = "Geo:ElevationUrl", ["GEO_SNAPSHOT_TTL_DAYS"] = "Geo:SnapshotTtlDays",
        // v2 (Task 10) — persistence paths read as flat keys by PersistenceModule /
        // FileSystemObjectStore directly; mapped here too so a value from .env always wins
        // over an OS-level env var of the same name, consistent with every other entry.
        ["HARMONIQ_DB"] = "HARMONIQ_DB", ["HARMONIQ_OBJECT_STORE"] = "HARMONIQ_OBJECT_STORE",
        // v2 — orientation seam (fixture locally; SightMap client is stubbed, no partner key yet)
        ["ORIENTATION_PROVIDER"] = "Orientation:Provider",
        ["SIGHTMAP_API_KEY"] = "SightMap:ApiKey", ["SIGHTMAP_BASE_URL"] = "SightMap:BaseUrl",
        // v2 — scoring mode / batch API gate (Tier 3)
        ["SCORING_MODE"] = "Scoring:Mode", ["BATCH_API_ENABLED"] = "Scoring:BatchApiEnabled",
        // v2 — task-zero sampling (Tier 3)
        ["TASKZERO_SAMPLE_N"] = "TaskZero:SampleN",
    };
    var result = new Dictionary<string, string?>();
    foreach (var (env, key) in map)
        if (Environment.GetEnvironmentVariable(env) is { Length: > 0 } v) result[key] = v;
    return result;
}
