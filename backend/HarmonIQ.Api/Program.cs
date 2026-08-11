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
// Later tasks register services here (marker comment — keep):
// DI-REGISTRATIONS
builder.Services.AddSingleton<HarmonIQ.Api.Services.NumerologyService>();

var app = builder.Build();

// The real-LDP demo host (FR-6b) embeds the module from another local origin.
app.UseCors(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());

app.MapGet("/api/health", (IConfiguration cfg) => Results.Ok(new
{
    ok = true,
    live = !string.IsNullOrEmpty(cfg["Claude:ApiKey"]) && !string.IsNullOrEmpty(cfg["Claude:BaseUrl"])
}));

app.UseDefaultFiles(new DefaultFilesOptions { DefaultFileNames = { "mock-ldp.html" } });
app.UseStaticFiles();
app.MapControllers();
app.Run();

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
        ["GEO_GEOCODER_URL"] = "Geo:GeocoderUrl", ["GEO_OVERPASS_URL"] = "Geo:OverpassUrl",
        ["GEO_ELEVATION_URL"] = "Geo:ElevationUrl",
    };
    var result = new Dictionary<string, string?>();
    foreach (var (env, key) in map)
        if (Environment.GetEnvironmentVariable(env) is { Length: > 0 } v) result[key] = v;
    return result;
}
