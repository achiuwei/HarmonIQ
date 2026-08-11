using HarmonIQ.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HarmonIQ.Api.Infrastructure;

/// <summary>
/// Registers the publishing seam: <see cref="IEngineVersionService"/> and
/// <see cref="IPublicationService"/>. Both are scoped to ride along with the
/// per-request/per-job <c>HarmonIQDbContext</c> registered by <c>PersistenceModule</c>.
/// </summary>
public class PublishingModule : IServiceModule
{
    public void Register(IServiceCollection services, IConfiguration config)
    {
        services.AddScoped<IEngineVersionService, EngineVersionService>();
        services.AddScoped<IPublicationService, PublicationService>();
    }
}
