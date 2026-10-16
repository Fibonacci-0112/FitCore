namespace FitCore.Client.Auth;

/// <summary>The Supabase project the device apps sign members in with.</summary>
/// <param name="Url">The project URL, such as <c>https://&lt;ref&gt;.supabase.co</c>, or the local stack's address.</param>
/// <param name="PublishableKey">
/// The project's publishable key (<c>sb_publishable_…</c>). It is meant to ship in apps: it only lets callers reach the
/// public Auth endpoints, and RLS decides what any signed-in member can see. Never a secret key (NFR-SEC-5).
/// </param>
public sealed record SupabaseAuthOptions(Uri Url, string PublishableKey)
{
    /// <summary>How long a call to Supabase Auth may take before the app says it couldn't reach it.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>The Auth API base, <c>{Url}/auth/v1</c>.</summary>
    internal string AuthUrl => Url.ToString().TrimEnd('/') + "/auth/v1";
}
