using FitCore.Platform.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FitCore.Modules.Identity;

/// <summary>Profile, age gate, roles and consents (NFR-PRV-7). Owns the <c>identity</c> schema.</summary>
public sealed class IdentityModule : IModule
{
    public string Name => "Identity";

    public string Schema => "identity";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder v1)
    {
    }
}
