using FitCore.Platform.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FitCore.Modules.Nutrition;

/// <summary>Diary, snapshots, recipes, saved meals, planner, groceries and purchases. Owns the <c>nutrition</c> schema.</summary>
public sealed class NutritionModule : IModule
{
    public string Name => "Nutrition";

    public string Schema => "nutrition";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder v1)
    {
    }
}
