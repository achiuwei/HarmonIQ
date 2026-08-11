using System.Text.Json;

namespace HarmonIQ.Api.Services.Orientation;

/// <summary>
/// Local-demo orientation path: reads <c>Data/sample-orientation.json</c>, keyed
/// <c>propertyKey -> planKey -> unit placements</c>. This is the only way the with-orientation
/// path is exercisable locally (no SightMap key exists), so the fixture is written to cover all
/// three Q5 shapes, keyed onto real plans from the multi-plan fixture
/// (<c>Data/sample-multiplan-listing.json</c>, task 5) rather than placeholder plan ids:
/// - <c>rk-101</c>: 9/10 placed units concentrate north (90% ≥ 80%) → resolves, source
///   "sightmap".
/// - <c>rk-102</c>: placed units split ~50/50 north/south → resolves to <c>Source = "none"</c>
///   (below the 80% threshold).
/// - <c>rk-105</c> (the plan with no image, so already unscored on that path): absent entirely
///   from the fixture → <c>ResolveAsync</c> returns <c>null</c>, same as "not covered".
///
/// Any other propertyKey/subjectId combination absent from the fixture also returns
/// <c>null</c> ("not covered"), matching the interface contract.
/// </summary>
public class FixtureOrientationProvider : IOrientationProvider
{
    private readonly Dictionary<string, Dictionary<string, List<UnitPlacement>>> _byProperty;

    public FixtureOrientationProvider(IWebHostEnvironment env)
        : this(Path.Combine(env.ContentRootPath, "Data", "sample-orientation.json"))
    {
    }

    /// <summary>Path-based constructor for tests that don't need a hosting environment.</summary>
    public FixtureOrientationProvider(string fixturePath)
    {
        _byProperty = new(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(fixturePath)) return;

        using var doc = JsonDocument.Parse(File.ReadAllText(fixturePath));
        foreach (var propertyProp in doc.RootElement.EnumerateObject())
        {
            var plans = new Dictionary<string, List<UnitPlacement>>(StringComparer.OrdinalIgnoreCase);
            foreach (var planProp in propertyProp.Value.EnumerateObject())
            {
                var placements = new List<UnitPlacement>();
                foreach (var unit in planProp.Value.EnumerateArray())
                {
                    placements.Add(new UnitPlacement(
                        unit.GetProperty("unitNumber").GetString()!,
                        unit.TryGetProperty("building", out var b) && b.ValueKind != JsonValueKind.Null
                            ? b.GetString() : null,
                        unit.TryGetProperty("level", out var l) && l.ValueKind != JsonValueKind.Null
                            ? l.GetInt32() : null,
                        unit.TryGetProperty("facingDegrees", out var f) && f.ValueKind != JsonValueKind.Null
                            ? f.GetDouble() : null));
                }
                plans[planProp.Name] = placements;
            }
            _byProperty[propertyProp.Name] = plans;
        }
    }

    public Task<SubjectOrientation?> ResolveAsync(string propertyKey, string subjectId, CancellationToken ct)
    {
        if (!_byProperty.TryGetValue(propertyKey, out var plans) ||
            !plans.TryGetValue(subjectId, out var placements))
        {
            return Task.FromResult<SubjectOrientation?>(null);
        }

        return Task.FromResult(OrientationResolution.Resolve(subjectId, placements, DateTimeOffset.UtcNow));
    }
}
