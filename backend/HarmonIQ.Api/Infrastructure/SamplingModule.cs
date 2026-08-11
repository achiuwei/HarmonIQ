using HarmonIQ.Api.Commands;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HarmonIQ.Api.Infrastructure;

/// <summary>
/// Registers the <c>task-zero</c> command (plan Task 13). <see cref="TaskZeroCommand"/> is a
/// singleton — <see cref="CommandRunner"/> resolves <c>IHarmonIQCommand</c> from the root
/// provider, so the command itself must not depend on scoped services directly. It takes an
/// <c>IServiceScopeFactory</c> instead and opens its own scope around the scoped
/// <c>HarmonIQDbContext</c> / <c>ISubjectService</c> / <c>IAnalysisPipeline</c> it needs.
/// </summary>
public class SamplingModule : IServiceModule
{
    public void Register(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<IHarmonIQCommand, TaskZeroCommand>();
    }
}
