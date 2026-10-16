namespace FitCore.Platform.Auth;

/// <summary>
/// A member's Supabase session. On the web it lives only in the encrypted session cookie (ADR 0005 decision 1) and
/// never reaches WebAssembly, JavaScript or browser storage.
/// </summary>
/// <param name="AccessToken">The short-lived access token, validated on every request.</param>
/// <param name="RefreshToken">The rotating refresh token.</param>
/// <param name="ExpiresAt">When the access token expires.</param>
public sealed record AuthSession(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt)
{
    /// <summary>Leaves the tokens out, so a session that reaches a log or an exception message gives nothing away.</summary>
    public override string ToString() => $"AuthSession {{ ExpiresAt = {ExpiresAt:O} }}";
}

/// <summary>What happened when fitcore-app asked Supabase Auth to sign a member in, up or out.</summary>
public enum AuthStatus
{
    /// <summary>The member has a session.</summary>
    SignedIn,

    /// <summary>The account exists and Supabase sent a confirmation email; there is no session until it's confirmed.</summary>
    ConfirmationSent,

    /// <summary>The email address and password don't match an account.</summary>
    InvalidCredentials,

    /// <summary>The account's email address isn't confirmed yet.</summary>
    EmailNotConfirmed,

    /// <summary>An account with the email address already exists.</summary>
    EmailTaken,

    /// <summary>Supabase rejected the password as too weak or breached (NFR-SEC-2).</summary>
    WeakPassword,

    /// <summary>The birth date is under 18, so no account was created (BRD §6.2).</summary>
    Underage,

    /// <summary>The confirmation link is expired, used or wrong.</summary>
    InvalidLink,

    /// <summary>The session is over: Supabase refused its refresh token, or signed it out.</summary>
    SessionEnded,

    /// <summary>Supabase refused the request for a reason none of the others name, such as an email address it won't accept.</summary>
    Refused,

    /// <summary>Too many attempts from this address or account; try again later.</summary>
    RateLimited,

    /// <summary>Supabase Auth didn't answer, or answered with a server error. Nothing is known about the session.</summary>
    Unavailable,
}

/// <summary>The status of an Auth call and, when the member is signed in, their session.</summary>
public sealed record AuthOutcome(AuthStatus Status, AuthSession? Session = null);
