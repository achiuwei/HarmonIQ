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
