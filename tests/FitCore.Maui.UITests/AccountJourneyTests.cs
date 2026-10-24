namespace FitCore.Maui.UITests;

/// <summary>
/// PR 4's acceptance on Android: a member signs up, signs in and signs out through the app's own screens, against the
/// local Supabase stack. In between, the app is killed and reopened, and the member is still signed in (the session
/// is in the secure store); after sign-out, Supabase holds no session for them and the sign-in form is empty.
/// </summary>
public sealed class AccountJourneyTests(AndroidApp app) : IClassFixture<AndroidApp>
{
    private const string Password = "correct horse battery staple";
    private const string NoApp = "Needs the app on an Android emulator: set FITCORE_ANDROID_APK (run-on-emulator.sh).";

    private static readonly TimeSpan AuthWait = TimeSpan.FromSeconds(60);

    [Fact(Skip = NoApp, SkipUnless = nameof(AndroidApp.Configured), SkipType = typeof(AndroidApp))]
    public async Task A_member_signs_up_signs_in_stays_signed_in_across_a_restart_and_signs_out()
    {
        var email = AndroidApp.NewEmail();

        // A new install opens on sign-in.
        app.WaitFor("SignInEmail", AuthWait);

        // Sign up.
        app.WaitFor("CreateAccountLink").Click();
        app.Type("SignUpName", "Ada Member");
        app.Type("SignUpEmail", email);
        app.Type("SignUpPassword", Password);
        app.PickYear("SignUpBirthDate", 1990);
        app.WaitFor("CreateAccountButton").Click();
        app.WaitFor("CheckEmailTitle", AuthWait);

        Assert.Equal(
            1990,
            await app.Database.ScalarAsync<int>(
                """
                select extract(year from p.birth_date)::int
                from identity.profiles p join auth.users u on u.id = p.member_id
                where u.email = $1
                """,
                email));

        // Opening the confirmation link, as the member would from their inbox.
        await app.Database.ExecuteAsync("update auth.users set email_confirmed_at = now() where email = $1", email);

        // Sign in.
        app.WaitFor("SignInLink").Click();
        app.Type("SignInEmail", email);
        app.Type("SignInPassword", Password);
        app.WaitFor("SignInButton").Click();
        app.WaitFor("HomeSignedIn", AuthWait);
        Assert.Equal(1L, await SessionsAsync(email));

        // Killed and reopened, the app finds the session in the secure store.
        app.Driver.TerminateApp(AndroidApp.Package);
        app.Driver.ActivateApp(AndroidApp.Package);
        app.WaitFor("HomeSignedIn", AuthWait);

        // Sign out: Supabase ends the session, and the next person sees an empty form.
        app.WaitFor("SignOutButton").Click();
        app.WaitFor("SignInEmail", AuthWait);
        app.WaitUntilGone("HomeSignedIn");
        Assert.Equal(0L, await SessionsAsync(email));

        // An empty Android field reports its hint (the field's description, "Email") as its text, so check the
        // address is gone rather than for an empty string.
        Assert.DoesNotContain(email, app.WaitFor("SignInEmail").Text, StringComparison.Ordinal);
    }

    private Task<long> SessionsAsync(string email) =>
        app.Database.ScalarAsync<long>(
            "select count(*) from auth.sessions s join auth.users u on u.id = s.user_id where u.email = $1", email);
}
