using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FitCore.Platform.Modules;

/// <summary>
/// A backend module (ADR 0003 decision 2). Each module owns one Postgres schema and exposes only its
/// <c>Contracts</c> namespace to other modules. Side effects that cross modules go through the outbox.
/// </summary>
public interface IModule
{
    /// <summary>The module's name, for example <c>DataRights</c>.</summary>
    string Name { get; }

    /// <summary>The Postgres schema the module owns: its name in snake case, for example <c>data_rights</c>.</summary>
    string Schema { get; }

    /// <summary>Registers the module's services. Called by both the app and the worker.</summary>
    void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Maps the module's endpoints under the <c>/v1</c> route group. Called by the app only.</summary>
    void MapEndpoints(IEndpointRouteBuilder v1);
}
