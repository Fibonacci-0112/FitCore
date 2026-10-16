using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace FitCore.Web.Tests;

/// <summary>
/// The web session (ADR 0005): the cookie, server-side refresh, one identity for bearer and cookie callers, and the
/// cross-site write checks. Supabase Auth is <see cref="FakeSupabase"/>; the integration tests cover the real one.
/// </summary>
public sealed partial class SessionTests : IClassFixture<FitCoreApp>
{
    private const string SignedIn = "re signed in.";
    private readonly FitCoreApp app;
    private readonly HttpClient browser;

    public SessionTests(FitCoreApp app)
    {
        this.app = app;
        app.Supabase.ExpiresIn = 900;
        app.Supabase.Refresh = FakeSupabase.RefreshAnswer.NewSession;
        app.Supabase.LogoutWorks = true;
        app.Supabase.Requests.Clear();
        browser = app.Browser();
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Signing_in_sets_the_session_cookie_and_goes_home()
    {
        using var response = await FitCoreApp.SignInAsync(browser);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", RedirectPath(response));
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-fitcore-session=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(SignedIn, await browser.GetStringAsync("/", Cancel), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sign_in_calls_Supabase_with_the_secret_key_and_the_members_IP()
    {
        using var response = await FitCoreApp.SignInAsync(browser);

        var signIn = Assert.Single(app.Supabase.Requests, r => r.Path == "/auth/v1/token?grant_type=password");
        Assert.Equal(FakeSupabase.SecretKey, signIn.ApiKey);
        Assert.Equal(FitCoreApp.MemberIp.ToString(), signIn.ForwardedFor);
    }

    [Fact]
    public async Task A_wrong_password_shows_why_and_sets_no_cookie()
    {
        using var response = await FitCoreApp.SignInAsync(browser, password: "not the right password");
        var html = await response.Content.ReadAsStringAsync(Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("match an account", html, StringComparison.Ordinal);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("__Host-fitcore-session=", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("/account/check-email", "/account/check-email")]
    [InlineData("//evil.example/steal", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("https://evil.example/", "/")]
    public async Task Sign_in_returns_only_to_local_pages(string returnUrl, string expected)
    {
        using var response = await FitCoreApp.SignInAsync(browser, returnUrl: returnUrl);

        Assert.Equal(expected, RedirectPath(response));
    }

    [Fact]
    public async Task The_page_never_carries_the_token()
    {
        (await FitCoreApp.SignInAsync(browser)).Dispose();
        var html = await browser.GetStringAsync("/", Cancel);

        // The page, and the component state persisted into it for WebAssembly, which is base64.
        var state = PersistedState().Matches(html).Select(m => Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups[1].Value)));
        var everything = string.Join('\n', state.Prepend(html));

        Assert.Contains(SignedIn, html, StringComparison.Ordinal);
        Assert.NotEmpty(app.Supabase.IssuedTokens);
        Assert.All(app.Supabase.IssuedTokens, token => Assert.DoesNotContain(token, everything, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_forged_cookie_is_ignored_and_deleted()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Cookie", "__Host-fitcore-session=not-a-session");
        using var response = await browser.SendAsync(request, Cancel);

        Assert.DoesNotContain(SignedIn, await response.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-fitcore-session=;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_API_answers_an_anonymous_caller_with_401()
    {
        using var response = await browser.GetAsync("/v1/profile", Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_session_close_to_expiry_is_refreshed_on_the_server()
    {
        app.Supabase.ExpiresIn = 60; // inside the two-minute refresh window
        (await FitCoreApp.SignInAsync(browser)).Dispose();

        using var response = await browser.GetAsync("/", Cancel);

        Assert.Contains(SignedIn, await response.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);
        Assert.Contains(app.Supabase.Requests, r => r.Path == "/auth/v1/token?grant_type=refresh_token" && r.ApiKey == FakeSupabase.SecretKey);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-fitcore-session=", StringComparison.Ordinal) && !c.StartsWith("__Host-fitcore-session=;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_refused_refresh_ends_the_session()
    {
        app.Supabase.ExpiresIn = 60;
        (await FitCoreApp.SignInAsync(browser)).Dispose();
        app.Supabase.Refresh = FakeSupabase.RefreshAnswer.Refused;

        using var response = await browser.GetAsync("/", Cancel);

        Assert.DoesNotContain(SignedIn, await response.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-fitcore-session=;", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(FakeSupabase.RefreshAnswer.KeyRejected)]
    [InlineData(FakeSupabase.RefreshAnswer.ServerError)]
    [InlineData(FakeSupabase.RefreshAnswer.Unreachable)]
    public async Task An_outage_with_an_expired_token_answers_503_and_keeps_the_cookie(FakeSupabase.RefreshAnswer answer)
    {
        app.Supabase.ExpiresIn = -60;
        (await FitCoreApp.SignInAsync(browser)).Dispose();
        app.Supabase.Refresh = answer;

        using var response = await browser.GetAsync("/v1/profile", Cancel);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("__Host-fitcore-session=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task An_outage_while_the_token_still_works_keeps_the_member_signed_in()
    {
        app.Supabase.ExpiresIn = 60;
        (await FitCoreApp.SignInAsync(browser)).Dispose();
        app.Supabase.Refresh = FakeSupabase.RefreshAnswer.ServerError;

        using var response = await browser.GetAsync("/", Cancel);

        Assert.Contains(SignedIn, await response.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("__Host-fitcore-session=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_cookie_write_to_the_API_needs_the_antiforgery_token_and_this_origin()
    {
        (await FitCoreApp.SignInAsync(browser)).Dispose();
        var token = FitCoreApp.AntiforgeryToken(await browser.GetStringAsync("/", Cancel));

        // GET /v1/profile has no POST, so 405 shows the checks let the request through to routing.
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync(origin: "https://localhost", token: null));
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync(origin: "https://evil.example", token: token));
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync(origin: null, token: token));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, await PostAsync(origin: "https://localhost", token: token));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, await PostAsync(origin: null, token: token, fetchSite: "same-origin"));
    }

    [Fact]
    public async Task A_bearer_write_skips_the_cross_site_checks()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", app.Supabase.IssueToken());
        using var response = await app.Browser().SendAsync(request, Cancel);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task An_invalid_bearer_token_gets_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", app.Supabase.IssueToken(expiresIn: -120));
        using var response = await app.Browser().SendAsync(request, Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Signing_out_revokes_the_session_and_clears_the_browser()
    {
        (await FitCoreApp.SignInAsync(browser)).Dispose();
        var home = await browser.GetStringAsync("/", Cancel);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = FitCoreApp.AntiforgeryToken(home) });

        using var response = await browser.PostAsync("/account/sign-out", form, Cancel);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("\"cache\", \"storage\"", Assert.Single(response.Headers.GetValues("Clear-Site-Data")));
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-fitcore-session=;", StringComparison.Ordinal));
        Assert.Contains(app.Supabase.Requests, r => r.Path == "/auth/v1/logout?scope=local" && r.Bearer is { Length: > 0 });
        Assert.DoesNotContain(SignedIn, await browser.GetStringAsync("/", Cancel), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sign_out_keeps_the_session_when_Supabase_cant_revoke_it()
    {
        (await FitCoreApp.SignInAsync(browser)).Dispose();
        app.Supabase.LogoutWorks = false;
        var home = await browser.GetStringAsync("/", Cancel);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = FitCoreApp.AntiforgeryToken(home) });

        using var response = await browser.PostAsync("/account/sign-out", form, Cancel);

        Assert.Equal("/account/sign-out-failed", RedirectPath(response));
        Assert.False(response.Headers.Contains("Clear-Site-Data"));
        Assert.Contains(SignedIn, await browser.GetStringAsync("/", Cancel), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Signing_out_without_a_session_just_goes_home()
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>());

        using var response = await browser.PostAsync("/account/sign-out", form, Cancel);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", RedirectPath(response));
        Assert.DoesNotContain(app.Supabase.Requests, r => r.Path.StartsWith("/auth/v1/logout", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unreachable_JWKS_answers_503_and_backs_off()
    {
        await using var fresh = new FitCoreApp();
        fresh.Supabase.JwksReachable = false;
        var client = fresh.Browser();

        for (var i = 0; i < 3; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/profile");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fresh.Supabase.IssueToken());
            using var response = await client.SendAsync(request, Cancel);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        // One fetch, then requests answer at once instead of each waiting on Supabase.
        Assert.Single(fresh.Supabase.Requests, r => r.Path == "/auth/v1/.well-known/jwks.json");
    }

    [Fact]
    public async Task Signing_out_needs_the_antiforgery_token()
    {
        (await FitCoreApp.SignInAsync(browser)).Dispose();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>());

        using var response = await browser.PostAsync("/account/sign-out", form, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(app.Supabase.Requests, r => r.Path.StartsWith("/auth/v1/logout", StringComparison.Ordinal));
    }

    // NavigationManager redirects to an absolute URL on this site.
    private static string? RedirectPath(HttpResponseMessage response) =>
        response.Headers.Location is { IsAbsoluteUri: true } url ? url.PathAndQuery : response.Headers.Location?.OriginalString;

    private async Task<HttpStatusCode> PostAsync(string? origin, string? token, string? fetchSite = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/profile") { Content = new StringContent("{}") };
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        if (token is not null)
        {
            request.Headers.Add("RequestVerificationToken", token);
        }

        if (fetchSite is not null)
        {
            request.Headers.Add("Sec-Fetch-Site", fetchSite);
        }

        using var response = await browser.SendAsync(request, Cancel);
        return response.StatusCode;
    }

    [GeneratedRegex("Component-State:([A-Za-z0-9+/=]+)", RegexOptions.None, "en-US")]
    private static partial Regex PersistedState();
}
