using HarmonIQ.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarmonIQ.Api.Infrastructure;

/// <summary>
/// Registers the perception/judgment pipeline: the Claude seam, the two perception services
/// (live + demo), the floor-plan lens, the evidence loader, the report body writer, and
/// <see cref="IAnalysisPipeline"/> itself.
///
/// The pipeline is <b>scoped</b> because it writes through the scoped
/// <c>HarmonIQDbContext</c>; everything it depends on is stateless and registered as a singleton.
/// Shared services other modules also need (<see cref="SiteAnalysisService"/>,
/// <see cref="NumerologyService"/>, <see cref="IClaudeClient"/>) go in with <c>TryAdd</c> so
/// whichever module runs first wins and no duplicate registration shadows another.
/// </summary>
public class AnalysisModule : IServiceModule
{
    public void Register(IServiceCollection services, IConfiguration config)
    {
        services.AddHttpClient();
        services.TryAddSingleton<SiteAnalysisService>();
        services.TryAddSingleton<NumerologyService>();

        if (services.All(d => d.ServiceType != typeof(IClaudeClient)))
        {
            services.AddHttpClient<IClaudeClient, ClaudeClient>(c => c.Timeout = TimeSpan.FromSeconds(60));
        }

        services.TryAddSingleton<MockAnalysisService>();
        services.TryAddSingleton<ClaudeAnalysisService>();

        services.TryAddSingleton<IFloorPlanLens, FloorPlanLensService>();
        services.TryAddSingleton<IEvidenceLoader, FileEvidenceLoader>();
        services.TryAddSingleton<ReportBodyWriter>();

        services.TryAddScoped<IAnalysisPipeline, AnalysisPipeline>();
    }
}
