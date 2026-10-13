using FitCore.Platform.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FitCore.Modules.Care;

/// <summary>Grants and scopes, roster, plan assignment, comments, check-ins and messaging. Owns the <c>care</c> schema.</summary>
public sealed class CareModule : IModule
{
    public string Name => "Care";

    public string Schema => "care";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder v1)
    {
    }
}
