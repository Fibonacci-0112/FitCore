namespace FitCore.Platform.Auth;

/// <summary>
/// The Supabase project fitcore-app signs members in with (ADR 0005 decision 2). Bound from the <c>Supabase</c>
/// configuration section.
/// </summary>
public sealed class SupabaseOptions
{
    /// <summary>The configuration section: <c>Supabase:Url</c> and <c>Supabase:SecretKey</c>.</summary>
    public const string Section = "Supabase";

    /// <summary>The project URL, such as <c>https://&lt;ref&gt;.supabase.co</c>, or <c>http://127.0.0.1:54321</c> locally.</summary>
    public Uri? Url { get; set; }

    /// <summary>
    /// A secret API key. Supabase accepts the member's IP in <c>Sb-Forwarded-For</c> only with one. It stays in the
    /// server-side secret store (NFR-SEC-5) and is sent only to Auth endpoints, never used for data.
    /// </summary>
    public string? SecretKey { get; set; }

    /// <summary>The Auth API base, <c>{Url}/auth/v1</c>, which is also the access tokens' issuer.</summary>
    internal Uri AuthUrl => new(RequiredUrl, "auth/v1/");

    /// <summary>The issuer claim the project's access tokens carry.</summary>
    internal string Issuer => new Uri(RequiredUrl, "auth/v1").ToString();

    internal string RequiredSecretKey => string.IsNullOrWhiteSpace(SecretKey)
        ? throw new InvalidOperationException(
            $"{Section}:SecretKey is not set. Locally, set it from `npx supabase status` with user secrets or the Supabase__SecretKey environment variable (supabase/README.md).")
        : SecretKey;

    private Uri RequiredUrl => Url is { IsAbsoluteUri: true } url
        ? new Uri(url.ToString().TrimEnd('/') + "/")
        : throw new InvalidOperationException($"{Section}:Url is not set to an absolute URL.");
}
