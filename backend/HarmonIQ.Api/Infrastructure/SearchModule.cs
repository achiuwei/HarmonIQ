using HarmonIQ.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HarmonIQ.Api.Infrastructure;

/// <summary>
/// Registers <see cref="ISearchService"/>. Scoped: it holds the per-request
/// <c>HarmonIQDbContext</c> (via the scoped services it depends on — <see cref="IPublicationService"/>
/// and <see cref="IEngineVersionService"/>, both registered scoped by <c>PublishingModule</c>) and
/// directly queries <c>db.Subjects</c> itself for the local demo corpus count and subject-id
/// resolution, so a singleton here would capture a disposed context.
/// </summary>
public class SearchModule : IServiceModule
{
    public void Register(IServiceCollection services, IConfiguration config)
    {
        services.AddScoped<ISearchService, SearchService>();
    }
}
