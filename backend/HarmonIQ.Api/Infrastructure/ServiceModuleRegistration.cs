using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HarmonIQ.Api.Infrastructure;

public static class ServiceModuleRegistration
{
    /// <summary>
    /// Reflects over the API assembly, instantiates every non-abstract IServiceModule
    /// with a parameterless constructor, and calls Register on each in stable
    /// type-name order. This is the only way modules get wired up; Program.cs simply
    /// calls this once.
    /// </summary>
    public static IServiceCollection AddHarmonIQModules(this IServiceCollection services, IConfiguration config)
    {
        var assembly = typeof(ServiceModuleRegistration).Assembly;

        var moduleTypes = assembly.GetTypes()
            .Where(t => typeof(IServiceModule).IsAssignableFrom(t)
                && !t.IsAbstract
                && !t.IsInterface
                && t.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

        foreach (var moduleType in moduleTypes)
        {
            var module = (IServiceModule)Activator.CreateInstance(moduleType)!;
            module.Register(services, config);
        }

        return services;
    }
}
