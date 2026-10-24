namespace FitCore.Client.Core.Accounts;

/// <summary>
/// The member's account with Supabase Auth, as the device apps reach it (implementation plan §3.3). The web never
/// uses this: its sign-in runs on the server (ADR 0005).
/// </summary>
public interface IMemberAccounts
{
    /// <summary>
    /// Creates an account and has Supabase Auth email a confirmation link. The database makes the profile from the
    /// request in the same transaction, and refuses an under-18 birth date whatever the app checked.
    /// </summary>
    /// <returns>What happened. Failures from Supabase Auth or the network come back as a status, never as an exception.</returns>
    Task<SignUpStatus> SignUpAsync(SignUpRequest request, CancellationToken cancellationToken = default);

    /// <summary>Signs a member in with their email address and password.</summary>
    /// <returns>The new session when <see cref="SignInStatus.SignedIn"/>. Failures come back as a status, never as an exception.</returns>
    Task<SignInResult> SignInAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Swaps the session's refresh token for a new session. Refresh tokens rotate, so the old one stops working once
    /// Supabase's reuse interval has passed.
    /// </summary>
    Task<RefreshResult> RefreshAsync(MemberSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the session and revokes its refresh tokens (NFR-SEC-4). Only <see cref="SignOutStatus.SessionEnded"/> means
    /// the session is gone; <see cref="SignOutStatus.Unavailable"/> may have left it alive.
    /// </summary>
    Task<SignOutStatus> SignOutAsync(MemberSession session, CancellationToken cancellationToken = default);
}

/// <summary>A new member's sign-up.</summary>
/// <param name="Email">The address Supabase Auth sends the confirmation link to.</param>
/// <param name="Password">15 to 64 characters, no composition rules (NFR-SEC-2).</param>
/// <param name="DisplayName">The name FitCore greets the member by.</param>
/// <param name="BirthDate">Checked against 18+ on the member's local date (BRD §6.2).</param>
/// <param name="TimeZone">The device's IANA time zone; diary dates follow it (BRD §6.1).</param>
public sealed record SignUpRequest(string Email, string Password, string DisplayName, DateOnly BirthDate, string TimeZone)
{
    /// <summary>Leaves the password out, so a request never reaches a log or crash report with it (NFR-PRV-6).</summary>
    public override string ToString() => nameof(SignUpRequest);
}

/// <summary>How a sign-up ended.</summary>
public enum SignUpStatus
{
    /// <summary>The account exists and the confirmation email is on its way.</summary>
    ConfirmationSent,

    /// <summary>An account with the address already exists and is confirmed.</summary>
    EmailTaken,

    /// <summary>Supabase Auth refused the password: too short, or found in a breach.</summary>
    WeakPassword,

    /// <summary>The database refused the birth date: the member is under 18.</summary>
    Underage,

    /// <summary>Too many attempts from this device or address.</summary>
    RateLimited,

    /// <summary>Supabase Auth refused the details for another reason, such as an address it won't send to.</summary>
    Refused,

    /// <summary>Supabase Auth couldn't be reached or failed. Trying again later may work.</summary>
    Unavailable,
}

/// <summary>How a sign-in ended, with the session when it worked.</summary>
public sealed record SignInResult(SignInStatus Status, MemberSession? Session = null);

/// <summary>How a sign-in ended.</summary>
public enum SignInStatus
{
    /// <summary>The member is signed in.</summary>
    SignedIn,

    /// <summary>The address and password don't match an account. Never says which of the two was wrong.</summary>
    InvalidCredentials,

    /// <summary>The account exists, but the member hasn't opened the confirmation link yet.</summary>
    EmailNotConfirmed,

    /// <summary>Too many attempts from this device or address.</summary>
    RateLimited,

    /// <summary>Supabase Auth couldn't be reached or failed. Trying again later may work.</summary>
    Unavailable,
}

/// <summary>How a refresh ended, with the new session when it worked.</summary>
public sealed record RefreshResult(RefreshStatus Status, MemberSession? Session = null);

/// <summary>How a refresh ended.</summary>
public enum RefreshStatus
{
    /// <summary>A new session replaces the old one.</summary>
    Refreshed,

    /// <summary>Supabase Auth says the session is over: signed out elsewhere, expired, or the account is gone.</summary>
    SessionEnded,

    /// <summary>Supabase Auth couldn't be reached or failed. The session may still be alive.</summary>
    Unavailable,
}

/// <summary>How a sign-out ended.</summary>
public enum SignOutStatus
{
    /// <summary>The session is gone and its refresh tokens are revoked.</summary>
    SessionEnded,

    /// <summary>Supabase Auth couldn't be reached or failed, so the session may still be alive.</summary>
    Unavailable,
}
