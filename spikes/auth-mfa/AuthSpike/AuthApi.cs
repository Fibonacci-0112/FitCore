using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AuthSpike;

/// <summary>Result of an Auth REST call: status, JSON body, and the error code if any.</summary>
public sealed record AuthResult(int Status, JsonNode? Body)
{
    public bool Ok => Status is >= 200 and < 300;
    public string? ErrorCode => Body?["error_code"]?.GetValue<string>() ?? Body?["code"]?.ToString();
    public string? Message => Body?["msg"]?.GetValue<string>() ?? Body?["message"]?.GetValue<string>() ?? Body?["error_description"]?.GetValue<string>();
    public string AccessToken => Body!["access_token"]!.GetValue<string>();
    public string RefreshToken => Body!["refresh_token"]!.GetValue<string>();
    public override string ToString() => Ok ? $"HTTP {Status}" : $"HTTP {Status} {ErrorCode}: {Message}";
}

/// <summary>
/// Thin client for the Supabase Auth REST API (GoTrue), as the API or a MAUI
/// app would call it. Local CLI defaults; the publishable key is public.
/// </summary>
public sealed class AuthApi(string baseUrl = "http://127.0.0.1:54321/auth/v1",
    string publishableKey = "sb_publishable_ACJWlzQHlZjBrEguHvfOxg_3BJgxAaH")
{
    private readonly HttpClient _http = new();
    public string BaseUrl { get; } = baseUrl;
    public string PublishableKey { get; } = publishableKey;

    public Task<AuthResult> SignUpAsync(string email, string password) =>
        SendAsync(HttpMethod.Post, "/signup", null, new { email, password });

    public Task<AuthResult> SignInWithPasswordAsync(string email, string password) =>
        SendAsync(HttpMethod.Post, "/token?grant_type=password", null, new { email, password });

    public Task<AuthResult> RefreshAsync(string refreshToken) =>
        SendAsync(HttpMethod.Post, "/token?grant_type=refresh_token", null, new { refresh_token = refreshToken });

    /// <summary>Native Sign in with Apple: the app sends Apple's ID token and the raw nonce.</summary>
    public Task<AuthResult> SignInWithIdTokenAsync(string provider, string idToken, string? nonce) =>
        SendAsync(HttpMethod.Post, "/token?grant_type=id_token", null, new { provider, id_token = idToken, nonce });

    public Task<AuthResult> GetUserAsync(string accessToken) =>
        SendAsync(HttpMethod.Get, "/user", accessToken, null);

    public Task<AuthResult> EnrollTotpAsync(string accessToken, string friendlyName) =>
        SendAsync(HttpMethod.Post, "/factors", accessToken, new { factor_type = "totp", friendly_name = friendlyName, issuer = "FitCore" });

    public Task<AuthResult> ChallengeAsync(string accessToken, string factorId) =>
        SendAsync(HttpMethod.Post, $"/factors/{factorId}/challenge", accessToken, new { });

    public Task<AuthResult> VerifyAsync(string accessToken, string factorId, string challengeId, string code) =>
        SendAsync(HttpMethod.Post, $"/factors/{factorId}/verify", accessToken, new { challenge_id = challengeId, code });

    public Task<AuthResult> UnenrollAsync(string accessToken, string factorId) =>
        SendAsync(HttpMethod.Delete, $"/factors/{factorId}", accessToken, null);

    public Task<AuthResult> RecoveryCodesStatusAsync(string accessToken) =>
        SendAsync(HttpMethod.Get, "/factors/recovery-codes", accessToken, null);

    private async Task<AuthResult> SendAsync(HttpMethod method, string path, string? accessToken, object? body)
    {
        using var req = new HttpRequestMessage(method, BaseUrl + path);
        req.Headers.Add("apikey", PublishableKey);
        if (accessToken is not null) req.Headers.Authorization = new("Bearer", accessToken);
        if (body is not null) req.Content = JsonContent.Create(body);
        using var res = await _http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        JsonNode? json = null;
        if (text.Length > 0)
        {
            try { json = JsonNode.Parse(text); } catch (JsonException) { json = JsonValue.Create(text); }
        }
        return new((int)res.StatusCode, json);
    }
}
