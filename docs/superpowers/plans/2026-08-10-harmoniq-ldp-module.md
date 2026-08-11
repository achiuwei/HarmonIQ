# HarmonIQ LDP Module Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the HarmonIQ embeddable LDP module (badge + expandable Feng Shui / Vastu report) backed by an ASP.NET Core API that ingests listing photos/address/numbers, runs Claude vision + deterministic site/numerology analysis, and demos on a mock listing detail page with brand theming plus a **local-only embed in the real apartments-web LDP** — per `SPEC.md` v1.6.

**Architecture:** A single ASP.NET Core (.NET 10) API on :5080 serves three things: `GET /api/listing/{id}` (listing context: photos, numbers, environment prefill), `POST /api/analyze` (rooms via Claude vision fan-out or mock fallback + deterministic site & numerology engines, merged into a weighted score), and static files (the mock LDP host page + the built embed bundle). The frontend is one Vite build target: a `<harmoniq-module>` web component (React 18 inside shadow DOM) that auto-fetches, auto-analyzes, renders badge/report, and re-grades via a Refine drawer. Brand theming flows through CSS custom-property tokens with three presets.

**Tech Stack:** .NET 10 / ASP.NET Core MVC controllers, SixLabors.ImageSharp (photo downscale), xUnit (deterministic engines only), React 18 + TypeScript strict + Vite (iife lib build), Claude Messages API via hackathon proxy (`claude-sonnet-5`, forced tool calls), Nominatim + Overpass + Open-Meteo elevation (keyless geo), sharp (one-time fixture-photo rasterization).

## Global Constraints

Copied from SPEC.md — every task implicitly includes these:

