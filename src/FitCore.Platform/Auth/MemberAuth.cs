using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace FitCore.Platform.Auth;

/// <summary>
/// Calls the Supabase Auth REST API for fitcore-app's server-side sign-in (ADR 0005 decision 2). Every call sends the
/// member's IP in <c>Sb-Forwarded-For</c>, so Supabase's per-IP rate limits count members, not fitcore-app's own
/// address. Nothing here logs request or response bodies: they hold passwords and tokens (NFR-PRV-6).
/// </summary>
public sealed class MemberAuth
{
    /// <summary>The named <see cref="HttpClient"/> for Supabase Auth, shared with the access-token validator.</summary>
    public const string HttpClientName = "FitCore.SupabaseAuth";

    private readonly IHttpClientFactory _httpClients;
    private readonly IOptions<SupabaseOptions> _options;
    private readonly TimeProvider _time;

    public MemberAuth(IHttpClientFactory httpClients, IOptions<SupabaseOptions> options, TimeProvider time)
    {
        _httpClients = httpClients;
        _options = options;
        _time = time;
    }

    /// <summary>
    /// Creates an account. The metadata becomes the member's profile in the same transaction, and the database refuses
    /// an under-18 birth date (supabase/migrations, identity sign-up).
    /// </summary>
    public Task<AuthOutcome> SignUpAsync(string email, string password, SignUpProfile profile, IPAddress? memberIp, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var data = new JsonObject
        {
            ["display_name"] = profile.DisplayName,
            ["birth_date"] = profile.BirthDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["time_zone"] = profile.TimeZone,
        };
        return SendAsync("signup", new JsonObject { ["email"] = email, ["password"] = password, ["data"] = data }, memberIp, null, cancellationToken);
    }

    /// <summary>Signs a member in with their email address and password.</summary>
    public Task<AuthOutcome> SignInWithPasswordAsync(string email, string password, IPAddress? memberIp, CancellationToken cancellationToken = default) =>
        SendAsync("token?grant_type=password", new JsonObject { ["email"] = email, ["password"] = password }, memberIp, null, cancellationToken);

    /// <summary>
    /// Swaps a refresh token for a new session. Parallel refreshes of one session get the same refresh token back
    /// from Supabase (ADR 0005 decision 4).
    /// </summary>
    public Task<AuthOutcome> RefreshAsync(string refreshToken, IPAddress? memberIp, CancellationToken cancellationToken = default) =>
        SendAsync("token?grant_type=refresh_token", new JsonObject { ["refresh_token"] = refreshToken }, memberIp, null, cancellationToken);

    /// <summary>Confirms an email address from the token hash in a confirmation link, and signs the member in.</summary>
    public Task<AuthOutcome> VerifyEmailAsync(string tokenHash, IPAddress? memberIp, CancellationToken cancellationToken = default) =>
        SendAsync("verify", new JsonObject { ["token_hash"] = tokenHash, ["type"] = "email" }, memberIp, null, cancellationToken);

    /// <summary>
    /// Ends the session the access token belongs to and revokes its refresh tokens (NFR-SEC-4). Only
    /// <see cref="AuthStatus.SessionEnded"/> means the session is gone; anything else may have left it alive.
    /// </summary>
    public async Task<AuthOutcome> SignOutAsync(string accessToken, IPAddress? memberIp, CancellationToken cancellationToken = default)
    {
        var outcome = await SendAsync("logout?scope=local", null, memberIp, accessToken, cancellationToken);
        return outcome.Status == AuthStatus.ConfirmationSent ? new(AuthStatus.SessionEnded) : outcome;
    }

    private async Task<AuthOutcome> SendAsync(string path, JsonObject? body, IPAddress? memberIp, string? accessToken, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(options.AuthUrl, path));
        request.Headers.Add("apikey", options.RequiredSecretKey);
        if (memberIp is not null)
        {
            request.Headers.Add("Sb-Forwarded-For", memberIp.ToString());
        }

        if (accessToken is not null)
        {
            request.Headers.Authorization = new("Bearer", accessToken);
        }

        request.Content = body is null ? null : JsonContent.Create(body);

