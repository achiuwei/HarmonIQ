namespace HarmonIQ.Api.Commands;

/// <summary>
/// A CLI command runnable via `dotnet run --project backend/HarmonIQ.Api -- &lt;name&gt; [args]`
/// instead of starting the web host. Implementations register themselves as
/// <c>IHarmonIQCommand</c> from their own Infrastructure/&lt;Area&gt;Module.cs (see
/// <see cref="HarmonIQ.Api.Infrastructure.IServiceModule"/>) — this is the seam that lets
/// later tasks add commands without ever opening Program.cs.
/// </summary>
public interface IHarmonIQCommand
{
    /// <summary>Matched against <c>args[0]</c>, case-sensitive, e.g. "backfill", "task-zero".</summary>
    string Name { get; }

    /// <summary>One-line summary shown by <c>--help</c>.</summary>
    string Description { get; }

    /// <summary>Runs with the remaining args (i.e. <c>args[1..]</c>). Returns the process exit code.</summary>
    Task<int> RunAsync(string[] args, CancellationToken ct);
}
