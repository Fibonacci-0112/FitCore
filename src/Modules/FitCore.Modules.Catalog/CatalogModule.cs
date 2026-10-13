using FitCore.Platform.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FitCore.Modules.Catalog;

/// <summary>Shared and custom foods and exercises, USDA bulk import and search, admin curation. Owns the <c>catalog</c> schema.</summary>
public sealed class CatalogModule : IModule
{
    public string Name => "Catalog";

    public string Schema => "catalog";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder v1)
    {
    }
}