        try
        {
            using var response = await _httpClients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            var json = await ReadJsonAsync(response, cancellationToken);
            return response.IsSuccessStatusCode ? Succeeded(json) : Failed(path, response.StatusCode, json);
        }
        catch (HttpRequestException)
        {
            return new(AuthStatus.Unavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient's own timeout, not the caller cancelling.
            return new(AuthStatus.Unavailable);
        }
    }

    private AuthOutcome Succeeded(JsonNode? json)
    {
        var accessToken = json?["access_token"]?.GetValue<string>();
        var refreshToken = json?["refresh_token"]?.GetValue<string>();
        if (accessToken is null || refreshToken is null)
        {
            // Sign-up with email confirmation on returns the user without a session; logout returns nothing.
            return new(AuthStatus.ConfirmationSent);
        }

        var expiresIn = json?["expires_in"]?.GetValue<int>() ?? 0;
        return new(AuthStatus.SignedIn, new AuthSession(accessToken, refreshToken, _time.GetUtcNow().AddSeconds(expiresIn)));
    }

    private static AuthOutcome Failed(string path, HttpStatusCode status, JsonNode? json)
    {
        var errorCode = Text(json?["error_code"]) ?? Text(json?["code"]);

        // The profile trigger's check_violation, passed through by Supabase Auth as a 500.
        if (errorCode == "23514" && Text(json?["message"])?.Contains("18", StringComparison.Ordinal) == true)
        {
            return new(AuthStatus.Underage);
        }

        if (status == HttpStatusCode.TooManyRequests)
        {
            return new(AuthStatus.RateLimited);
        }

        if ((int)status >= 500)
        {
            return new(AuthStatus.Unavailable);
        }

        if (errorCode is null && status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            // Auth answers its own refusals with an error code. Without one, the gateway turned away fitcore-app's API
            // key: a server problem, which the member's details didn't cause and can't fix.
            return new(AuthStatus.Unavailable);
        }

        if (path.StartsWith("token?grant_type=refresh_token", StringComparison.Ordinal))
        {
            // Only Supabase saying the token or session is gone ends the session (ADR 0005 decision 4). Anything else,
            // such as a rejected API key, is fitcore-app's problem, and signing every member out wouldn't fix it.
            return errorCode is "refresh_token_not_found" or "refresh_token_already_used" or "session_not_found"
                or "session_expired" or "user_not_found" or "user_banned"
                ? new(AuthStatus.SessionEnded)
                : new(AuthStatus.Unavailable);
        }

        if (path.StartsWith("logout", StringComparison.Ordinal) && errorCode is "session_not_found" or "user_not_found")
        {
            // Already gone, so there is nothing left to revoke.
            return new(AuthStatus.SessionEnded);
        }

        return errorCode switch
        {
            "invalid_credentials" => new(AuthStatus.InvalidCredentials),
            "email_not_confirmed" => new(AuthStatus.EmailNotConfirmed),
            "user_already_exists" or "email_exists" => new(AuthStatus.EmailTaken),
            "weak_password" => new(AuthStatus.WeakPassword),
            "over_request_rate_limit" or "over_email_send_rate_limit" => new(AuthStatus.RateLimited),
            _ when path == "verify" => new(AuthStatus.InvalidLink),
            _ when path.StartsWith("token?grant_type=password", StringComparison.Ordinal) => new(AuthStatus.InvalidCredentials),
            _ => new(AuthStatus.Refused),
        };
    }

    private static string? Text(JsonNode? node) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonValue value when value.TryGetValue<int>(out var number) => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => null,
    };

    private static async Task<JsonNode?> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (text.Length == 0)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>What fitcore-app's sign-up form sends for the new member's profile.</summary>
/// <param name="DisplayName">The name FitCore greets the member by.</param>
/// <param name="BirthDate">Checked against 18+ on the member's local date (BRD §6.2).</param>
/// <param name="TimeZone">An IANA time zone name from the browser; diary dates follow it (BRD §6.1).</param>
public sealed record SignUpProfile(string DisplayName, DateOnly BirthDate, string TimeZone);
