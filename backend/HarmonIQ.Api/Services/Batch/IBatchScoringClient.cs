using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services.Batch;

/// <summary>State of a submitted batch, as the Claude Batch API would report it.</summary>
public enum BatchJobState
{
    Submitted,
    InProgress,
    Completed,
    Failed,
}

/// <summary>Poll result for a submitted batch.</summary>
public record BatchStatus(string BatchId, BatchJobState State, int Completed, int Total, string? Error);

/// <summary>
/// The Claude Batch API seam (design §6/§12). This is a <b>config-gated stub</b> locally:
/// <see cref="StubBatchScoringClient"/> throws on every call because no live Claude key and no
/// batch endpoint exist on this machine. <see cref="BackfillCommand"/> only ever reaches this
/// interface when <c>Scoring:Mode == "batch"</c> and <c>Scoring:BatchApiEnabled == true</c> — both
/// default off, so the batch path never runs in the local demo. The interactive path
/// (<see cref="InteractiveScoringDriver"/>, driving <see cref="AnalysisPipeline"/> directly) is
/// what the demo actually exercises.
/// </summary>
public interface IBatchScoringClient
{
    Task<string> SubmitAsync(IReadOnlyList<ScoringJob> jobs, CancellationToken ct);

    Task<BatchStatus> PollAsync(string batchId, CancellationToken ct);
}
