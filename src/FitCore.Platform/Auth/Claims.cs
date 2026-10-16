using System.Security.Claims;
using FitCore.Platform.Data;

namespace FitCore.Platform.Auth;

/// <summary>The authentication schemes and the claims FitCore reads from a validated caller.</summary>
public static class Claims
{
    /// <summary>The default scheme: a policy scheme that forwards to <see cref="BearerScheme"/> or <see cref="CookieScheme"/> (ADR 0005 decision 3).</summary>
    public const string Scheme = "Supabase";

    /// <summary>MAUI and other API clients: <c>Authorization: Bearer</c> with a Supabase access token.</summary>
    public const string BearerScheme = "SupabaseBearer";

    /// <summary>The web: the encrypted session cookie.</summary>
    public const string CookieScheme = "SupabaseCookie";

    /// <summary>FitCore's own role claim. Supabase's <c>role</c> claim is always <c>authenticated</c>, so roles come from elsewhere (Coach/Pro).</summary>
    public const string Role = "fitcore:role";

    /// <summary>Whether the caller was authenticated by the session cookie, which browsers send on their own.</summary>
    public static bool IsCookieSession(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.Identities.Any(i => i.IsAuthenticated && i.AuthenticationType == CookieScheme);
    }

    /// <summary>
    /// The identity a unit of work runs as: the token's <c>sub</c> and <c>aal</c>. Null for an anonymous caller.
    /// </summary>
    public static DbIdentity? ToDbIdentity(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var identity = principal.Identities.FirstOrDefault(i => i.IsAuthenticated && i.AuthenticationType is BearerScheme or CookieScheme);
        return identity is not null && Guid.TryParse(identity.FindFirst("sub")?.Value, out var subject)
            ? new DbIdentity(subject, identity.FindFirst("aal")?.Value ?? "aal1")
            : null;
    }
}
