using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FitCore.Client.Core.Accounts;
using Supabase.Gotrue;
using Supabase.Gotrue.Exceptions;
using static Supabase.Gotrue.Constants;

namespace FitCore.Client.Auth;

/// <summary>
/// The device apps' calls to Supabase Auth, through <c>Supabase.Gotrue</c> (auth spike rule 5). The stateless client
/// keeps no session of its own; <see cref="DeviceSession"/> does. Nothing here logs request or response bodies: they
/// hold passwords and tokens (NFR-PRV-6). Errors map to statuses as fitcore-app's server-side sign-in maps them
/// (FitCore.Platform, MemberAuth), so both UIs tell the member the same thing.
/// </summary>
public sealed class SupabaseMemberAccounts : IMemberAccounts, IDisposable
{
    private readonly StatelessClient _client = new();
    private readonly HttpClient _http;
    private readonly StatelessClient.StatelessClientOptions _options;
    private readonly TimeProvider _time;

    public SupabaseMemberAccounts(SupabaseAuthOptions options)
        : this(options, TimeProvider.System)
    {
    }

    public SupabaseMemberAccounts(SupabaseAuthOptions options, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        _time = time;
        _http = new HttpClient { Timeout = options.Timeout };
        _options = new StatelessClient.StatelessClientOptions { Url = options.AuthUrl, HttpClient = _http };
        _options.Headers["apikey"] = options.PublishableKey;
    }

    /// <inheritdoc />
    public async Task<SignUpStatus> SignUpAsync(SignUpRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The same metadata as the web's sign-up: the database turns it into the profile, or refuses an under-18 birth
        // date (supabase/migrations, identity sign-up).
        var signUp = new SignUpOptions
        {
            Data = new Dictionary<string, object>
            {
                ["display_name"] = request.DisplayName,
                ["birth_date"] = request.BirthDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["time_zone"] = request.TimeZone,
            },
        };

        var (session, failure) = await CallAsync(() => _client.SignUp(request.Email, request.Password, _options, signUp), cancellationToken);
        if (failure is not null)
        {
            return SignUpFailed(failure);
        }

        // With email confirmation on, Supabase Auth answers with the user and no session, whether the address is new
        // or already has an account, so the answer never says which.
        if (session?.AccessToken is not null)
        {
            throw new InvalidOperationException(
                "Supabase Auth signed a new member in without confirming their email. Turn on Confirm email for the project (supabase/README.md, Auth settings).");
        }

        return SignUpStatus.ConfirmationSent;
    }

    /// <inheritdoc />
    public async Task<SignInResult> SignInAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var (session, failure) = await CallAsync(() => _client.SignIn(email, password, _options), cancellationToken);
        if (failure is not null)
        {
            return new(SignInFailed(failure));
        }

