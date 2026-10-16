using FitCore.Client.Auth;
using FitCore.Client.Core.Accounts;

namespace FitCore.Client.Tests;

/// <summary>
/// Sign-up from the device apps, straight to Supabase Auth with the publishable key (implementation plan §3.3): the
/// profile, the 18+ gate and Auth's own refusals, which skip every check the app makes.
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

    private static SignUpRequest Request(string email, DateOnly birthDate) =>
        new(email, LocalAuth.Password, "Ada Member", birthDate, "Europe/Stockholm");
}
