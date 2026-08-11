using HarmonIQ.Api.Commands;
using HarmonIQ.Api.Services.Batch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HarmonIQ.Api.Infrastructure;

/// <summary>
/// Registers the backfill command and its scoring drivers. <see cref="BackfillCommand"/> is a
/// singleton discovered by <c>CommandRunner</c> via <c>GetServices&lt;IHarmonIQCommand&gt;()</c>
/// off the <b>root</b> service provider (Program.cs calls it before any request scope exists) —
/// it therefore takes only singleton-safe dependencies (<see cref="IServiceScopeFactory"/>,
/// <see cref="IConfiguration"/>, a logger) and opens its own scope per run to resolve the scoped
/// persistence/analysis services (<c>HarmonIQDbContext</c>, <c>ISubjectService</c>, etc.).
/// </summary>
public class CommandsModule : IServiceModule
{
    public void Register(IServiceCollection services, IConfiguration config)
    {
        services.AddScoped<IScoringDriver, InteractiveScoringDriver>();
        services.AddSingleton<IBatchScoringClient, StubBatchScoringClient>();
        services.AddSingleton<IHarmonIQCommand, BackfillCommand>();
    }
}
