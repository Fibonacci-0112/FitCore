using FitCore.Platform.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FitCore.Modules.Progress;

/// <summary>Weight, measurements, photos, wellness and adaptive-target proposals. Owns the <c>progress</c> schema.</summary>
public sealed class ProgressModule : IModule
{
    public string Name => "Progress";

    public string Schema => "progress";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder v1)
    {
    }
}
