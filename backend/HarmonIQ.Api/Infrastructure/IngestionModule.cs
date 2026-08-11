using HarmonIQ.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HarmonIQ.Api.Infrastructure;

/// <summary>
/// Registers task 6's ingestion seam: the LDP scraper, the content-signature image loader, the
/// demo plan source (<see cref="SampleListingProvider"/>, already registered as itself by
/// Program.cs — this module only adds the <see cref="IPlanSource"/> facet), and
/// <see cref="ISubjectService"/>, which is scoped because it depends on the scoped
/// <c>HarmonIQDbContext</c>.
/// </summary>
public class IngestionModule : IServiceModule
{
    public void Register(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<IPlanScraper, PlanScraper>();
        services.AddSingleton<IPlanImageLoader, FilePlanImageLoader>();
        services.AddSingleton<IPlanSource>(sp => sp.GetRequiredService<SampleListingProvider>());
        services.AddScoped<ISubjectService, SubjectService>();
    }
}
