using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace FitCore.Platform.Limits;

/// <summary>Registers the rate limits (NFR-SCL-5) with a host.</summary>
public static class LimitServiceExtensions
{
    /// <summary>The connection string that names the shared Valkey: <c>ConnectionStrings:valkey</c>, as the AppHost sets it.</summary>
    public const string ConnectionStringName = "valkey";

    /// <summary>
    /// Registers <see cref="SignInLimits"/> with its counters in Valkey when <c>ConnectionStrings:valkey</c> is set, and
    /// in memory otherwise. Only a Development host may count in memory: deployed instances must share their counts,
    /// or each would allow the full limit (NFR-SCL-2).
    /// </summary>
    public static IServiceCollection AddFitCoreLimits(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SignInLimits>();

        if (configuration.GetConnectionString(ConnectionStringName) is { Length: > 0 } valkey)
        {
            var options = ConfigurationOptions.Parse(valkey);

            // Starts without Valkey and reconnects in the background; until then the counters let requests through.
            options.AbortOnConnectFail = false;
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options));
            services.AddSingleton<ILimitStore>(sp => new ValkeyLimitStore(
                sp.GetRequiredService<IConnectionMultiplexer>(), "fitcore:limits:", sp.GetRequiredService<ILogger<ValkeyLimitStore>>()));
        }
        else if (environment.IsDevelopment())
        {
            services.AddSingleton<ILimitStore, MemoryLimitStore>();
        }
        else
        {
            throw new InvalidOperationException(
                $"A {environment.EnvironmentName} host needs ConnectionStrings:{ConnectionStringName}: rate-limit counters must be shared by every instance (NFR-SCL-2).");
        }

        return services;
    }
}
