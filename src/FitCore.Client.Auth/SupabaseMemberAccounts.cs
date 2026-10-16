using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FitCore.Client.Core.Accounts;
using Supabase.Gotrue;
using Supabase.Gotrue.Exceptions;

namespace FitCore.Client.Auth;

/// <summary>
/// The device apps' calls to Supabase Auth, through <c>Supabase.Gotrue</c> (auth spike rule 5). The stateless client
/// keeps no session of its own. Nothing here logs request or response bodies: they hold passwords and tokens
/// (NFR-PRV-6).
/// </summary>
public sealed class SupabaseMemberAccounts : IMemberAccounts, IDisposable
{
    private readonly StatelessClient _client = new();
    private readonly HttpClient _http;
    private readonly StatelessClient.StatelessClientOptions _options;

    public SupabaseMemberAccounts(SupabaseAuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
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

        Session? session;
        try
        {
            // Gotrue's sign-up takes no cancellation token, so cancelling stops the wait, not the request.
            session = await _client.SignUp(request.Email, request.Password, _options, signUp).WaitAsync(cancellationToken);
        }
        catch (GotrueException e)
        {
            return Failed(e);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The HttpClient's own timeout, not the caller cancelling.
            return SignUpStatus.Unavailable;
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

    public void Dispose() => _http.Dispose();

    private static SignUpStatus Failed(GotrueException e)
    {
        if (e.Reason == FailureHint.Reason.Offline || e.StatusCode == 0)
        {
            return SignUpStatus.Unavailable;
        }

        var body = Parse(e.Content);
        var errorCode = Text(body?["error_code"]) ?? Text(body?["code"]);

        // The profile trigger's check_violation, passed through by Supabase Auth as a 500.
        if (errorCode == "23514" && Text(body?["message"])?.Contains("18", StringComparison.Ordinal) == true)
        {
            return SignUpStatus.Underage;
        }

        if (e.StatusCode == 429)
        {
            return SignUpStatus.RateLimited;
        }

        if (e.StatusCode >= 500)
        {
            return SignUpStatus.Unavailable;
        }

        return errorCode switch
        {
            "user_already_exists" or "email_exists" => SignUpStatus.EmailTaken,
            "weak_password" => SignUpStatus.WeakPassword,
            "over_request_rate_limit" or "over_email_send_rate_limit" => SignUpStatus.RateLimited,
            _ => SignUpStatus.Refused,
        };
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
