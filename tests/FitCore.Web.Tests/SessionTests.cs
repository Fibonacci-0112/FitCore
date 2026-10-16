using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        app.Supabase.Logout = FakeSupabase.LogoutAnswer.SignedOut;
        app.Supabase.MetadataLength = 0;
        app.Supabase.SignInKeyRejected = false;
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

        // The page, and the component state persisted into it for WebAssembly: base64 JSON whose values are base64 again.
        var state = PersistedState().Matches(html).SelectMany(m => Decoded(m.Groups[1].Value)).ToList();
        var everything = string.Join('\n', state.Prepend(html));

        // The decoding reaches the inner values: the authentication state persisted there names the member.
        Assert.Contains(state, text => text.Contains(app.Supabase.Member.ToString(), StringComparison.Ordinal));

        Assert.Contains(SignedIn, html, StringComparison.Ordinal);
        Assert.NotEmpty(app.Supabase.IssuedTokens);
        Assert.All(app.Supabase.IssuedTokens, token => Assert.DoesNotContain(token, everything, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_rejected_API_key_at_sign_in_says_the_service_is_unavailable()
    {
        app.Supabase.SignInKeyRejected = true;

        using var response = await FitCoreApp.SignInAsync(browser);

        var page = await response.Content.ReadAsStringAsync(Cancel);
        Assert.Contains("couldn&#x27;t reach its sign-in service", page, StringComparison.Ordinal);
        Assert.DoesNotContain("don&#x27;t match an account", page, StringComparison.Ordinal);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("__Host-fitcore-session=", StringComparison.Ordinal)));
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
    public async Task An_outage_with_an_expired_token_shows_the_error_page_on_any_page()
    {
        app.Supabase.ExpiresIn = -60;
        (await FitCoreApp.SignInAsync(browser)).Dispose();
        app.Supabase.Refresh = FakeSupabase.RefreshAnswer.ServerError;

        using var response = await browser.GetAsync("/", Cancel);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
        Assert.Contains("Sign-in is unavailable", await response.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("__Host-fitcore-session=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_session_bigger_than_one_cookie_is_split_and_deleted_whole()
    {
        app.Supabase.MetadataLength = 6000;
        using var signIn = await FitCoreApp.SignInAsync(browser);
        Assert.Contains(signIn.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-fitcore-sessionC2=", StringComparison.Ordinal));

        var home = await browser.GetStringAsync("/", Cancel);
        Assert.Contains(SignedIn, home, StringComparison.Ordinal);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = FitCoreApp.AntiforgeryToken(home) });
        using var signOut = await browser.PostAsync("/account/sign-out", form, Cancel);
        Assert.Contains(signOut.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-fitcore-sessionC2=;", StringComparison.Ordinal));
        Assert.DoesNotContain(SignedIn, await browser.GetStringAsync("/", Cancel), StringComparison.Ordinal);
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
    public async Task A_cookie_write_to_the_API_needs_the_token_in_the_header_not_a_form_field()
    {
        (await FitCoreApp.SignInAsync(browser)).Dispose();
        var token = FitCoreApp.AntiforgeryToken(await browser.GetStringAsync("/", Cancel));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/profile")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }),
        };
        request.Headers.Add("Origin", "https://localhost");

        using var response = await browser.SendAsync(request, Cancel);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    public async Task A_malformed_bearer_token_gets_401(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await app.Browser().SendAsync(request, Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_bearer_token_before_its_not_before_time_gets_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", app.Supabase.IssueToken(notBeforeIn: 300));
        using var response = await app.Browser().SendAsync(request, Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_signed_with_a_key_rotated_in_during_an_outage_answers_503()
    {
        await using var fresh = new FitCoreApp();
        var client = fresh.Browser();
        Assert.Equal((HttpStatusCode.OK, true), await BearerHomeAsync(client, fresh.Supabase.IssueToken()));

        fresh.Clock.Offset = TimeSpan.FromMinutes(1);
        fresh.Supabase.RotateKey();
        fresh.Supabase.JwksReachable = false;

        // Unverifiable, not invalid: during the outage and its back-off alike.
        Assert.Equal((HttpStatusCode.ServiceUnavailable, false), await BearerHomeAsync(client, fresh.Supabase.IssueToken()));
        Assert.Equal((HttpStatusCode.ServiceUnavailable, false), await BearerHomeAsync(client, fresh.Supabase.IssueToken()));

        fresh.Supabase.JwksReachable = true;
        fresh.Clock.Offset = TimeSpan.FromMinutes(2);
        Assert.Equal((HttpStatusCode.OK, true), await BearerHomeAsync(client, fresh.Supabase.IssueToken()));
    }

    [Fact]
    public async Task A_key_rotated_in_right_after_a_fetch_is_unverifiable_until_the_next_one()
    {
        await using var fresh = new FitCoreApp();
        var client = fresh.Browser();
        var retired = fresh.Supabase.IssueToken();
        Assert.Equal((HttpStatusCode.OK, true), await BearerHomeAsync(client, retired));

        // Within the back-off since that fetch, so the new key can't be fetched yet: 503, not a signed-out member.
        fresh.Supabase.RotateKey();
        Assert.Equal((HttpStatusCode.ServiceUnavailable, false), await BearerHomeAsync(client, fresh.Supabase.IssueToken()));

        fresh.Clock.Offset = TimeSpan.FromMinutes(1);
        Assert.Equal((HttpStatusCode.OK, true), await BearerHomeAsync(client, fresh.Supabase.IssueToken()));

        // A key a fetch made for it came back without is invalid, then and during the back-off.
        fresh.Clock.Offset = TimeSpan.FromMinutes(2);
        Assert.Equal((HttpStatusCode.OK, false), await BearerHomeAsync(client, retired));
        Assert.Equal((HttpStatusCode.OK, false), await BearerHomeAsync(client, retired));
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

    [Theory]
    [InlineData(FakeSupabase.LogoutAnswer.ServerError)]
    [InlineData(FakeSupabase.LogoutAnswer.KeyRejected)]
    public async Task Sign_out_keeps_the_session_when_Supabase_doesnt_confirm_it(FakeSupabase.LogoutAnswer answer)
    {
        (await FitCoreApp.SignInAsync(browser)).Dispose();
        app.Supabase.Logout = answer;
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
    public async Task A_cross_site_sign_out_without_the_cookie_changes_nothing()
    {
        // SameSite=Lax keeps the cookie off a cross-site POST, so the browser may still hold a session.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/sign-out") { Content = new FormUrlEncodedContent([]) };
        request.Headers.Add("Origin", "https://evil.example");

        using var response = await app.Browser().SendAsync(request, Cancel);

        Assert.Equal("/", RedirectPath(response));
        Assert.False(response.Headers.Contains("Clear-Site-Data"));
        Assert.False(response.Headers.Contains("Set-Cookie"));
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

    /// <summary>The text, and every base64 string in it once decoded, in JSON values to any depth.</summary>
    private static IEnumerable<string> Decoded(string base64)
    {
        string text;
        try
        {
            text = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            yield break;
        }

        yield return text;
        JsonNode? json;
        try
        {
            json = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            yield break;
        }

        foreach (var value in Strings(json))
        {
            foreach (var inner in Decoded(value))
            {
                yield return inner;
            }
        }
    }

    private static IEnumerable<string> Strings(JsonNode? node) => node switch
    {
        JsonObject o => o.SelectMany(p => Strings(p.Value)),
        JsonArray a => a.SelectMany(Strings),
        JsonValue v when v.TryGetValue<string>(out var text) => [text],
        _ => [],
    };

    /// <summary>The home page as a bearer caller: its status, and whether it shows the caller signed in.</summary>
    private static async Task<(HttpStatusCode Status, bool SignedIn)> BearerHomeAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, Cancel);
        return (response.StatusCode, (await response.Content.ReadAsStringAsync(Cancel)).Contains(SignedIn, StringComparison.Ordinal));
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
