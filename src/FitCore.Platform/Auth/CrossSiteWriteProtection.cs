using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FitCore.Platform.Auth;

/// <summary>
/// Refuses cross-site writes to the API (ADR 0005 decision 5). A request authenticated by the session cookie with a
/// method other than GET, HEAD, OPTIONS or TRACE must carry the antiforgery token in a header and come from this
/// origin, or it gets 403 before any module code runs. Bearer requests skip both checks: browsers never attach
/// bearer tokens on their own.
/// </summary>
public static class CrossSiteWriteProtection
{
    /// <summary>Adds the checks. Use it on the API's branch, after <c>UseAuthentication</c>.</summary>
    public static IApplicationBuilder UseCrossSiteWriteProtection(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var method = context.Request.Method;
            if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method)
                || !context.User.IsCookieSession())
            {
                await next();
                return;
            }

            if (!IsSameOrigin(context.Request) || !await HasAntiforgeryTokenAsync(context))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next();
        });

    // Behind the load balancer this needs the forwarded scheme and host applied first (PR 9).
    private static bool IsSameOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (origin.Length > 0)
        {
            return string.Equals(origin, $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(request.Headers["Sec-Fetch-Site"].ToString(), "same-origin", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> HasAntiforgeryTokenAsync(HttpContext context)
    {
        try
        {
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }
}
