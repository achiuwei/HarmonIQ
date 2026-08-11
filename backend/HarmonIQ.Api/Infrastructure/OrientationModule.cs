using HarmonIQ.Api.Services.Orientation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HarmonIQ.Api.Infrastructure;

/// <summary>
/// Registers the orientation seam (design §4): <see cref="FixtureOrientationProvider"/> by
/// default (the only exercisable path locally — no SightMap key exists), or
/// <see cref="SightMapOrientationProvider"/> when <c>ORIENTATION_PROVIDER=sightmap</c> is set,
/// which is still unreachable end-to-end since <see cref="SightMapClient"/> is a stub.
/// </summary>
public class OrientationModule : IServiceModule
{
    public void Register(IServiceCollection services, IConfiguration config)
    {
        var providerName = config["ORIENTATION_PROVIDER"] ?? Environment.GetEnvironmentVariable("ORIENTATION_PROVIDER");

        if (string.Equals(providerName, "sightmap", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<ISightMapClient, SightMapClient>();
            services.AddSingleton<IOrientationProvider, SightMapOrientationProvider>();
        }
        else
        {
            services.AddSingleton<IOrientationProvider, FixtureOrientationProvider>();
        }
    }
}
