using HarmonIQ.Api.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HarmonIQ.Api.Infrastructure;

/// <summary>
/// Registers the v2 API surface's own dependency: <see cref="SubjectsReadService"/>, the shared
/// read path behind the bulk subjects call, the report body, the grades feed and refine.
///
/// It is <b>scoped</b>, not singleton: it holds the per-request <c>HarmonIQDbContext</c> and the
/// scoped <c>ISubjectService</c> / <c>IAnalysisPipeline</c>, so a singleton here would capture a
/// disposed context. Everything else the controllers need is already registered by the modules
/// that own it — this module deliberately re-registers nothing.
/// </summary>
public class ApiModule : IServiceModule
{
    public void Register(IServiceCollection services, IConfiguration config)
    {
        services.AddScoped<SubjectsReadService>();
    }
}