        return ToMemberSession(session) is { } signedIn
            ? new(SignInStatus.SignedIn, signedIn)
            : new(SignInStatus.Unavailable);
    }

    /// <inheritdoc />
    public async Task<RefreshResult> RefreshAsync(MemberSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var (refreshed, failure) = await CallAsync(() => _client.RefreshToken(session.AccessToken, session.RefreshToken, _options), cancellationToken);
        if (failure is not null)
        {
            // Only Supabase saying the token or session is gone ends the session (ADR 0005 decision 4). Anything else,
            // such as Auth being out of reach, leaves the member signed in to try again later.
            return new(failure.ErrorCode is "refresh_token_not_found" or "refresh_token_already_used" or "session_not_found"
                or "session_expired" or "user_not_found" or "user_banned"
                ? RefreshStatus.SessionEnded
                : RefreshStatus.Unavailable);
        }

        return ToMemberSession(refreshed) is { } next
            ? new(RefreshStatus.Refreshed, next)
            : new(RefreshStatus.Unavailable);
    }

    /// <inheritdoc />
    public async Task<SignOutStatus> SignOutAsync(MemberSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        // Local scope: this device's session only, as on the web.
        var (_, failure) = await CallAsync(
            async () =>
            {
                await _client.SignOut(session.AccessToken, _options, SignOutScope.Local);
                return (Session?)null;
            },
            cancellationToken);

        // Already gone, so there is nothing left to revoke.
        return failure is null || failure.ErrorCode is "session_not_found" or "user_not_found"
            ? SignOutStatus.SessionEnded
            : SignOutStatus.Unavailable;
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Runs a Gotrue call, turning its failures into a <see cref="Failure"/>; a caller's cancellation still throws.</summary>
    private static async Task<(Session? Session, Failure? Failure)> CallAsync(Func<Task<Session?>> call, CancellationToken cancellationToken)
    {
        try
        {
            // Gotrue's calls take no cancellation token, so cancelling stops the wait, not the request.
            return (await call().WaitAsync(cancellationToken), null);
        }
        catch (GotrueException e)
        {
            return (null, Failure.From(e));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The HttpClient's own timeout, not the caller cancelling.
            return (null, Failure.Unreachable);
        }
    }

    private MemberSession? ToMemberSession(Session? session) =>
        session is { AccessToken: { Length: > 0 } access, RefreshToken: { Length: > 0 } refresh }
            ? new MemberSession(access, refresh, _time.GetUtcNow().AddSeconds(session.ExpiresIn))
            : null;

    private static SignUpStatus SignUpFailed(Failure failure)
    {
        if (failure.IsUnderage)
        {
            return SignUpStatus.Underage;
        }

        if (failure.IsUnavailable)
        {
            return SignUpStatus.Unavailable;
        }

        if (failure.StatusCode == 429)
        {
            return SignUpStatus.RateLimited;
        }

        return failure.ErrorCode switch
        {
            "user_already_exists" or "email_exists" => SignUpStatus.EmailTaken,
            "weak_password" => SignUpStatus.WeakPassword,
            "over_request_rate_limit" or "over_email_send_rate_limit" => SignUpStatus.RateLimited,
            _ => SignUpStatus.Refused,
        };
    }

    private static SignInStatus SignInFailed(Failure failure)
    {
        if (failure.IsUnavailable)
        {
            return SignInStatus.Unavailable;
        }

        if (failure.StatusCode == 429)
        {
            return SignInStatus.RateLimited;
        }

        // Any other refusal reads as a wrong address or password, which says nothing about whether an account exists.
        return failure.ErrorCode switch
        {
            "email_not_confirmed" => SignInStatus.EmailNotConfirmed,
            "over_request_rate_limit" => SignInStatus.RateLimited,
            _ => SignInStatus.InvalidCredentials,
        };
    }

    /// <summary>What Supabase Auth answered when a call failed: its status and error code, never the body itself.</summary>
    private sealed record Failure(int StatusCode, string? ErrorCode, bool IsUnderage)
    {
        public static readonly Failure Unreachable = new(0, null, false);

        /// <summary>
        /// Auth couldn't be reached or failed, or the gateway turned the publishable key away: none of these is the
        /// member's doing, and trying again later may work. Auth answers its own refusals with an error code.
        /// </summary>
        public bool IsUnavailable => StatusCode == 0 || StatusCode >= 500 || (ErrorCode is null && StatusCode is 401 or 403);

        public static Failure From(GotrueException e)
        {
            if (e.Reason == FailureHint.Reason.Offline || e.StatusCode == 0)
            {
                return Unreachable;
            }

            var body = Parse(e.Content);
            var errorCode = Text(body?["error_code"]) ?? Text(body?["code"]);

            // The profile trigger's check_violation, passed through by Supabase Auth as a 500.
            var underage = errorCode == "23514" && Text(body?["message"])?.Contains("18", StringComparison.Ordinal) == true;
            return new(e.StatusCode, errorCode, underage);
        }
    }

    private static JsonNode? Parse(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(content);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonNode? node) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonValue value when value.TryGetValue<int>(out var number) => number.ToString(CultureInfo.InvariantCulture),
        _ => null,
    };
}
