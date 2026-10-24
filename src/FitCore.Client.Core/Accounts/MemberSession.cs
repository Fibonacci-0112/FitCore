namespace FitCore.Client.Core.Accounts;

/// <summary>A Supabase Auth session on a device, kept in the platform secure store (NFR-SEC-8).</summary>
/// <param name="AccessToken">The JWT the API checks, short-lived.</param>
/// <param name="RefreshToken">Swaps for a new session; single-use once Supabase's reuse interval has passed.</param>
/// <param name="ExpiresAt">When the access token stops working.</param>
public sealed record MemberSession(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt)
{
    /// <summary>Leaves the tokens out, so a session never reaches a log or crash report (NFR-PRV-6).</summary>
    public override string ToString() => nameof(MemberSession);
}
