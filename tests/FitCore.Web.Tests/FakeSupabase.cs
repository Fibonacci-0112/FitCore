using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FitCore.Web.Tests;

/// <summary>
/// Supabase Auth as fitcore-app sees it, in memory: a signing key with its JWKS, password sign-in, refresh and logout.
/// Tests set how refresh answers, to cover what a real stack can't do on demand, such as being unreachable.
/// </summary>
public sealed class FakeSupabase : HttpMessageHandler
{
    public static readonly Uri Url = new("https://supabase.test/");
    public const string SecretKey = "sb_secret_test";
    public const string Password = "correct horse battery staple";

    private readonly List<ECDsa> _keys = [];
    private ECDsaSecurityKey _securityKey = null!;
    private int _refreshTokens;

    public FakeSupabase() => RotateKey();

    public enum RefreshAnswer
    {
        NewSession,
        Refused,
        KeyRejected,
        ServerError,
        Unreachable,
    }

    public enum LogoutAnswer
    {
        SignedOut,
        ServerError,
        KeyRejected,
    }

    /// <summary>The member every session belongs to.</summary>
    public Guid Member { get; } = Guid.NewGuid();

    /// <summary>Seconds until the access tokens it issues expire; negative issues expired tokens.</summary>
    public int ExpiresIn { get; set; } = 900;

    public RefreshAnswer Refresh { get; set; } = RefreshAnswer.NewSession;

    public LogoutAnswer Logout { get; set; } = LogoutAnswer.SignedOut;

    /// <summary>Characters of user metadata in the access tokens it issues, to make sessions bigger than one cookie.</summary>
    public int MetadataLength { get; set; }

    /// <summary>Whether the JWKS endpoint answers; false makes it unreachable.</summary>
    public bool JwksReachable { get; set; } = true;

    public List<Received> Requests { get; } = [];

    /// <summary>Every access and refresh token it has handed out.</summary>
    public List<string> IssuedTokens { get; } = [];

    /// <summary>A valid access token for <see cref="Member"/>, for bearer callers.</summary>
    public string IssueToken(int expiresIn = 900, int? notBeforeIn = null)
    {
        var expires = DateTime.UtcNow.AddSeconds(expiresIn);
        var claims = new Dictionary<string, object>
        {
            ["sub"] = Member.ToString(),
            ["role"] = "authenticated",
            ["aal"] = "aal1",
            ["session_id"] = Guid.NewGuid().ToString(),
        };
        if (MetadataLength > 0)
        {
            claims["user_metadata"] = new Dictionary<string, object> { ["display_name"] = new string('x', MetadataLength) };
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = new Uri(Url, "auth/v1").ToString(),
            Audience = "authenticated",
            IssuedAt = expires.AddHours(-1),
            NotBefore = notBeforeIn is { } nbf ? DateTime.UtcNow.AddSeconds(nbf) : expires.AddHours(-1),
            Expires = expires,
            Claims = claims,
            SigningCredentials = new SigningCredentials(_securityKey, SecurityAlgorithms.EcdsaSha256),
        });
    }

    /// <summary>Signs from now on with a new key, which the JWKS then publishes alone.</summary>
    public void RotateKey()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _keys.Add(key);
        _securityKey = new ECDsaSecurityKey(key) { KeyId = $"test-key-{_keys.Count}" };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.PathAndQuery;
        var body = request.Content is null ? null : await request.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        Requests.Add(new Received(
            path,
            request.Headers.TryGetValues("apikey", out var key) ? key.Single() : null,
            request.Headers.TryGetValues("Sb-Forwarded-For", out var ip) ? ip.Single() : null,
            request.Headers.Authorization?.Parameter));

        return path switch
        {
            "/auth/v1/.well-known/jwks.json" => JwksReachable ? Json(HttpStatusCode.OK, Jwks()) : throw new HttpRequestException("Supabase Auth is unreachable."),
            "/auth/v1/token?grant_type=password" => (string?)body?["password"] == Password
                ? Json(HttpStatusCode.OK, NewSession())
                : Json(HttpStatusCode.BadRequest, new JsonObject { ["error_code"] = "invalid_credentials" }),
            "/auth/v1/token?grant_type=refresh_token" => Refresh switch
            {
                RefreshAnswer.NewSession => Json(HttpStatusCode.OK, NewSession()),
                RefreshAnswer.Refused => Json(HttpStatusCode.BadRequest, new JsonObject { ["error_code"] = "refresh_token_not_found" }),
                RefreshAnswer.KeyRejected => Json(HttpStatusCode.Unauthorized, new JsonObject { ["message"] = "Invalid API key" }),
                RefreshAnswer.ServerError => Json(HttpStatusCode.BadGateway, new JsonObject()),
                _ => throw new HttpRequestException("Supabase Auth is unreachable."),
            },
            "/auth/v1/logout?scope=local" => Logout switch
            {
                LogoutAnswer.SignedOut => new HttpResponseMessage(HttpStatusCode.NoContent),
                LogoutAnswer.KeyRejected => Json(HttpStatusCode.Unauthorized, new JsonObject { ["message"] = "Invalid API key" }),
                _ => new HttpResponseMessage(HttpStatusCode.BadGateway),
            },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private JsonObject NewSession()
    {
        var accessToken = IssueToken(ExpiresIn);
        var refreshToken = $"refresh-{Guid.NewGuid():N}-{Interlocked.Increment(ref _refreshTokens)}";
        IssuedTokens.Add(accessToken);
        IssuedTokens.Add(refreshToken);
        return new()
        {
            ["access_token"] = accessToken,
            ["refresh_token"] = refreshToken,
            ["expires_in"] = ExpiresIn,
            ["token_type"] = "bearer",
        };
    }

    private JsonObject Jwks()
    {
        var parameters = _securityKey.ECDsa.ExportParameters(includePrivateParameters: false);
        return new JsonObject
        {
            ["keys"] = new JsonArray(new JsonObject
            {
                ["kty"] = "EC",
                ["crv"] = "P-256",
                ["alg"] = "ES256",
                ["use"] = "sig",
                ["kid"] = _securityKey.KeyId,
                ["x"] = Base64UrlEncoder.Encode(parameters.Q.X),
                ["y"] = Base64UrlEncoder.Encode(parameters.Q.Y),
            }),
        };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, JsonObject body) =>
        new(status) { Content = JsonContent.Create(body) };

    public sealed record Received(string Path, string? ApiKey, string? ForwardedFor, string? Bearer);
}
