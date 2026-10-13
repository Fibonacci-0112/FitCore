using FitCore.Platform.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FitCore.Modules.DataRights;

/// <summary>Export, import (MyFitnessPal) and deletion. Owns the <c>data_rights</c> schema.</summary>
public sealed class DataRightsModule : IModule
{
    public string Name => "DataRights";

    public string Schema => "data_rights";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder v1)
    {
    }
}
