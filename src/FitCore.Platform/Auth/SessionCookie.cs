using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace FitCore.Platform.Auth;

/// <summary>
/// The web session cookie (ADR 0005 decision 1): the Supabase session, encrypted and signed with Data Protection, in
/// an HttpOnly, Secure, SameSite=Lax cookie whose <c>__Host-</c> prefix pins it to this origin and path <c>/</c>.
/// A session too big for one cookie is split into chunks (<c>__Host-fitcore-sessionC1</c>, …), which keep the prefix.
/// </summary>
internal sealed class SessionCookie
{
    public const string Name = "__Host-fitcore-session";

    /// <summary>The cookie outlives no session: the session time-box is 30 days (supabase/README.md, Auth settings).</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    private static readonly ChunkingCookieManager Chunks = new();

    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;

    public SessionCookie(IDataProtectionProvider dataProtection, TimeProvider time)
    {
        _protector = dataProtection.CreateProtector("FitCore.Auth.SessionCookie.v1");
        _time = time;
    }

    /// <summary>Whether the request brought a session cookie at all, whether or not it decrypts.</summary>
    public static bool IsPresent(HttpContext context) => !string.IsNullOrEmpty(Chunks.GetRequestCookie(context, Name));

    /// <summary>The session in the request's cookie, or null if there is none or it doesn't decrypt.</summary>
    public AuthSession? Read(HttpContext context)
    {
        var value = Chunks.GetRequestCookie(context, Name);
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(_protector.Unprotect(value));
            return stored is { A: { Length: > 0 } a, R: { Length: > 0 } r }
                ? new AuthSession(a, r, DateTimeOffset.FromUnixTimeSeconds(stored.E))
                : null;
        }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }

    public void Write(HttpContext context, AuthSession session)
    {
        var value = _protector.Protect(JsonSerializer.Serialize(new Stored(session.AccessToken, session.RefreshToken, session.ExpiresAt.ToUnixTimeSeconds())));
        Chunks.AppendResponseCookie(context, Name, value, Options(_time.GetUtcNow().Add(Lifetime)));
        context.Items[typeof(SessionCookie)] = session;
    }

    public static void Delete(HttpContext context)
    {
        Chunks.DeleteCookie(context, Name, Options(null));
        context.Items[typeof(SessionCookie)] = null;
    }

    /// <summary>The session this request ends with: one written or deleted during the request, otherwise the cookie's.</summary>
    public AuthSession? Current(HttpContext context) =>
        context.Items.TryGetValue(typeof(SessionCookie), out var written) ? written as AuthSession : Read(context);

    private static CookieOptions Options(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        IsEssential = true,
        Expires = expires,
    };

    private sealed record Stored(string A, string R, long E);
}
