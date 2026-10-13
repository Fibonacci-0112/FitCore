using Microsoft.AspNetCore.Builder;

namespace FitCore.Platform.Http;

/// <summary>
/// Stops browsers and proxies from storing HTML. Prerendered pages and their persisted state carry a member's
/// health data (<see href="https://github.com/Fibonacci-0112/FitCore/blob/main/docs/spikes/blazor-prerendering-web-vitals.md">Blazor spike</see>, rule 3).
/// </summary>
public static class NoStoreHtmlMiddleware
{
    /// <summary>Sends <c>Cache-Control: no-store</c> on every <c>text/html</c> response.</summary>
    public static IApplicationBuilder UseNoStoreHtml(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                if (context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
                {
                    context.Response.Headers.CacheControl = "no-store";
                }

                return Task.CompletedTask;
            });
            return next();
        });
}
