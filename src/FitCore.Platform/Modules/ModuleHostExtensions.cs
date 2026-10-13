using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FitCore.Platform.Modules;

/// <summary>Registers modules with a host and maps their endpoints.</summary>
public static class ModuleHostExtensions
{
    /// <summary>Registers each module's services and the module list itself.</summary>
    public static IServiceCollection AddModules(this IServiceCollection services, IConfiguration configuration, IReadOnlyList<IModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var duplicate = modules.GroupBy(m => m.Schema).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"More than one module claims the schema '{duplicate.Key}'.");
        }

        foreach (var module in modules)
        {
            module.AddServices(services, configuration);
        }

        services.AddSingleton(modules);
        return services;
    }

    /// <summary>Maps the endpoints of every registered module under <paramref name="v1"/>.</summary>
    public static IEndpointRouteBuilder MapModules(this IEndpointRouteBuilder v1)
    {
        ArgumentNullException.ThrowIfNull(v1);

        foreach (var module in v1.ServiceProvider.GetRequiredService<IReadOnlyList<IModule>>())
        {
            module.MapEndpoints(v1);
        }

        return v1;
    }
}
