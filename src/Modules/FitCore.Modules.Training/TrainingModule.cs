using FitCore.Platform.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FitCore.Modules.Training;

/// <summary>Workouts, templates, programs, PRs and training load. Owns the <c>training</c> schema.</summary>
public sealed class TrainingModule : IModule
{
    public string Name => "Training";

    public string Schema => "training";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder v1)
    {
    }
}
