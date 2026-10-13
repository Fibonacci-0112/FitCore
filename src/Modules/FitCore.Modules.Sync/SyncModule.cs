using FitCore.Platform.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FitCore.Modules.Sync;

/// <summary>Offline sync protocol, conflict reporting and grant-status delivery. Owns the <c>sync</c> schema.</summary>
public sealed class SyncModule : IModule
{
    public string Name => "Sync";

    public string Schema => "sync";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder v1)
    {
    }
}
