using FitCore.Contracts.Profiles;
using FitCore.Platform.Auth;
using FitCore.Platform.Data;
using FitCore.Platform.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

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
        v1.MapGet("/profile", GetProfileAsync).RequireAuthorization();
    }

    // RLS returns only the caller's own row, so the query needs no filter of its own.
    private static async Task<IResult> GetProfileAsync(HttpContext context, FitCoreDatabase database, CancellationToken cancellationToken)
    {
        var identity = context.User.ToDbIdentity() ?? throw new InvalidOperationException("The endpoint requires a signed-in member.");
        var profile = await database.ReadAsync(
            identity,
            batch => batch.BatchCommands.Add(new NpgsqlBatchCommand("select display_name, time_zone, unit_system, currency from identity.profiles")),
            async (reader, ct) => await reader.ReadAsync(ct)
                ? new ProfileResponse(
                    reader.IsDBNull(0) ? null : reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3))
                : null,
            cancellationToken);

        return profile is null ? Results.NotFound() : Results.Ok(profile);
    }
}
