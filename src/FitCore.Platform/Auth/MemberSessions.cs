using Microsoft.AspNetCore.Http;

namespace FitCore.Platform.Auth;

/// <summary>Starts and ends a member's web session: the session cookie, and Supabase's side of sign-out.</summary>
public sealed class MemberSessions
{
    private readonly SessionCookie _cookie;
    private readonly MemberAuth _auth;

    internal MemberSessions(SessionCookie cookie, MemberAuth auth)
    {
        _cookie = cookie;
        _auth = auth;
    }

    /// <summary>Puts a new session in the response's cookie. The page then does a full load as the member (ADR 0005 decision 2).</summary>
    public void Start(HttpContext context, AuthSession session)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(session);
        _cookie.Write(context, session);
    }

    /// <summary>Whether the request carries a session at all, valid or not. Authentication has already run, so a session it refused is gone.</summary>
    public bool HasSession(HttpContext context) => _cookie.Current(context) is not null;

    /// <summary>
    /// Signs the member out (ADR 0005 decision 7): Supabase revokes the session's refresh tokens, the cookie goes, and
    /// <c>Clear-Site-Data</c> tells the browser to drop everything the web client cached. If Supabase can't be reached
    /// the member stays signed in, rather than leaving a refresh token alive behind a deleted cookie (NFR-SEC-4).
    /// </summary>
    /// <returns>False if Supabase couldn't revoke the session; the cookie is kept so the member can try again.</returns>
    public async Task<bool> EndAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Authentication already ran for this request and refreshed the session if it was close to expiring.
        if (_cookie.Current(context) is { } session)
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                // A session authentication couldn't use: Supabase was unreachable and the access token has expired.
                return false;
            }

            var outcome = await _auth.SignOutAsync(session.AccessToken, context.Connection.RemoteIpAddress, context.RequestAborted);
            if (outcome.Status is AuthStatus.Unavailable or AuthStatus.RateLimited)
            {
                return false;
            }
        }

        SessionCookie.Delete(context);
        context.Response.Headers["Clear-Site-Data"] = "\"cache\", \"storage\"";
        return true;
    }
}