- Model: `claude-sonnet-5` (override via `CLAUDE_MODEL`); **no `stream: true`, no `temperature`** (proxy rejects both); `max_tokens` ≤ 16,384.
- ≤ 9 concurrent Claude requests per analysis (6 rooms + classification + site phrasing + summary); retry 429/5xx with linear backoff up to 3 retries.
- Photo cap: exactly 1–6 photos per analysis; interiors auto-selected up to 6 by listing photo order.
- Photos downscaled server-side to ≤ 1568 px long edge; cached **in memory only**, TTL ~30 min; never written to disk.
- Secrets only in root `.env` (gitignored); real env vars take precedence; `CLAUDE_API_KEY`, `CLAUDE_BASE_URL`, `CLAUDE_MODEL`, `LISTING_SOURCE`, geo endpoints configurable.
- JSON: camelCase; the tradition field serializes as `"system"` with values `fengshui|vastu|both`; severity `minor|moderate|major`; effort/impact `low|medium|high`.
- Scoring: overall = rooms 70% + site 30%, then numerology adjustment clamped to ±3; grades A+ ≥95 … F <40 (full table in Task 5).
- Backend port **5080**; frontend dev server 5173 proxies `/api` → 5080.
- Module never breaks/leaks into the host page: shadow DOM, fails to unobtrusive error state.
- Badge = a host-style **score card** placed directly beneath the LDP's existing scores (the Transportation section's "Getting Around" grid, which follows the Schools section — matching apartments-web `Modules/BuildingProfile`), showing title "HarmonIQ Score", grade, and score `/ 100`, with a **"Data provided by HarmonIQ"** attribution link to the HarmonIQ page at `/harmoniq` (FR-3, mirroring the LDP's Local Logic / GreatSchools attribution convention).
- The module must work **cross-origin** (FR-1): all API calls, thumbnail URLs, and the attribution link resolve against an API base derived from the embed script's origin (override: `api-base` attribute; same-origin default `''`); the API serves permissive CORS. This is what lets the real apartments-web LDP embed the bundle with one script tag.
- The apartments-web integration (Task 18) is **local only**: a demo branch in that repo, never merged, never pushed, never deployed. Nothing from apartments-web is copied into this repo.
- Cultural framing: verdicts phrased as tradition ("in Chinese numerology…"), never objective fact.
- SPEC §8 puts automated tests out of scope for the demo. This plan adds xUnit tests **only** for the three deterministic engines (numerology, site rules, score math) because they're cheap and the Refine flow's "deterministic re-grade" acceptance criterion depends on them. No UI/e2e/LLM tests. Everything else is verified with `curl`/browser steps included in each task.
- Demo must work fully offline via listing id `sample` (fixture + demo mode).

## File Structure

```
.env.example                     # documented env vars (no secrets)
.gitignore
HarmonIQ.sln
backend/HarmonIQ.Api/
  HarmonIQ.Api.csproj
  Program.cs                     # DI, .env loader, port 5080, static files
  appsettings.json               # non-secret defaults (geo endpoints, model)
  Controllers/
    ListingController.cs         # GET /api/listing/{id}, photo passthrough
    AnalysisController.cs        # POST /api/analyze: validate, route, merge
  Models/
    ListingModels.cs             # ListingResponse, ListingPhoto, ListingNumbers, SideEnvironment, ListingEnvironment
    AnalysisModels.cs            # AnalyzeRequest/Response, RoomAnalysis, Finding, ViolationFinding, Suggestion, ElementBalance, SiteAnalysis, NumerologyResult/Check
    Json.cs                      # shared JsonSerializerOptions
  Services/
    SampleListingProvider.cs     # bundled fixture for id "sample"
    ListingService.cs            # IListingService: fetch/scrape, classify, select, cache, downscale
    GeoContextService.cs         # IGeoContextService: geocode + Overpass + elevation → environment prefill
    NumerologyService.cs         # deterministic rules engine
    SiteAnalysisService.cs       # deterministic form-school rules
    ScoreMath.cs                 # grade table, weighted overall, element averaging, LocalSummary
    ClaudeClient.cs              # IClaudeClient + ClaudeUnavailableException
    ClaudeAnalysisService.cs     # per-photo fan-out, forced tool call, summary, site rephrase
    MockAnalysisService.cs       # demo fallback from templates
    Prompts.cs                   # system prompts + tool JSON schemas
  Data/
    sample-listing.json          # fixture metadata
    mock-analysis.json           # demo-mode room templates
    sample-photos/*.jpg          # rendered fixture photos (committed)
  wwwroot/
    mock-ldp.html                # demo host page (brand switcher)
    harmoniq.html                # the HarmonIQ page — attribution link target (/harmoniq)
    embed/harmoniq-module.js     # built by frontend (gitignored)
backend/HarmonIQ.Tests/
  HarmonIQ.Tests.csproj
  NumerologyServiceTests.cs
  SiteAnalysisServiceTests.cs
  ScoreMathTests.cs
frontend/
  package.json  tsconfig.json  vite.config.ts
  index.html                    # dev-only harness page
  src/
    main.ts                     # registers <harmoniq-module> custom element
    base.ts                     # API base resolution (script origin / api-base attr)
    Module.tsx                  # root React component (badge ⇄ expanded)
    api.ts                      # TS mirrors of DTOs + fetch helpers
    useHarmonIQ.ts              # state machine hook
    components/
      HarmonIQBadge.tsx  ReportPanel.tsx  ScoreGauge.tsx  ElementBars.tsx
      RoomCard.tsx  SiteCard.tsx  NumbersCard.tsx  RefineDrawer.tsx  ModePill.tsx
    styles/
      tokens.css                # default design tokens on :host
      base.css                  # component styles (token-driven)
      themes.ts                 # per-brand :host token override strings
tools/
  package.json                  # sharp
  make-fixture-photos.mjs       # SVG scenes → Data/sample-photos/*.jpg (run once, commit output)
docs/superpowers/plans/2026-08-10-harmoniq-ldp-module.md   # this plan
```

**Shared value vocabularies** (used verbatim across backend, fixture, and frontend):
- `road`: `none | quiet | busy | t-junction | highway | unknown`
- `water`: `none | pond | lake | river | pool | unknown`
- `structures`: `open | similar | taller-building | unknown`
- `slope`: `level | rises | falls | unknown`
- `orientation`: `unknown | north | northeast | east | southeast | south | southwest | west | northwest`
- `systems`: `both | fengshui | vastu`
- Real listing ids may not contain `/` (breaks routing); hosts encode slug paths with `~` (e.g. `the-elm-arlington-va~xyz123`), which `ListingService` converts back to `/` when building the source URL. Id `sample` is reserved for the fixture.

---

### Task 1: Repo scaffold + API skeleton with health endpoint

**Files:**
- Create: `.gitignore`, `.env.example`, `HarmonIQ.sln`
- Create: `backend/HarmonIQ.Api/HarmonIQ.Api.csproj`, `backend/HarmonIQ.Api/Program.cs`, `backend/HarmonIQ.Api/appsettings.json`
- Create: `backend/HarmonIQ.Api/wwwroot/.gitkeep`

**Interfaces:**
- Consumes: nothing (first task).
- Produces: running API on `http://localhost:5080` with `GET /api/health` → `{ "ok": true, "live": bool }`; `LoadDotEnv()` + config keys `Claude:ApiKey`, `Claude:BaseUrl`, `Claude:Model`, `Listing:Source`, `Geo:GeocoderUrl`, `Geo:OverpassUrl`, `Geo:ElevationUrl` that all later tasks read via `IConfiguration`.

- [ ] **Step 1: Create solution + project**

```bash
cd /Users/achiuwei/Documents/HarmonIQ
dotnet new sln -n HarmonIQ
dotnet new web -n HarmonIQ.Api -o backend/HarmonIQ.Api -f net10.0
dotnet sln add backend/HarmonIQ.Api
dotnet add backend/HarmonIQ.Api package SixLabors.ImageSharp
mkdir -p backend/HarmonIQ.Api/wwwroot && touch backend/HarmonIQ.Api/wwwroot/.gitkeep
```

- [ ] **Step 2: Write `.gitignore` and `.env.example`**

`.gitignore`:
```
bin/
obj/
node_modules/
.env
frontend/dist/
backend/HarmonIQ.Api/wwwroot/embed/
.DS_Store
*.user
```

`.env.example`:
```
# Copy to .env at the repo root. Real environment variables take precedence.
CLAUDE_API_KEY=            # shared event key — required for live mode
CLAUDE_BASE_URL=           # hackathon proxy base, e.g. https://proxy.example.com
CLAUDE_MODEL=claude-sonnet-5
LISTING_SOURCE=scrape      # scrape | api
GEO_GEOCODER_URL=https://nominatim.openstreetmap.org/search
GEO_OVERPASS_URL=https://overpass-api.de/api/interpreter
GEO_ELEVATION_URL=https://api.open-meteo.com/v1/elevation
```

- [ ] **Step 3: Write `appsettings.json`** (non-secret defaults; env/.env override):

```json
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "Claude": { "ApiKey": "", "BaseUrl": "", "Model": "claude-sonnet-5" },
  "Listing": { "Source": "scrape" },
  "Geo": {
    "GeocoderUrl": "https://nominatim.openstreetmap.org/search",
    "OverpassUrl": "https://overpass-api.de/api/interpreter",
    "ElevationUrl": "https://api.open-meteo.com/v1/elevation"
  }
}
```

- [ ] **Step 4: Write `Program.cs`**

```csharp
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
```

Delete any template endpoint (`app.MapGet("/", ...)`) the `web` template generated; `Program.cs` should contain exactly the above.

- [ ] **Step 5: Run and verify**

```bash
dotnet run --project backend/HarmonIQ.Api &
sleep 5 && curl -s http://localhost:5080/api/health
```
Expected: `{"ok":true,"live":false}` (no `.env` yet). Stop the server (`kill %1`).

- [ ] **Step 6: Commit** (include `SPEC.md` and this plan — first commit in the repo)

```bash
git add SPEC.md docs .gitignore .env.example HarmonIQ.sln backend
git commit -m "feat: scaffold HarmonIQ API with health endpoint and env config"
```

---

### Task 2: DTO models (the API contract as C# records)

**Files:**
- Create: `backend/HarmonIQ.Api/Models/Json.cs`
- Create: `backend/HarmonIQ.Api/Models/ListingModels.cs`
- Create: `backend/HarmonIQ.Api/Models/AnalysisModels.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: every type below, used verbatim by all backend tasks. **Do not rename anything** — frontend `api.ts` (Task 13) mirrors these 1:1. Tradition property is `Tradition` in C# but serializes as `"system"`.

- [ ] **Step 1: Write `Models/Json.cs`**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HarmonIQ.Api.Models;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
```

- [ ] **Step 2: Write `Models/ListingModels.cs`**

```csharp
namespace HarmonIQ.Api.Models;

public record ListingPhoto(
    string PhotoId, string ThumbnailUrl, string? Caption,
    bool Interior, bool Selected, string? SuggestedRoomType);

public record ListingNumbers(string? UnitNumber, int? Floor, string? StreetNumber);

public record SideEnvironment(string Road, string Water, string Structures, string Slope)
{
    public static readonly SideEnvironment Unknown = new("unknown", "unknown", "unknown", "unknown");
}

public record ListingEnvironment(
    SideEnvironment North, SideEnvironment East, SideEnvironment South, SideEnvironment West)
{
    public static readonly ListingEnvironment AllUnknown =
        new(SideEnvironment.Unknown, SideEnvironment.Unknown, SideEnvironment.Unknown, SideEnvironment.Unknown);
    public SideEnvironment Side(string dir) => dir switch
    {
        "north" => North, "east" => East, "south" => South, "west" => West,
        _ => SideEnvironment.Unknown,
    };
}

public record ListingResponse(
    string ListingId, string Title, string Address, string Url,
    IReadOnlyList<ListingPhoto> Photos, ListingNumbers Numbers, ListingEnvironment Environment);

public record PhotoBytes(byte[] Data, string ContentType);

public class ListingNotFoundException(string message) : Exception(message);
public class ListingSourceException(string message, Exception? inner = null) : Exception(message, inner);
```

- [ ] **Step 3: Write `Models/AnalysisModels.cs`**

```csharp
using System.Text.Json.Serialization;

namespace HarmonIQ.Api.Models;

public record PhotoSelection(string PhotoId, string? RoomType);

public record AnalyzeRequest(
    string ListingId,
    List<PhotoSelection> Photos,
    string Systems = "both",
    string Orientation = "unknown",
    ListingEnvironment? Environment = null,
    ListingNumbers? Numbers = null,
    string? Brand = null);

public record Finding(
    string Principle, string Observation,
    [property: JsonPropertyName("system")] string Tradition);

public record ViolationFinding(
    string Principle, string Observation, string Severity,
    [property: JsonPropertyName("system")] string Tradition);

public record Suggestion(string Title, string Detail, string Effort, string Impact);

public record ElementBalance(int Wood, int Fire, int Earth, int Metal, int Water);

public record RoomAnalysis(
    string PhotoId, string RoomType, int Score, ElementBalance ElementBalance,
    List<Finding> Adhering, List<ViolationFinding> Violations, List<Suggestion> Suggestions);

public record SiteAnalysis(
    int Score, List<Finding> Adhering, List<ViolationFinding> Violations, List<Suggestion> Suggestions);

public record NumerologyCheck(
    string Subject, string Value, string Verdict, string Tradition, string Reason, string? Remedy);

public record NumerologyResult(int ScoreAdjustment, List<NumerologyCheck> Checks);

public record ListingSummary(string ListingId, string Title, string Address, string Url);

public record AnalysisResult(
    int OverallScore, string Grade, string Summary, ElementBalance ElementBalance,
    List<RoomAnalysis> Rooms, SiteAnalysis Site, NumerologyResult Numerology);

public record AnalyzeResponse(
    string Mode, string? ModelId, string? Notice, ListingSummary Listing, AnalysisResult Analysis);
```

Note: `NumerologyCheck.Tradition` intentionally serializes as `"tradition"` (spec §4.2 uses `tradition` for numerology checks but `system` for findings — that's why only `Finding`/`ViolationFinding` carry the `JsonPropertyName` attribute).

- [ ] **Step 4: Verify it compiles**

```bash
dotnet build backend/HarmonIQ.Api
```
Expected: `Build succeeded`.

- [ ] **Step 5: Commit**

```bash
git add backend/HarmonIQ.Api/Models
git commit -m "feat: add listing and analysis DTOs matching the API contract"
```

---
### Task 3: Test project + NumerologyService (deterministic rules engine)

**Files:**
- Create: `backend/HarmonIQ.Tests/HarmonIQ.Tests.csproj` (via `dotnet new xunit`)
- Create: `backend/HarmonIQ.Tests/NumerologyServiceTests.cs`
- Create: `backend/HarmonIQ.Api/Services/NumerologyService.cs`
- Modify: `backend/HarmonIQ.Api/Program.cs` (DI registration)

**Interfaces:**
- Consumes: `ListingNumbers`, `NumerologyResult`, `NumerologyCheck` (Task 2).
- Produces: `NumerologyResult NumerologyService.Evaluate(ListingNumbers? numbers, string systems)` — registered as singleton `NumerologyService`. Rules: Chinese checks run when systems ∈ {both, fengshui}; Vastu digit-sum when systems ∈ {both, vastu}; Western (13/666) always, but only emitted when triggered. `ScoreAdjustment` = Σ(lucky +1, unlucky −2), clamped to [−3, +3]. FR-17..20.

- [ ] **Step 1: Create the test project**

```bash
dotnet new xunit -n HarmonIQ.Tests -o backend/HarmonIQ.Tests -f net10.0
dotnet sln add backend/HarmonIQ.Tests
dotnet add backend/HarmonIQ.Tests reference backend/HarmonIQ.Api
rm backend/HarmonIQ.Tests/UnitTest1.cs
```

- [ ] **Step 2: Write the failing tests** — `backend/HarmonIQ.Tests/NumerologyServiceTests.cs`:

```csharp
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;

namespace HarmonIQ.Tests;

public class NumerologyServiceTests
{
    private readonly NumerologyService _svc = new();

    [Fact]
    public void Unit414_Fengshui_IsUnluckyWithReasonAndRemedy()
    {
        var r = _svc.Evaluate(new ListingNumbers("414", 4, "123"), "fengshui");
        var unit = r.Checks.Single(c => c.Subject == "unitNumber" && c.Tradition == "fengshui");
        Assert.Equal("unlucky", unit.Verdict);
        Assert.Contains("4", unit.Reason);
        Assert.NotNull(unit.Remedy);
    }

    [Fact]
    public void Unit88_Fengshui_IsLucky()
    {
        var r = _svc.Evaluate(new ListingNumbers("88", null, null), "fengshui");
        Assert.Equal("lucky", r.Checks.Single(c => c.Subject == "unitNumber").Verdict);
    }

    [Fact]
    public void Street123_Vastu_DigitSum6_IsLucky()
    {
        var r = _svc.Evaluate(new ListingNumbers(null, null, "123"), "vastu");
        var street = r.Checks.Single(c => c.Subject == "streetNumber" && c.Tradition == "vastu");
        Assert.Equal("lucky", street.Verdict);
        Assert.Contains("6", street.Reason);
    }

    [Fact]
    public void Floor13_TriggersWesternCheck_EvenUnderVastuFilter()
    {
        var r = _svc.Evaluate(new ListingNumbers(null, 13, null), "vastu");
        Assert.Contains(r.Checks, c => c.Subject == "floor" && c.Tradition == "western" && c.Verdict == "unlucky");
    }

    [Fact]
    public void SystemsVastu_ExcludesChineseChecks()
    {
        var r = _svc.Evaluate(new ListingNumbers("44", 4, "444"), "vastu");
        Assert.DoesNotContain(r.Checks, c => c.Tradition == "fengshui");
    }

    [Fact]
    public void AdjustmentIsClampedToMinusThree()
    {
        var r = _svc.Evaluate(new ListingNumbers("44", 4, "40"), "both"); // many unlucky hits
        Assert.Equal(-3, r.ScoreAdjustment);
    }

    [Fact]
    public void NullNumbers_YieldsNoChecksAndZeroAdjustment()
    {
        var r = _svc.Evaluate(null, "both");
        Assert.Empty(r.Checks);
        Assert.Equal(0, r.ScoreAdjustment);
    }
}
```

- [ ] **Step 3: Run tests, verify they fail to compile** (`NumerologyService` doesn't exist yet)

```bash
dotnet test backend/HarmonIQ.Tests
```
Expected: build error `The type or namespace name 'NumerologyService' could not be found`.

- [ ] **Step 4: Implement** `backend/HarmonIQ.Api/Services/NumerologyService.cs`:

```csharp
using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

public class NumerologyService
{
    public NumerologyResult Evaluate(ListingNumbers? numbers, string systems)
    {
        var checks = new List<NumerologyCheck>();
        if (numbers is not null)
        {
            foreach (var (subject, value) in Subjects(numbers))
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (systems is "both" or "fengshui") checks.Add(Chinese(subject, value));
                if (systems is "both" or "vastu") checks.Add(Vastu(subject, value));
                if (Western(subject, value) is { } w) checks.Add(w); // only when triggered
            }
        }
        var adj = Math.Clamp(
            checks.Sum(c => c.Verdict switch { "lucky" => 1, "unlucky" => -2, _ => 0 }), -3, 3);
        return new NumerologyResult(adj, checks);
    }

    private static IEnumerable<(string, string?)> Subjects(ListingNumbers n) =>
    [
        ("unitNumber", n.UnitNumber),
        ("floor", n.Floor?.ToString()),
        ("streetNumber", n.StreetNumber),
    ];

    private static NumerologyCheck Chinese(string subject, string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Contains('4'))
        {
            var combo = digits.Contains("14") ? " The pair 14 (yao sì) sounds like \"will die\" — considered especially inauspicious."
                      : digits.Contains("24") ? " The pair 24 (èr sì) sounds like \"easy to die\" — considered especially inauspicious." : "";
            return new(subject, value, "unlucky", "fengshui",
                $"Contains the digit 4 (sì), a homophone of death (sǐ) in Chinese numerology.{combo}",
                "Add a small interior plaque so the number read at the door sums to an auspicious digit, or place a red accent at the threshold.");
        }
        if (digits.Contains('8'))
            return new(subject, value, "lucky", "fengshui",
                "Contains 8 (bā), a homophone of prosperity (fā) — the most auspicious digit in Chinese numerology.", null);
        if (digits.Contains('9'))
            return new(subject, value, "lucky", "fengshui",
                "Contains 9 (jiǔ), a homophone of long-lasting — associated with longevity in Chinese numerology.", null);
        return new(subject, value, "neutral", "fengshui",
            "No strongly charged digits (4, 8, 9) in Chinese numerology.", null);
    }

    private static NumerologyCheck Vastu(string subject, string value)
    {
        var digits = value.Where(char.IsDigit).Select(c => c - '0').ToList();
        if (digits.Count == 0)
            return new(subject, value, "neutral", "vastu", "No digits to reduce.", null);
        var sum = digits.Sum();
        while (sum > 9) sum = sum.ToString().Sum(c => c - '0');
        var (verdict, meaning) = sum switch
        {
            1 => ("lucky", "1 (Sun) — leadership and new beginnings"),
            2 => ("neutral", "2 (Moon) — sensitivity and partnership; balanced, not charged"),
            3 => ("lucky", "3 (Jupiter) — growth, learning, and family expansion"),
            4 => ("unlucky", "4 (Rahu) — instability and sudden change"),
            5 => ("lucky", "5 (Mercury) — communication, adaptability, and movement"),
            6 => ("lucky", "6 (Venus) — harmony and domestic wellbeing"),
            7 => ("neutral", "7 (Ketu) — introspection and spirituality; suits quiet households"),
            8 => ("unlucky", "8 (Saturn) — heaviness and karmic lessons"),
            _ => ("lucky", "9 (Mars) — energy, courage, and completion"),
        };
        return new(subject, value, verdict, "vastu",
            $"Digit sum {sum} — in Indian numerology, {meaning}.",
            verdict == "unlucky"
                ? "Add an interior door plaque with an extra digit so the number as read reduces to 1, 3, 5, 6, or 9."
                : null);
    }

    private static NumerologyCheck? Western(string subject, string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits == "13")
            return new(subject, value, "unlucky", "western",
                "13 is widely considered unlucky in Western tradition (triskaidekaphobia).",
                "A door wreath or plant at the entry is a common softening touch; many buildings simply relabel.");
        if (digits.Contains("666"))
            return new(subject, value, "unlucky", "western",
                "666 carries strong negative connotations in Western culture — flagged as culturally sensitive.",
                "An interior plaque adding a digit changes the number as read at the door.");
        return null;
    }
}
```

- [ ] **Step 5: Register in DI** — in `Program.cs`, under the `// DI-REGISTRATIONS` marker add:

```csharp
builder.Services.AddSingleton<HarmonIQ.Api.Services.NumerologyService>();
```

- [ ] **Step 6: Run tests, verify pass**

```bash
dotnet test backend/HarmonIQ.Tests
```
Expected: all 7 tests PASS. (If `AdjustmentIsClampedToMinusThree` fails, check: "44"/4/"40" under `both` → chinese unlucky ×3 = −6, vastu: 44→8 unlucky −2, 4 unlucky −2, 40→4 unlucky −2 → total −12 → clamps to −3.)

- [ ] **Step 7: Commit**

```bash
git add backend/HarmonIQ.Tests backend/HarmonIQ.Api/Services/NumerologyService.cs backend/HarmonIQ.Api/Program.cs HarmonIQ.sln
git commit -m "feat: deterministic numerology engine with Chinese, Vastu, and Western rules"
```

---

### Task 4: SiteAnalysisService (deterministic form-school site rules)

**Files:**
- Create: `backend/HarmonIQ.Api/Services/SiteAnalysisService.cs`
- Create: `backend/HarmonIQ.Tests/SiteAnalysisServiceTests.cs`
- Modify: `backend/HarmonIQ.Api/Program.cs` (DI registration)

**Interfaces:**
- Consumes: `ListingEnvironment`, `SideEnvironment`, `SiteAnalysis`, `Finding`, `ViolationFinding`, `Suggestion` (Task 2).
- Produces: `SiteAnalysis SiteAnalysisService.Analyze(ListingEnvironment? env, string orientation, string systems)` — singleton. Unknown values produce **no** findings (FR-16). Score = 70 + 5·adhering − (minor 5 / moderate 10 / major 18), clamped 5–98. The T-junction/highway rule fires on **any** side even with unknown orientation (a road dead-ending into the building is sha chi regardless of which side the door is on) — this is what makes the offline fixture produce the acceptance-criterion violation.

- [ ] **Step 1: Write the failing tests** — `backend/HarmonIQ.Tests/SiteAnalysisServiceTests.cs`:

```csharp
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;

namespace HarmonIQ.Tests;

public class SiteAnalysisServiceTests
{
    private readonly SiteAnalysisService _svc = new();
    private static SideEnvironment U => SideEnvironment.Unknown;
    private static SideEnvironment Side(string road = "unknown", string water = "unknown",
        string structures = "unknown", string slope = "unknown") => new(road, water, structures, slope);

    [Fact]
    public void TJunctionNorth_UnknownOrientation_ProducesMajorShaChiViolation()
    {
        var env = new ListingEnvironment(Side(road: "t-junction"), U, U, U);
        var r = _svc.Analyze(env, "unknown", "both");
        var v = Assert.Single(r.Violations, v => v.Principle.Contains("T-Junction"));
        Assert.Equal("major", v.Severity);
        Assert.Equal("fengshui", v.Tradition);
        Assert.Contains(r.Suggestions, s => s.Impact == "high"); // screening remedy
    }

    [Fact]
    public void PondEast_Vastu_IsAdhering()
    {
        var env = new ListingEnvironment(U, Side(water: "pond"), U, U);
        var r = _svc.Analyze(env, "unknown", "vastu");
        Assert.Contains(r.Adhering, a => a.Tradition == "vastu" && a.Observation.Contains("pond"));
    }

    [Fact]
    public void WaterSouth_Vastu_IsViolation()
    {
        var env = new ListingEnvironment(U, U, Side(water: "lake"), U);
        var r = _svc.Analyze(env, "unknown", "vastu");
        Assert.Contains(r.Violations, v => v.Tradition == "vastu" && v.Severity == "moderate");
    }

    [Fact]
    public void SlopeFallsNorth_Vastu_Adhering_And_FallsSouth_Violation()
    {
        var falls = _svc.Analyze(new ListingEnvironment(Side(slope: "falls"), U, U, U), "unknown", "vastu");
        Assert.Contains(falls.Adhering, a => a.Principle.Contains("Slope"));
        var bad = _svc.Analyze(new ListingEnvironment(U, U, Side(slope: "falls"), U), "unknown", "vastu");
        Assert.Contains(bad.Violations, v => v.Principle.Contains("Slope"));
    }

    [Fact]
    public void ArmchairPosition_TallerBehindNorthEntrance_Adhering()
    {
        // Entrance faces north → back is south; taller structure behind = support.
        var env = new ListingEnvironment(U, U, Side(structures: "taller-building"), U);
        var r = _svc.Analyze(env, "north", "fengshui");
        Assert.Contains(r.Adhering, a => a.Principle == "Armchair Position");
    }

    [Fact]
    public void OpenFront_NorthEntrance_BrightHallAdhering()
    {
        var env = new ListingEnvironment(Side(structures: "open"), U, U, U);
        var r = _svc.Analyze(env, "north", "fengshui");
        Assert.Contains(r.Adhering, a => a.Principle == "Bright Hall");
    }

    [Fact]
    public void TallerBuildingInFront_ModerateViolation()
    {
        var env = new ListingEnvironment(Side(structures: "taller-building"), U, U, U);
        var r = _svc.Analyze(env, "north", "fengshui");
        Assert.Contains(r.Violations, v => v.Principle == "Overshadowed Facing" && v.Severity == "moderate");
    }

    [Fact]
    public void SystemsFengshui_ExcludesVastuRules()
    {
        var env = new ListingEnvironment(U, Side(water: "pond"), U, U);
        var r = _svc.Analyze(env, "unknown", "fengshui");
        Assert.DoesNotContain(r.Adhering, a => a.Tradition == "vastu");
        Assert.DoesNotContain(r.Violations, v => v.Tradition == "vastu");
    }

    [Fact]
    public void AllUnknown_NoFindings_Score70()
    {
        var r = _svc.Analyze(ListingEnvironment.AllUnknown, "unknown", "both");
        Assert.Empty(r.Adhering);
        Assert.Empty(r.Violations);
        Assert.Equal(70, r.Score);
    }

    [Fact]
    public void NullEnvironment_BehavesLikeAllUnknown()
    {
        var r = _svc.Analyze(null, "unknown", "both");
        Assert.Empty(r.Adhering);
        Assert.Equal(70, r.Score);
    }
}
```

- [ ] **Step 2: Run tests, verify they fail to compile**

```bash
dotnet test backend/HarmonIQ.Tests
```
Expected: build error — `SiteAnalysisService` not found.

- [ ] **Step 3: Implement** `backend/HarmonIQ.Api/Services/SiteAnalysisService.cs`:

```csharp
using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

public class SiteAnalysisService
{
    private static readonly string[] Sides = ["north", "east", "south", "west"];

    public SiteAnalysis Analyze(ListingEnvironment? env, string orientation, string systems)
    {
        env ??= ListingEnvironment.AllUnknown;
        var adhering = new List<Finding>();
        var violations = new List<ViolationFinding>();
        var suggestions = new List<Suggestion>();
        bool Fs() => systems is "both" or "fengshui";
        bool Va() => systems is "both" or "vastu";

        var front = FrontSides(orientation);
        var back = front.Select(Opposite).ToArray();

        // --- Feng Shui: sha chi roads (any side; the road points at the building regardless of door) ---
        if (Fs())
            foreach (var s in Sides)
            {
                var road = env.Side(s).Road;
                if (road is "t-junction")
                {
                    violations.Add(new("T-Junction Facing the Building",
                        $"A T-junction on the {s} side aims a straight line of fast-moving energy (sha chi) directly at the building.",
                        "major", "fengshui"));
                    suggestions.Add(new("Screen the entrance line",
                        "Break the straight-on rush with a hedge, a pair of planters, or a screen inside the lobby/entry line; heavy curtains on windows facing that side also soften it.",
                        "low", "high"));
                }
                else if (road is "highway")
                {
                    violations.Add(new("Rushing Road (Sha Chi)",
                        $"A highway runs along the {s} side — fast, cutting energy in form-school terms (and real noise).",
                        "moderate", "fengshui"));
                    suggestions.Add(new("Soften the rushing side",
                        $"Dense plants and layered curtains on {s}-facing windows slow the visual rush and dampen noise.",
                        "low", "medium"));
                }
            }

        // --- Feng Shui: orientation-dependent rules ---
        if (Fs() && front.Length > 0)
        {
            foreach (var f in front)
            {
                var side = env.Side(f);
                if (side.Structures == "open")
                    adhering.Add(new("Bright Hall",
                        $"Open space to the {f} (the facing direction) forms a 'bright hall' where chi can gather before the entrance.",
                        "fengshui"));
                if (side.Structures == "taller-building")
                {
                    violations.Add(new("Overshadowed Facing",
                        $"A much taller structure to the {f} looms over the facing direction, pressing on the building's outlook.",
                        "moderate", "fengshui"));
                    suggestions.Add(new("Lift the entry light",
                        "Brighten the entrance and front-facing rooms with warm lighting and a mirror placed to widen the view — not facing the door.",
                        "low", "medium"));
                }
                if (side.Water is not ("none" or "unknown"))
                    adhering.Add(new("Water at the Facing",
                        $"Water ({side.Water}) at the {f} front is classically auspicious — wealth gathers where water settles before the entrance.",
                        "fengshui"));
                if (side.Road == "busy")
                    violations.Add(new("Rushing Chi at the Entrance",
                        $"A busy road at the {f} front rushes chi past the entrance rather than letting it settle.",
                        "moderate", "fengshui"));
            }
            foreach (var b in back)
            {
                var side = env.Side(b);
                if (side.Structures is "taller-building" or "similar")
                    adhering.Add(new("Armchair Position",
                        $"Solid structures to the {b} give the building 'mountain' backing — the classic armchair arrangement.",
                        "fengshui"));
                else if (side.Structures == "open")
                {
                    violations.Add(new("Missing Backing",
                        $"Open ground to the {b} leaves the building without support behind — an exposed armchair.",
                        "minor", "fengshui"));
                    suggestions.Add(new("Weight the rear rooms",
                        "Place heavier furniture and earthy tones in rooms on the rear side to symbolically anchor the back.",
                        "low", "low"));
                }
                if (side.Water is not ("none" or "unknown"))
                    violations.Add(new("Water Behind",
                        $"Water ({side.Water}) behind the building ({b}) undermines its backing in form-school terms.",
                        "minor", "fengshui"));
            }
        }

        // --- Vastu: absolute-direction rules (no orientation needed) ---
        if (Va())
        {
            foreach (var s in new[] { "north", "east" })
            {
                var side = env.Side(s);
                if (side.Water is not ("none" or "unknown"))
                    adhering.Add(new("Water in the North/East",
                        $"A {side.Water} to the {s} sits in the auspicious water zone (toward NE, the zone of Jala).", "vastu"));
                if (side.Slope == "falls")
                    adhering.Add(new("Auspicious Slope",
                        $"Ground falling away to the {s} lets energy and water flow toward the favorable NE.", "vastu"));
                if (side.Slope == "rises")
                    violations.Add(new("Rising Slope in the North/East",
                        $"Ground rising to the {s} blocks the light, open NE quadrant.", "minor", "vastu"));
                if (side.Structures == "taller-building")
                    violations.Add(new("Mass in the North/East",
                        $"A taller structure to the {s} weighs down the quadrant Vastu keeps light and open.", "minor", "vastu"));
                if (side.Road is not ("none" or "unknown"))
                    adhering.Add(new("Approach from the North/East",
                        $"Road access on the {s} side is a favorable approach direction in Vastu.", "vastu"));
            }
            foreach (var s in new[] { "south", "west" })
            {
                var side = env.Side(s);
                if (side.Water is not ("none" or "unknown"))
                {
                    violations.Add(new("Water in the South/West",
                        $"A {side.Water} to the {s} places water in the quadrant Vastu reserves for weight and stability.",
                        "moderate", "vastu"));
                    suggestions.Add(new("Counterweight the south-west",
                        "Keep the SW corner of the home visually heavy — bookshelves, earthy colors, stone or ceramic decor.",
                        "low", "medium"));
                }
                if (side.Slope == "falls")
                {
                    violations.Add(new("Falling Slope in the South/West",
                        $"Ground falling away to the {s} drains support from the quadrant that should be highest.",
                        "moderate", "vastu"));
                    suggestions.Add(new("Anchor the south-west corner",
                        "Weight the SW rooms with the heaviest furniture and warm, dark tones.", "low", "medium"));
                }
                if (side.Slope == "rises")
                    adhering.Add(new("Higher Ground in the South/West",
                        $"Rising ground to the {s} gives the SW the height and weight Vastu favors.", "vastu"));
                if (side.Structures is "taller-building" or "similar")
                    adhering.Add(new("Mass in the South/West",
                        $"Substantial structures to the {s} provide the heaviness Vastu wants in the SW.", "vastu"));
            }
        }

        var score = Math.Clamp(
            70 + 5 * adhering.Count
               - violations.Sum(v => v.Severity switch { "major" => 18, "moderate" => 10, _ => 5 }),
            5, 98);
        return new SiteAnalysis(score, adhering, violations, suggestions);
    }

    // Facing side(s): intercardinal orientations touch two sides.
    private static string[] FrontSides(string orientation) => orientation switch
    {
        "north" or "east" or "south" or "west" => [orientation],
        "northeast" => ["north", "east"], "southeast" => ["south", "east"],
        "southwest" => ["south", "west"], "northwest" => ["north", "west"],
        _ => [],
    };

    private static string Opposite(string side) => side switch
    {
        "north" => "south", "south" => "north", "east" => "west", _ => "east",
    };
}
```

- [ ] **Step 4: Register in DI** — under `// DI-REGISTRATIONS`:

```csharp
builder.Services.AddSingleton<HarmonIQ.Api.Services.SiteAnalysisService>();
```

- [ ] **Step 5: Run tests, verify pass**

```bash
dotnet test backend/HarmonIQ.Tests
```
Expected: all tests PASS (7 numerology + 10 site).

- [ ] **Step 6: Commit**

```bash
git add backend/HarmonIQ.Api/Services/SiteAnalysisService.cs backend/HarmonIQ.Tests/SiteAnalysisServiceTests.cs backend/HarmonIQ.Api/Program.cs
git commit -m "feat: deterministic form-school site analysis rules"
```

---

### Task 5: ScoreMath (grades, weighted overall, element averaging, local summary)

**Files:**
- Create: `backend/HarmonIQ.Api/Services/ScoreMath.cs`
- Create: `backend/HarmonIQ.Tests/ScoreMathTests.cs`

**Interfaces:**
- Consumes: `RoomAnalysis`, `SiteAnalysis`, `NumerologyResult`, `ElementBalance` (Task 2).
- Produces (all static, used by Task 10 & 11):
  - `int ScoreMath.Overall(IReadOnlyList<RoomAnalysis> rooms, SiteAnalysis site, int numerologyAdjustment)` — `clamp(round(0.7·avg(room scores) + 0.3·site.Score) + adj, 0, 100)`; with zero rooms, site score stands alone before adjustment.
  - `string ScoreMath.Grade(int score)` — A+ ≥95, A ≥90, A− ≥85, B+ ≥80, B ≥75, B− ≥70, C+ ≥65, C ≥60, C− ≥55, D+ ≥50, D ≥45, D− ≥40, F <40.
  - `ElementBalance ScoreMath.AverageElements(IReadOnlyList<RoomAnalysis> rooms)` — per-element mean, zeros when empty.
  - `string ScoreMath.LocalSummary(IReadOnlyList<RoomAnalysis> rooms, SiteAnalysis site, NumerologyResult numerology)` — deterministic 2–3 sentence fallback naming the strongest asset and the highest-impact fix.

- [ ] **Step 1: Write the failing tests** — `backend/HarmonIQ.Tests/ScoreMathTests.cs`:

```csharp
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;

namespace HarmonIQ.Tests;

public class ScoreMathTests
{
    private static RoomAnalysis Room(int score, ElementBalance? el = null) =>
        new("p1", "Bedroom", score, el ?? new ElementBalance(20, 20, 20, 20, 20), [], [], []);
    private static SiteAnalysis Site(int score) => new(score, [], [], []);

    [Theory]
    [InlineData(95, "A+")] [InlineData(94, "A")] [InlineData(85, "A-")]
    [InlineData(80, "B+")] [InlineData(75, "B")] [InlineData(70, "B-")]
    [InlineData(65, "C+")] [InlineData(60, "C")] [InlineData(55, "C-")]
    [InlineData(50, "D+")] [InlineData(45, "D")] [InlineData(40, "D-")] [InlineData(39, "F")]
    public void GradeBands(int score, string grade) => Assert.Equal(grade, ScoreMath.Grade(score));

    [Fact]
    public void Overall_Weights70_30_ThenAdjusts()
    {
        // rooms avg 80, site 60 → 0.7*80 + 0.3*60 = 74; adj -2 → 72
        var overall = ScoreMath.Overall([Room(70), Room(90)], Site(60), -2);
        Assert.Equal(72, overall);
    }

    [Fact]
    public void Overall_NoRooms_UsesSiteScore()
    {
        Assert.Equal(63, ScoreMath.Overall([], Site(60), 3));
    }

    [Fact]
    public void Overall_ClampsTo0_100()
    {
        Assert.Equal(100, ScoreMath.Overall([Room(100)], Site(100), 3));
    }

    [Fact]
    public void AverageElements_MeansEachElement()
    {
        var avg = ScoreMath.AverageElements(
            [Room(50, new ElementBalance(10, 0, 30, 0, 0)), Room(50, new ElementBalance(30, 20, 50, 0, 10))]);
        Assert.Equal(new ElementBalance(20, 10, 40, 0, 5), avg);
    }

    [Fact]
    public void LocalSummary_NamesStrongestAssetAndTopFix()
    {
        var rooms = new List<RoomAnalysis> {
            new("p1", "Bedroom", 62, new ElementBalance(20,20,20,20,20),
                [new Finding("Natural Light", "Large window floods the room", "fengshui")],
                [new ViolationFinding("Mirror Facing Bed", "Wardrobe mirror faces the bed", "major", "fengshui")],
                [new Suggestion("Reposition the mirror", "Angle it away from the bed", "low", "high")]),
        };
        var s = ScoreMath.LocalSummary(rooms, Site(70), new NumerologyResult(0, []));
        Assert.Contains("Bedroom", s);
        Assert.Contains("Reposition the mirror", s);
    }
}
```

- [ ] **Step 2: Run tests, verify compile failure**

```bash
dotnet test backend/HarmonIQ.Tests
```
Expected: `ScoreMath` not found.

- [ ] **Step 3: Implement** `backend/HarmonIQ.Api/Services/ScoreMath.cs`:

```csharp
using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

public static class ScoreMath
{
    public static int Overall(IReadOnlyList<RoomAnalysis> rooms, SiteAnalysis site, int numerologyAdjustment)
    {
        var baseScore = rooms.Count == 0
            ? site.Score
            : 0.7 * rooms.Average(r => r.Score) + 0.3 * site.Score;
        return Math.Clamp((int)Math.Round(baseScore) + numerologyAdjustment, 0, 100);
    }

    public static string Grade(int score) => score switch
    {
        >= 95 => "A+", >= 90 => "A", >= 85 => "A-",
        >= 80 => "B+", >= 75 => "B", >= 70 => "B-",
        >= 65 => "C+", >= 60 => "C", >= 55 => "C-",
        >= 50 => "D+", >= 45 => "D", >= 40 => "D-",
        _ => "F",
    };

    public static ElementBalance AverageElements(IReadOnlyList<RoomAnalysis> rooms)
    {
        if (rooms.Count == 0) return new ElementBalance(0, 0, 0, 0, 0);
        return new ElementBalance(
            (int)rooms.Average(r => r.ElementBalance.Wood),
            (int)rooms.Average(r => r.ElementBalance.Fire),
            (int)rooms.Average(r => r.ElementBalance.Earth),
            (int)rooms.Average(r => r.ElementBalance.Metal),
            (int)rooms.Average(r => r.ElementBalance.Water));
    }

    public static string LocalSummary(
        IReadOnlyList<RoomAnalysis> rooms, SiteAnalysis site, NumerologyResult numerology)
    {
        var bestRoom = rooms.OrderByDescending(r => r.Score).FirstOrDefault();
        var strongest = bestRoom?.Adhering.FirstOrDefault();
        var allSuggestions = rooms.SelectMany(r => r.Suggestions.Select(s => (room: r.RoomType, s)))
            .Concat(site.Suggestions.Select(s => (room: "the site", s)))
            .OrderByDescending(x => Rank(x.s.Impact)).ThenBy(x => Rank(x.s.Effort))
            .ToList();
        var parts = new List<string>();
        parts.Add(strongest is not null && bestRoom is not null
            ? $"The strongest asset is the {bestRoom.RoomType} — {strongest.Observation.TrimEnd('.')}."
            : "This home shows a mixed harmony profile across its rooms and site.");
        if (allSuggestions.Count > 0)
        {
            var top = allSuggestions[0];
            parts.Add($"The highest-impact fix: {top.s.Title} ({top.room}) — {top.s.Detail.TrimEnd('.')}.");
        }
        var unlucky = numerology.Checks.Count(c => c.Verdict == "unlucky");
        if (unlucky > 0)
            parts.Add($"Note: {unlucky} of the listing's numbers read as inauspicious in the selected traditions — easy to soften with the suggested remedies.");
        return string.Join(" ", parts);

        static int Rank(string level) => level switch { "high" => 3, "medium" => 2, _ => 1 };
    }
}
```

- [ ] **Step 4: Run tests, verify pass**

```bash
dotnet test backend/HarmonIQ.Tests
```
Expected: full suite PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/HarmonIQ.Api/Services/ScoreMath.cs backend/HarmonIQ.Tests/ScoreMathTests.cs
git commit -m "feat: score aggregation, grade table, and deterministic summary fallback"
```

---
### Task 6: Fixture photos + sample listing + photo cache + ListingController

**Files:**
- Create: `tools/package.json`, `tools/make-fixture-photos.mjs`
- Create: `backend/HarmonIQ.Api/Data/sample-photos/*.jpg` (generated, committed)
- Create: `backend/HarmonIQ.Api/Data/sample-listing.json`
- Create: `backend/HarmonIQ.Api/Services/SampleListingProvider.cs`
- Create: `backend/HarmonIQ.Api/Services/ListingService.cs` (sample path only in this task; scrape added in Task 8)
- Create: `backend/HarmonIQ.Api/Controllers/ListingController.cs`
- Modify: `backend/HarmonIQ.Api/Program.cs` (DI), `backend/HarmonIQ.Api/HarmonIQ.Api.csproj` (copy Data to output)

**Interfaces:**
- Consumes: DTOs (Task 2).
- Produces (used by Tasks 8, 11, 13):
  - `interface IListingService { Task<ListingResponse> GetListingAsync(string listingId, CancellationToken ct); Task<PhotoBytes?> GetPhotoAsync(string listingId, string photoId, int? width, CancellationToken ct); }`
  - `GET /api/listing/{listingId}` → `ListingResponse` JSON (404 unknown, 502 source unreachable)
  - `GET /api/listing/{listingId}/photos/{photoId}?w=300` → JPEG bytes
  - `SampleListingProvider.ListingId == "sample"`; `SampleListingProvider.GetListing()` returns the fixture `ListingResponse`; `SampleListingProvider.GetPhotoPath(photoId)` returns the JPEG file path or null.

- [ ] **Step 1: Write the fixture-photo generator** — `tools/package.json`:

```json
{ "name": "harmoniq-tools", "private": true, "type": "module", "dependencies": { "sharp": "^0.33.0" } }
```

`tools/make-fixture-photos.mjs` — schematic, clearly-labeled room illustrations with the spec's deliberate violations (labels make findings reliably visible to vision):

```js
import sharp from 'sharp';
import { mkdirSync } from 'fs';

const OUT = new URL('../backend/HarmonIQ.Api/Data/sample-photos/', import.meta.url).pathname;
mkdirSync(OUT, { recursive: true });

const W = 1200, H = 900;
const txt = (x, y, t, size = 26, fill = '#3a3a3a') =>
  `<text x="${x}" y="${y}" font-family="Helvetica, Arial" font-size="${size}" font-weight="bold" fill="${fill}" text-anchor="middle">${t}</text>`;
const room = (wall, floor, body) =>
  `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}">
     <rect width="${W}" height="${H}" fill="${wall}"/>
     <rect y="600" width="${W}" height="300" fill="${floor}"/>${body}</svg>`;

const scenes = {
  // Bedroom: bed in direct line with the door, mirror facing the bed, storage under the bed, beam over the headboard.
  bedroom: room('#f3ead9', '#c9a97a', `
    <rect x="60" y="60" width="1080" height="34" fill="#8b7355"/>${txt(600, 84, 'EXPOSED CEILING BEAM', 22, '#fff')}
    <rect x="90" y="150" width="280" height="230" fill="#bfe0f5" stroke="#7d7d7d" stroke-width="8"/>${txt(230, 410, 'WINDOW')}
    <rect x="520" y="120" width="170" height="340" fill="#a0522d"/><rect x="530" y="130" width="150" height="320" fill="#8b4020"/>${txt(605, 100, 'OPEN DOOR')}
    <rect x="470" y="470" width="280" height="60" fill="#7a5c3e"/>
    <rect x="470" y="530" width="280" height="220" rx="10" fill="#ffffff" stroke="#8a8a8a" stroke-width="5"/>${txt(610, 640, 'BED — FOOT POINTS AT DOOR', 22)}
    <rect x="485" y="760" width="110" height="48" fill="#9a8465"/><rect x="625" y="760" width="110" height="48" fill="#9a8465"/>${txt(610, 845, 'STORAGE BOXES UNDER BED', 22)}
    <rect x="960" y="280" width="130" height="330" fill="#d7ecf2" stroke="#8a8a8a" stroke-width="7"/>${txt(1025, 255, 'MIRROR (FACES BED)', 22)}
  `),
  // Living room: sofa blocking the path from the door (blocked chi), heavy bookcase in the bright window corner, clutter.
  living: room('#efe7dc', '#b58d5f', `
    <rect x="60" y="130" width="150" height="330" fill="#a0522d"/>${txt(135, 110, 'DOOR')}
    <rect x="240" y="380" width="420" height="170" rx="14" fill="#5b7ea3"/>${txt(450, 470, 'SOFA BLOCKS PATH FROM DOOR', 22, '#fff')}
    <rect x="880" y="120" width="260" height="250" fill="#bfe0f5" stroke="#7d7d7d" stroke-width="8"/>${txt(1010, 100, 'BRIGHT WINDOW CORNER')}
    <rect x="900" y="380" width="220" height="330" fill="#6e4b2a"/>${txt(1010, 545, 'HEAVY BOOKCASE', 22, '#fff')}
    <rect x="300" y="620" width="70" height="50" fill="#c96"/><rect x="390" y="640" width="90" height="40" fill="#996"/><rect x="500" y="615" width="60" height="60" fill="#a77"/>${txt(430, 730, 'CLUTTER: BOXES & PILES', 22)}
    <rect x="680" y="560" width="120" height="150" fill="#4c7a4c"/>${txt(740, 545, 'PLANT')}
  `),
  // Kitchen: stove directly beside the sink (fire/water clash), knife block on the counter, decent light.
  kitchen: room('#f2f2ea', '#9aa2a8', `
    <rect x="80" y="430" width="1040" height="120" fill="#d8d2c6"/><rect x="80" y="550" width="1040" height="200" fill="#7a746a"/>
    <rect x="330" y="360" width="220" height="80" fill="#333"/><circle cx="380" cy="400" r="24" fill="#e25822"/><circle cx="480" cy="400" r="24" fill="#e25822"/>${txt(440, 340, 'STOVE')}
    <rect x="560" y="380" width="180" height="60" rx="8" fill="#b9c7cf"/>${txt(650, 360, 'SINK (TOUCHES STOVE)', 22)}
    <rect x="820" y="360" width="90" height="80" fill="#5a3d2b"/>${txt(865, 340, 'KNIFE BLOCK', 20)}
    <rect x="900" y="90" width="230" height="220" fill="#cdeafd" stroke="#7d7d7d" stroke-width="8"/>${txt(1015, 70, 'WINDOW')}
    <rect x="120" y="620" width="140" height="120" fill="#4c7a4c"/>${txt(190, 780, 'HERB PLANTS', 22)}
  `),
  // Bathroom: toilet lid up, mirror over sink, dark and windowless.
  bathroom: room('#dfe3e6', '#aab4ba', `
    <rect width="${W}" height="${H}" fill="#c9ced3" opacity="0.45"/>${txt(600, 60, 'NO WINDOW — DIM LIGHT', 24)}
    <rect x="180" y="380" width="180" height="230" rx="16" fill="#fff" stroke="#888" stroke-width="5"/>
    <ellipse cx="270" cy="380" rx="90" ry="34" fill="#eef"/>${txt(270, 660, 'TOILET — LID OPEN', 22)}
    <rect x="620" y="430" width="260" height="110" rx="10" fill="#dfe9ee" stroke="#888" stroke-width="5"/>${txt(750, 580, 'SINK')}
    <rect x="640" y="150" width="220" height="240" fill="#d7ecf2" stroke="#8a8a8a" stroke-width="7"/>${txt(750, 130, 'MIRROR')}
    <circle cx="1030" cy="700" r="26" fill="#556"/>${txt(1030, 760, 'FLOOR DRAIN', 20)}
  `),
  // Home office: desk with back to the door (not commanding), cable clutter, one plant.
  office: room('#efe9df', '#b58d5f', `
    <rect x="920" y="120" width="160" height="340" fill="#a0522d"/>${txt(1000, 100, 'DOOR BEHIND DESK')}
    <rect x="300" y="400" width="420" height="40" fill="#7a5c3e"/><rect x="330" y="440" width="30" height="180" fill="#7a5c3e"/><rect x="660" y="440" width="30" height="180" fill="#7a5c3e"/>${txt(510, 380, 'DESK — CHAIR BACK TO DOOR', 22)}
    <rect x="430" y="300" width="170" height="100" fill="#222"/>${txt(515, 290, 'MONITOR', 20)}
    <path d="M320 630 q60 40 140 10 t180 20 t120 -15" stroke="#444" stroke-width="8" fill="none"/>${txt(520, 700, 'CABLE CLUTTER', 22)}
    <rect x="120" y="140" width="240" height="220" fill="#bfe0f5" stroke="#7d7d7d" stroke-width="8"/>${txt(240, 120, 'WINDOW')}
    <rect x="130" y="520" width="110" height="140" fill="#4c7a4c"/>${txt(185, 690, 'PLANT', 22)}
  `),
  // Non-interior shots for classification/selection behavior.
  exterior: `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}">
    <rect width="${W}" height="${H}" fill="#bfe0f5"/><rect y="700" width="${W}" height="200" fill="#7fa96b"/>
    <rect x="380" y="200" width="440" height="520" fill="#c9b8a3"/>
    ${[0,1,2,3].map(r => [0,1,2].map(c => `<rect x="${420 + c * 130}" y="${240 + r * 110}" width="80" height="70" fill="#8fb6cf"/>`).join('')).join('')}
    <rect x="560" y="620" width="90" height="100" fill="#6e4b2a"/>${txt(600, 780, 'THE ELM — BUILDING EXTERIOR', 28)}</svg>`,
  floorplan: `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}">
    <rect width="${W}" height="${H}" fill="#ffffff"/><rect x="150" y="100" width="900" height="700" fill="none" stroke="#333" stroke-width="6"/>
    <line x1="600" y1="100" x2="600" y2="500" stroke="#333" stroke-width="5"/><line x1="150" y1="500" x2="1050" y2="500" stroke="#333" stroke-width="5"/>
    ${txt(370, 300, 'BEDROOM 1')}${txt(830, 300, 'BEDROOM 2')}${txt(600, 660, 'LIVING / KITCHEN')}${txt(600, 860, 'UNIT 414 — FLOOR PLAN', 30)}</svg>`,
};

for (const [name, svg] of Object.entries(scenes)) {
  await sharp(Buffer.from(svg)).jpeg({ quality: 88 }).toFile(`${OUT}${name}.jpg`);
  console.log(`wrote ${name}.jpg`);
}
```

- [ ] **Step 2: Generate and eyeball the photos**

```bash
npm install --prefix tools
node tools/make-fixture-photos.mjs
open backend/HarmonIQ.Api/Data/sample-photos/bedroom.jpg
```
Expected: 7 JPEGs; the bedroom shows labeled bed/door/mirror/storage/beam. Commit the JPEGs (demo must work offline).

- [ ] **Step 3: Write `Data/sample-listing.json`** (fixture per FR-6; environment includes the T-junction so the sha-chi acceptance check works offline):

```json
{
  "listingId": "sample",
  "title": "The Elm — 2BR/2BA · Unit 414",
  "address": "123 Main St, Arlington, VA 22201",
  "url": "https://www.apartments.com/the-elm-arlington-va/sample/",
  "photos": [
    { "photoId": "p1", "file": "bedroom.jpg",  "caption": "Master Bedroom",    "interior": true,  "selected": true,  "suggestedRoomType": "Bedroom" },
    { "photoId": "p2", "file": "living.jpg",   "caption": "Living Room",       "interior": true,  "selected": true,  "suggestedRoomType": "Living Room" },
    { "photoId": "p3", "file": "kitchen.jpg",  "caption": "Chef's Kitchen",    "interior": true,  "selected": true,  "suggestedRoomType": "Kitchen" },
    { "photoId": "p4", "file": "bathroom.jpg", "caption": "Primary Bath",      "interior": true,  "selected": true,  "suggestedRoomType": "Bathroom" },
    { "photoId": "p5", "file": "office.jpg",   "caption": "Den / Home Office", "interior": true,  "selected": true,  "suggestedRoomType": "Home Office" },
    { "photoId": "p6", "file": "exterior.jpg", "caption": "Building Exterior", "interior": false, "selected": false, "suggestedRoomType": null },
    { "photoId": "p7", "file": "floorplan.jpg","caption": "Floor Plan",        "interior": false, "selected": false, "suggestedRoomType": null }
  ],
  "numbers": { "unitNumber": "414", "floor": 4, "streetNumber": "123" },
  "environment": {
    "north": { "road": "t-junction", "water": "none",    "structures": "open",            "slope": "level" },
    "east":  { "road": "none",       "water": "pond",    "structures": "open",            "slope": "falls" },
    "south": { "road": "quiet",      "water": "none",    "structures": "taller-building", "slope": "level" },
    "west":  { "road": "none",       "water": "unknown", "structures": "similar",         "slope": "unknown" }
  }
}
```

- [ ] **Step 4: Make `Data/` ship with the build** — in `HarmonIQ.Api.csproj` add:

```xml
<ItemGroup>
  <None Include="Data/**" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 5: Write `Services/SampleListingProvider.cs`**

```csharp
using System.Text.Json;
using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

public class SampleListingProvider
{
    public const string ListingId = "sample";
    private readonly ListingResponse _listing;
    private readonly Dictionary<string, string> _photoFiles = [];
    private readonly string _photoDir;

    public SampleListingProvider(IWebHostEnvironment env)
    {
        var dataDir = Path.Combine(env.ContentRootPath, "Data");
        _photoDir = Path.Combine(dataDir, "sample-photos");
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDir, "sample-listing.json")));
        var root = doc.RootElement;

        var photos = new List<ListingPhoto>();
        foreach (var p in root.GetProperty("photos").EnumerateArray())
        {
            var id = p.GetProperty("photoId").GetString()!;
            _photoFiles[id] = p.GetProperty("file").GetString()!;
            photos.Add(new ListingPhoto(
                id, $"/api/listing/{ListingId}/photos/{id}?w=300",
                p.GetProperty("caption").GetString(),
                p.GetProperty("interior").GetBoolean(),
                p.GetProperty("selected").GetBoolean(),
                p.GetProperty("suggestedRoomType").ValueKind == JsonValueKind.Null
                    ? null : p.GetProperty("suggestedRoomType").GetString()));
        }
        _listing = new ListingResponse(
            ListingId,
            root.GetProperty("title").GetString()!,
            root.GetProperty("address").GetString()!,
            root.GetProperty("url").GetString()!,
            photos,
            root.GetProperty("numbers").Deserialize<ListingNumbers>(Json.Options)!,
            root.GetProperty("environment").Deserialize<ListingEnvironment>(Json.Options)!);
    }

    public ListingResponse GetListing() => _listing;

    public string? GetPhotoPath(string photoId) =>
        _photoFiles.TryGetValue(photoId, out var f) ? Path.Combine(_photoDir, f) : null;
}
```

- [ ] **Step 6: Write `Services/ListingService.cs`** (sample routing + downscale/resize helpers; the `scrape` branch throws `ListingNotFoundException` until Task 8 replaces it):

```csharp
using HarmonIQ.Api.Models;
using Microsoft.Extensions.Caching.Memory;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace HarmonIQ.Api.Services;

public interface IListingService
{
    Task<ListingResponse> GetListingAsync(string listingId, CancellationToken ct);
    Task<PhotoBytes?> GetPhotoAsync(string listingId, string photoId, int? width, CancellationToken ct);
}

public class ListingService(
    SampleListingProvider sample, IMemoryCache cache, IHttpClientFactory httpFactory,
    IServiceProvider services, ILogger<ListingService> log) : IListingService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);
    public const int MaxLongEdge = 1568;

    public async Task<ListingResponse> GetListingAsync(string listingId, CancellationToken ct)
    {
        if (listingId == SampleListingProvider.ListingId) return sample.GetListing();
        if (cache.TryGetValue<ListingResponse>($"listing:{listingId}", out var cached) && cached is not null)
            return cached;
        var listing = await ScrapeListingAsync(listingId, ct); // Task 8 implements
        cache.Set($"listing:{listingId}", listing, Ttl);
        return listing;
    }

    public async Task<PhotoBytes?> GetPhotoAsync(string listingId, string photoId, int? width, CancellationToken ct)
    {
        var key = $"photo:{listingId}/{photoId}";
        if (!cache.TryGetValue<byte[]>(key, out var jpeg) || jpeg is null)
        {
            byte[]? raw = null;
            if (listingId == SampleListingProvider.ListingId)
            {
                var path = sample.GetPhotoPath(photoId);
                if (path is not null && File.Exists(path)) raw = await File.ReadAllBytesAsync(path, ct);
            }
            else
            {
                raw = await FetchRemotePhotoAsync(listingId, photoId, ct); // Task 8 implements
            }
            if (raw is null) return null;
            jpeg = DownscaleToJpeg(raw, MaxLongEdge);
            cache.Set(key, jpeg, Ttl);
        }
        return width is { } w and > 0 and < MaxLongEdge
            ? new PhotoBytes(DownscaleToJpeg(jpeg, w), "image/jpeg")
            : new PhotoBytes(jpeg, "image/jpeg");
    }

    public static byte[] DownscaleToJpeg(byte[] input, int maxEdge)
    {
        using var img = Image.Load(input);
        var longEdge = Math.Max(img.Width, img.Height);
        if (longEdge > maxEdge)
        {
            var scale = (double)maxEdge / longEdge;
            img.Mutate(x => x.Resize((int)(img.Width * scale), (int)(img.Height * scale)));
        }
        using var ms = new MemoryStream();
        img.Save(ms, new JpegEncoder { Quality = 82 });
        return ms.ToArray();
    }

    // --- Replaced with a real implementation in Task 8 ---
    private Task<ListingResponse> ScrapeListingAsync(string listingId, CancellationToken ct) =>
        throw new ListingNotFoundException($"Listing '{listingId}' not found (live listing fetch lands in Task 8).");
    private Task<byte[]?> FetchRemotePhotoAsync(string listingId, string photoId, CancellationToken ct) =>
        Task.FromResult<byte[]?>(null);
}
```

- [ ] **Step 7: Write `Controllers/ListingController.cs`**

```csharp
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HarmonIQ.Api.Controllers;

[ApiController]
public class ListingController(IListingService listings) : ControllerBase
{
    [HttpGet("/api/listing/{listingId}")]
    public async Task<IActionResult> GetListing(string listingId, [FromQuery] string? brand, CancellationToken ct)
    {
        try
        {
            return Ok(await listings.GetListingAsync(listingId, ct));
        }
        catch (ListingNotFoundException e) { return NotFound(new { error = e.Message }); }
        catch (ListingSourceException e) { return StatusCode(502, new { error = e.Message }); }
    }

    [HttpGet("/api/listing/{listingId}/photos/{photoId}")]
    public async Task<IActionResult> GetPhoto(string listingId, string photoId, [FromQuery] int? w, CancellationToken ct)
    {
        try
        {
            var photo = await listings.GetPhotoAsync(listingId, photoId, w, ct);
            return photo is null ? NotFound(new { error = $"Photo '{photoId}' not found." }) : File(photo.Data, photo.ContentType);
        }
        catch (ListingNotFoundException e) { return NotFound(new { error = e.Message }); }
        catch (ListingSourceException e) { return StatusCode(502, new { error = e.Message }); }
    }
}
```

- [ ] **Step 8: Register in DI** — under `// DI-REGISTRATIONS`:

```csharp
builder.Services.AddHttpClient();
builder.Services.AddSingleton<HarmonIQ.Api.Services.SampleListingProvider>();
builder.Services.AddSingleton<HarmonIQ.Api.Services.IListingService, HarmonIQ.Api.Services.ListingService>();
```

- [ ] **Step 9: Run and verify**

```bash
dotnet run --project backend/HarmonIQ.Api &
sleep 5
curl -s http://localhost:5080/api/listing/sample | python3 -m json.tool | head -30
curl -s -o /tmp/p1.jpg -w "%{http_code} %{content_type}\n" "http://localhost:5080/api/listing/sample/photos/p1?w=300"
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5080/api/listing/nope
kill %1
```
Expected: fixture JSON with 7 photos (5 `selected: true`); `200 image/jpeg` for the thumbnail; `404` for the unknown id.

- [ ] **Step 10: Commit**

```bash
git add tools backend/HarmonIQ.Api
git commit -m "feat: bundled sample listing fixture with generated photos, photo cache, and listing endpoints"
```

---

### Task 7: GeoContextService (environment prefill from public map data)

**Files:**
- Create: `backend/HarmonIQ.Api/Services/GeoContextService.cs`
- Modify: `backend/HarmonIQ.Api/Program.cs` (DI)

**Interfaces:**
- Consumes: `ListingEnvironment`, `SideEnvironment` (Task 2); config keys `Geo:GeocoderUrl`, `Geo:OverpassUrl`, `Geo:ElevationUrl`.
- Produces: `interface IGeoContextService { Task<ListingEnvironment> GetEnvironmentAsync(string listingId, string address, CancellationToken ct); }` — never throws; every failure degrades to `unknown` values (FR-14, FR-33). Cached per listing 30 min. Used by Task 8's scraper.

- [ ] **Step 1: Implement** `backend/HarmonIQ.Api/Services/GeoContextService.cs`:

```csharp
using System.Text.Json;
using HarmonIQ.Api.Models;
using Microsoft.Extensions.Caching.Memory;

namespace HarmonIQ.Api.Services;

public interface IGeoContextService
{
    Task<ListingEnvironment> GetEnvironmentAsync(string listingId, string address, CancellationToken ct);
}

public class GeoContextService(
    IHttpClientFactory httpFactory, IConfiguration cfg, IMemoryCache cache,
    ILogger<GeoContextService> log) : IGeoContextService
{
    private const string UserAgent = "HarmonIQ-Hackathon-Demo/1.0 (contact: achiuwei@costar.com)";

    public async Task<ListingEnvironment> GetEnvironmentAsync(string listingId, string address, CancellationToken ct)
    {
        return await cache.GetOrCreateAsync($"geo:{listingId}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            try { return await BuildAsync(address, ct); }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Geo prefill failed for {Address}; returning unknowns", address);
                return ListingEnvironment.AllUnknown;
            }
        }) ?? ListingEnvironment.AllUnknown;
    }

    private async Task<ListingEnvironment> BuildAsync(string address, CancellationToken ct)
    {
        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        // 1) Geocode (Nominatim requires a UA identifying the app).
        var geoUrl = $"{cfg["Geo:GeocoderUrl"]}?q={Uri.EscapeDataString(address)}&format=json&limit=1";
        using var geoDoc = JsonDocument.Parse(await http.GetStringAsync(geoUrl, ct));
        var first = geoDoc.RootElement.EnumerateArray().FirstOrDefault();
        if (first.ValueKind != JsonValueKind.Object) return ListingEnvironment.AllUnknown;
        var lat = double.Parse(first.GetProperty("lat").GetString()!);
        var lon = double.Parse(first.GetProperty("lon").GetString()!);

        // 2) Overpass: roads/water/buildings near the point. Failures → sides stay unknown.
        var sides = new Dictionary<string, (string road, string water, string structures, string slope)>
        {
            ["north"] = ("unknown", "unknown", "unknown", "unknown"),
            ["east"] = ("unknown", "unknown", "unknown", "unknown"),
            ["south"] = ("unknown", "unknown", "unknown", "unknown"),
            ["west"] = ("unknown", "unknown", "unknown", "unknown"),
        };
        try
        {
            var q = $@"[out:json][timeout:10];
(
  way(around:120,{lat},{lon})[highway];
  way(around:250,{lat},{lon})[natural=water];
  way(around:250,{lat},{lon})[waterway~""river|stream""];
  way(around:120,{lat},{lon})[building];
);
out center tags;";
            using var resp = await http.PostAsync(cfg["Geo:OverpassUrl"],
                new FormUrlEncodedContent([new KeyValuePair<string, string>("data", q)]), ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

            // Start from "none"/"open" once Overpass answered: absence of features is information.
            foreach (var k in sides.Keys.ToList())
                sides[k] = ("none", "none", "open", sides[k].slope);

            foreach (var el in doc.RootElement.GetProperty("elements").EnumerateArray())
            {
                if (!el.TryGetProperty("center", out var c)) continue;
                var side = BearingSide(lat, lon, c.GetProperty("lat").GetDouble(), c.GetProperty("lon").GetDouble());
                var cur = sides[side];
                var tags = el.TryGetProperty("tags", out var t) ? t : default;
                string? Tag(string name) =>
                    tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty(name, out var v) ? v.GetString() : null;

                if (Tag("highway") is { } hw)
                {
                    var kind = hw switch
                    {
                        "motorway" or "trunk" => "highway",
                        "primary" or "secondary" => "busy",
                        "tertiary" or "residential" or "unclassified" or "living_street" => "quiet",
                        _ => (string?)null,
                    };
                    // T-junction detection needs topology we don't fetch — leave that value to the Refine drawer.
                    if (kind is not null && Rank(kind) > Rank(cur.road)) cur.road = kind;
                }
                if (Tag("natural") == "water" || Tag("waterway") is not null)
                {
                    var w = Tag("waterway") is not null ? "river"
                          : Tag("water") == "lake" ? "lake" : "pond";
                    if (cur.water == "none") cur.water = w;
                }
                if (Tag("building") is not null)
                {
                    var levels = int.TryParse(Tag("building:levels"), out var l) ? l : 0;
                    var s = levels >= 8 ? "taller-building" : "similar";
                    if (cur.structures == "open" || (s == "taller-building" && cur.structures == "similar"))
                        cur.structures = s;
                }
                sides[side] = cur;
            }
        }
        catch (Exception ex) { log.LogWarning(ex, "Overpass lookup failed"); }

        // 3) Elevation: center + 4 points ~150 m out → slope per side. Failure → slope unknown.
        try
        {
            const double dLat = 0.00135; // ≈150 m
            var dLon = dLat / Math.Cos(lat * Math.PI / 180);
            double[] lats = [lat, lat + dLat, lat, lat - dLat, lat];
            double[] lons = [lon, lon, lon + dLon, lon, lon - dLon]; // center, N, E, S, W
            var url = $"{cfg["Geo:ElevationUrl"]}?latitude={string.Join(',', lats)}&longitude={string.Join(',', lons)}";
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
            var el = doc.RootElement.GetProperty("elevation").EnumerateArray().Select(x => x.GetDouble()).ToArray();
            string[] order = ["north", "east", "south", "west"];
            for (var i = 0; i < 4; i++)
            {
                var diff = el[i + 1] - el[0];
                var slope = diff > 2 ? "rises" : diff < -2 ? "falls" : "level";
                var cur = sides[order[i]];
                sides[order[i]] = (cur.road, cur.water, cur.structures, slope);
            }
        }
        catch (Exception ex) { log.LogWarning(ex, "Elevation lookup failed"); }

        SideEnvironment S(string k) => new(sides[k].road, sides[k].water, sides[k].structures, sides[k].slope);
        return new ListingEnvironment(S("north"), S("east"), S("south"), S("west"));
    }

    private static int Rank(string road) => road switch
    { "highway" => 4, "busy" => 3, "quiet" => 2, "none" => 1, _ => 0 };

    private static string BearingSide(double lat0, double lon0, double lat1, double lon1)
    {
        var dy = lat1 - lat0;
        var dx = (lon1 - lon0) * Math.Cos(lat0 * Math.PI / 180);
        var bearing = Math.Atan2(dx, dy) * 180 / Math.PI; // 0 = north, 90 = east
        return bearing switch
        {
            >= -45 and < 45 => "north",
            >= 45 and < 135 => "east",
            >= -135 and < -45 => "west",
            _ => "south",
        };
    }
}
```

- [ ] **Step 2: Register in DI** — under `// DI-REGISTRATIONS`:

```csharp
builder.Services.AddSingleton<HarmonIQ.Api.Services.IGeoContextService, HarmonIQ.Api.Services.GeoContextService>();
```

- [ ] **Step 3: Verify with a temporary debug endpoint** — add to `Program.cs` right after the health endpoint:

```csharp
app.MapGet("/api/debug/geo", (string address, HarmonIQ.Api.Services.IGeoContextService geo, CancellationToken ct) =>
    geo.GetEnvironmentAsync($"debug:{address}", address, ct));
```

```bash
dotnet run --project backend/HarmonIQ.Api &
sleep 5
curl -s "http://localhost:5080/api/debug/geo?address=1201%20Wilson%20Blvd%2C%20Arlington%2C%20VA" | python3 -m json.tool
kill %1
```
Expected: four sides with plausible values (Wilson Blvd area: roads present, structures `taller-building` or `similar`) — exact values vary with live OSM data; the point is non-`unknown` values and no exception. Also verify degradation: temporarily set `GEO_OVERPASS_URL=http://localhost:1/x` in the environment, rerun, and expect roads/structures `unknown` (or geocode-only values) with a 200 response. **Keep the debug endpoint** — it's harmless and useful at the event.

- [ ] **Step 4: Commit**

```bash
git add backend/HarmonIQ.Api/Services/GeoContextService.cs backend/HarmonIQ.Api/Program.cs
git commit -m "feat: environment prefill from Nominatim, Overpass, and open elevation data"
```

---

### Task 8: ListingService scrape path (real listing ids)

**Files:**
- Modify: `backend/HarmonIQ.Api/Services/ListingService.cs` (replace the two Task-6 stubs; add scraping + classification + number extraction)

**Interfaces:**
- Consumes: `IGeoContextService` (Task 7), `IClaudeClient` (Task 9 — this task references the *interface* only via `IServiceProvider`, so it compiles and runs before Task 9 lands; classification silently degrades until then).
- Produces: real-id behavior of `IListingService` (FR-7..10, FR-12): id `the-elm-arlington-va~xyz123` → fetch `https://www.apartments.com/the-elm-arlington-va/xyz123/`, extract title/address/photos/captions/numbers, classify interiors, auto-select ≤6, prefill environment via geo. Internal record `ScrapedPhoto(string PhotoId, string SourceUrl)` map cached under `photo-urls:{listingId}`.

- [ ] **Step 1: Replace the stubs in `ListingService.cs`** — delete the two placeholder methods at the bottom and add:

```csharp
    private const string UserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) HarmonIQ-Hackathon/1.0 (contact: achiuwei@costar.com)";
    private static readonly string[] InteriorWords =
        ["bedroom", "living", "kitchen", "bath", "dining", "office", "den", "closet", "interior", "room"];
    private static readonly string[] NonInteriorWords =
        ["exterior", "building", "pool", "floor plan", "floorplan", "amenity", "gym", "fitness",
         "lobby", "courtyard", "aerial", "map", "community", "playground", "garage", "view"];

    private async Task<ListingResponse> ScrapeListingAsync(string listingId, CancellationToken ct)
    {
        var slug = listingId.Replace('~', '/').Trim('/');
        var url = $"https://www.apartments.com/{slug}/";
        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        string html;
        try
        {
            using var resp = await http.GetAsync(url, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new ListingNotFoundException($"Listing '{listingId}' not found at the source.");
            if (!resp.IsSuccessStatusCode)
                throw new ListingSourceException($"Listing source returned {(int)resp.StatusCode}.");
            html = await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new ListingSourceException("Listing source could not be reached.", e);
        }

        var title = Regex.Match(html, "<title>(.*?)</title>", RegexOptions.Singleline) is { Success: true } tm
            ? System.Net.WebUtility.HtmlDecode(tm.Groups[1].Value.Split('|')[0].Trim()) : slug;

        // Address: JSON-LD block first, og:title fallback.
        var address = "";
        var ld = Regex.Match(html,
            "\"streetAddress\"\\s*:\\s*\"([^\"]+)\"[\\s\\S]{0,300}?\"addressLocality\"\\s*:\\s*\"([^\"]+)\"[\\s\\S]{0,200}?\"addressRegion\"\\s*:\\s*\"([^\"]+)\"");
        if (ld.Success) address = $"{ld.Groups[1].Value}, {ld.Groups[2].Value}, {ld.Groups[3].Value}";

        // Photos: distinct CDN image URLs in listing-page markup, capped at 12 candidates.
        var photoUrls = Regex.Matches(html, @"https://images1?\.apartments\.com/[^\s""'\\]+?\.jpg")
            .Select(m => m.Value).Distinct().Take(12).ToList();
        if (photoUrls.Count == 0)
            throw new ListingNotFoundException($"Listing '{listingId}' has no photos we can read.");

        // Captions: alt text adjacent to each URL when present.
        string? CaptionFor(string photoUrl)
        {
            var m = Regex.Match(html,
                $@"alt=""([^""]{{3,60}})""[^>]{{0,200}}{Regex.Escape(photoUrl)}|{Regex.Escape(photoUrl)}[^>]{{0,200}}alt=""([^""]{{3,60}})""");
            var alt = m.Success ? (m.Groups[1].Value != "" ? m.Groups[1].Value : m.Groups[2].Value) : null;
            return string.IsNullOrWhiteSpace(alt) ? null : System.Net.WebUtility.HtmlDecode(alt);
        }

        var scraped = new List<(string PhotoId, string SourceUrl, string? Caption)>();
        for (var i = 0; i < photoUrls.Count; i++)
            scraped.Add(($"p{i + 1}", photoUrls[i], CaptionFor(photoUrls[i])));
        cache.Set($"photo-urls:{listingId}",
            scraped.ToDictionary(p => p.PhotoId, p => p.SourceUrl), Ttl);

        // Classify: caption keywords → else batched Claude thumbnail call → else permissive default.
        var photos = new List<ListingPhoto>();
        var needsModel = new List<int>();
        for (var i = 0; i < scraped.Count; i++)
        {
            var cap = scraped[i].Caption?.ToLowerInvariant() ?? "";
            bool? interior =
                NonInteriorWords.Any(cap.Contains) ? false :
                InteriorWords.Any(cap.Contains) ? true : null;
            if (interior is null) needsModel.Add(i);
            photos.Add(new ListingPhoto(
                scraped[i].PhotoId,
                $"/api/listing/{Uri.EscapeDataString(listingId)}/photos/{scraped[i].PhotoId}?w=300",
                scraped[i].Caption, interior ?? true, false,
                interior == true ? SuggestRoomType(cap) : null));
        }
        if (needsModel.Count > 0)
        {
            var verdicts = await TryClassifyWithClaudeAsync(listingId, needsModel.Select(i => photos[i]).ToList(), ct);
            if (verdicts is not null)
                foreach (var (idx, isInterior) in needsModel.Zip(verdicts))
                    photos[idx] = photos[idx] with { Interior = isInterior };
        }

        // Auto-select interiors up to 6 by listing photo order (FR-8).
        var selectedCount = 0;
        for (var i = 0; i < photos.Count; i++)
            if (photos[i].Interior && selectedCount < 6) { photos[i] = photos[i] with { Selected = true }; selectedCount++; }

        var numbers = ExtractNumbers(title, html, address);
        var environment = string.IsNullOrEmpty(address)
            ? ListingEnvironment.AllUnknown
            : await services.GetRequiredService<IGeoContextService>().GetEnvironmentAsync(listingId, address, ct);

        return new ListingResponse(listingId, title, address, url, photos, numbers, environment);
    }

    private async Task<byte[]?> FetchRemotePhotoAsync(string listingId, string photoId, CancellationToken ct)
    {
        if (!cache.TryGetValue<Dictionary<string, string>>($"photo-urls:{listingId}", out var map) || map is null)
        {
            await GetListingAsync(listingId, ct); // repopulate after cache expiry
            cache.TryGetValue($"photo-urls:{listingId}", out map);
        }
        if (map is null || !map.TryGetValue(photoId, out var src)) return null;
        var http = httpFactory.CreateClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        try { return await http.GetByteArrayAsync(src, ct); }
        catch (Exception e) { log.LogWarning(e, "Photo fetch failed: {Url}", src); return null; }
    }

    private static string? SuggestRoomType(string caption) => caption switch
    {
        var c when c.Contains("bed") => "Bedroom",
        var c when c.Contains("living") => "Living Room",
        var c when c.Contains("kitchen") => "Kitchen",
        var c when c.Contains("bath") => "Bathroom",
        var c when c.Contains("dining") => "Dining Room",
        var c when c.Contains("office") || c.Contains("den") => "Home Office",
        _ => null,
    };

    private static ListingNumbers ExtractNumbers(string title, string html, string address)
    {
        var street = Regex.Match(address, @"^(\d+)").Groups[1].Value;
        var unit = Regex.Match(title + " " + html[..Math.Min(html.Length, 20000)],
            @"(?:Unit|Apt|#)\s*([0-9]{1,5}[A-Z]?)", RegexOptions.IgnoreCase).Groups[1].Value;
        int? floor = null;
        var unitDigits = new string(unit.Where(char.IsDigit).ToArray());
        if (unitDigits.Length >= 3 && int.TryParse(unitDigits[..^2], out var f)) floor = f;
        else if (Regex.Match(html, @"(\d{1,2})(?:st|nd|rd|th)\s+[Ff]loor") is { Success: true } fm)
            floor = int.Parse(fm.Groups[1].Value);
        return new ListingNumbers(
            string.IsNullOrEmpty(unit) ? null : unit, floor,
            string.IsNullOrEmpty(street) ? null : street);
    }

    // Batched thumbnail classification; any failure returns null (callers keep the permissive default).
    private async Task<List<bool>?> TryClassifyWithClaudeAsync(
        string listingId, List<ListingPhoto> unclassified, CancellationToken ct)
    {
        try
        {
            var claude = services.GetService<IClaudeClient>();
            if (claude is null || !claude.IsConfigured) return null;
            var content = new List<object>();
            foreach (var p in unclassified)
            {
                var bytes = await GetPhotoAsync(listingId, p.PhotoId, 300, ct);
                if (bytes is null) return null;
                content.Add(new { type = "image", source = new { type = "base64", media_type = "image/jpeg", data = Convert.ToBase64String(bytes.Data) } });
            }
            content.Add(new { type = "text", text = $"Classify each of the {unclassified.Count} photos above, in order." });
            var resp = await claude.MessagesAsync(new
            {
                model = claude.Model, max_tokens = 1024,
                tools = new[] { Prompts.ClassifyTool },
                tool_choice = new { type = "tool", name = "classify_photos" },
                messages = new object[] { new { role = "user", content } },
            }, ct);
            var input = resp.GetProperty("content").EnumerateArray()
                .First(c => c.GetProperty("type").GetString() == "tool_use").GetProperty("input");
            return input.GetProperty("categories").EnumerateArray()
                .Select(c => c.GetString() == "interior").ToList();
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Photo classification degraded to permissive default");
            return null;
        }
    }
```

Add `using System.Text.RegularExpressions;` at the top of the file.

Note: `Prompts.ClassifyTool` and `IClaudeClient` arrive in Task 9. **If executing tasks strictly in order**, land this task with the classification method body reduced to `return null;` plus a `// enabled in Task 9` comment, then restore the full body as part of Task 9 Step 6 (that step repeats the code).

- [ ] **Step 2: Build and verify against a live listing**

```bash
dotnet build backend/HarmonIQ.Api
dotnet run --project backend/HarmonIQ.Api &
sleep 5
# Pick any current apartments.com listing; convert its slug to ~ form, e.g.:
curl -s "http://localhost:5080/api/listing/the-clarendon-arlington-va~xyz" -w "\n%{http_code}\n" | tail -5
kill %1
```
Expected: either a full `ListingResponse` (200) for a valid slug, or a clean `404 {"error": ...}` — never a 500. Scrape fragility is accepted (SPEC §7); the demo path is `sample`.

- [ ] **Step 3: Commit**

```bash
git add backend/HarmonIQ.Api/Services/ListingService.cs
git commit -m "feat: scrape real listings — photos, captions, numbers, geo-prefilled environment"
```

---
### Task 9: ClaudeClient + Prompts

**Files:**
- Create: `backend/HarmonIQ.Api/Services/ClaudeClient.cs`
- Create: `backend/HarmonIQ.Api/Services/Prompts.cs`
- Modify: `backend/HarmonIQ.Api/Program.cs` (DI)
- Modify: `backend/HarmonIQ.Api/Services/ListingService.cs` (restore full `TryClassifyWithClaudeAsync` body from Task 8 Step 1 if it was stubbed)

**Interfaces:**
- Consumes: config keys `Claude:ApiKey`, `Claude:BaseUrl`, `Claude:Model`.
- Produces (used by Tasks 8, 10, 11):
  - `interface IClaudeClient { bool IsConfigured { get; } string Model { get; } Task<JsonElement> MessagesAsync(object payload, CancellationToken ct); }`
  - `class ClaudeUnavailableException : Exception` — thrown on missing config, 401/403, network failure, or exhausted retries; Task 11 catches it to fall back to demo mode.
  - `Prompts.RoomSystemPrompt(systems, orientation)`, `Prompts.RoomTool`, `Prompts.ClassifyTool`, `Prompts.SummaryPrompt(digest)` (all static).
- Constraint reminders: no `stream`, no `temperature`; retry 429/5xx with linear backoff ×3.

- [ ] **Step 1: Write `Services/ClaudeClient.cs`**

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

public class ClaudeUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public interface IClaudeClient
{
    bool IsConfigured { get; }
    string Model { get; }
    Task<JsonElement> MessagesAsync(object payload, CancellationToken ct = default);
}

public class ClaudeClient(HttpClient http, IConfiguration cfg, ILogger<ClaudeClient> log) : IClaudeClient
{
    public bool IsConfigured =>
        !string.IsNullOrEmpty(cfg["Claude:ApiKey"]) && !string.IsNullOrEmpty(cfg["Claude:BaseUrl"]);
    public string Model => cfg["Claude:Model"] is { Length: > 0 } m ? m : "claude-sonnet-5";

    public async Task<JsonElement> MessagesAsync(object payload, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new ClaudeUnavailableException("Claude API key / base URL not configured.");
        var url = $"{cfg["Claude:BaseUrl"]!.TrimEnd('/')}/v1/messages";
        var body = JsonSerializer.Serialize(payload, Json.Options);

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage resp;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Add("x-api-key", cfg["Claude:ApiKey"]);
                req.Headers.Add("anthropic-version", "2023-06-01");
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                resp = await http.SendAsync(req, ct);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                throw new ClaudeUnavailableException("Claude endpoint unreachable.", e);
            }

            using (resp)
            {
                if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new ClaudeUnavailableException($"Claude key rejected ({(int)resp.StatusCode}).");
                if (((int)resp.StatusCode == 429 || (int)resp.StatusCode >= 500) && attempt <= 3)
                {
                    log.LogWarning("Claude {Status}, retry {Attempt}/3", (int)resp.StatusCode, attempt);
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct); // linear backoff: 2s, 4s, 6s
                    continue;
                }
                var text = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                    throw new ClaudeUnavailableException($"Claude error {(int)resp.StatusCode}: {Truncate(text)}");
                using var doc = JsonDocument.Parse(text);
                return doc.RootElement.Clone();
            }
        }
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300];
}
```

- [ ] **Step 2: Write `Services/Prompts.cs`**

```csharp
namespace HarmonIQ.Api.Services;

public static class Prompts
{
    public static string RoomSystemPrompt(string systems, string orientation)
    {
        var tradition = systems switch
        {
            "fengshui" => "Feng Shui (form school and Black Hat) only. Tag every finding with system \"fengshui\".",
            "vastu" => "Vastu Shastra only. Tag every finding with system \"vastu\".",
            _ => "both Feng Shui and Vastu Shastra. Tag each finding with the system it comes from (\"fengshui\", \"vastu\", or \"both\" when shared).",
        };
        var orient = orientation == "unknown"
            ? "The unit's entrance orientation is unknown — skip principles that require compass directions rather than guessing."
            : $"The unit's entrance faces {orientation} — you may apply directional principles relative to that.";
        return $"""
You are HarmonIQ, an expert consultant grading apartment rooms against {tradition}
{orient}

Hard rules:
- Reference ONLY what is actually visible in the photo. Never invent furniture, windows, or directions you cannot see.
- Findings to look for include: commanding position (bed/desk/stove), chi flow and clutter, five-element balance, mirror placement, bed under window or beam, under-bed storage, pairs and symmetry, natural light, poison arrows (sharp corners aimed at seating/bed); for Vastu: room-appropriate colors, heavy furniture placement, openness of the center, water element placement, sleep/work orientation.
- Score the room 0-100 (100 = textbook harmony). Estimate the five-element balance (wood/fire/earth/metal/water, each 0-100) from visible materials and colors.
- Return 2-4 adhering findings, 0-4 violations (severity minor|moderate|major), and 2-4 suggestions.
- Every suggestion must be renter-feasible: rearranging furniture, decor, plants, mirrors, textiles, lighting. Never structural work.
- Phrase observations concretely, naming the visible objects ("the wardrobe mirror directly faces the bed").
- Record your analysis by calling the record_room_analysis tool. If the room type was provided, keep it; otherwise identify it from the image.
""";
    }

    private static readonly object FindingItems = new
    {
        type = "object",
        properties = new
        {
            principle = new { type = "string" },
            observation = new { type = "string" },
            system = new { type = "string", @enum = new[] { "fengshui", "vastu", "both" } },
        },
        required = new[] { "principle", "observation", "system" },
    };

    public static readonly object RoomTool = new
    {
        name = "record_room_analysis",
        description = "Record the structured harmony analysis of one room photo.",
        input_schema = new
        {
            type = "object",
            properties = new
            {
                roomType = new { type = "string" },
                score = new { type = "integer", minimum = 0, maximum = 100 },
                elementBalance = new
                {
                    type = "object",
                    properties = new
                    {
                        wood = new { type = "integer", minimum = 0, maximum = 100 },
                        fire = new { type = "integer", minimum = 0, maximum = 100 },
                        earth = new { type = "integer", minimum = 0, maximum = 100 },
                        metal = new { type = "integer", minimum = 0, maximum = 100 },
                        water = new { type = "integer", minimum = 0, maximum = 100 },
                    },
                    required = new[] { "wood", "fire", "earth", "metal", "water" },
                },
                adhering = new { type = "array", minItems = 2, maxItems = 4, items = FindingItems },
                violations = new
                {
                    type = "array", maxItems = 4,
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            principle = new { type = "string" },
                            observation = new { type = "string" },
                            severity = new { type = "string", @enum = new[] { "minor", "moderate", "major" } },
                            system = new { type = "string", @enum = new[] { "fengshui", "vastu", "both" } },
                        },
                        required = new[] { "principle", "observation", "severity", "system" },
                    },
                },
                suggestions = new
                {
                    type = "array", minItems = 2, maxItems = 4,
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            title = new { type = "string" },
                            detail = new { type = "string" },
                            effort = new { type = "string", @enum = new[] { "low", "medium", "high" } },
                            impact = new { type = "string", @enum = new[] { "low", "medium", "high" } },
                        },
                        required = new[] { "title", "detail", "effort", "impact" },
                    },
                },
            },
            required = new[] { "roomType", "score", "elementBalance", "adhering", "violations", "suggestions" },
        },
    };

    public static readonly object ClassifyTool = new
    {
        name = "classify_photos",
        description = "Classify each listing photo, in the order given.",
        input_schema = new
        {
            type = "object",
            properties = new
            {
                categories = new
                {
                    type = "array",
                    items = new { type = "string", @enum = new[] { "interior", "exterior", "floorplan", "amenity", "other" } },
                },
            },
            required = new[] { "categories" },
        },
    };

    public static string SummaryPrompt(string digest) => $"""
You are HarmonIQ. Below is the findings digest for one apartment listing (rooms, site, numerology).
Write a 2-3 sentence overall assessment for a renter: name the strongest asset and the single
highest-impact fix across all three lenses. Warm, concrete, no headers, no bullet points,
under 80 words. Frame tradition-based claims as tradition ("in Vastu terms...").

{digest}
""";
}
```

- [ ] **Step 3: Register in DI** — under `// DI-REGISTRATIONS`:

```csharp
builder.Services.AddHttpClient<HarmonIQ.Api.Services.IClaudeClient, HarmonIQ.Api.Services.ClaudeClient>(
    c => c.Timeout = TimeSpan.FromSeconds(60));
```

- [ ] **Step 4: If Task 8 stubbed `TryClassifyWithClaudeAsync`, restore the full body now** (code is in Task 8 Step 1).

- [ ] **Step 5: Build + verify config plumbing**

```bash
dotnet build backend/HarmonIQ.Api
cp .env.example .env   # then paste the real event key/base URL into .env
dotnet run --project backend/HarmonIQ.Api &
sleep 5 && curl -s http://localhost:5080/api/health
kill %1
```
Expected: `{"ok":true,"live":true}` with a key in `.env`, `"live":false` without. Confirm `.env` is ignored: `git status --short` must not list `.env`.

- [ ] **Step 6: Commit**

```bash
git add backend/HarmonIQ.Api
git commit -m "feat: Claude client with retries and prompt/tool-schema definitions"
```

---

### Task 10: MockAnalysisService (demo fallback templates)

**Files:**
- Create: `backend/HarmonIQ.Api/Data/mock-analysis.json`
- Create: `backend/HarmonIQ.Api/Services/MockAnalysisService.cs`
- Modify: `backend/HarmonIQ.Api/Program.cs` (DI)

**Interfaces:**
- Consumes: DTOs; `Prompts` not needed; fixture room types.
- Produces (used by Task 11): `List<RoomAnalysis> MockAnalysisService.AnalyzeRooms(IReadOnlyList<PhotoSelection> photos, string systems)` — deterministic (score varies only by photoId hash), respects the tradition filter by dropping findings whose system doesn't match, templates keyed by lower-cased room type with a `default` fallback (FR-32).

- [ ] **Step 1: Write `Data/mock-analysis.json`** — templates aligned with the fixture's deliberate violations so demo mode reads true:

```json
{
  "bedroom": {
    "score": 54,
    "elementBalance": { "wood": 22, "fire": 8, "earth": 46, "metal": 12, "water": 12 },
    "adhering": [
      { "principle": "Natural Light", "observation": "A large window brings strong daylight into the sleeping area.", "system": "both" },
      { "principle": "Solid Headboard", "observation": "The bed has a solid headboard against a wall, giving supported rest.", "system": "fengshui" }
    ],
    "violations": [
      { "principle": "Bed in Line with the Door", "observation": "The foot of the bed points straight at the open door — the classic 'coffin position' that drains restful energy.", "severity": "major", "system": "both" },
      { "principle": "Mirror Facing the Bed", "observation": "A wall mirror directly faces the bed, bouncing active energy at sleepers.", "severity": "major", "system": "fengshui" },
      { "principle": "Under-Bed Storage", "observation": "Boxes stored under the bed block chi from circulating beneath the sleeper.", "severity": "moderate", "system": "fengshui" },
      { "principle": "Beam Over the Bed", "observation": "An exposed beam crosses above the bed, pressing down on the sleeping area.", "severity": "moderate", "system": "both" }
    ],
    "suggestions": [
      { "title": "Shift the bed off the door line", "detail": "Slide the bed so the foot no longer aims at the doorway while keeping a view of the door.", "effort": "medium", "impact": "high" },
      { "title": "Reposition or cover the mirror", "detail": "Angle the mirror away from the bed or drape it at night.", "effort": "low", "impact": "high" },
      { "title": "Clear under the bed", "detail": "Move the boxes to a closet so air and energy can flow beneath you.", "effort": "low", "impact": "medium" }
    ]
  },
  "living room": {
    "score": 61,
    "elementBalance": { "wood": 34, "fire": 12, "earth": 30, "metal": 10, "water": 14 },
    "adhering": [
      { "principle": "Living Greenery", "observation": "A healthy plant adds wood energy and life to the room.", "system": "both" },
      { "principle": "Bright Natural Corner", "observation": "The window corner brings generous daylight into the seating area.", "system": "fengshui" }
    ],
    "violations": [
      { "principle": "Blocked Chi Path", "observation": "The sofa sits across the walkway from the door, interrupting the natural flow through the room.", "severity": "moderate", "system": "fengshui" },
      { "principle": "Heavy Mass in the Light Corner", "observation": "A tall, heavy bookcase occupies the brightest corner, weighing down its uplifting energy.", "severity": "moderate", "system": "both" },
      { "principle": "Clutter", "observation": "Boxes and piles gather in the center of the room, stagnating energy.", "severity": "minor", "system": "both" }
    ],
    "suggestions": [
      { "title": "Open the entry path", "detail": "Rotate the sofa to run parallel with the walkway so the route from the door flows freely.", "effort": "medium", "impact": "high" },
      { "title": "Swap the bookcase and plant", "detail": "Move the bookcase to a dimmer wall and let the plant take the bright corner.", "effort": "medium", "impact": "medium" },
      { "title": "Clear the floor piles", "detail": "Basket or shelve the loose items to restore open floor.", "effort": "low", "impact": "medium" }
    ]
  },
  "kitchen": {
    "score": 66,
    "elementBalance": { "wood": 18, "fire": 34, "earth": 20, "metal": 20, "water": 8 },
    "adhering": [
      { "principle": "Fresh Growth", "observation": "Herb plants on the floor add living wood energy to balance the fire of the stove.", "system": "fengshui" },
      { "principle": "Natural Light", "observation": "A window keeps the cooking area bright and airy.", "system": "both" }
    ],
    "violations": [
      { "principle": "Fire-Water Clash", "observation": "The stove and sink sit directly adjacent, placing fire and water elements in conflict.", "severity": "moderate", "system": "fengshui" },
      { "principle": "Exposed Blades", "observation": "A knife block sits in open view, projecting sharp energy across the counter.", "severity": "minor", "system": "fengshui" }
    ],
    "suggestions": [
      { "title": "Buffer stove and sink", "detail": "Place a small wooden cutting board or a potted herb between the stove and sink to mediate fire and water.", "effort": "low", "impact": "medium" },
      { "title": "Store knives in a drawer", "detail": "Move the knife block into a drawer with an in-drawer organizer.", "effort": "low", "impact": "low" }
    ]
  },
  "bathroom": {
    "score": 58,
    "elementBalance": { "wood": 6, "fire": 4, "earth": 22, "metal": 22, "water": 46 },
    "adhering": [
      { "principle": "Contained Water Room", "observation": "Fixtures are grouped tidily along one wall, keeping the water zone contained.", "system": "vastu" },
      { "principle": "Clear Mirror", "observation": "A clean mirror above the sink expands light in a small space.", "system": "fengshui" }
    ],
    "violations": [
      { "principle": "Open Toilet Lid", "observation": "The toilet lid is up — in Feng Shui terms, wealth energy literally drains away.", "severity": "minor", "system": "fengshui" },
      { "principle": "Dim, Windowless Space", "observation": "No natural light leaves the room's energy heavy and damp.", "severity": "moderate", "system": "both" }
    ],
    "suggestions": [
      { "title": "Keep the lid down", "detail": "Close the toilet lid (and the door) to stop the symbolic drain.", "effort": "low", "impact": "medium" },
      { "title": "Warm the light", "detail": "Add warm-white bulbs and a small plant that tolerates low light (pothos) to lift the energy.", "effort": "low", "impact": "medium" }
    ]
  },
  "home office": {
    "score": 60,
    "elementBalance": { "wood": 30, "fire": 10, "earth": 24, "metal": 26, "water": 10 },
    "adhering": [
      { "principle": "Natural Light at the Desk", "observation": "The window gives the work area steady daylight.", "system": "both" },
      { "principle": "Living Plant", "observation": "A plant near the desk adds growth energy to the work zone.", "system": "fengshui" }
    ],
    "violations": [
      { "principle": "Back to the Door", "observation": "The desk chair faces away from the door — outside the commanding position, inviting unease.", "severity": "major", "system": "fengshui" },
      { "principle": "Cable Clutter", "observation": "Loose cables tangle beneath the desk, snarling the flow of the workspace.", "severity": "minor", "system": "both" }
    ],
    "suggestions": [
      { "title": "Turn the desk to command the room", "detail": "Rotate the desk so you face the door with the wall at your back.", "effort": "medium", "impact": "high" },
      { "title": "Tame the cables", "detail": "Bundle cables into a sleeve or under-desk tray.", "effort": "low", "impact": "low" }
    ]
  },
  "default": {
    "score": 68,
    "elementBalance": { "wood": 24, "fire": 16, "earth": 28, "metal": 16, "water": 16 },
    "adhering": [
      { "principle": "Balanced Proportions", "observation": "The room reads open and usable, with furniture scaled to the space.", "system": "both" },
      { "principle": "Natural Light", "observation": "Daylight reaches the main area of the room.", "system": "both" }
    ],
    "violations": [
      { "principle": "Mixed Element Balance", "observation": "Materials and colors lean on one or two elements, leaving the palette slightly unbalanced.", "severity": "minor", "system": "fengshui" }
    ],
    "suggestions": [
      { "title": "Round out the elements", "detail": "Add an accent in an underrepresented element — a plant (wood), warm lamp (fire), or ceramic piece (earth).", "effort": "low", "impact": "low" },
      { "title": "Keep pathways clear", "detail": "Maintain an open route from the door through the room.", "effort": "low", "impact": "medium" }
    ]
  }
}
```

- [ ] **Step 2: Write `Services/MockAnalysisService.cs`**

```csharp
using System.Text.Json;
using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

public class MockAnalysisService
{
    private readonly Dictionary<string, JsonElement> _templates;

    public MockAnalysisService(IWebHostEnvironment env)
    {
        var path = Path.Combine(env.ContentRootPath, "Data", "mock-analysis.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        _templates = doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    public List<RoomAnalysis> AnalyzeRooms(IReadOnlyList<PhotoSelection> photos, string systems)
    {
        return photos.Select(p =>
        {
            var roomType = string.IsNullOrWhiteSpace(p.RoomType) || p.RoomType == "Auto-detect"
                ? "Room" : p.RoomType!;
            var key = roomType.ToLowerInvariant();
            var tpl = _templates.TryGetValue(key, out var t) ? t : _templates["default"];

            bool Fits(string sys) => systems == "both" || sys == "both" || sys == systems;
            var adhering = tpl.GetProperty("adhering").Deserialize<List<Finding>>(Json.Options)!
                .Where(f => Fits(f.Tradition)).ToList();
            var violations = tpl.GetProperty("violations").Deserialize<List<ViolationFinding>>(Json.Options)!
                .Where(f => Fits(f.Tradition)).ToList();
            var suggestions = tpl.GetProperty("suggestions").Deserialize<List<Suggestion>>(Json.Options)!;

            // Deterministic per-photo jitter (-3..+3) so identical templates don't render
            // identical chips. NOT string.GetHashCode() — that's randomized per process.
            var hash = p.PhotoId.Aggregate(17, (h, c) => unchecked(h * 31 + c));
            var jitter = Math.Abs(hash) % 7 - 3;
            var score = Math.Clamp(tpl.GetProperty("score").GetInt32() + jitter, 0, 100);

            return new RoomAnalysis(
                p.PhotoId, roomType, score,
                tpl.GetProperty("elementBalance").Deserialize<ElementBalance>(Json.Options)!,
                adhering, violations, suggestions);
        }).ToList();
    }
}
```

- [ ] **Step 3: Register in DI** — under `// DI-REGISTRATIONS`:

```csharp
builder.Services.AddSingleton<HarmonIQ.Api.Services.MockAnalysisService>();
```

- [ ] **Step 4: Build to verify, then commit** (behavior is exercised end-to-end in Task 11's verification)

```bash
dotnet build backend/HarmonIQ.Api
git add backend/HarmonIQ.Api/Data/mock-analysis.json backend/HarmonIQ.Api/Services/MockAnalysisService.cs backend/HarmonIQ.Api/Program.cs
git commit -m "feat: demo-mode room analysis templates keyed by room type"
```

---

### Task 11: ClaudeAnalysisService + AnalysisController (`POST /api/analyze`)

**Files:**
- Create: `backend/HarmonIQ.Api/Services/ClaudeAnalysisService.cs`
- Create: `backend/HarmonIQ.Api/Controllers/AnalysisController.cs`
- Modify: `backend/HarmonIQ.Api/Program.cs` (DI)

**Interfaces:**
- Consumes: `IClaudeClient`, `Prompts` (Task 9), `MockAnalysisService` (Task 10), `IListingService` (Task 6/8), `NumerologyService` (3), `SiteAnalysisService` (4), `ScoreMath` (5), DTOs (2).
- Produces:
  - `ClaudeAnalysisService.AnalyzeRoomsAsync(IReadOnlyList<RoomInput> rooms, string systems, string orientation, CancellationToken)` → `List<RoomAnalysis>`; `record RoomInput(string PhotoId, string? RoomType, byte[] ImageJpeg)`.
  - `ClaudeAnalysisService.SummarizeAsync(...)` → string (falls back to `ScoreMath.LocalSummary`).
  - `ClaudeAnalysisService.RephraseSiteAsync(SiteAnalysis site, CancellationToken)` → `SiteAnalysis` (live phrasing polish; returns input unchanged on any failure — the deterministic templates are the fallback).
  - `POST /api/analyze` per SPEC §4.2 — 200 live/demo, 400 invalid input, 502 non-fallback upstream failure. `rooms[i]` corresponds to `photos[i]` in request order.

- [ ] **Step 1: Write `Services/ClaudeAnalysisService.cs`**

```csharp
using System.Text;
using System.Text.Json;
using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

public record RoomInput(string PhotoId, string? RoomType, byte[] ImageJpeg);

public class ClaudeAnalysisService(IClaudeClient claude, ILogger<ClaudeAnalysisService> log)
{
    public async Task<List<RoomAnalysis>> AnalyzeRoomsAsync(
        IReadOnlyList<RoomInput> rooms, string systems, string orientation, CancellationToken ct)
    {
        // One request per photo, fanned out (SPEC §5.3): stays under the proxy's 29 s gateway timeout.
        var results = await Task.WhenAll(rooms.Select(r => AnalyzeOneAsync(r, systems, orientation, ct)));
        return results.ToList();
    }

    private async Task<RoomAnalysis> AnalyzeOneAsync(
        RoomInput room, string systems, string orientation, CancellationToken ct)
    {
        var hint = string.IsNullOrWhiteSpace(room.RoomType) || room.RoomType == "Auto-detect"
            ? "Identify the room type from the image, then analyze it."
            : $"This photo is tagged as: {room.RoomType}. Analyze it as that room.";
        var resp = await claude.MessagesAsync(new
        {
            model = claude.Model,
            max_tokens = 4096,
            system = Prompts.RoomSystemPrompt(systems, orientation),
            tools = new[] { Prompts.RoomTool },
            tool_choice = new { type = "tool", name = "record_room_analysis" },
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "image", source = new { type = "base64", media_type = "image/jpeg", data = Convert.ToBase64String(room.ImageJpeg) } },
                        new { type = "text", text = hint },
                    },
                },
            },
        }, ct);

        var input = resp.GetProperty("content").EnumerateArray()
            .First(c => c.GetProperty("type").GetString() == "tool_use")
            .GetProperty("input");
        return new RoomAnalysis(
            room.PhotoId,
            input.GetProperty("roomType").GetString() ?? room.RoomType ?? "Room",
            Math.Clamp(input.GetProperty("score").GetInt32(), 0, 100),
            input.GetProperty("elementBalance").Deserialize<ElementBalance>(Json.Options)!,
            input.GetProperty("adhering").Deserialize<List<Finding>>(Json.Options)!,
            input.GetProperty("violations").Deserialize<List<ViolationFinding>>(Json.Options)!,
            input.GetProperty("suggestions").Deserialize<List<Suggestion>>(Json.Options)!);
    }

    public async Task<string> SummarizeAsync(
        IReadOnlyList<RoomAnalysis> rooms, SiteAnalysis site, NumerologyResult numerology, CancellationToken ct)
    {
        try
        {
            var digest = new StringBuilder();
            foreach (var r in rooms)
            {
                digest.AppendLine($"{r.RoomType} (score {r.Score}): " +
                    $"strengths: {string.Join("; ", r.Adhering.Select(a => a.Principle))}. " +
                    $"violations: {string.Join("; ", r.Violations.Select(v => $"{v.Principle} ({v.Severity})"))}.");
            }
            digest.AppendLine($"Site (score {site.Score}): " +
                $"strengths: {string.Join("; ", site.Adhering.Select(a => a.Principle))}. " +
                $"violations: {string.Join("; ", site.Violations.Select(v => $"{v.Principle} ({v.Severity})"))}.");
            digest.AppendLine($"Numerology adjustment {numerology.ScoreAdjustment}: " +
                string.Join("; ", numerology.Checks.Select(c => $"{c.Subject} {c.Value} {c.Verdict} ({c.Tradition})")));

            var resp = await claude.MessagesAsync(new
            {
                model = claude.Model,
                max_tokens = 300,
                messages = new object[] { new { role = "user", content = Prompts.SummaryPrompt(digest.ToString()) } },
            }, ct);
            var text = resp.GetProperty("content").EnumerateArray()
                .FirstOrDefault(c => c.GetProperty("type").GetString() == "text");
            var s = text.ValueKind == JsonValueKind.Object ? text.GetProperty("text").GetString() : null;
            return string.IsNullOrWhiteSpace(s)
                ? ScoreMath.LocalSummary(rooms, site, numerology) : s.Trim();
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Summary call failed; using local summary");
            return ScoreMath.LocalSummary(rooms, site, numerology);
        }
    }

    // Live-mode polish of the deterministic site templates (SPEC §5.1). Any failure → input unchanged.
    public async Task<SiteAnalysis> RephraseSiteAsync(SiteAnalysis site, CancellationToken ct)
    {
        if (site.Adhering.Count + site.Violations.Count == 0) return site;
        try
        {
            var lines = site.Adhering.Select(a => a.Observation)
                .Concat(site.Violations.Select(v => v.Observation)).ToList();
            var resp = await claude.MessagesAsync(new
            {
                model = claude.Model,
                max_tokens = 1024,
                messages = new object[]
                {
                    new
                    {
                        role = "user",
                        content = "Rewrite each numbered observation below in one warm, concrete sentence for a renter. " +
                                  "Keep the meaning exactly; keep cultural framing. Return the same count of lines, numbered.\n" +
                                  string.Join("\n", lines.Select((l, i) => $"{i + 1}. {l}")),
                    },
                },
            }, ct);
            var text = resp.GetProperty("content").EnumerateArray()
                .First(c => c.GetProperty("type").GetString() == "text").GetProperty("text").GetString() ?? "";
            var rewritten = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => System.Text.RegularExpressions.Regex.Replace(l.Trim(), @"^\d+[.)]\s*", ""))
                .ToList();
            if (rewritten.Count != lines.Count) return site;
            var a = site.Adhering.Select((f, i) => f with { Observation = rewritten[i] }).ToList();
            var v = site.Violations.Select((f, i) => f with { Observation = rewritten[site.Adhering.Count + i] }).ToList();
            return site with { Adhering = a, Violations = v };
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Site rephrase failed; keeping template phrasing");
            return site;
        }
    }
}
```

- [ ] **Step 2: Write `Controllers/AnalysisController.cs`**

```csharp
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HarmonIQ.Api.Controllers;

[ApiController]
public class AnalysisController(
    IListingService listings, IClaudeClient claude, ClaudeAnalysisService live,
    MockAnalysisService mock, SiteAnalysisService siteSvc, NumerologyService numerologySvc,
    ILogger<AnalysisController> log) : ControllerBase
{
    private static readonly string[] ValidSystems = ["both", "fengshui", "vastu"];
    private static readonly string[] ValidOrientations =
        ["unknown", "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest"];

    [HttpPost("/api/analyze")]
    public async Task<IActionResult> Analyze([FromBody] AnalyzeRequest req, CancellationToken ct)
    {
        // --- Validation (FR-34) ---
        if (string.IsNullOrWhiteSpace(req.ListingId))
            return BadRequest(new { error = "listingId is required." });
        if (req.Photos is null || req.Photos.Count == 0)
            return BadRequest(new { error = "Select at least one photo to analyze." });
        if (req.Photos.Count > 6)
            return BadRequest(new { error = "At most 6 photos can be analyzed per report." });
        var systems = string.IsNullOrEmpty(req.Systems) ? "both" : req.Systems;
        if (!ValidSystems.Contains(systems))
            return BadRequest(new { error = $"systems must be one of: {string.Join(", ", ValidSystems)}." });
        var orientation = string.IsNullOrEmpty(req.Orientation) ? "unknown" : req.Orientation;
        if (!ValidOrientations.Contains(orientation))
            return BadRequest(new { error = "orientation is not a recognized compass direction." });

        ListingResponse listing;
        try { listing = await listings.GetListingAsync(req.ListingId, ct); }
        catch (ListingNotFoundException e) { return BadRequest(new { error = e.Message }); }
        catch (ListingSourceException e) { return StatusCode(502, new { error = e.Message }); }

        var known = listing.Photos.Select(p => p.PhotoId).ToHashSet();
        var unknown = req.Photos.FirstOrDefault(p => !known.Contains(p.PhotoId));
        if (unknown is not null)
            return BadRequest(new { error = $"Unknown photoId '{unknown.PhotoId}'." });

        // --- Deterministic lenses run concurrently with room analysis (NFR-1) ---
        var numbers = req.Numbers ?? listing.Numbers;
        var environment = req.Environment ?? listing.Environment;
        var numerology = numerologySvc.Evaluate(numbers, systems);
        var site = siteSvc.Analyze(environment, orientation, systems);

        // --- Room photos ---
        List<RoomAnalysis> rooms;
        string mode;
        string? modelId = null;
        string? notice = null;
        if (claude.IsConfigured)
        {
            try
            {
                var inputs = new List<RoomInput>();
                foreach (var p in req.Photos)
                {
                    var bytes = await listings.GetPhotoAsync(req.ListingId, p.PhotoId, null, ct);
                    if (bytes is null)
                        return StatusCode(502, new { error = $"Photo '{p.PhotoId}' could not be fetched from the listing source." });
                    inputs.Add(new RoomInput(p.PhotoId, p.RoomType, bytes.Data));
                }
                var siteTask = live.RephraseSiteAsync(site, ct);
                rooms = await live.AnalyzeRoomsAsync(inputs, systems, orientation, ct);
                site = await siteTask;
                mode = "live";
                modelId = claude.Model;
            }
            catch (ClaudeUnavailableException e)
            {
                log.LogWarning(e, "Claude unavailable; serving demo analysis");
                rooms = mock.AnalyzeRooms(req.Photos, systems);
                mode = "demo";
                notice = "The Claude endpoint was unavailable, so this is a built-in demonstration analysis.";
            }
        }
        else
        {
            rooms = mock.AnalyzeRooms(req.Photos, systems);
            mode = "demo";
            notice = "No Claude API key is configured, so this is a built-in demonstration analysis.";
        }

        // --- Merge (FR-25) ---
        var overall = ScoreMath.Overall(rooms, site, numerology.ScoreAdjustment);
        var summary = mode == "live"
            ? await live.SummarizeAsync(rooms, site, numerology, ct)
            : ScoreMath.LocalSummary(rooms, site, numerology);

        return Ok(new AnalyzeResponse(
            mode, modelId, notice,
            new ListingSummary(listing.ListingId, listing.Title, listing.Address, listing.Url),
            new AnalysisResult(
                overall, ScoreMath.Grade(overall), summary,
                ScoreMath.AverageElements(rooms), rooms, site, numerology)));
    }
}
```

- [ ] **Step 3: Register in DI** — under `// DI-REGISTRATIONS`:

```csharp
builder.Services.AddSingleton<HarmonIQ.Api.Services.ClaudeAnalysisService>();
```

- [ ] **Step 4: Verify demo mode (no key)**

```bash
mv .env .env.bak 2>/dev/null; dotnet run --project backend/HarmonIQ.Api &
sleep 5
curl -s -X POST http://localhost:5080/api/analyze -H 'Content-Type: application/json' -d '{
  "listingId": "sample",
  "photos": [{ "photoId": "p1", "roomType": "Bedroom" }, { "photoId": "p2", "roomType": "Living Room" }],
  "systems": "both", "orientation": "unknown",
  "numbers": { "unitNumber": "414", "floor": 4, "streetNumber": "123" }
}' | python3 -m json.tool | head -40
```
Expected: `"mode": "demo"`, a `notice`, grade + overallScore present, bedroom violations include "Mirror Facing the Bed", numerology flags 414/4 as unlucky. Environment defaults come from the fixture (request omitted `environment`) → site violations include the T-junction sha chi.

Also verify the error paths:

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5080/api/analyze -H 'Content-Type: application/json' -d '{"listingId":"sample","photos":[]}'          # expect 400
curl -s -X POST http://localhost:5080/api/analyze -H 'Content-Type: application/json' -d '{"listingId":"sample","photos":[{"photoId":"bogus"}]}' # expect 400 + unknown photoId message
kill %1
```

- [ ] **Step 5: Verify live mode (with the event key)**

```bash
mv .env.bak .env; dotnet run --project backend/HarmonIQ.Api &
sleep 5
time curl -s -X POST http://localhost:5080/api/analyze -H 'Content-Type: application/json' -d '{
  "listingId": "sample",
  "photos": [{ "photoId": "p1", "roomType": "Bedroom" }],
  "systems": "both", "orientation": "north"
}' | python3 -m json.tool | head -60
kill %1
```
Expected: `"mode": "live"`, `"modelId": "claude-sonnet-5"`, findings that reference the labeled objects in the schematic bedroom (mirror, bed/door line), completes well under 25 s.

- [ ] **Step 6: Run the full test suite, then commit**

```bash
dotnet test backend/HarmonIQ.Tests
git add backend/HarmonIQ.Api
git commit -m "feat: full analyze endpoint — Claude room fan-out, demo fallback, merged scoring"
```

---
### Task 12: Frontend scaffold — web component shell, tokens, brand themes

**Files:**
- Create: `frontend/package.json`, `frontend/tsconfig.json`, `frontend/vite.config.ts`, `frontend/index.html`, `frontend/src/vite-env.d.ts`
- Create: `frontend/src/main.ts`, `frontend/src/base.ts`, `frontend/src/Module.tsx` (placeholder body this task; real one in Task 13)
- Create: `frontend/src/styles/tokens.css`, `frontend/src/styles/base.css`, `frontend/src/styles/themes.ts`

**Interfaces:**
- Consumes: nothing from the backend yet.
- Produces (used by Tasks 13–17):
  - Custom element `<harmoniq-module listing-id="…" brand="apartments|apartmentfinder|forrent" state="badge|expanded" api-base="…">` with shadow DOM (NFR-9); brand attribute swaps a `<style>` of `:host` token overrides **without remounting React** (Acceptance 5). `api-base` is optional: the default comes from the embed script's origin (`base.ts`), so cross-origin hosts need only the script tag (FR-1).
  - `base.ts` exports `apiUrl(path)` — **every** server path in later tasks (fetches in `api.ts`, `<img>` thumbnail `src`es, the `/harmoniq` attribution href) must go through it.
  - `npm run build --prefix frontend` emits `backend/HarmonIQ.Api/wwwroot/embed/harmoniq-module.js` (single iife file, CSS inlined).
  - `Module` React component receives `{ listingId, brand, initialState }`.
  - Design tokens (used by all component CSS): `--hiq-primary, --hiq-primary-contrast, --hiq-accent, --hiq-surface, --hiq-surface-2, --hiq-text, --hiq-muted, --hiq-border, --hiq-good, --hiq-warn, --hiq-bad, --hiq-radius, --hiq-font-display, --hiq-font-body`.

- [ ] **Step 1: Scaffold**

```bash
mkdir -p frontend/src/{components,styles}
```

`frontend/package.json`:
```json
{
  "name": "harmoniq-module",
  "private": true,
  "type": "module",
  "scripts": { "dev": "vite", "build": "tsc --noEmit && vite build" },
  "dependencies": { "react": "^18.3.0", "react-dom": "^18.3.0" },
  "devDependencies": {
    "@types/react": "^18.3.0", "@types/react-dom": "^18.3.0",
    "@vitejs/plugin-react": "^4.3.0", "typescript": "^5.6.0", "vite": "^5.4.0"
  }
}
```

`frontend/tsconfig.json`:
```json
{
  "compilerOptions": {
    "target": "ES2020", "lib": ["ES2020", "DOM", "DOM.Iterable"], "module": "ESNext",
    "moduleResolution": "bundler", "jsx": "react-jsx", "strict": true, "esModuleInterop": true,
    "noUnusedLocals": true, "skipLibCheck": true, "isolatedModules": true, "noEmit": true
  },
  "include": ["src"]
}
```

`frontend/vite.config.ts`:
```ts
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  define: { 'process.env.NODE_ENV': JSON.stringify('production') },
  build: {
    lib: { entry: 'src/main.ts', formats: ['iife'], name: 'HarmonIQModule', fileName: () => 'harmoniq-module.js' },
    outDir: '../backend/HarmonIQ.Api/wwwroot/embed',
    emptyOutDir: true,
  },
  server: { proxy: { '/api': 'http://localhost:5080' } },
});
```

`frontend/index.html` (dev harness only — not shipped):
```html
<!doctype html>
<html><head><meta charset="utf-8"><title>HarmonIQ dev</title></head>
<body style="background:#f0f0f0;padding:40px;font-family:sans-serif">
  <h3>Dev harness (badge)</h3>
  <harmoniq-module listing-id="sample" brand="apartments"></harmoniq-module>
  <h3>Expanded</h3>
  <harmoniq-module listing-id="sample" brand="forrent" state="expanded"></harmoniq-module>
  <script type="module" src="/src/main.ts"></script>
</body></html>
```

`frontend/src/vite-env.d.ts` (TS strict needs the `?inline` import typed):
```ts
/// <reference types="vite/client" />
declare module '*.css?inline' {
  const css: string;
  export default css;
}
```

- [ ] **Step 2: Write `src/styles/tokens.css`** (defaults; brands override):

```css
:host {
  --hiq-primary: #4a7c2f;
  --hiq-primary-contrast: #ffffff;
  --hiq-accent: #b98a2f;
  --hiq-surface: #ffffff;
  --hiq-surface-2: #f6f4ef;
  --hiq-text: #22271f;
  --hiq-muted: #6b7263;
  --hiq-border: #dfdcd2;
  --hiq-good: #2e8b57;
  --hiq-warn: #d99a2b;
  --hiq-bad: #c0392b;
  --hiq-radius: 12px;
  --hiq-font-display: Georgia, 'Times New Roman', serif;
  --hiq-font-body: -apple-system, 'Segoe UI', Roboto, sans-serif;
  all: initial;
  display: block;
  font-family: var(--hiq-font-body);
  color: var(--hiq-text);
}
```

Note: `all: initial` must come **before** re-declaring `display`/`font-family`/`color` but after the custom properties would be reset by it — custom properties set in the same rule are not affected by `all: initial` ordering in practice, but to be safe put `all: initial;` as the **first** declaration in the rule. Final rule order: `all: initial;` first line, then all `--hiq-*` variables, then `display/font/color`.

- [ ] **Step 3: Write `src/styles/themes.ts`** (brand presets, FR-4):

```ts
export const themes: Record<string, string> = {
  apartments: `:host {
    --hiq-primary: #5f8f22; --hiq-primary-contrast: #fff; --hiq-accent: #2b4f12;
    --hiq-radius: 10px;
    --hiq-font-display: 'Helvetica Neue', Arial, sans-serif;
    --hiq-font-body: 'Helvetica Neue', Arial, sans-serif;
  }`,
  apartmentfinder: `:host {
    --hiq-primary: #0b6bb1; --hiq-primary-contrast: #fff; --hiq-accent: #f28c00;
    --hiq-radius: 6px;
    --hiq-font-display: Verdana, Geneva, sans-serif;
    --hiq-font-body: Verdana, Geneva, sans-serif;
  }`,
  forrent: `:host {
    --hiq-primary: #8a1e7c; --hiq-primary-contrast: #fff; --hiq-accent: #e84393;
    --hiq-radius: 18px;
    --hiq-font-display: Georgia, serif;
    --hiq-font-body: 'Trebuchet MS', Tahoma, sans-serif;
  }`,
};
export const defaultBrand = 'apartments';
```

- [ ] **Step 4: Write `src/styles/base.css`** (all component classes, token-driven — used by Tasks 13–16):

```css
.hiq-root { max-width: 860px; }

/* Badge — a host-style score card (FR-3): title + tagline left, grade + score right */
.hiq-badge {
  display: flex; justify-content: space-between; align-items: center; gap: 16px; cursor: pointer;
  width: 100%; background: var(--hiq-surface); border: 1px solid var(--hiq-border);
  border-radius: var(--hiq-radius); padding: 12px 16px;
  font-family: var(--hiq-font-body); box-shadow: 0 1px 4px rgba(0,0,0,.08);
}
.hiq-badge:hover { box-shadow: 0 2px 8px rgba(0,0,0,.15); }
.hiq-badge-info { display: flex; flex-direction: column; align-items: flex-start; text-align: left; gap: 2px; }
.hiq-badge-logo { font-family: var(--hiq-font-display); font-weight: 700; color: var(--hiq-primary); font-size: 14px; }
.hiq-badge-tagline { color: var(--hiq-muted); font-size: 12px; }
.hiq-badge-value { display: inline-flex; align-items: baseline; gap: 6px; }
.hiq-badge-grade { font-size: 20px; font-weight: 800; }
.hiq-badge-score { color: var(--hiq-muted); font-size: 13px; }
.hiq-badge-error { color: var(--hiq-muted); font-size: 12px; font-style: italic; }
.hiq-attribution { margin-top: 6px; font-size: 12px; color: var(--hiq-muted); }
.hiq-attribution a { color: var(--hiq-primary); text-decoration: underline; cursor: pointer; }
.hiq-spinner {
  width: 16px; height: 16px; border-radius: 50%;
  border: 2px solid var(--hiq-border); border-top-color: var(--hiq-primary);
  animation: hiq-spin 0.9s linear infinite;
}
@keyframes hiq-spin { to { transform: rotate(360deg); } }

/* Panel */
.hiq-panel {
  margin-top: 12px; background: var(--hiq-surface); border: 1px solid var(--hiq-border);
  border-radius: var(--hiq-radius); padding: 20px; box-shadow: 0 2px 10px rgba(0,0,0,.08);
}
.hiq-panel-head { display: flex; gap: 24px; align-items: center; flex-wrap: wrap; }
.hiq-panel-title { font-family: var(--hiq-font-display); font-size: 22px; margin: 0 0 4px; color: var(--hiq-primary); }
.hiq-summary { font-size: 14px; line-height: 1.5; color: var(--hiq-text); max-width: 480px; }
.hiq-banner {
  background: var(--hiq-surface-2); border: 1px dashed var(--hiq-border); border-radius: var(--hiq-radius);
  padding: 10px 14px; font-size: 13px; color: var(--hiq-muted); margin: 12px 0;
}
.hiq-pill {
  display: inline-flex; align-items: center; gap: 6px; font-size: 11px; letter-spacing: .4px;
  background: var(--hiq-surface-2); color: var(--hiq-muted); border: 1px solid var(--hiq-border);
  border-radius: 999px; padding: 3px 10px; text-transform: uppercase;
}
.hiq-pill-dot { width: 7px; height: 7px; border-radius: 50%; background: var(--hiq-good); }
.hiq-pill-dot--demo { background: var(--hiq-warn); }

/* Gauge + elements */
.hiq-gauge-num { font-family: var(--hiq-font-display); }
.hiq-elements { flex: 1; min-width: 220px; }
.hiq-el-row { display: flex; align-items: center; gap: 8px; margin: 4px 0; font-size: 12px; }
.hiq-el-label { width: 46px; color: var(--hiq-muted); text-transform: capitalize; }
.hiq-el-track { flex: 1; height: 8px; background: var(--hiq-surface-2); border-radius: 4px; overflow: hidden; }
.hiq-el-fill { height: 100%; border-radius: 4px; transition: width .6s ease; }
.hiq-el-val { width: 28px; text-align: right; color: var(--hiq-muted); }

/* Cards */
.hiq-card { border: 1px solid var(--hiq-border); border-radius: var(--hiq-radius); padding: 16px; margin-top: 16px; }
.hiq-card-head { display: flex; align-items: center; gap: 12px; margin-bottom: 10px; }
.hiq-card-title { font-family: var(--hiq-font-display); font-size: 16px; margin: 0; flex: 1; }
.hiq-thumb { width: 84px; height: 63px; object-fit: cover; border-radius: calc(var(--hiq-radius) / 2); }
.hiq-chip { font-weight: 800; font-size: 14px; border-radius: 999px; padding: 4px 12px; color: #fff; }
.hiq-cols { display: grid; grid-template-columns: 1fr 1fr; gap: 14px; }
@media (max-width: 640px) { .hiq-cols { grid-template-columns: 1fr; } }
.hiq-col-title { font-size: 12px; font-weight: 700; text-transform: uppercase; letter-spacing: .5px; margin-bottom: 6px; }
.hiq-col-title--good { color: var(--hiq-good); }
.hiq-col-title--bad { color: var(--hiq-bad); }
.hiq-finding { font-size: 13px; line-height: 1.45; margin-bottom: 8px; }
.hiq-finding b { display: block; }
.hiq-tag { display: inline-block; font-size: 10px; border-radius: 4px; padding: 1px 6px; margin-left: 6px; vertical-align: 1px; }
.hiq-tag--minor { background: #f4e9c8; color: #7a5d10; }
.hiq-tag--moderate { background: #f8d9b0; color: #8a4b09; }
.hiq-tag--major { background: #f5c6c0; color: #8a1508; }
.hiq-tag--sys { background: var(--hiq-surface-2); color: var(--hiq-muted); }

/* Suggestions */
.hiq-sugs { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-top: 12px; }
@media (max-width: 640px) { .hiq-sugs { grid-template-columns: 1fr; } }
.hiq-sug { background: var(--hiq-surface-2); border-radius: calc(var(--hiq-radius) / 1.5); padding: 10px 12px; font-size: 13px; }
.hiq-sug b { display: block; margin-bottom: 2px; }
.hiq-sug-tags { margin-top: 6px; display: flex; gap: 6px; font-size: 10px; color: var(--hiq-muted); }

/* Compass */
.hiq-compass { flex-shrink: 0; }
.hiq-compass text { font-family: var(--hiq-font-body); }

/* Buttons + drawer */
.hiq-btn {
  cursor: pointer; border: none; border-radius: var(--hiq-radius);
  background: var(--hiq-primary); color: var(--hiq-primary-contrast);
  font-family: var(--hiq-font-body); font-size: 13px; font-weight: 600; padding: 8px 16px;
}
.hiq-btn--ghost { background: var(--hiq-surface-2); color: var(--hiq-text); border: 1px solid var(--hiq-border); }
.hiq-drawer { border: 1px solid var(--hiq-border); border-radius: var(--hiq-radius); padding: 16px; margin-top: 16px; background: var(--hiq-surface-2); }
.hiq-drawer h4 { margin: 14px 0 6px; font-size: 13px; text-transform: uppercase; letter-spacing: .5px; color: var(--hiq-muted); }
.hiq-drawer select, .hiq-drawer input {
  font-family: var(--hiq-font-body); font-size: 13px; padding: 4px 6px;
  border: 1px solid var(--hiq-border); border-radius: 6px; background: var(--hiq-surface);
}
.hiq-photo-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(140px, 1fr)); gap: 10px; }
.hiq-photo-cell { background: var(--hiq-surface); border: 1px solid var(--hiq-border); border-radius: 8px; padding: 6px; font-size: 12px; }
.hiq-photo-cell img { width: 100%; height: 80px; object-fit: cover; border-radius: 6px; }
.hiq-env-grid { display: grid; grid-template-columns: repeat(2, 1fr); gap: 10px; }
.hiq-env-side { background: var(--hiq-surface); border: 1px solid var(--hiq-border); border-radius: 8px; padding: 8px; font-size: 12px; }
.hiq-env-side label { display: flex; justify-content: space-between; gap: 6px; margin: 4px 0; align-items: center; }
.hiq-row { display: flex; gap: 10px; align-items: center; flex-wrap: wrap; }
.hiq-seg { display: inline-flex; border: 1px solid var(--hiq-border); border-radius: 999px; overflow: hidden; }
.hiq-seg button { border: none; background: var(--hiq-surface); padding: 6px 14px; font-size: 12px; cursor: pointer; }
.hiq-seg button.on { background: var(--hiq-primary); color: var(--hiq-primary-contrast); }
```

- [ ] **Step 5: Write `src/Module.tsx`** placeholder (replaced in Task 13):

```tsx
export interface ModuleProps {
  listingId: string;
  brand: string;
  initialState: 'badge' | 'expanded';
}

export function Module({ listingId }: ModuleProps) {
  return <div className="hiq-root"><div className="hiq-badge"><span className="hiq-badge-logo">HarmonIQ</span><span className="hiq-badge-score">{listingId}</span></div></div>;
}
```

- [ ] **Step 6: Write `src/base.ts` and `src/main.ts`**

`src/base.ts` — API base resolution (FR-1). Same-origin hosts get `''` (relative paths, unchanged behavior); cross-origin hosts (the real apartments-web LDP loading the bundle from :5080) automatically get the script's origin; an explicit `api-base` attribute wins:

```ts
// Captured at bundle load: where did this script come from?
const src = (document.currentScript as HTMLScriptElement | null)?.src;
export const scriptOrigin =
  src && new URL(src).origin !== location.origin ? new URL(src).origin : '';

let apiBase = scriptOrigin;
export function setApiBase(base: string | null) {
  apiBase = (base ?? scriptOrigin).replace(/\/$/, '');
}
/** Prefix a server path (API call, thumbnail URL, /harmoniq) with the API base. */
export function apiUrl(path: string): string {
  return apiBase + path;
}
```

`src/main.ts` (custom element registration):

```ts
import React from 'react';
import { createRoot, Root } from 'react-dom/client';
import { Module } from './Module';
import { setApiBase } from './base';
import { themes, defaultBrand } from './styles/themes';
import tokensCss from './styles/tokens.css?inline';
import baseCss from './styles/base.css?inline';

class HarmonIQModuleElement extends HTMLElement {
  static observedAttributes = ['listing-id', 'brand', 'state', 'api-base'];
  private root?: Root;
  private themeStyle?: HTMLStyleElement;

  connectedCallback() {
    const shadow = this.shadowRoot ?? this.attachShadow({ mode: 'open' });
    if (!this.root) {
      const style = document.createElement('style');
      style.textContent = tokensCss + '\n' + baseCss;
      shadow.appendChild(style);
      this.themeStyle = document.createElement('style');
      shadow.appendChild(this.themeStyle);
      const mount = document.createElement('div');
      shadow.appendChild(mount);
      this.root = createRoot(mount);
    }
    this.sync();
  }

  attributeChangedCallback() {
    if (this.root) this.sync();
  }

  disconnectedCallback() {
    // Defer unmount so brand-switcher DOM moves don't tear down state.
    queueMicrotask(() => {
      if (!this.isConnected) { this.root?.unmount(); this.root = undefined; }
    });
  }

  private sync() {
    setApiBase(this.getAttribute('api-base'));
    const brand = this.getAttribute('brand') ?? defaultBrand;
    this.themeStyle!.textContent = themes[brand] ?? themes[defaultBrand];
    this.root!.render(
      React.createElement(Module, {
        listingId: this.getAttribute('listing-id') ?? '',
        brand,
        initialState: (this.getAttribute('state') as 'badge' | 'expanded') ?? 'badge',
      }),
    );
  }
}

if (!customElements.get('harmoniq-module')) {
  customElements.define('harmoniq-module', HarmonIQModuleElement);
}
```

- [ ] **Step 7: Install, build, verify**

```bash
npm install --prefix frontend
npm run build --prefix frontend
ls -la backend/HarmonIQ.Api/wwwroot/embed/
```
Expected: `harmoniq-module.js` (single file, ~150–200 KB with React inlined; a stray `style.css` must NOT exist — CSS rides in via `?inline`). Then a browser smoke check:

```bash
npm run dev --prefix frontend
```
Open http://localhost:5173 — two placeholder badges render, each styled by its brand (green sans vs purple serif), with page styles not leaking in (inspect: content inside `#shadow-root`).

- [ ] **Step 8: Commit**

```bash
git add frontend
git commit -m "feat: harmoniq-module web component shell with shadow DOM and brand token themes"
```

---

### Task 13: `api.ts`, state machine hook, and the badge (auto-analyze flow)

**Files:**
- Create: `frontend/src/api.ts`, `frontend/src/useHarmonIQ.ts`, `frontend/src/components/HarmonIQBadge.tsx`
- Modify: `frontend/src/Module.tsx` (real implementation)

**Interfaces:**
- Consumes: backend endpoints (Tasks 6, 11); tokens/classes (Task 12).
- Produces (used by Tasks 14–16):
  - `api.ts`: TS mirrors of every DTO (names below are canonical for all later tasks) + `fetchListing(listingId, brand)` and `postAnalyze(req)` throwing `ApiError(status, message)`.
  - `useHarmonIQ(listingId, brand)` returns `{ phase, listing, report, error, start, refine }` where `phase: 'idle' | 'fetching-listing' | 'analyzing' | 'report' | 'error'`; `refine(r: Refinement)` re-enters `analyzing` (FR-11); `Refinement = { photos: PhotoSelection[]; systems: Systems; orientation: string; environment: ListingEnvironment; numbers: ListingNumbers }`.
  - `Module` auto-starts analysis when the element scrolls into view (FR-3) and toggles `expanded` on badge click.
  - `HarmonIQBadge({ phase, report, error, expanded, onToggle })`.

- [ ] **Step 1: Write `src/api.ts`**

```ts
export type Systems = 'both' | 'fengshui' | 'vastu';
export type Severity = 'minor' | 'moderate' | 'major';
export type Level = 'low' | 'medium' | 'high';

export interface ListingPhoto {
  photoId: string; thumbnailUrl: string; caption: string | null;
  interior: boolean; selected: boolean; suggestedRoomType: string | null;
}
export interface SideEnvironment { road: string; water: string; structures: string; slope: string; }
export interface ListingEnvironment {
  north: SideEnvironment; east: SideEnvironment; south: SideEnvironment; west: SideEnvironment;
}
export interface ListingNumbers { unitNumber: string | null; floor: number | null; streetNumber: string | null; }
export interface Listing {
  listingId: string; title: string; address: string; url: string;
  photos: ListingPhoto[]; numbers: ListingNumbers; environment: ListingEnvironment;
}

export interface PhotoSelection { photoId: string; roomType: string | null; }
export interface AnalyzeRequest {
  listingId: string; photos: PhotoSelection[]; systems: Systems; orientation: string;
  environment: ListingEnvironment | null; numbers: ListingNumbers | null; brand: string | null;
}

export interface Finding { principle: string; observation: string; system: string; }
export interface ViolationFinding extends Finding { severity: Severity; }
export interface Suggestion { title: string; detail: string; effort: Level; impact: Level; }
export interface ElementBalance { wood: number; fire: number; earth: number; metal: number; water: number; }
export interface RoomAnalysis {
  photoId: string; roomType: string; score: number; elementBalance: ElementBalance;
  adhering: Finding[]; violations: ViolationFinding[]; suggestions: Suggestion[];
}
export interface SiteAnalysis {
  score: number; adhering: Finding[]; violations: ViolationFinding[]; suggestions: Suggestion[];
}
export interface NumerologyCheck {
  subject: string; value: string; verdict: 'lucky' | 'neutral' | 'unlucky';
  tradition: string; reason: string; remedy: string | null;
}
export interface NumerologyResult { scoreAdjustment: number; checks: NumerologyCheck[]; }
export interface AnalysisResult {
  overallScore: number; grade: string; summary: string; elementBalance: ElementBalance;
  rooms: RoomAnalysis[]; site: SiteAnalysis; numerology: NumerologyResult;
}
export interface AnalyzeResponse {
  mode: 'live' | 'demo'; modelId?: string; notice?: string;
  listing: { listingId: string; title: string; address: string; url: string };
  analysis: AnalysisResult;
}

export class ApiError extends Error {
  constructor(public status: number, message: string) { super(message); }
}

async function handle<T>(resp: Response): Promise<T> {
  if (!resp.ok) {
    let message = `Request failed (${resp.status})`;
    try { message = (await resp.json()).error ?? message; } catch { /* keep default */ }
    throw new ApiError(resp.status, message);
  }
  return resp.json() as Promise<T>;
}

export function fetchListing(listingId: string, brand: string): Promise<Listing> {
  return fetch(apiUrl(`/api/listing/${encodeURIComponent(listingId)}?brand=${encodeURIComponent(brand)}`))
    .then(r => handle<Listing>(r));
}

export function postAnalyze(req: AnalyzeRequest): Promise<AnalyzeResponse> {
  return fetch(apiUrl('/api/analyze'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(req),
  }).then(r => handle<AnalyzeResponse>(r));
}
```

Add `import { apiUrl } from './base';` at the top — cross-origin hosts resolve against the HarmonIQ origin (FR-1).

- [ ] **Step 2: Write `src/useHarmonIQ.ts`**

```ts
import { useCallback, useRef, useState } from 'react';
import {
  AnalyzeResponse, Listing, ListingEnvironment, ListingNumbers,
  PhotoSelection, Systems, fetchListing, postAnalyze,
} from './api';

export type Phase = 'idle' | 'fetching-listing' | 'analyzing' | 'report' | 'error';

export interface Refinement {
  photos: PhotoSelection[];
  systems: Systems;
  orientation: string;
  environment: ListingEnvironment;
  numbers: ListingNumbers;
}

export function defaultRefinement(listing: Listing): Refinement {
  return {
    photos: listing.photos.filter(p => p.selected)
      .map(p => ({ photoId: p.photoId, roomType: p.suggestedRoomType })),
    systems: 'both',
    orientation: 'unknown',
    environment: listing.environment,
    numbers: listing.numbers,
  };
}

export function useHarmonIQ(listingId: string, brand: string) {
  const [phase, setPhase] = useState<Phase>('idle');
  const [listing, setListing] = useState<Listing | null>(null);
  const [report, setReport] = useState<AnalyzeResponse | null>(null);
  const [refinement, setRefinement] = useState<Refinement | null>(null);
  const [error, setError] = useState<string | null>(null);
  const started = useRef(false);

  const runAnalyze = useCallback(async (id: string, r: Refinement) => {
    setPhase('analyzing');
    try {
      const resp = await postAnalyze({
        listingId: id, photos: r.photos, systems: r.systems, orientation: r.orientation,
        environment: r.environment, numbers: r.numbers, brand,
      });
      setReport(resp);
      setPhase('report');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Analysis failed');
      setPhase('error');
    }
  }, [brand]);

  const start = useCallback(async () => {
    if (started.current || !listingId) return;
    started.current = true;
    setPhase('fetching-listing');
    try {
      const l = await fetchListing(listingId, brand);
      setListing(l);
      const r = defaultRefinement(l);
      setRefinement(r);
      await runAnalyze(l.listingId, r);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Listing unavailable');
      setPhase('error');
    }
  }, [listingId, brand, runAnalyze]);

  const refine = useCallback((r: Refinement) => {
    if (!listing) return;
    setRefinement(r);
    void runAnalyze(listing.listingId, r);
  }, [listing, runAnalyze]);

  return { phase, listing, report, refinement, error, start, refine };
}
```

- [ ] **Step 3: Write `src/components/HarmonIQBadge.tsx`** — a host-style score card (title "HarmonIQ Score", grade tagline, score `/ 100`) so it reads as a native listing score next to the LDP's "Getting Around" cards, followed by the "Data provided by HarmonIQ" attribution link to `/harmoniq` (FR-3):

```tsx
import { AnalyzeResponse } from '../api';
import { apiUrl } from '../base';
import { Phase } from '../useHarmonIQ';

export interface BadgeProps {
  phase: Phase;
  report: AnalyzeResponse | null;
  error: string | null;
  expanded: boolean;
  onToggle: () => void;
}

function Attribution() {
  return (
    <div className="hiq-attribution">
      Data provided by <a href={apiUrl('/harmoniq')} target="_blank" rel="noopener">HarmonIQ</a>
    </div>
  );
}

export function HarmonIQBadge({ phase, report, error, expanded, onToggle }: BadgeProps) {
  if (phase === 'error') {
    return (
      <div className="hiq-badge" role="status">
        <span className="hiq-badge-logo">HarmonIQ Score</span>
        <span className="hiq-badge-error">Score unavailable</span>
      </div>
    );
  }
  const loading = phase === 'idle' || phase === 'fetching-listing' ||
    (phase === 'analyzing' && !report);
  const a = report?.analysis;
  const color = !a ? 'var(--hiq-muted)'
    : a.overallScore >= 75 ? 'var(--hiq-good)'
    : a.overallScore >= 55 ? 'var(--hiq-warn)' : 'var(--hiq-bad)';
  return (
    <>
      <button className="hiq-badge" onClick={onToggle}
        aria-expanded={expanded} title="HarmonIQ harmony score" type="button"
        style={{ font: 'inherit' }}>
        <span className="hiq-badge-info">
          <span className="hiq-badge-logo">HarmonIQ Score</span>
          <span className="hiq-badge-tagline">Feng Shui &amp; Vastu harmony</span>
        </span>
        {loading ? (
          <span className="hiq-badge-value">
            <span className="hiq-spinner" aria-hidden="true" />
            <span className="hiq-badge-score">reading the energy…</span>
          </span>
        ) : (
          <span className="hiq-badge-value">
            <span className="hiq-badge-grade" style={{ color }}>{a!.grade}</span>
            <span className="hiq-badge-score">{a!.overallScore} / 100</span>
          </span>
        )}
      </button>
      <Attribution />
    </>
  );
}
```

- [ ] **Step 4: Replace `src/Module.tsx`** with the real component (ReportPanel arrives in Task 14 — reference it now; create a stub file so this task compiles):

```tsx
import { useEffect, useRef, useState } from 'react';
import { HarmonIQBadge } from './components/HarmonIQBadge';
import { ReportPanel } from './components/ReportPanel';
import { useHarmonIQ } from './useHarmonIQ';

export interface ModuleProps {
  listingId: string;
  brand: string;
  initialState: 'badge' | 'expanded';
}

export function Module({ listingId, brand, initialState }: ModuleProps) {
  const { phase, listing, report, refinement, error, start, refine } = useHarmonIQ(listingId, brand);
  const [expanded, setExpanded] = useState(initialState === 'expanded');
  const rootRef = useRef<HTMLDivElement>(null);

  // FR-3: analysis starts automatically when the badge first becomes visible.
  useEffect(() => {
    const el = rootRef.current;
    if (!el) return;
    const io = new IntersectionObserver((entries) => {
      if (entries.some(e => e.isIntersecting)) { void start(); io.disconnect(); }
    }, { threshold: 0.1 });
    io.observe(el);
    return () => io.disconnect();
  }, [start]);

  return (
    <div className="hiq-root" ref={rootRef}>
      <HarmonIQBadge phase={phase} report={report} error={error}
        expanded={expanded} onToggle={() => setExpanded(e => !e)} />
      {expanded && phase !== 'error' && (
        <ReportPanel phase={phase} listing={listing} report={report}
          refinement={refinement} onRefine={refine} />
      )}
    </div>
  );
}
```

Stub `src/components/ReportPanel.tsx` for now:

```tsx
import { AnalyzeResponse, Listing } from '../api';
import { Phase, Refinement } from '../useHarmonIQ';

export interface ReportPanelProps {
  phase: Phase;
  listing: Listing | null;
  report: AnalyzeResponse | null;
  refinement: Refinement | null;
  onRefine: (r: Refinement) => void;
}

export function ReportPanel({ phase, report }: ReportPanelProps) {
  return <div className="hiq-panel">{phase === 'analyzing' ? 'Analyzing…' : report?.analysis.summary}</div>;
}
```

- [ ] **Step 5: Verify in the browser** — backend running (`dotnet run --project backend/HarmonIQ.Api`), no `.env` needed (demo mode):

```bash
npm run dev --prefix frontend
```
Open http://localhost:5173. Expected: both badges show the spinner then resolve to a grade (demo mode is near-instant); the expanded instance shows the summary text; changing `listing-id` to `nope` in `index.html` shows "Score unavailable" without console errors breaking the page. Run `npm run build --prefix frontend` to confirm strict TS passes.

- [ ] **Step 6: Commit**

```bash
git add frontend/src
git commit -m "feat: auto-analyzing badge with state machine and typed API client"
```

---

### Task 14: ReportPanel — gauge, element bars, room cards, mode pill

**Files:**
- Create: `frontend/src/components/ScoreGauge.tsx`, `frontend/src/components/ElementBars.tsx`, `frontend/src/components/RoomCard.tsx`, `frontend/src/components/ModePill.tsx`
- Modify: `frontend/src/components/ReportPanel.tsx` (replace stub)

**Interfaces:**
- Consumes: types from `api.ts`, `ReportPanelProps` (Task 13), CSS classes (Task 12).
- Produces:
  - `ScoreGauge({ score, grade })` — animated circular gauge, color green ≥75 / amber ≥55 / red (FR-26).
  - `ElementBars({ balance })` (FR-27).
  - `RoomCard({ room, thumbnailUrl })` (FR-28).
  - `ModePill({ mode, modelId })` (FR-31).
  - `ReportPanel` renders: pill, demo banner, header (gauge + summary + element bars), room cards; leaves two marked slots (`{/* SiteCard: Task 15 */}`, `{/* NumbersCard: Task 15 */}`, `{/* RefineDrawer: Task 16 */}`).
  - Shared helper exported from `ScoreGauge.tsx`: `scoreColor(score: number): string` — used by RoomCard and Task 15 cards.

- [ ] **Step 1: `src/components/ScoreGauge.tsx`**

```tsx
export function scoreColor(score: number): string {
  return score >= 75 ? 'var(--hiq-good)' : score >= 55 ? 'var(--hiq-warn)' : 'var(--hiq-bad)';
}

export function ScoreGauge({ score, grade }: { score: number; grade: string }) {
  const r = 52;
  const c = 2 * Math.PI * r;
  return (
    <svg viewBox="0 0 120 120" width="130" height="130" role="img"
      aria-label={`HarmonIQ grade ${grade}, ${score} out of 100`}>
      <circle cx="60" cy="60" r={r} fill="none" stroke="var(--hiq-surface-2)" strokeWidth="10" />
      <circle cx="60" cy="60" r={r} fill="none" stroke={scoreColor(score)} strokeWidth="10"
        strokeLinecap="round" strokeDasharray={`${(c * score) / 100} ${c}`}
        transform="rotate(-90 60 60)"
        style={{ transition: 'stroke-dasharray 1s ease, stroke .4s' }} />
      <text x="60" y="58" textAnchor="middle" fontSize="30" fontWeight="800"
        fill={scoreColor(score)} className="hiq-gauge-num">{grade}</text>
      <text x="60" y="80" textAnchor="middle" fontSize="13" fill="var(--hiq-muted)">{score}/100</text>
    </svg>
  );
}
```

- [ ] **Step 2: `src/components/ElementBars.tsx`**

```tsx
import { ElementBalance } from '../api';

const ELEMENT_COLORS: Record<keyof ElementBalance, string> = {
  wood: '#4a7c2f', fire: '#c0392b', earth: '#b98a2f', metal: '#8e9aa5', water: '#2e6b8a',
};

export function ElementBars({ balance }: { balance: ElementBalance }) {
  return (
    <div className="hiq-elements">
      {(Object.keys(ELEMENT_COLORS) as (keyof ElementBalance)[]).map(el => (
        <div className="hiq-el-row" key={el}>
          <span className="hiq-el-label">{el}</span>
          <div className="hiq-el-track">
            <div className="hiq-el-fill"
              style={{ width: `${balance[el]}%`, background: ELEMENT_COLORS[el] }} />
          </div>
          <span className="hiq-el-val">{balance[el]}</span>
        </div>
      ))}
    </div>
  );
}
```

- [ ] **Step 3: `src/components/ModePill.tsx`**

```tsx
export function ModePill({ mode, modelId }: { mode: 'live' | 'demo'; modelId?: string }) {
  return (
    <span className="hiq-pill">
      <span className={`hiq-pill-dot${mode === 'demo' ? ' hiq-pill-dot--demo' : ''}`} />
      {mode === 'live' ? `Live · ${modelId ?? 'claude'}` : 'Demo mode'}
    </span>
  );
}
```

- [ ] **Step 4: `src/components/RoomCard.tsx`** (two-column findings + suggestion cards — this layout is reused verbatim by SiteCard, so export the columns):

```tsx
import { Finding, RoomAnalysis, Suggestion, ViolationFinding } from '../api';
import { scoreColor } from './ScoreGauge';

export function FindingColumns({ adhering, violations }: {
  adhering: Finding[]; violations: ViolationFinding[];
}) {
  return (
    <div className="hiq-cols">
      <div>
        <div className="hiq-col-title hiq-col-title--good">Working in your favor</div>
        {adhering.length === 0 && <div className="hiq-finding">Nothing notable detected.</div>}
        {adhering.map((f, i) => (
          <div className="hiq-finding" key={i}>
            <b>{f.principle}<span className="hiq-tag hiq-tag--sys">{f.system}</span></b>
            {f.observation}
          </div>
        ))}
      </div>
      <div>
        <div className="hiq-col-title hiq-col-title--bad">Breaking the principles</div>
        {violations.length === 0 && <div className="hiq-finding">No violations detected.</div>}
        {violations.map((f, i) => (
          <div className="hiq-finding" key={i}>
            <b>{f.principle}
              <span className={`hiq-tag hiq-tag--${f.severity}`}>{f.severity}</span>
              <span className="hiq-tag hiq-tag--sys">{f.system}</span>
            </b>
            {f.observation}
          </div>
        ))}
      </div>
    </div>
  );
}

export function SuggestionCards({ suggestions }: { suggestions: Suggestion[] }) {
  if (suggestions.length === 0) return null;
  return (
    <div className="hiq-sugs">
      {suggestions.map((s, i) => (
        <div className="hiq-sug" key={i}>
          <b>{s.title}</b>
          {s.detail}
          <div className="hiq-sug-tags">
            <span>impact: {s.impact}</span><span>effort: {s.effort}</span>
          </div>
        </div>
      ))}
    </div>
  );
}

export function RoomCard({ room, thumbnailUrl }: { room: RoomAnalysis; thumbnailUrl?: string }) {
  return (
    <div className="hiq-card">
      <div className="hiq-card-head">
        {thumbnailUrl && <img className="hiq-thumb" src={thumbnailUrl} alt={room.roomType} />}
        <h4 className="hiq-card-title">{room.roomType}</h4>
        <span className="hiq-chip" style={{ background: scoreColor(room.score) }}>{room.score}</span>
      </div>
      <FindingColumns adhering={room.adhering} violations={room.violations} />
      <SuggestionCards suggestions={room.suggestions} />
    </div>
  );
}
```

- [ ] **Step 5: Replace `src/components/ReportPanel.tsx`**

```tsx
import { useState } from 'react';
import { AnalyzeResponse, Listing } from '../api';
import { apiUrl } from '../base';
import { Phase, Refinement } from '../useHarmonIQ';
import { ScoreGauge } from './ScoreGauge';
import { ElementBars } from './ElementBars';
import { ModePill } from './ModePill';
import { RoomCard } from './RoomCard';

export interface ReportPanelProps {
  phase: Phase;
  listing: Listing | null;
  report: AnalyzeResponse | null;
  refinement: Refinement | null;
  onRefine: (r: Refinement) => void;
}

export function ReportPanel({ phase, listing, report, refinement, onRefine }: ReportPanelProps) {
  const [drawerOpen, setDrawerOpen] = useState(false);
  if (!report) {
    return (
      <div className="hiq-panel">
        <span className="hiq-spinner" /> Reading this home's energy — photos, surroundings, and numbers…
      </div>
    );
  }
  const a = report.analysis;
  const thumb = (photoId: string) => {
    const rel = listing?.photos.find(p => p.photoId === photoId)?.thumbnailUrl;
    return rel ? apiUrl(rel) : undefined; // thumbnails are API-relative; resolve cross-origin
  };
  return (
    <div className="hiq-panel" style={phase === 'analyzing' ? { opacity: 0.5, pointerEvents: 'none' } : undefined}>
      <div className="hiq-row" style={{ justifyContent: 'space-between' }}>
        <ModePill mode={report.mode} modelId={report.modelId} />
        <button className="hiq-btn hiq-btn--ghost" type="button"
          onClick={() => setDrawerOpen(o => !o)}>
          {drawerOpen ? 'Close refine' : 'Refine'}
        </button>
      </div>
      {report.mode === 'demo' && report.notice && (
        <div className="hiq-banner">{report.notice}</div>
      )}
      <div className="hiq-panel-head">
        <ScoreGauge score={a.overallScore} grade={a.grade} />
        <div style={{ flex: 2, minWidth: 260 }}>
          <h3 className="hiq-panel-title">HarmonIQ Report</h3>
          <div className="hiq-summary">{a.summary}</div>
        </div>
        <ElementBars balance={a.elementBalance} />
      </div>
      {/* RefineDrawer: Task 16 (render when drawerOpen) */}
      {a.rooms.map(room => (
        <RoomCard key={room.photoId} room={room} thumbnailUrl={thumb(room.photoId)} />
      ))}
      {/* SiteCard: Task 15 */}
      {/* NumbersCard: Task 15 */}
    </div>
  );
}
```

Note: `refinement`, `onRefine`, and `drawerOpen` are wired but unused until Tasks 15–16 — TS `noUnusedLocals` will flag `refinement`/`onRefine`; silence for now by prefixing the destructured names with underscores in this task (`refinement: _refinement, onRefine: _onRefine`) and restoring them in Task 16.

- [ ] **Step 6: Verify in the browser** (backend running, demo mode):

Open http://localhost:5173 — the expanded instance shows: Demo pill + banner, animated gauge with grade, five element bars, one card per analyzed photo with thumbnail, red/green columns, severity tags, and suggestion tags. `npm run build --prefix frontend` passes.

- [ ] **Step 7: Commit**

```bash
git add frontend/src
git commit -m "feat: expanded report panel with gauge, element bars, and room cards"
```

---
### Task 15: SiteCard (compass diagram) + NumbersCard

**Files:**
- Create: `frontend/src/components/SiteCard.tsx`, `frontend/src/components/NumbersCard.tsx`
- Modify: `frontend/src/components/ReportPanel.tsx` (fill the two marked slots)

**Interfaces:**
- Consumes: `SiteAnalysis`, `NumerologyResult`, `ListingEnvironment` (api.ts); `FindingColumns`, `SuggestionCards`, `scoreColor` (Task 14).
- Produces:
  - `SiteCard({ site, environment })` — compass diagram summarizing each side + the two-column findings (FR-29).
  - `NumbersCard({ numerology })` — verdict rows with tradition, reason, remedy, framed as cultural guidance (FR-19, NFR-8).

- [ ] **Step 1: `src/components/SiteCard.tsx`**

```tsx
import { ListingEnvironment, SideEnvironment, SiteAnalysis } from '../api';
import { FindingColumns, SuggestionCards } from './RoomCard';
import { scoreColor } from './ScoreGauge';

function sideSummary(s: SideEnvironment): string {
  const bits: string[] = [];
  if (s.road !== 'none' && s.road !== 'unknown') bits.push(s.road === 't-junction' ? 'T-junction' : `${s.road} road`);
  if (s.water !== 'none' && s.water !== 'unknown') bits.push(s.water);
  if (s.structures === 'taller-building') bits.push('taller bldg');
  else if (s.structures === 'similar') bits.push('buildings');
  else if (s.structures === 'open') bits.push('open');
  if (s.slope === 'rises' || s.slope === 'falls') bits.push(`ground ${s.slope}`);
  return bits.length ? bits.join(' · ') : '?';
}

function Compass({ environment }: { environment: ListingEnvironment }) {
  const sides = [
    { label: 'N', x: 110, y: 24, text: sideSummary(environment.north), tx: 110, ty: 44 },
    { label: 'E', x: 202, y: 114, text: sideSummary(environment.east), tx: 168, ty: 114 },
    { label: 'S', x: 110, y: 206, text: sideSummary(environment.south), tx: 110, ty: 186 },
    { label: 'W', x: 18, y: 114, text: sideSummary(environment.west), tx: 52, ty: 114 },
  ];
  return (
    <svg viewBox="0 0 220 220" width="220" height="220" className="hiq-compass"
      role="img" aria-label="What surrounds the building on each side">
      <rect x="60" y="60" width="100" height="100" rx="8"
        fill="var(--hiq-surface-2)" stroke="var(--hiq-border)" strokeWidth="2" />
      <text x="110" y="115" textAnchor="middle" fontSize="11" fill="var(--hiq-muted)">BUILDING</text>
      {sides.map(s => (
        <g key={s.label}>
          <text x={s.x} y={s.y} textAnchor="middle" fontSize="14" fontWeight="700"
            fill="var(--hiq-primary)">{s.label}</text>
          <text x={s.tx} y={s.ty} textAnchor="middle" fontSize="8.5" fill="var(--hiq-text)">
            {s.text.length > 26 ? s.text.slice(0, 25) + '…' : s.text}
          </text>
        </g>
      ))}
    </svg>
  );
}

export function SiteCard({ site, environment }: { site: SiteAnalysis; environment: ListingEnvironment }) {
  return (
    <div className="hiq-card">
      <div className="hiq-card-head">
        <h4 className="hiq-card-title">Site &amp; Surroundings</h4>
        <span className="hiq-chip" style={{ background: scoreColor(site.score) }}>{site.score}</span>
      </div>
      <div className="hiq-row" style={{ alignItems: 'flex-start' }}>
        <Compass environment={environment} />
        <div style={{ flex: 1, minWidth: 260 }}>
          <FindingColumns adhering={site.adhering} violations={site.violations} />
        </div>
      </div>
      <SuggestionCards suggestions={site.suggestions} />
    </div>
  );
}
```

- [ ] **Step 2: `src/components/NumbersCard.tsx`**

```tsx
import { NumerologyCheck, NumerologyResult } from '../api';

const SUBJECT_LABELS: Record<string, string> = {
  unitNumber: 'Unit', floor: 'Floor', streetNumber: 'Street number',
};
const VERDICT_COLOR: Record<NumerologyCheck['verdict'], string> = {
  lucky: 'var(--hiq-good)', neutral: 'var(--hiq-muted)', unlucky: 'var(--hiq-bad)',
};

export function NumbersCard({ numerology }: { numerology: NumerologyResult }) {
  if (numerology.checks.length === 0) return null;
  return (
    <div className="hiq-card">
      <div className="hiq-card-head">
        <h4 className="hiq-card-title">Numbers</h4>
        <span className="hiq-pill">
          score {numerology.scoreAdjustment >= 0 ? '+' : ''}{numerology.scoreAdjustment}
        </span>
      </div>
      {numerology.checks.map((c, i) => (
        <div className="hiq-finding" key={i}>
          <b>
            {SUBJECT_LABELS[c.subject] ?? c.subject} {c.value}
            <span className="hiq-tag" style={{
              background: 'transparent',
              border: `1px solid ${VERDICT_COLOR[c.verdict]}`,
              color: VERDICT_COLOR[c.verdict],
            }}>{c.verdict}</span>
            <span className="hiq-tag hiq-tag--sys">{c.tradition}</span>
          </b>
          {c.reason}
          {c.remedy && <div style={{ color: 'var(--hiq-muted)', marginTop: 2 }}>Remedy: {c.remedy}</div>}
        </div>
      ))}
      <div className="hiq-banner" style={{ marginBottom: 0 }}>
        Number readings are cultural tradition, offered as guidance — not statements of fact about this home.
      </div>
    </div>
  );
}
```

- [ ] **Step 3: Fill the slots in `ReportPanel.tsx`** — add imports and replace the two comment markers:

```tsx
import { SiteCard } from './SiteCard';
import { NumbersCard } from './NumbersCard';
```

```tsx
      <SiteCard site={a.site}
        environment={refinement?.environment ?? listing!.environment} />
      <NumbersCard numerology={a.numerology} />
```

(If `refinement` was underscore-prefixed in Task 14, restore the name here.)

- [ ] **Step 4: Verify in the browser** (backend running, demo mode)

Open http://localhost:5173, expanded instance. Expected: Site & Surroundings card shows the compass with "T-junction" on N, "pond · ground falls" on E, "quiet road · taller bldg" on S; violations column includes "T-Junction Facing the Building" (major); Numbers card shows Unit 414 unlucky (fengshui) with remedy, Floor 4 unlucky, Street 123 lucky (vastu, digit sum 6), plus the cultural-framing banner. `npm run build --prefix frontend` passes.

- [ ] **Step 5: Commit**

```bash
git add frontend/src
git commit -m "feat: site compass card and numerology card in the report"
```

---

### Task 16: RefineDrawer (re-grade in place)

**Files:**
- Create: `frontend/src/components/RefineDrawer.tsx`
- Modify: `frontend/src/components/ReportPanel.tsx` (render the drawer)

**Interfaces:**
- Consumes: `Listing`, `PhotoSelection`, `Systems`, environment/number types (api.ts); `Refinement` (Task 13).
- Produces: `RefineDrawer({ listing, refinement, onApply, onClose })` — edits a local draft; Apply calls `onApply(draft)` which is `useHarmonIQ.refine` → report re-grades in place (FR-11). Client-side guard: 1–6 photos selected, matching the API's 400 rules.

- [ ] **Step 1: `src/components/RefineDrawer.tsx`**

```tsx
import { useState } from 'react';
import {
  Listing, ListingEnvironment, PhotoSelection, SideEnvironment, Systems,
} from '../api';
import { apiUrl } from '../base';
import { Refinement } from '../useHarmonIQ';

const ROOM_TYPES = ['Auto-detect', 'Bedroom', 'Living Room', 'Kitchen', 'Bathroom',
  'Dining Room', 'Home Office', 'Entryway', 'Balcony'];
const ORIENTATIONS = ['unknown', 'north', 'northeast', 'east', 'southeast',
  'south', 'southwest', 'west', 'northwest'];
const SIDES = ['north', 'east', 'south', 'west'] as const;
const ENV_FIELDS: { key: keyof SideEnvironment; label: string; options: string[] }[] = [
  { key: 'road', label: 'Road', options: ['unknown', 'none', 'quiet', 'busy', 't-junction', 'highway'] },
  { key: 'water', label: 'Water', options: ['unknown', 'none', 'pond', 'lake', 'river', 'pool'] },
  { key: 'structures', label: 'Structures', options: ['unknown', 'open', 'similar', 'taller-building'] },
  { key: 'slope', label: 'Slope', options: ['unknown', 'level', 'rises', 'falls'] },
];

export interface RefineDrawerProps {
  listing: Listing;
  refinement: Refinement;
  onApply: (r: Refinement) => void;
  onClose: () => void;
}

export function RefineDrawer({ listing, refinement, onApply, onClose }: RefineDrawerProps) {
  const [draft, setDraft] = useState<Refinement>(() => ({
    ...refinement,
    photos: refinement.photos.map(p => ({ ...p })),
    environment: structuredClone(refinement.environment),
    numbers: { ...refinement.numbers },
  }));

  const selectedIds = new Set(draft.photos.map(p => p.photoId));
  const togglePhoto = (photoId: string, suggested: string | null) =>
    setDraft(d => ({
      ...d,
      photos: selectedIds.has(photoId)
        ? d.photos.filter(p => p.photoId !== photoId)
        : [...d.photos, { photoId, roomType: suggested }],
    }));
  const setRoomType = (photoId: string, roomType: string) =>
    setDraft(d => ({
      ...d,
      photos: d.photos.map(p => p.photoId === photoId
        ? { ...p, roomType: roomType === 'Auto-detect' ? null : roomType } : p),
    }));
  const setEnv = (side: typeof SIDES[number], key: keyof SideEnvironment, value: string) =>
    setDraft(d => ({
      ...d,
      environment: { ...d.environment, [side]: { ...d.environment[side], [key]: value } },
    }));

  const count = draft.photos.length;
  const valid = count >= 1 && count <= 6;

  return (
    <div className="hiq-drawer">
      <h4>Photos to analyze ({count}/6)</h4>
      <div className="hiq-photo-grid">
        {listing.photos.map(p => {
          const sel = selectedIds.has(p.photoId);
          const chosen = draft.photos.find(x => x.photoId === p.photoId);
          return (
            <div className="hiq-photo-cell" key={p.photoId} style={sel ? { borderColor: 'var(--hiq-primary)' } : undefined}>
              <img src={apiUrl(p.thumbnailUrl)} alt={p.caption ?? p.photoId} />
              <label style={{ display: 'flex', gap: 4, alignItems: 'center', margin: '4px 0' }}>
                <input type="checkbox" checked={sel}
                  disabled={!sel && count >= 6}
                  onChange={() => togglePhoto(p.photoId, p.suggestedRoomType)} />
                {p.caption ?? (p.interior ? 'Interior' : 'Other')}
              </label>
              {sel && (
                <select value={chosen?.roomType ?? 'Auto-detect'}
                  onChange={e => setRoomType(p.photoId, e.target.value)}>
                  {ROOM_TYPES.map(t => <option key={t}>{t}</option>)}
                </select>
              )}
            </div>
          );
        })}
      </div>

      <h4>Surroundings (what's on each side)</h4>
      <div className="hiq-env-grid">
        {SIDES.map(side => (
          <div className="hiq-env-side" key={side}>
            <b style={{ textTransform: 'capitalize' }}>{side}</b>
            {ENV_FIELDS.map(f => (
              <label key={f.key}>{f.label}
                <select value={draft.environment[side][f.key]}
                  onChange={e => setEnv(side, f.key, e.target.value)}>
                  {f.options.map(o => <option key={o}>{o}</option>)}
                </select>
              </label>
            ))}
          </div>
        ))}
      </div>

      <h4>Numbers</h4>
      <div className="hiq-row">
        <label>Unit <input value={draft.numbers.unitNumber ?? ''} size={6}
          onChange={e => setDraft(d => ({ ...d, numbers: { ...d.numbers, unitNumber: e.target.value || null } }))} /></label>
        <label>Floor <input value={draft.numbers.floor ?? ''} size={4} inputMode="numeric"
          onChange={e => setDraft(d => ({
            ...d,
            numbers: { ...d.numbers, floor: e.target.value === '' ? null : Number(e.target.value) || null },
          }))} /></label>
        <label>Street # <input value={draft.numbers.streetNumber ?? ''} size={6}
          onChange={e => setDraft(d => ({ ...d, numbers: { ...d.numbers, streetNumber: e.target.value || null } }))} /></label>
      </div>

      <h4>Entrance orientation &amp; tradition</h4>
      <div className="hiq-row">
        <select value={draft.orientation}
          onChange={e => setDraft(d => ({ ...d, orientation: e.target.value }))}>
          {ORIENTATIONS.map(o => <option key={o}>{o}</option>)}
        </select>
        <span className="hiq-seg">
          {(['both', 'fengshui', 'vastu'] as Systems[]).map(s => (
            <button key={s} type="button" className={draft.systems === s ? 'on' : ''}
              onClick={() => setDraft(d => ({ ...d, systems: s }))}>
              {s === 'both' ? 'Both' : s === 'fengshui' ? 'Feng Shui' : 'Vastu'}
            </button>
          ))}
        </span>
      </div>

      <div className="hiq-row" style={{ marginTop: 14 }}>
        <button className="hiq-btn" type="button" disabled={!valid}
          onClick={() => { onApply(draft); onClose(); }}>
          Re-grade with these settings
        </button>
        {!valid && <span style={{ fontSize: 12, color: 'var(--hiq-bad)' }}>Select 1–6 photos.</span>}
        <button className="hiq-btn hiq-btn--ghost" type="button" onClick={onClose}>Cancel</button>
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Render it in `ReportPanel.tsx`** — replace the `{/* RefineDrawer: Task 16 */}` marker:

```tsx
      {drawerOpen && listing && refinement && (
        <RefineDrawer listing={listing} refinement={refinement}
          onApply={onRefine} onClose={() => setDrawerOpen(false)} />
      )}
```

with import `import { RefineDrawer } from './RefineDrawer';` (restore any underscore-prefixed props from Task 14).

- [ ] **Step 3: Verify the deterministic re-grade** (backend in demo mode):

Open http://localhost:5173, expand, click Refine, then check each control changes the report as the engines dictate:
1. Switch tradition to **Vastu** → bedroom card loses the "Mirror Facing the Bed" (fengshui) violation; Numbers card loses the fengshui 414 check but keeps the vastu digit-sum rows.
2. Set north road to `none` → the T-junction violation disappears from the Site card and the site score rises.
3. Change unit number to `888` → Numbers card flips to lucky (fengshui) and `scoreAdjustment` goes positive.
4. Set orientation to `north` → Site card gains orientation-dependent findings (Bright Hall / armchair, from fixture structures).
5. Deselect all photos → Apply button disables with the "Select 1–6 photos." hint.

`npm run build --prefix frontend` passes.

- [ ] **Step 4: Commit**

```bash
git add frontend/src
git commit -m "feat: refine drawer — photos, surroundings, numbers, orientation, tradition"
```

---

### Task 17: Mock LDP host page + brand switcher + acceptance pass

**Files:**
- Create: `backend/HarmonIQ.Api/wwwroot/mock-ldp.html`
- Create: `backend/HarmonIQ.Api/wwwroot/harmoniq.html`
- Modify: `backend/HarmonIQ.Api/Program.cs` (serve `/harmoniq`)
- Delete: `backend/HarmonIQ.Api/wwwroot/.gitkeep`

**Interfaces:**
- Consumes: the built embed bundle (`/embed/harmoniq-module.js`), fixture photo endpoints (Task 6), the custom element (Task 12).
- Produces: the demo host at `http://localhost:5080/` (Acceptance 1–7) and the HarmonIQ page at `/harmoniq` (the attribution link target, FR-3). Host supplies **only** `listing-id` (+ `brand`); the module does the rest (FR-1/2/6).

- [ ] **Step 1: Write `wwwroot/mock-ldp.html`** — a static replica of the apartments-web `BuildingProfile` LDP: gallery, pricing, amenities, a **Schools** section with GreatSchools-style ratings, and a **Transportation** section ending in the "Getting Around" score-card grid, with the HarmonIQ badge slotted directly beneath those score cards (FR-3/FR-6). Host styles are deliberately aggressive to prove shadow-DOM isolation:

```html
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>The Elm — 2BR/2BA · Apartments.com (mock LDP)</title>
<style>
  /* Aggressive host styles on purpose: the module must not inherit them (NFR-9). */
  * { box-sizing: border-box; }
  body { margin: 0; font-family: 'Helvetica Neue', Arial, sans-serif; color: #333; background: #fafafa;
         font-size: 19px; line-height: 2.2; letter-spacing: 1px; }
  a { color: inherit; text-decoration: none; }
  .topbar { color: #fff; padding: 14px 28px; font-weight: 800; font-size: 22px; letter-spacing: 0; display: flex; align-items: center; gap: 24px; }
  .topbar nav { font-size: 14px; font-weight: 400; display: flex; gap: 18px; }
  .wrap { max-width: 1100px; margin: 0 auto; padding: 20px 28px 80px; letter-spacing: 0; line-height: 1.5; font-size: 15px; }
  .gallery { display: grid; grid-template-columns: 2fr 1fr 1fr; gap: 8px; border-radius: 12px; overflow: hidden; }
  .gallery img { width: 100%; height: 100%; object-fit: cover; display: block; }
  .gallery img:first-child { grid-row: span 2; }
  .head { display: flex; justify-content: space-between; align-items: flex-start; gap: 20px; margin: 22px 0 6px; flex-wrap: wrap; }
  h1 { font-size: 26px; margin: 0; }
  .addr { color: #666; margin: 2px 0 10px; }
  .price { font-size: 22px; font-weight: 800; }
  .amenities { display: flex; gap: 10px; flex-wrap: wrap; margin: 14px 0 26px; }
  .amenity { background: #fff; border: 1px solid #e2e2e2; border-radius: 20px; padding: 6px 14px; font-size: 13px; }
  h2 { font-size: 19px; margin: 30px 0 10px; }
  h3.section-heading { font-size: 15px; margin: 14px 0 8px; color: #444; }
  .schools { display: flex; flex-direction: column; gap: 8px; }
  .school { background: #fff; border: 1px solid #e2e2e2; border-radius: 8px; padding: 10px 14px; display: flex; align-items: center; gap: 12px; }
  .school-rating { background: #2f6f8f; color: #fff; border-radius: 50%; width: 34px; height: 34px; display: inline-flex; align-items: center; justify-content: center; font-weight: 800; flex: none; }
  .score-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; margin: 10px 0; }
  .score-card { background: #fff; border: 1px solid #e2e2e2; border-radius: 8px; padding: 12px 14px; display: flex; justify-content: space-between; align-items: center; }
  .score-title { font-weight: 700; font-size: 14px; }
  .score-tagline { color: #777; font-size: 12px; }
  .score-value { font-size: 20px; font-weight: 800; }
  .score-value span { font-size: 12px; color: #999; font-weight: 400; }
  .attribution { font-size: 12px; color: #777; margin: 4px 0 18px; }
  .attribution a { text-decoration: underline; }
  .brandbar { position: fixed; right: 18px; bottom: 18px; background: #fff; border: 1px solid #ddd; border-radius: 10px;
              padding: 10px 14px; box-shadow: 0 4px 16px rgba(0,0,0,.15); font-size: 13px; z-index: 50; }
  .brandbar button { margin-left: 6px; cursor: pointer; padding: 5px 10px; border-radius: 6px; border: 1px solid #ccc; background: #f5f5f5; }
  .brandbar button.on { color: #fff; border-color: transparent; }
  /* Per-brand host chrome so the page itself also changes flavor */
  body[data-brand="apartments"] .topbar { background: #5f8f22; }
  body[data-brand="apartments"] .brandbar button.on { background: #5f8f22; }
  body[data-brand="apartmentfinder"] .topbar { background: #0b6bb1; }
  body[data-brand="apartmentfinder"] .brandbar button.on { background: #0b6bb1; }
  body[data-brand="forrent"] .topbar { background: #8a1e7c; }
  body[data-brand="forrent"] .brandbar button.on { background: #8a1e7c; }
</style>
</head>
<body data-brand="apartments">
  <div class="topbar"><span id="brand-name">Apartments.com</span>
    <nav><a href="#">Rent</a><a href="#">Buy</a><a href="#">Saved</a></nav>
  </div>
  <div class="wrap">
    <div class="gallery">
      <img src="/api/listing/sample/photos/p6?w=1200" alt="Building exterior">
      <img src="/api/listing/sample/photos/p2?w=600" alt="Living room">
      <img src="/api/listing/sample/photos/p1?w=600" alt="Bedroom">
      <img src="/api/listing/sample/photos/p3?w=600" alt="Kitchen">
      <img src="/api/listing/sample/photos/p4?w=600" alt="Bathroom">
    </div>
    <div class="head">
      <div>
        <h1>The Elm — 2BR/2BA · Unit 414</h1>
        <div class="addr">123 Main St, Arlington, VA 22201</div>
      </div>
      <div class="price">$2,450/mo</div>
    </div>
    <div class="amenities">
      <span class="amenity">2 bed</span><span class="amenity">2 bath</span>
      <span class="amenity">1,050 sq ft</span><span class="amenity">4th floor</span>
      <span class="amenity">In-unit laundry</span><span class="amenity">Pet friendly</span>
    </div>
    <h2>About this apartment</h2>
    <p>Bright two-bedroom in the heart of Arlington: chef's kitchen, generous living space,
       a dedicated den for working from home, and a leafy pond just east of the building.
       Photos, surroundings, and unit numbers on this page feed the HarmonIQ report below.</p>
    <h2>Schools</h2>
    <div class="schools">
      <div class="school"><span class="school-rating">8</span> Arlington Science Focus Elementary</div>
      <div class="school"><span class="school-rating">7</span> Dorothy Hamm Middle School</div>
      <div class="school"><span class="school-rating">9</span> Washington-Liberty High School</div>
    </div>
    <p class="attribution">School data provided by <a href="https://www.greatschools.org">GreatSchools</a></p>
    <h2>Transportation</h2>
    <h3 class="section-heading">Getting Around</h3>
    <div class="score-grid">
      <div class="score-card"><div><div class="score-title">Walk Score®</div><div class="score-tagline">Very Walkable</div></div><div class="score-value">78 <span>/ 100</span></div></div>
      <div class="score-card"><div><div class="score-title">Transit Score®</div><div class="score-tagline">Excellent Transit</div></div><div class="score-value">82 <span>/ 100</span></div></div>
      <div class="score-card"><div><div class="score-title">Bike Score®</div><div class="score-tagline">Bikeable</div></div><div class="score-value">65 <span>/ 100</span></div></div>
    </div>
    <p class="attribution">Scores provided by <a href="https://locallogic.co/" rel="nofollow">Local Logic</a></p>
    <!-- HarmonIQ compact badge: a native-feeling score card directly beneath the listing's scores (FR-3) -->
    <harmoniq-module id="hiq-badge" listing-id="sample" brand="apartments"></harmoniq-module>
    <h2>Harmony report</h2>
    <!-- Expanded module inline in the page body (FR-6) -->
    <harmoniq-module id="hiq-expanded" listing-id="sample" brand="apartments" state="expanded"></harmoniq-module>
  </div>

  <div class="brandbar">
    Brand:
    <button data-brand="apartments" class="on">Apartments.com</button>
    <button data-brand="apartmentfinder">ApartmentFinder</button>
    <button data-brand="forrent">ForRent</button>
  </div>

  <script src="/embed/harmoniq-module.js" defer></script>
  <script>
    const NAMES = { apartments: 'Apartments.com', apartmentfinder: 'ApartmentFinder', forrent: 'ForRent.com' };
    document.querySelectorAll('.brandbar button').forEach(btn => {
      btn.addEventListener('click', () => {
        const brand = btn.dataset.brand;
        document.body.dataset.brand = brand;
        document.getElementById('brand-name').textContent = NAMES[brand];
        document.querySelectorAll('.brandbar button').forEach(b => b.classList.toggle('on', b === btn));
        // Restyle both module instances without reload (Acceptance 5).
        document.querySelectorAll('harmoniq-module').forEach(m => m.setAttribute('brand', brand));
      });
    });
  </script>
</body>
</html>
```

- [ ] **Step 2: Write `wwwroot/harmoniq.html`** — the HarmonIQ page the attribution link opens (FR-3), and serve it at `/harmoniq`:

```html
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>HarmonIQ — spatial harmony scores for rental listings</title>
<style>
  body { margin: 0; font-family: Georgia, 'Times New Roman', serif; color: #22271f; background: #f6f4ef; }
  main { max-width: 640px; margin: 0 auto; padding: 60px 24px; line-height: 1.6; }
  h1 { color: #4a7c2f; font-size: 34px; margin: 0 0 6px; }
  .tag { color: #6b7263; font-style: italic; margin: 0 0 24px; }
  p { font-size: 16px; }
  .fine { font-size: 13px; color: #6b7263; }
  a { color: #4a7c2f; }
</style>
</head>
<body>
<main>
  <h1>HarmonIQ</h1>
  <p class="tag">Feng Shui &amp; Vastu harmony scores, on every listing.</p>
  <p>HarmonIQ grades apartments against Feng Shui and Vastu Shastra principles — the rooms in the
     listing's photos, the site that surrounds the building, and the numbers on its door — and
     surfaces the result right on the listing page across the CoStar rentals network.</p>
  <p class="fine">Scores reflect cultural tradition, never objective claims about safety or value.
     Analysis runs automatically from data the listing already has.</p>
  <p><a href="/">← Back to the demo listing</a></p>
</main>
</body>
</html>
```

In `Program.cs`, next to the static-files setup, add the extensionless route:

```csharp
app.MapGet("/harmoniq", (IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.WebRootPath, "harmoniq.html"), "text/html"));
```

- [ ] **Step 3: Full build + serve**

```bash
npm run build --prefix frontend
dotnet run --project backend/HarmonIQ.Api
```
Open http://localhost:5080/ (Acceptance 1).

- [ ] **Step 4: Walk the acceptance criteria** (SPEC §9) — with the event key in `.env` where noted:

1. ☐ `npm run build --prefix frontend` + `dotnet run --project backend/HarmonIQ.Api` serves the mock LDP at :5080.
2. ☐ Badge renders as a score card directly beneath the Transportation section's "Getting Around" score cards (which follow the Schools section), in loading state, resolving to a grade with no user input (live: ~15–25 s; time it). Its "Data provided by HarmonIQ" link opens `/harmoniq`.
3. ☐ Expanded report shows gauge, element bars, room cards with findings that reference visible photo content, pill `Live · claude-sonnet-5`, Site card with compass + T-junction sha-chi violation, Numbers card with unit 414 flagged + remedy.
4. ☐ Refine drawer: run the five checks from Task 16 Step 3 against the live backend; re-grade happens in place.
5. ☐ Brand switcher restyles both instances instantly (colors, radius, fonts) without reload; badge grade survives the switch (no re-analysis); host page styling unaffected (inspect shadow root; the module must not show the host's 19px/2.2 line-height text).
6. ☐ Offline demo: rename `.env` away, restart backend, disconnect Wi-Fi → same flow completes with `Demo mode` pill + banner, fixture environment and numbers intact.
7. ☐ Error paths: change the badge module's `listing-id` to `nope` in devtools → unobtrusive "Score unavailable"; `curl -s -o /dev/null -w "%{http_code}" http://localhost:5080/api/listing/nope` → 404; `curl` POST with 0 photos → 400; break `GEO_OVERPASS_URL`, restart, analyze a real (scraped) listing → report still renders with an editable, sparse surroundings section.
8. (Real LDP, local) — verified separately in Task 18.

Fix anything that fails before committing; re-run the failing criterion after each fix.

- [ ] **Step 5: Update the module's spinner copy if latency feels dead** *(optional polish, only if time allows)* — e.g. cycle "reading the rooms… / walking the site… / checking the numbers…" in `HarmonIQBadge` with a `setInterval` over an array of phrases.

- [ ] **Step 6: Final commit**

```bash
dotnet test backend/HarmonIQ.Tests
git add backend/HarmonIQ.Api/wwwroot backend/HarmonIQ.Api/Program.cs
git rm --cached backend/HarmonIQ.Api/wwwroot/.gitkeep 2>/dev/null; rm -f backend/HarmonIQ.Api/wwwroot/.gitkeep
git commit -m "feat: mock LDP host page with brand switcher and HarmonIQ page — demo complete"
```

---

### Task 18: Local-only embed in the real apartments-web LDP (FR-6b, Acceptance 8)

**Files (in the apartments-web repo, on a local demo branch — never merged, pushed, or PR'd):**
- Modify: `Source/AptsWeb/Modules/BuildingProfile/Views/Index.cshtml`

**Interfaces:**
- Consumes: the built embed bundle served at `http://localhost:5080/embed/harmoniq-module.js` (Task 12), CORS + `/harmoniq` on the API (Tasks 1/17), the module's script-origin API-base resolution (`base.ts`).
- Produces: Acceptance 8 — the HarmonIQ score card on a real, locally served listing page. Nothing in the HarmonIQ repo changes in this task.

- [ ] **Step 1: Create the local demo branch in apartments-web**

```bash
cd /Users/achiuwei/Documents/apartments-web
git switch -c harmoniq-demo
```

Local only: never push this branch, never open a PR (SPEC §7/§8).

- [ ] **Step 2: Inject the module into the LDP** — in `Source/AptsWeb/Modules/BuildingProfile/Views/Index.cshtml`, find the section renders:

```cshtml
@{ Html.RenderPartialEx(views._EducationSection, Model.Education); }
@{ Html.RenderPartialEx(views._TransportationSection, Model.Transportation); }
```

and insert **immediately after the `_TransportationSection` line** (that section ends with the "Getting Around" vendor score cards, so this lands the badge directly beneath them, before `_PointsOfInterestSection`):

```cshtml
@* HarmonIQ demo — LOCAL ONLY, never merge (HarmonIQ SPEC FR-6b). Score card beneath the Getting Around scores. *@
<harmoniq-module listing-id="@Model.PropertyKey" brand="apartments"></harmoniq-module>
<script src="http://localhost:5080/embed/harmoniq-module.js" defer></script>
```

`Model` is `BuildingProfileModel`; `PropertyKey` is the network's shared listing key. HarmonIQ's `ListingService` resolves a bare key by fetching `https://www.apartments.com/{key}/` (the site canonicalizes bare-key URLs to the listing), so no slug encoding is needed. Desktop view is enough for the demo — skip the mobile variant.

- [ ] **Step 3: Run both apps**

```bash
npm run build --prefix frontend      # in the HarmonIQ repo — bundle must be current
dotnet run --project backend/HarmonIQ.Api
```

Then start apartments-web locally using that repo's normal dev workflow, and open any listing detail page.

- [ ] **Step 4: Verify Acceptance 8**

1. ☐ The HarmonIQ score card renders directly beneath the "Getting Around" score cards on a real LDP, loads, and resolves to a grade (live analysis of that actual listing; demo mode if no key).
2. ☐ Cross-origin plumbing works: devtools Network shows `/api/listing` + `/api/analyze` hitting `localhost:5080` with CORS OK; room thumbnails load; no mixed-origin errors in the console.
3. ☐ Expanding shows the full report inline; Refine re-grades.
4. ☐ "Data provided by HarmonIQ" opens `http://localhost:5080/harmoniq`.
5. ☐ The host page is unaffected: its own styles/scripts behave normally; the module is fully inside its shadow root.

- [ ] **Step 5: Keep it local** — optionally commit on the `harmoniq-demo` branch for safekeeping (apartments-web's pre-commit hooks require its `npm install` to have been run); **do not push, do not PR, do not merge**. Switch apartments-web back to `main` when not demoing.

---

## Execution notes

- **Order matters:** Tasks 1→11 are backend and strictly ordered (8 can slot after 9 if you prefer to avoid the classification stub dance). Tasks 12→17 are frontend and strictly ordered. Task 12 can start any time after Task 1 if two people/agents work in parallel.
- **Live vs demo while developing:** develop everything against demo mode (no `.env`); touch the live path only in Tasks 9, 11, and the final acceptance pass — the event key is shared and rate-limited (NFR-4).
- **The proxy dies Wed Aug 12** (SPEC §7): after the event, the module runs in demo mode unless `CLAUDE_BASE_URL`/`CLAUDE_API_KEY` are repointed. Nothing in the code should hard-depend on live mode.
- **Task 18 is last and optional-env:** it needs a working local apartments-web dev environment and the finished bundle/backend (Tasks 12–17). If apartments-web can't run on the demo machine, the mock LDP (Task 17) alone still satisfies Acceptance 1–7; only Acceptance 8 depends on Task 18.
- **Out of scope — do not build:** standalone app/URL entry, photo upload, persistence, **deploying/merging to real production LDPs** (Task 18 stays on a local branch), search badges/filters, Matterport, satellite/street-view imagery, Kua/BaZi, multi-language, PDF export, cost tracking (SPEC §8).
