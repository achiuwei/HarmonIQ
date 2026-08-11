namespace HarmonIQ.Api.Models;

/// <summary>
/// One page of the versioned grades feed. Always scoped to a single, explicit
/// <see cref="EngineVersion"/> — a reader (SRP badge, LDP chip) that requests the same version
/// twice gets the same rows, even if a newer version has since been published. This is what
/// keeps an SRP badge and an LDP card fetched seconds apart in agreement.
/// </summary>
public record GradesFeedPage(string EngineVersion, IReadOnlyList<ProjectionRow> Rows, string? NextCursor);

/// <summary>
/// The outcome of a <c>PublishVersionAsync</c> call. Returned identically whether the call
/// performed the publish or found the version already published (idempotent no-op).
/// </summary>
public record PublishResult(string EngineVersion, int RowsWritten, DateTimeOffset PublishedAt);
