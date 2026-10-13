using FitCore.Platform.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FitCore.Modules.Audit;

/// <summary>Audit queries and the member-visible audit view. Owns the <c>audit</c> schema.</summary>
public sealed class AuditModule : IModule
{
    public string Name => "Audit";

    public string Schema => "audit";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder v1)
    {
    }
}
