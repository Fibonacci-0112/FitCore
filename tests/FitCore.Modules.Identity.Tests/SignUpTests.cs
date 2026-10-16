using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FitCore.Contracts.Profiles;
using FitCore.Platform.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace FitCore.Modules.Identity.Tests;

/// <summary>
/// The web's email sign-up end to end (implementation plan PR 4): the form, Supabase Auth's confirmation email, the
/// profile the database creates from it, the 18+ gate, the session cookie, sign-out, and bearer access for MAUI.
/// </summary>
public sealed class SignUpTests(SupabaseApp stack) : IClassFixture<SupabaseApp>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_member_signs_up_confirms_their_email_and_reads_their_profile()
    {
        var browser = stack.Browser();
        var email = SupabaseApp.NewEmail();

        using var signUp = await SignUpAsync(browser, email, new DateOnly(1990, 4, 1));
        Assert.Equal(HttpStatusCode.Redirect, signUp.StatusCode);
        Assert.EndsWith("/account/check-email", signUp.Headers.Location!.ToString(), StringComparison.Ordinal);

        // Opening the link shows a page; only the POST confirms and signs in.
        var link = await stack.ConfirmationLinkAsync(email);
        using var confirm = await SupabaseApp.PostFormAsync(browser, link, "confirm", new() { ["Input.TokenHash"] = TokenHash(link) });
        Assert.Equal(HttpStatusCode.Redirect, confirm.StatusCode);

        using var profileResponse = await browser.GetAsync("/v1/profile", Cancel);
        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
        var profile = await profileResponse.Content.ReadFromJsonAsync<ProfileResponse>(Cancel);
        Assert.Equal(new ProfileResponse("Ada Member", "America/Chicago", "us_customary", "USD"), profile);
    }

    [Fact]
    public async Task An_under_18_birth_date_is_refused_and_creates_no_account()
    {
        var browser = stack.Browser();
        var email = SupabaseApp.NewEmail();
        var birthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-17);

        using var response = await SignUpAsync(browser, email, birthDate);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("18 or older", await response.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);
        Assert.Equal(0L, await CountUsersAsync(email));
    }

    [Fact]
    public async Task Supabase_Auth_refuses_an_under_18_sign_up_that_skips_the_form()
    {
        var auth = stack.Services.GetRequiredService<MemberAuth>();
        var email = SupabaseApp.NewEmail();

        var outcome = await auth.SignUpAsync(
            email, SupabaseApp.Password, new SignUpProfile("Kid", DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-12), "UTC"), null, Cancel);

        Assert.Equal(AuthStatus.Underage, outcome.Status);
        Assert.Equal(0L, await CountUsersAsync(email));
    }

    [Fact]
    public async Task Sign_in_before_confirming_says_to_confirm_first()
    {
        var browser = stack.Browser();
        var email = SupabaseApp.NewEmail();
        (await SignUpAsync(browser, email, new DateOnly(1985, 1, 1))).Dispose();

        using var response = await SignInAsync(browser, email, SupabaseApp.Password);

        Assert.Contains("Confirm your email address first", await response.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Signing_out_ends_the_session_in_Supabase()
    {
        var browser = stack.Browser();
        var email = await ConfirmedMemberAsync(browser);
        Assert.Equal(1L, await CountSessionsAsync(email));

        var home = await browser.GetStringAsync("/", Cancel);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = SupabaseApp.AntiforgeryToken(home) });
        using var signOut = await browser.PostAsync("/account/sign-out", form, Cancel);

        Assert.Equal(HttpStatusCode.Redirect, signOut.StatusCode);
        Assert.Equal(0L, await CountSessionsAsync(email));
        using var profile = await browser.GetAsync("/v1/profile", Cancel);
        Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);
    }

    [Fact]
    public async Task A_wrong_password_is_refused_and_the_right_one_signs_in()
    {
        var browser = stack.Browser();
        var email = await ConfirmedMemberAsync(stack.Browser());

        using var wrong = await SignInAsync(browser, email, "not the right password");
        Assert.Contains("match an account", await wrong.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);

        using var right = await SignInAsync(browser, email, SupabaseApp.Password);
        Assert.Equal(HttpStatusCode.Redirect, right.StatusCode);
        using var profile = await browser.GetAsync("/v1/profile", Cancel);
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
    }

    [Fact]
    public async Task A_bearer_token_reads_the_same_profile_as_the_cookie()
    {
        var email = await ConfirmedMemberAsync(stack.Browser());
        var outcome = await stack.Services.GetRequiredService<MemberAuth>().SignInWithPasswordAsync(email, SupabaseApp.Password, null, Cancel);
        Assert.Equal(AuthStatus.SignedIn, outcome.Status);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", outcome.Session!.AccessToken);
        using var response = await stack.CreateClient().SendAsync(request, Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Ada Member", (await response.Content.ReadFromJsonAsync<ProfileResponse>(Cancel))!.DisplayName);
    }

    private async Task<string> ConfirmedMemberAsync(HttpClient browser)
    {
        var email = SupabaseApp.NewEmail();
        (await SignUpAsync(browser, email, new DateOnly(1990, 4, 1))).Dispose();
        var link = await stack.ConfirmationLinkAsync(email);
        (await SupabaseApp.PostFormAsync(browser, link, "confirm", new() { ["Input.TokenHash"] = TokenHash(link) })).Dispose();
        return email;
    }

    private static Task<HttpResponseMessage> SignUpAsync(HttpClient browser, string email, DateOnly birthDate) =>
        SupabaseApp.PostFormAsync(browser, "/account/sign-up", "sign-up", new()
        {
            ["Input.DisplayName"] = "Ada Member",
            ["Input.Email"] = email,
            ["Input.Password"] = SupabaseApp.Password,
            ["Input.BirthDate"] = birthDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Input.TimeZone"] = "America/Chicago",
        });

    private static Task<HttpResponseMessage> SignInAsync(HttpClient browser, string email, string password) =>
        SupabaseApp.PostFormAsync(browser, "/account/sign-in", "sign-in", new()
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        });

    private static string TokenHash(string link) =>
        Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri("https://localhost" + link).Query)["token_hash"].ToString();

    private Task<long> CountUsersAsync(string email) =>
        stack.Database.ScalarAsync<long>("select count(*) from auth.users where email = $1", email);

    private Task<long> CountSessionsAsync(string email) =>
        stack.Database.ScalarAsync<long>(
            "select count(*) from auth.sessions s join auth.users u on u.id = s.user_id where u.email = $1", email);
}
