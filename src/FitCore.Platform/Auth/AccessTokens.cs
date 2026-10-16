using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FitCore.Platform.Auth;

/// <summary>
/// Validates Supabase access tokens against the project's signing keys (implementation plan §3.3, auth spike §3.1):
/// signature, issuer, audience and expiry. Bearer and cookie requests both come through here, so every caller gets
/// the same claims (ADR 0005 decision 3).
/// </summary>
internal sealed class AccessTokens : IDisposable
{
    /// <summary>How long a fetched key set is trusted before it's fetched again.</summary>
    private static readonly TimeSpan KeysLifetime = TimeSpan.FromMinutes(10);

    /// <summary>The shortest gap between two fetches, so tokens with unknown key ids can't flood the JWKS endpoint.</summary>
    private static readonly TimeSpan MinimumRefetch = TimeSpan.FromSeconds(30);

    private readonly JsonWebTokenHandler _handler = new() { MapInboundClaims = false };
    private readonly IHttpClientFactory _httpClients;
    private readonly IOptions<SupabaseOptions> _options;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _fetch = new(1, 1);
    private IList<SecurityKey> _keys = [];
    private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _retryAfter = DateTimeOffset.MinValue;
    private bool _fetchFailed;

    public AccessTokens(IHttpClientFactory httpClients, IOptions<SupabaseOptions> options, TimeProvider time)
    {
        _httpClients = httpClients;
        _options = options;
        _time = time;
    }

    public void Dispose() => _fetch.Dispose();

    /// <summary>The validated caller, or null if the token is invalid, expired, or signed with a key the project doesn't publish.</summary>
    public async Task<ClaimsIdentity?> ValidateAsync(string token, string authenticationType, CancellationToken cancellationToken)
    {
        string? keyId;
        try
        {
            keyId = _handler.ReadJsonWebToken(token).Kid;
        }
        catch (ArgumentException)
        {
            // Includes SecurityTokenMalformedException.
            return null;
        }

        var keys = await KeysAsync(keyId, cancellationToken);
        var options = _options.Value;
        var result = await _handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudience = "authenticated",
            IssuerSigningKeys = keys,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.RsaSha256],
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            LifetimeValidator = (notBefore, expires, _, parameters) => IsCurrent(notBefore, expires, parameters.ClockSkew),
            NameClaimType = "sub",
            RoleClaimType = Claims.Role,
            AuthenticationType = authenticationType,
        });

        if (!result.IsValid || result.ClaimsIdentity is not { } identity)
        {
            return null;
        }

        // Only signed-in members' tokens, never the anon or service keys; and the claims the database needs.
        var valid = identity.FindFirst("role")?.Value == "authenticated"
            && Guid.TryParse(identity.FindFirst("sub")?.Value, out var subject) && subject != Guid.Empty
            && identity.FindFirst("aal")?.Value is "aal1" or "aal2";
        return valid ? identity : null;
    }

    private async Task<IList<SecurityKey>> KeysAsync(string? keyId, CancellationToken cancellationToken)
    {
        if (!NeedsFetch(keyId))
        {
            return KnownKeys(keyId);
        }

        await _fetch.WaitAsync(cancellationToken);
        try
        {
            if (NeedsFetch(keyId))
            {
                var url = new Uri(_options.Value.AuthUrl, ".well-known/jwks.json");
                var json = await _httpClients.CreateClient(MemberAuth.HttpClientName).GetStringAsync(url, cancellationToken);
                _keys = new JsonWebKeySet(json).GetSigningKeys();
                _fetchedAt = _time.GetUtcNow();
                _fetchFailed = false;
            }
        }
        catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Supabase is down or timed out. Requests keep the keys already known, and none of them waits on another
            // fetch until the back-off ends, so an outage doesn't queue every request behind the HttpClient timeout.
            _retryAfter = _time.GetUtcNow() + MinimumRefetch;
            _fetchFailed = true;
        }
        finally
        {
            _fetch.Release();
        }

        return KnownKeys(keyId);
    }

    /// <summary>
    /// The keys to validate with, or an error if this instance couldn't fetch the ones it needs: none at all, or not
    /// the token's key because the last fetch failed. A token signed with a key rotated in during an outage is then
    /// unverifiable rather than invalid, so a valid session isn't ended (ADR 0005 decision 4).
    /// </summary>
    private IList<SecurityKey> KnownKeys(string? keyId) =>
        _keys.Count == 0 || (_fetchFailed && keyId is not null && _keys.All(k => k.KeyId != keyId))
            ? throw new HttpRequestException("Supabase Auth's signing keys are unavailable.")
            : _keys;

    /// <summary>Not before its <c>nbf</c>, not after its <c>exp</c>, both within the clock skew; and the range itself the right way round.</summary>
    private bool IsCurrent(DateTime? notBefore, DateTime? expires, TimeSpan skew)
    {
        if (expires is not { } until)
        {
            return false;
        }

        var now = _time.GetUtcNow().UtcDateTime;
        until = until.ToUniversalTime();
        return until > now - skew
            && (notBefore is not { } from || (from.ToUniversalTime() <= now + skew && from.ToUniversalTime() < until));
    }

    private bool NeedsFetch(string? keyId)
    {
        if (_time.GetUtcNow() < _retryAfter)
        {
            return false;
        }

        var age = _time.GetUtcNow() - _fetchedAt;
        if (age > KeysLifetime)
        {
            return true;
        }

        // A key id the set doesn't hold may mean the project rotated its keys.
        return keyId is not null && age > MinimumRefetch && _keys.All(k => k.KeyId != keyId);
    }
}
