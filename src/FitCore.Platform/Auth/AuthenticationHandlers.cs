using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FitCore.Platform.Resources;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FitCore.Platform.Auth;

/// <summary>Where the API lives, so challenges answer API callers with a status code and browsers with a page.</summary>
internal sealed class ApiPathOptions
{
    public PathString Prefix { get; set; } = "/v1";
}

/// <summary>Shared by both handlers: what a challenge sends when the caller isn't signed in or Supabase Auth is down.</summary>
internal static class Challenges
{
    /// <summary>Set when Supabase Auth couldn't be reached, so the challenge answers 503 instead of signing the member out.</summary>
    public const string UnavailableKey = "FitCore.Auth.Unavailable";

    public static async Task UnavailableAsync(HttpContext context, bool isApi)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter = "5";
        if (!isApi)
        {
            // A body of its own, so the status code pages don't swap in the not-found page.
            context.Response.ContentType = "text/html; charset=utf-8";
            static string Html(string text) => WebUtility.HtmlEncode(text);
            await context.Response.WriteAsync(
                $"<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>{Html(AuthStrings.OutageTitle)}</title></head><body>"
                + $"<h1>{Html(AuthStrings.OutageHeading)}</h1><p>{Html(AuthStrings.OutageBody)}</p>"
                + "</body></html>");
        }
    }
}

/// <summary>
/// <c>Authorization: Bearer</c> callers, such as the MAUI apps (implementation plan §3.3). The access token is
/// validated the same way as the cookie's.
/// </summary>
internal sealed class BearerHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AccessTokens tokens,
    IOptions<ApiPathOptions> api)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        try
        {
            var identity = await tokens.ValidateAsync(header["Bearer ".Length..].Trim(), Scheme.Name, Context.RequestAborted);
            return identity is null
                ? AuthenticateResult.Fail("The access token is invalid or expired.")
                : AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
        }
        catch (HttpRequestException)
        {
            Context.Items[Challenges.UnavailableKey] = true;
            return AuthenticateResult.Fail("Supabase Auth's signing keys are unavailable.");
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Context.Items.ContainsKey(Challenges.UnavailableKey))
        {
            return Challenges.UnavailableAsync(Context, Request.Path.StartsWithSegments(api.Value.Prefix));
        }

        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }
}

/// <summary>
/// The web session cookie (ADR 0005 decisions 1 and 4). Refreshes the session with Supabase when the access token has
/// less than two minutes left. A refused refresh token ends the session; an unreachable Supabase doesn't.
/// </summary>
internal sealed class SessionCookieHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SessionCookie cookie,
    MemberAuth auth,
    AccessTokens tokens,
    TimeProvider time,
    IOptions<ApiPathOptions> api)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>How long before the access token expires the session is refreshed.</summary>
    public static readonly TimeSpan RefreshAhead = TimeSpan.FromMinutes(2);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var session = cookie.Read(Context);
        if (session is null)
        {
            if (Request.Cookies.ContainsKey(SessionCookie.Name))
            {
                // Forged, garbled, or encrypted with a key this key ring no longer holds.
                SessionCookie.Delete(Context);
            }

            return AuthenticateResult.NoResult();
        }

        try
        {
            var now = time.GetUtcNow();
            if (session.ExpiresAt - now < RefreshAhead)
            {
                var outcome = await auth.RefreshAsync(session.RefreshToken, Context.Connection.RemoteIpAddress, Context.RequestAborted);
                switch (outcome.Status)
                {
                    case AuthStatus.SignedIn:
                        session = outcome.Session!;
                        cookie.Write(Context, session);
                        break;
                    case AuthStatus.Unavailable or AuthStatus.RateLimited when session.ExpiresAt > now:
                        // Not refreshed, but the access token still works; the next request tries again.
                        break;
                    case AuthStatus.Unavailable or AuthStatus.RateLimited:
                        return Unavailable();
                    default:
                        // Supabase refused the refresh token: the session is over.
                        SessionCookie.Delete(Context);
                        return AuthenticateResult.NoResult();
                }
            }

            var identity = await tokens.ValidateAsync(session.AccessToken, Scheme.Name, Context.RequestAborted);
            if (identity is null)
            {
                SessionCookie.Delete(Context);
                return AuthenticateResult.NoResult();
            }

            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
        }
        catch (HttpRequestException)
        {
            return Unavailable();
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var isApi = Request.Path.StartsWithSegments(api.Value.Prefix);
        if (Context.Items.ContainsKey(Challenges.UnavailableKey))
        {
            return Challenges.UnavailableAsync(Context, isApi);
        }

        if (isApi)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        // Pages send the member to sign in, and back to the same local page afterwards.
        var returnUrl = Request.PathBase + Request.Path + Request.QueryString;
        Response.Redirect("/account/sign-in?returnUrl=" + Uri.EscapeDataString(returnUrl));
        return Task.CompletedTask;
    }

    private AuthenticateResult Unavailable()
    {
        Context.Items[Challenges.UnavailableKey] = true;
        return AuthenticateResult.Fail("Supabase Auth is unavailable.");
    }
}
