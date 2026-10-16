using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FitCore.Platform.Auth;

/// <summary>Registers member authentication (ADR 0005) with a host.</summary>
public static class AuthServiceExtensions
{
    /// <summary>
    /// Registers the Supabase Auth client, access-token validation, the session cookie and the authentication scheme
    /// that serves bearer and cookie callers alike.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">Holds the <c>Supabase</c> section.</param>
    /// <param name="apiPrefix">The API's path, such as <c>/v1</c>: challenges there answer with a status code, not a sign-in page.</param>
    public static IServiceCollection AddFitCoreAuthentication(this IServiceCollection services, IConfiguration configuration, PathString apiPrefix)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<SupabaseOptions>(configuration.GetSection(SupabaseOptions.Section));
        services.Configure<ApiPathOptions>(o => o.Prefix = apiPrefix);
        services.AddHttpClient(MemberAuth.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<AccessTokens>();
        services.AddSingleton<SessionCookie>();
        services.AddSingleton<MemberAuth>();
        services.AddSingleton(sp => new MemberSessions(sp.GetRequiredService<SessionCookie>(), sp.GetRequiredService<MemberAuth>()));

        // Every instance reads every other's cookies: one application name, and (deployed) one shared key ring
        // (ADR 0005 decision 8). Locally the key ring is the default per-machine one.
        services.AddDataProtection().SetApplicationName("fitcore-app");

        services.AddAuthentication(Claims.Scheme)
            .AddPolicyScheme(Claims.Scheme, "Supabase", o => o.ForwardDefaultSelector = context =>
                context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? Claims.BearerScheme
                    : Claims.CookieScheme)
            .AddScheme<AuthenticationSchemeOptions, BearerHandler>(Claims.BearerScheme, null)
            .AddScheme<AuthenticationSchemeOptions, SessionCookieHandler>(Claims.CookieScheme, null);
        services.AddAuthorization();

        return services;
    }
}
