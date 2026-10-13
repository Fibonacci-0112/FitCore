using FitCore.Platform.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FitCore.Modules.Integrations;

/// <summary>AI vision with quotas, health-platform and device sync, calendar and notifications. Owns the <c>integrations</c> schema.</summary>
public sealed class IntegrationsModule : IModule
{
    public string Name => "Integrations";

    public string Schema => "integrations";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder v1)
    {
    }
}
