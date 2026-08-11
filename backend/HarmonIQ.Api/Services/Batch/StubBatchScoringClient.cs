using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services.Batch;

/// <summary>
/// The batch path's local stand-in. No Claude Batch API endpoint or key exists on this machine,
/// so both members throw unconditionally — this class exists only to satisfy the DI seam and to
/// make the config gate explicit: reaching this class at all means
/// <c>SCORING_MODE=batch &amp;&amp; BATCH_API_ENABLED=true</c>, which is not this machine's demo
/// configuration (both default off in <c>appsettings.json</c>).
/// </summary>
public class StubBatchScoringClient : IBatchScoringClient
{
    public const string UnavailableMessage = "Batch scoring requires BATCH_API_ENABLED and a live Claude key";

    public Task<string> SubmitAsync(IReadOnlyList<ScoringJob> jobs, CancellationToken ct) =>
        throw new NotSupportedException(UnavailableMessage);

    public Task<BatchStatus> PollAsync(string batchId, CancellationToken ct) =>
        throw new NotSupportedException(UnavailableMessage);
}
