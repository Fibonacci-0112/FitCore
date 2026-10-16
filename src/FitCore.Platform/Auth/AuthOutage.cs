using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FitCore.Platform.Auth;

/// <summary>
/// Answers a request whose session couldn't be checked because Supabase Auth is down (ADR 0005 decision 4): API
/// callers get 503 with <c>Retry-After</c>, browsers an error page with the same status. The cookie is kept. Without
/// this, a page anyone may open would render signed out, as if the session had ended.
/// </summary>
public static class AuthOutage
{
    /// <summary>Adds the check. Use it right after <c>UseAuthentication</c>.</summary>
    public static IApplicationBuilder UseAuthOutageResponse(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Items.ContainsKey(Challenges.UnavailableKey))
            {
                var api = context.RequestServices.GetRequiredService<IOptions<ApiPathOptions>>().Value.Prefix;
                await Challenges.UnavailableAsync(context, context.Request.Path.StartsWithSegments(api));
                return;
            }

            await next();
        });
}
