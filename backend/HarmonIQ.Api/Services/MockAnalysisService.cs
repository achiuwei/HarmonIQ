using System.Text.Json;
using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

/// <summary>
/// Demo-mode perception. Produces the same <b>tradition-agnostic observation</b> shapes the live
/// lenses produce, from templates in <c>Data/mock-analysis.json</c> (photos) and
/// <c>Data/mock-floorplan-analysis.json</c> (plans), so the deterministic judgment half of the
/// pipeline is exercised identically in demo and live mode.
///
/// <para><b>Demo keying.</b> Variation is keyed on the <b>PhotoId</b> on the photo path and on the
/// <b>SubjectId</b> on the floor-plan path. There is no PhotoId on the floor-plan path, and keying
/// on a constant would clone one grade across every plan of a property — twelve identical chips.
/// The key hash is hand-rolled on purpose: <c>string.GetHashCode()</c> is randomized per process,
/// so demo grades would change between runs.</para>
///
/// <para>Demo output is a read-path presentation. It is marked <c>Mode = "demo"</c> wherever it is
/// stored and is ineligible for the publish path.</para>
/// </summary>
public class MockAnalysisService
{
    private readonly Dictionary<string, JsonElement> _roomTemplates;
    private readonly JsonElement _planTemplate;

    public MockAnalysisService(IWebHostEnvironment env) : this(env.ContentRootPath)
    {
    }

    public MockAnalysisService(string contentRootPath)
    {
        using var rooms = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(contentRootPath, "Data", "mock-analysis.json")));
        _roomTemplates = rooms.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);

        using var plan = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(contentRootPath, "Data", "mock-floorplan-analysis.json")));
        _planTemplate = plan.RootElement.Clone();
    }

    /// <summary>
    /// Deterministic, process-stable key hash. Deliberately NOT <c>string.GetHashCode()</c>, which
    /// is randomized per process and would make demo grades flicker between runs.
    /// </summary>
    public static int DemoKey(string value) =>
        (value ?? string.Empty).Aggregate(17, (h, c) => unchecked(h * 31 + c));

    /// <summary>The classic ±3 demo jitter, keyed on whatever identity the evidence path supplies.</summary>
    public static int DemoJitter(string key) => Math.Abs(DemoKey(key)) % 7 - 3;

    // ------------------------------------------------------------------ photo path (keyed on PhotoId)

    /// <summary>
    /// One tradition-agnostic room observation, keyed on <see cref="PhotoSelection.PhotoId"/>.
    /// Every finding carries its own tradition tag; nothing is filtered here — filtering happens
    /// at score time so one observation serves both principle sets.
    /// </summary>
    public RoomObservation ObserveRoom(PhotoSelection photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        var roomType = string.IsNullOrWhiteSpace(photo.RoomType) || photo.RoomType == "Auto-detect"
            ? "Room" : photo.RoomType!;
        var tpl = Template(roomType);

        var findings = tpl.GetProperty("findings").Deserialize<List<LensFinding>>(Json.Options)!;
        var suggestions = tpl.GetProperty("suggestions").Deserialize<List<Suggestion>>(Json.Options)!;

        // Per-photo variation: drop at most one finding, and nudge the self-reported coverage.
        var key = Math.Abs(DemoKey(photo.PhotoId));
        if (findings.Count > 2)
        {
            var drop = key % (findings.Count + 2);
            if (drop < findings.Count) findings.RemoveAt(drop);
        }
        var coverage = Math.Clamp(tpl.GetProperty("coverage").GetDouble() + DemoJitter(photo.PhotoId) * 0.01, 0, 1);

        var elements = tpl.TryGetProperty("elementBalance", out var eb)
            ? eb.Deserialize<ElementBalance>(Json.Options)
            : null;
        if (elements is { IsAllZero: true }) elements = null;

        return new RoomObservation(photo.PhotoId, roomType, findings, suggestions, elements, coverage);
    }

    private JsonElement Template(string roomType) =>
        _roomTemplates.TryGetValue(roomType.ToLowerInvariant(), out var t) ? t : _roomTemplates["default"];

    // ------------------------------------------------------------------ floor-plan path (keyed on SubjectId)

    /// <summary>
    /// One tradition-agnostic floor-plan observation, keyed on the <b>subject id</b>. Distinct plans
    /// of the same property therefore read differently, which is the whole point of a per-plan grade.
    /// </summary>
    public FloorPlanObservation ObservePlan(string subjectId)
    {
        var key = Math.Abs(DemoKey(subjectId));

        var templates = _planTemplate.GetProperty("findings").Deserialize<List<LensFinding>>(Json.Options)!;
        var suggestions = _planTemplate.GetProperty("suggestions").Deserialize<List<Suggestion>>(Json.Options)!;
        var baseCoverage = _planTemplate.GetProperty("baseCoverage").GetDouble();
        var step = _planTemplate.GetProperty("coverageStep").GetDouble();
        var steps = _planTemplate.GetProperty("coverageSteps").GetInt32();

        // One bit of the key per catalogue rule decides whether this plan shows that configuration.
        var findings = templates.Where((_, i) => ((key >> i) & 1) == 1).ToList();
        var boundaryFullyDrawn = ((key >> templates.Count) & 1) == 1;
        var coverage = Math.Clamp(baseCoverage + key % Math.Max(steps, 1) * step, 0, 1);

        return FloorPlanLensService.Sanitize(new FloorPlanObservation(
            NotDeterminable: false,
            NotDeterminableReason: null,
            BoundaryFullyDrawn: boundaryFullyDrawn,
            Findings: findings,
            Suggestions: suggestions,
            Coverage: coverage));
    }
}
