using Supabase.Gotrue;
using Supabase.Gotrue.Mfa;

namespace AuthSpike;

/// <summary>The same TOTP flow through the Supabase.Gotrue NuGet package, to judge it against raw REST.</summary>
public static class LibraryCheck
{
    public static async Task<List<Result>> RunAsync()
    {
        var api = new AuthApi();
        var client = new Client(new ClientOptions
        {
            Url = api.BaseUrl,
            Headers = { ["apikey"] = api.PublishableKey },
        });
        var email = $"library-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}@spike.fitcore.test";
        var results = new List<Result>();
        try
        {
            await client.SignUp(email, "correct-horse-battery-staple");
            var enroll = await client.Enroll(new MfaEnrollParams { FactorType = "totp", FriendlyName = "Library" });
            var verified = await client.ChallengeAndVerify(new MfaChallengeAndVerifyParams { FactorId = enroll!.Id, Code = Totp.Code(enroll.Totp!.Secret) });
            var aal = await client.GetAuthenticatorAssuranceLevel();
            results.Add(new("Library", "Supabase.Gotrue 8.3.1: sign up, enroll TOTP, challenge and verify", aal?.CurrentLevel == AuthenticatorAssuranceLevel.aal2,
                $"current {aal?.CurrentLevel}, next {aal?.NextLevel}"));
        }
        catch (Exception ex)
        {
            results.Add(new("Library", "Supabase.Gotrue 8.3.1: sign up, enroll TOTP, challenge and verify", false, $"{ex.GetType().Name}: {ex.Message.Split('\n')[0]}"));
        }
        return results;
    }
}
