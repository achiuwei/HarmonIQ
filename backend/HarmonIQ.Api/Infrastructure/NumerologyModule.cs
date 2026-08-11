using HarmonIQ.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HarmonIQ.Api.Infrastructure;

/// <summary>
/// Registers <see cref="NumerologyService"/>. The service is stateless and pure —
/// no DbContext, no external calls — so it is safe as a singleton, both for the
/// subject-level check that feeds <c>ScoreMath.Aggregate</c> and for the read-time
/// per-unit annotations that never touch the database.
/// </summary>
public class NumerologyModule : IServiceModule
{
    public void Register(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<NumerologyService>();
    }
}
