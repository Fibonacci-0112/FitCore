using FitCore.Platform.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FitCore.Modules.Targets;

/// <summary>Estimates, guardrails, overrides, acceptance and precedence. Owns the <c>targets</c> schema.</summary>
public sealed class TargetsModule : IModule
{
    public string Name => "Targets";

    public string Schema => "targets";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder v1)
    {
    }
}
