namespace HarmonIQ.Api.Services.Orientation;

/// <summary>
/// Materializes design §4/§5's <c>subject_orientation(facing_degrees, cardinal, source,
/// confidence, resolved_at)</c> row for one subject (a floor plan, or the property itself on a
/// single-listing subject). Defined here (rather than in Models/Entities.cs, owned by Task 1)
/// because it is this task's exclusive-ownership seam contract; if Task 1's persistence entity
/// ends up with the same shape, the consumer (Task 6, which owns OrientationModule.cs) maps
/// between the two — this type is intentionally namespace-scoped to Services.Orientation so it
/// cannot collide with an EF entity of the same short name in HarmonIQ.Api.Models.
/// </summary>
public record SubjectOrientation(
    string SubjectId,
    double? FacingDegrees,
    string? Cardinal,
    string Source,
    double Confidence,
    DateTimeOffset ResolvedAt);

/// <summary>
/// The single seam through which a plan/property's cardinal facing enters the system.
/// SightMap (via <see cref="SightMapOrientationProvider"/>) is the only orientation source;
/// geocode/environment data never yields orientation (design §4, §1 Q4).
///
/// Contract:
/// - Returns <c>null</c> when the property is not covered by the provider at all (no data
///   ingested for this property/subject — the "without-orientation" path, same as no coverage).
/// - Returns a row with <see cref="SubjectOrientation.Source"/> == "none" only when coverage
///   exists for the subject but resolution failed the Q5 concentration rule (a plan whose
///   placed units disagree). This is still a resolved-but-empty result, distinct from no data.
/// </summary>
public interface IOrientationProvider
{
    Task<SubjectOrientation?> ResolveAsync(string propertyKey, string subjectId, CancellationToken ct);
}

/// <summary>
/// The SightMap unit-placement projection shape: one row per unit, keyed loosely by
/// unit/building/level, with the SightMap-derived facing bearing (true-north degrees,
/// 0 = north, clockwise) when known. <see cref="FacingDegrees"/> is null when SightMap has
/// no facing for that unit; such units are excluded from the Q5 concentration denominator.
/// </summary>
public record UnitPlacement(string UnitNumber, string? Building, int? Level, double? FacingDegrees);
