using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AuthSpike;

/// <summary>Validates Supabase access tokens against the project's JWKS, as the API will (architecture-options §B.4).</summary>
public sealed class Tokens
{
    private readonly JsonWebTokenHandler _handler = new();
    private readonly TokenValidationParameters _params;

    private Tokens(TokenValidationParameters p) => _params = p;

    public static async Task<Tokens> CreateAsync(AuthApi api)
    {
        using var http = new HttpClient();
        var jwks = new JsonWebKeySet(await http.GetStringAsync(api.BaseUrl + "/.well-known/jwks.json"));
        return new(new TokenValidationParameters
        {
            ValidIssuer = api.BaseUrl,
            ValidAudience = "authenticated",
            IssuerSigningKeys = jwks.GetSigningKeys(),
            ValidAlgorithms = ["ES256"],
            ClockSkew = TimeSpan.FromSeconds(30),
        });
    }

    /// <summary>Returns the validated claims as JSON (what the API forwards to Postgres), or null if invalid.</summary>
    public async Task<(JsonObject? Claims, string? Error)> ValidateAsync(string token)
    {
        var result = await _handler.ValidateTokenAsync(token, _params);
        if (!result.IsValid) return (null, result.Exception?.GetType().Name);
        var payload = System.Text.Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(token.Split('.')[1]));
        return (JsonNode.Parse(payload)!.AsObject(), null);
    }

    public static string Aal(JsonObject claims) => claims["aal"]?.GetValue<string>() ?? "aal1";

    public static IEnumerable<string> Amr(JsonObject claims) =>
        claims["amr"]?.AsArray().Select(e => e!["method"]!.GetValue<string>()) ?? [];

    /// <summary>Copies a real token's claims, changes one, and signs it with a key the project doesn't know.</summary>
    public static string Forge(string token, string claim, string value)
    {
        var payload = JsonNode.Parse(System.Text.Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(token.Split('.')[1])))!.AsObject();
        payload[claim] = value;
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = new ECDsaSecurityKey(ec) { KeyId = new JsonWebTokenHandler().ReadJsonWebToken(token).Kid };
        return new JsonWebTokenHandler().CreateToken(payload.ToJsonString(), new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256));
    }

    /// <summary>An Apple-shaped ID token signed with a key Apple never issued.</summary>
    public static string ForgeAppleIdToken(string audience, string sub, string? nonceHash)
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "forged" };
        var claims = new Dictionary<string, object>
        {
            ["iss"] = "https://appleid.apple.com", ["aud"] = audience, ["sub"] = sub,
            ["email"] = "forged@privaterelay.appleid.com", ["email_verified"] = true,
        };
        if (nonceHash is not null) claims["nonce"] = nonceHash;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Claims = claims, Expires = DateTime.UtcNow.AddMinutes(5), IssuedAt = DateTime.UtcNow,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
        });
    }
}
