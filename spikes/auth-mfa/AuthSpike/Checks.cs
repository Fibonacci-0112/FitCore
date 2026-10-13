using System.Security.Cryptography;
using System.Text;

namespace AuthSpike;

/// <param name="Gap">A known gap: the row shows whether it is still present, and doesn't fail the run.</param>
public sealed record Result(string Area, string Check, bool Pass, string Detail, bool Gap = false);

/// <summary>Password, TOTP, step-up, RLS and Apple ID-token checks against local Supabase Auth.</summary>
public static class Checks
{
    private const string Password = "correct-horse-battery-staple"; // 28 chars, NFR-SEC-2

    public static async Task<List<Result>> RunAsync()
    {
        var results = new List<Result>();
        void Add(string area, string check, bool pass, string detail) => results.Add(new(area, check, pass, detail));

        var api = new AuthApi();
        var tokens = await Tokens.CreateAsync(api);
        var run = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string Email(string who) => $"{who}-{run}@spike.fitcore.test";

        // Passwords (NFR-SEC-2): 15 characters minimum when the password is the only factor.
        var weak = await api.SignUpAsync(Email("weak"), "fourteen-chars");
        Add("Password", "14-character password rejected", !weak.Ok && weak.ErrorCode == "weak_password", weak.ToString());

        var member = await api.SignUpAsync(Email("member"), Password);
        var coach = await api.SignUpAsync(Email("coach"), Password);
        var clinician = await api.SignUpAsync(Email("clinician"), Password);
        Add("Password", "15+ character password accepted", member.Ok && coach.Ok && clinician.Ok, member.ToString());
        Guid Id(AuthResult r) => Guid.Parse(r.Body!["user"]!["id"]!.GetValue<string>());
        var (memberId, coachId, clinicianId) = (Id(member), Id(coach), Id(clinician));
        await Db.SeedAsync(memberId, coachId, clinicianId);

        // Tokens: validated against the JWKS, aal1 after a password.
        var memberClaims = (await tokens.ValidateAsync(member.AccessToken)).Claims!;
        Add("Token", "Access token validates against JWKS (ES256)", memberClaims is not null, $"iss `{memberClaims!["iss"]}`");
        Add("Token", "Password sign-in gives aal1, amr password", Tokens.Aal(memberClaims) == "aal1" && Tokens.Amr(memberClaims).SequenceEqual(["password"]),
            $"aal {Tokens.Aal(memberClaims)}, amr [{string.Join(", ", Tokens.Amr(memberClaims))}]");
        var forged = await tokens.ValidateAsync(Tokens.Forge(member.AccessToken, "aal", "aal2"));
        Add("Token", "Token with aal forged to aal2 is rejected", forged.Claims is null, forged.Error ?? "accepted");

        // TOTP enrollment for the clinician (NFR-SEC-3).
        var enroll = await api.EnrollTotpAsync(clinician.AccessToken, "Authenticator");
        var factorId = enroll.Body?["id"]?.GetValue<string>() ?? "";
        var secret = enroll.Body?["totp"]?["secret"]?.GetValue<string>() ?? "";
        Add("TOTP", "Enroll from an aal1 session (no factors yet)", enroll.Ok && secret.Length > 0, $"{enroll}, otpauth URI and QR code returned: {enroll.Body?["totp"]?["uri"] is not null && enroll.Body?["totp"]?["qr_code"] is not null}");

        var ch1 = await api.ChallengeAsync(clinician.AccessToken, factorId);
        var wrong = await api.VerifyAsync(clinician.AccessToken, factorId, ch1.Body!["id"]!.GetValue<string>(), "000000");
        Add("TOTP", "Wrong code rejected", !wrong.Ok, wrong.ToString());

        var ch2 = await api.ChallengeAsync(clinician.AccessToken, factorId);
        var code = Totp.Code(secret);
        var verified = await api.VerifyAsync(clinician.AccessToken, factorId, ch2.Body!["id"]!.GetValue<string>(), code);
        var aal2Claims = verified.Ok ? (await tokens.ValidateAsync(verified.AccessToken)).Claims! : null;
        Add("TOTP", "Verify upgrades the session to aal2", aal2Claims is not null && Tokens.Aal(aal2Claims) == "aal2",
            aal2Claims is null ? verified.ToString() : $"aal {Tokens.Aal(aal2Claims)}, amr [{string.Join(", ", Tokens.Amr(aal2Claims))}]");

        var ch3 = await api.ChallengeAsync(verified.AccessToken, factorId);
        var replay = await api.VerifyAsync(verified.AccessToken, factorId, ch3.Body!["id"]!.GetValue<string>(), code);
        // NIST SP 800-63B accepts a time-based OTP only once; Auth keeps no record of used codes.
        results.Add(new("TOTP", "GAP: a used code is accepted again within its window", !replay.Ok, replay.ToString(), Gap: true));

        // Step-up on a later sign-in.
        var again = await api.SignInWithPasswordAsync(Email("clinician"), Password);
        var againClaims = (await tokens.ValidateAsync(again.AccessToken)).Claims!;
        var user = await api.GetUserAsync(again.AccessToken);
        var hasVerified = user.Body?["factors"]?.AsArray().Any(f => f!["status"]!.GetValue<string>() == "verified") == true;
        Add("Step-up", "Password sign-in is aal1 and lists a verified factor, so the app must step up", Tokens.Aal(againClaims) == "aal1" && hasVerified,
            $"aal {Tokens.Aal(againClaims)}, verified factor listed: {hasVerified}");

        var second = await api.EnrollTotpAsync(again.AccessToken, "Second");
        Add("Step-up", "Enrolling another factor needs aal2", !second.Ok, second.ToString());
        var unenrollAal1 = await api.UnenrollAsync(again.AccessToken, factorId);
        Add("Step-up", "Removing a factor needs aal2", !unenrollAal1.Ok, unenrollAal1.ToString());

        var refreshed1 = await api.RefreshAsync(again.RefreshToken);
        Add("Step-up", "Refreshing an aal1 session stays aal1", Tokens.Aal((await tokens.ValidateAsync(refreshed1.AccessToken)).Claims!) == "aal1", refreshed1.ToString());

        await Task.Delay(TimeSpan.FromSeconds(31 - DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 30)); // next TOTP window
        var ch4 = await api.ChallengeAsync(refreshed1.AccessToken, factorId);
        var stepped = await api.VerifyAsync(refreshed1.AccessToken, factorId, ch4.Body!["id"]!.GetValue<string>(), Totp.Code(secret));
        var steppedClaims = stepped.Ok ? (await tokens.ValidateAsync(stepped.AccessToken)).Claims! : null;
        Add("Step-up", "Challenge and verify steps the session up to aal2", steppedClaims is not null && Tokens.Aal(steppedClaims) == "aal2", stepped.ToString());
        var refreshed2 = await api.RefreshAsync(stepped.RefreshToken);
        var refreshed2Claims = (await tokens.ValidateAsync(refreshed2.AccessToken)).Claims!;
        Add("Step-up", "Refreshing an aal2 session stays aal2", Tokens.Aal(refreshed2Claims) == "aal2", refreshed2.ToString());

        // RLS through the API's login role, with validated claims (rules from the RLS spike).
        var coachClaims = (await tokens.ValidateAsync(coach.AccessToken)).Claims!;
        var otherClaims = (await tokens.ValidateAsync(clinician.AccessToken)).Claims!; // clinician's first, aal1 token
        Add("RLS", "Member reads own diary at aal1", await Db.CountDiaryAsync(memberClaims, memberId) == 5, $"{await Db.CountDiaryAsync(memberClaims, memberId)} rows");
        Add("RLS", "Coach grant works at aal1", await Db.CountDiaryAsync(coachClaims, memberId) == 5, $"{await Db.CountDiaryAsync(coachClaims, memberId)} rows");
        Add("RLS", "Clinician grant sees nothing at aal1", await Db.CountDiaryAsync(otherClaims, memberId) == 0, $"{await Db.CountDiaryAsync(otherClaims, memberId)} rows");
        Add("RLS", "Clinician grant works at aal2", await Db.CountDiaryAsync(refreshed2Claims, memberId) == 5, $"{await Db.CountDiaryAsync(refreshed2Claims, memberId)} rows");

        var unenroll = await api.UnenrollAsync(refreshed2.AccessToken, factorId);
        Add("Step-up", "Removing a factor works at aal2", unenroll.Ok, unenroll.ToString());

        // Recovery codes: in Auth v2.197.0, not in the docs or the CLI config yet.
        var recovery = await api.RecoveryCodesStatusAsync(member.AccessToken);
        Add("Recovery", "Recovery codes endpoint (informational)", true, recovery.ToString());

        // Native Sign in with Apple: the server checks Apple's signature, audience and nonce.
        var rawNonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var nonceHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawNonce)));
        var apple = await api.SignInWithIdTokenAsync("apple", Tokens.ForgeAppleIdToken("app.fitcore.ios", "001234.forged", nonceHash), rawNonce);
        Add("Apple", "Forged Apple ID token rejected", !apple.Ok, apple.ToString());

        return results;
    }
}
