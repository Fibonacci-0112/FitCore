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
