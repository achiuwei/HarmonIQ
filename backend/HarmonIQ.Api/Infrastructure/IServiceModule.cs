using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HarmonIQ.Api.Infrastructure;

/// <summary>
/// DI registration seam. Program.cs is edited exactly once in the whole plan; every
/// other task that needs DI creates its own Infrastructure/&lt;Area&gt;Module.cs
/// implementing this interface, discovered and invoked by
/// ServiceModuleRegistration.AddHarmonIQModules.
/// </summary>
public interface IServiceModule
{
    void Register(IServiceCollection services, IConfiguration config);
}
