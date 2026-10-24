using FitCore.Client.Auth;
using FitCore.Client.Core.Accounts;

namespace FitCore.Client.Tests;

/// <summary>
/// The device apps' calls straight to Supabase Auth with the publishable key (implementation plan §3.3): sign-up with
/// the profile, the 18+ gate and Auth's own refusals, which skip every check the app makes; then sign-in, refresh and
/// sign-out, which revokes the session.
/// </summary>
public sealed class SupabaseMemberAccountsTests(LocalAuth auth) : IClassFixture<LocalAuth>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_sign_up_creates_an_unconfirmed_member_with_their_profile_and_emails_them()
    {
        var email = LocalAuth.NewEmail();

        var status = await auth.Accounts.SignUpAsync(Request(email, new DateOnly(1990, 4, 1)), Cancel);

        Assert.Equal(SignUpStatus.ConfirmationSent, status);
        Assert.False(await auth.Database.ScalarAsync<bool>("select email_confirmed_at is not null from auth.users where email = $1", email));
        Assert.Equal(
            "Ada Member|1990-04-01|Europe/Stockholm",
            await auth.Database.ScalarAsync<string>(
                """
                select concat_ws('|', p.display_name, p.birth_date, p.time_zone)
                from identity.profiles p join auth.users u on u.id = p.member_id
                where u.email = $1
                """,
                email));
        Assert.True(await auth.MailReachedAsync(email));
    }

    [Fact]
    public async Task An_under_18_birth_date_is_refused_and_creates_no_account()
    {
        var email = LocalAuth.NewEmail();

        var status = await auth.Accounts.SignUpAsync(Request(email, DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-17)), Cancel);

        Assert.Equal(SignUpStatus.Underage, status);
        Assert.Equal(0L, await auth.Database.ScalarAsync<long>("select count(*) from auth.users where email = $1", email));
    }

    [Fact]
    public async Task A_short_password_is_refused_by_Supabase_Auth_too()
    {
        var request = Request(LocalAuth.NewEmail(), new DateOnly(1990, 4, 1)) with { Password = "fourteen chars" };

        Assert.Equal(SignUpStatus.WeakPassword, await auth.Accounts.SignUpAsync(request, Cancel));
    }

    [Fact]
    public async Task An_address_Supabase_Auth_cant_use_is_refused()
    {
        var request = Request("not-an-address", new DateOnly(1990, 4, 1));

        Assert.Equal(SignUpStatus.Refused, await auth.Accounts.SignUpAsync(request, Cancel));
    }

    [Fact]
    public async Task Auth_that_cant_be_reached_is_unavailable()
    {
        // Nothing listens on the discard port locally.
        using var accounts = new SupabaseMemberAccounts(new SupabaseAuthOptions(new Uri("http://127.0.0.1:9"), "sb_publishable_unused"));

        var status = await accounts.SignUpAsync(Request(LocalAuth.NewEmail(), new DateOnly(1990, 4, 1)), Cancel);

        Assert.Equal(SignUpStatus.Unavailable, status);
    }

    [Fact]
    public async Task A_confirmed_member_signs_in_with_a_session_that_expires_with_its_access_token()
    {
        var email = await auth.ConfirmedMemberAsync();

        var result = await auth.Accounts.SignInAsync(email, LocalAuth.Password, Cancel);

        Assert.Equal(SignInStatus.SignedIn, result.Status);
        var session = Assert.IsType<MemberSession>(result.Session);
        Assert.NotEmpty(session.AccessToken);
        Assert.NotEmpty(session.RefreshToken);

        // supabase/config.toml: jwt_expiry = 900.
        Assert.InRange(session.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromSeconds(840), TimeSpan.FromSeconds(900));
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_address_read_the_same()
    {
        var email = await auth.ConfirmedMemberAsync();

        Assert.Equal(SignInStatus.InvalidCredentials, (await auth.Accounts.SignInAsync(email, "not the password at all", Cancel)).Status);
        Assert.Equal(SignInStatus.InvalidCredentials, (await auth.Accounts.SignInAsync(LocalAuth.NewEmail(), LocalAuth.Password, Cancel)).Status);
    }

    [Fact]
    public async Task A_member_who_hasnt_confirmed_their_address_is_told_to()
    {
        var email = LocalAuth.NewEmail();
        await auth.Accounts.SignUpAsync(Request(email, new DateOnly(1990, 4, 1)), Cancel);

        var result = await auth.Accounts.SignInAsync(email, LocalAuth.Password, Cancel);

        Assert.Equal(SignInStatus.EmailNotConfirmed, result.Status);
        Assert.Null(result.Session);
    }

    [Fact]
    public async Task A_refresh_needs_only_the_refresh_token_so_an_expired_access_token_doesnt_stop_it()
    {
        var signedIn = await SignedInAsync();

        var result = await auth.Accounts.RefreshAsync(signedIn with { AccessToken = "expired" }, Cancel);

        Assert.Equal(RefreshStatus.Refreshed, result.Status);
        Assert.NotEqual(signedIn.RefreshToken, Assert.IsType<MemberSession>(result.Session).RefreshToken);
    }

    [Fact]
    public async Task Signing_out_revokes_the_refresh_token()
    {
        var signedIn = await SignedInAsync();

        Assert.Equal(SignOutStatus.SessionEnded, await auth.Accounts.SignOutAsync(signedIn, Cancel));

        Assert.Equal(RefreshStatus.SessionEnded, (await auth.Accounts.RefreshAsync(signedIn, Cancel)).Status);
    }

    [Fact]
    public async Task Signing_out_a_session_that_is_already_gone_counts_as_ended()
    {
        var signedIn = await SignedInAsync();
        await auth.Accounts.SignOutAsync(signedIn, Cancel);

        Assert.Equal(SignOutStatus.SessionEnded, await auth.Accounts.SignOutAsync(signedIn, Cancel));
    }

    [Fact]
    public async Task Auth_that_cant_be_reached_leaves_sign_in_refresh_and_sign_out_unavailable()
    {
        using var accounts = new SupabaseMemberAccounts(new SupabaseAuthOptions(new Uri("http://127.0.0.1:9"), "sb_publishable_unused"));
        var session = new MemberSession("access", "refresh", DateTimeOffset.UtcNow);

        Assert.Equal(SignInStatus.Unavailable, (await accounts.SignInAsync(LocalAuth.NewEmail(), LocalAuth.Password, Cancel)).Status);
        Assert.Equal(RefreshStatus.Unavailable, (await accounts.RefreshAsync(session, Cancel)).Status);
        Assert.Equal(SignOutStatus.Unavailable, await accounts.SignOutAsync(session, Cancel));
    }

    private async Task<MemberSession> SignedInAsync()
    {
        var result = await auth.Accounts.SignInAsync(await auth.ConfirmedMemberAsync(), LocalAuth.Password, Cancel);
        return Assert.IsType<MemberSession>(result.Session);
    }

    private static SignUpRequest Request(string email, DateOnly birthDate) =>
        new(email, LocalAuth.Password, "Ada Member", birthDate, "Europe/Stockholm");
}
