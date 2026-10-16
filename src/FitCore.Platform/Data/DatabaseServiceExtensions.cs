using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FitCore.Platform.Data;

/// <summary>Registers <see cref="FitCoreDatabase"/> with a host.</summary>
public static class DatabaseServiceExtensions
{
    /// <summary>The connection string name: <c>ConnectionStrings:Fitcore</c>, for the <c>fitcore_api</c> login role.</summary>
    public const string ConnectionStringName = "Fitcore";

    /// <summary>
    /// Registers one <see cref="NpgsqlDataSource"/> and <see cref="FitCoreDatabase"/>. The connection string is read
    /// and checked when the database is first used, so a host that never touches the database starts without one.
    /// </summary>
    public static IServiceCollection AddFitCoreDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(_ =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException(
                    $"ConnectionStrings:{ConnectionStringName} is not set. Locally, start the stack with `npx supabase start` (supabase/README.md).");

            var builder = new NpgsqlDataSourceBuilder(ConnectionSettings.Check(connectionString));
            return new FitCoreDatabase(builder.Build());
        });

        return services;
    }
}
