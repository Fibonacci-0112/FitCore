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
    /// <c>Clear-Site-Data</c> tells the browser to drop everything the web client cached. If Supabase can't confirm it
    /// the member stays signed in, rather than leaving a refresh token alive behind a deleted cookie (NFR-SEC-4).
    /// A request that arrived without a session gets nothing: a cross-site POST comes without the SameSite=Lax cookie,
    /// and must not delete it or clear the site. One whose session authentication just ended, such as by a refused
    /// refresh, still gets the browser cleared if it came from this origin. Its antiforgery token was issued to the
    /// signed-in member and no longer validates, so the origin check stands in for it.
    /// </summary>
    /// <returns>False if Supabase didn't confirm it revoked the session; the cookie is kept so the member can try again.</returns>
    public async Task<bool> EndAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Authentication already ran for this request and refreshed the session if it was close to expiring.
        if (_cookie.Current(context) is not { } session)
        {
            if (_cookie.Read(context) is not null && CrossSiteWriteProtection.IsSameOrigin(context.Request))
            {
                ClearBrowser(context);
            }

            return true;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            // A session authentication couldn't use: Supabase was unreachable and the access token has expired.
            return false;
        }

        // Only a confirmed sign-out deletes the cookie: a rejected API key, for one, revokes nothing.
        var outcome = await _auth.SignOutAsync(session.AccessToken, context.Connection.RemoteIpAddress, context.RequestAborted);
        if (outcome.Status != AuthStatus.SessionEnded)
        {
            return false;
        }

        SessionCookie.Delete(context);
        ClearBrowser(context);
        return true;
    }

    private static void ClearBrowser(HttpContext context) => context.Response.Headers["Clear-Site-Data"] = "\"cache\", \"storage\"";
}
