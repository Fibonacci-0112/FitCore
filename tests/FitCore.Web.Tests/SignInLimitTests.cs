using System.Net;
using FitCore.Platform.Limits;

namespace FitCore.Web.Tests;

/// <summary>
/// The account endpoints' limits (NFR-SCL-5, NFR-SEC-4, ADR 0005 decision 9): per address and per account, with
/// progressive lockout, answered with 429 and <c>Retry-After</c> before Supabase Auth is asked. Each test gets its own
/// app, so its counters start empty.
/// </summary>
public sealed class SignInLimitTests : IAsyncLifetime
{
    private const string Email = "member@members.test";
    private const string WrongPassword = "not the password at all";
    private const string Locked = "Too many wrong passwords for this email address.";
    private const string TooMany = "Too many attempts.";

    private readonly FitCoreApp _app = new();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task The_fifth_wrong_password_locks_the_address_out_and_Supabase_is_not_asked_again()
    {
        using var browser = _app.Browser();
        for (var attempt = 1; attempt < SignInLimits.FailuresBeforeLockout; attempt++)
        {
            using var wrong = await FitCoreApp.SignInAsync(browser, WrongPassword);
            Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        }

        using var fifth = await FitCoreApp.SignInAsync(browser, WrongPassword);
        using var right = await FitCoreApp.SignInAsync(browser);

        await AssertRefusedAsync(fifth, Locked, SignInLimits.FirstLockout);
        await AssertRefusedAsync(right, Locked, SignInLimits.FirstLockout);
        Assert.Equal(SignInLimits.FailuresBeforeLockout, PasswordSignIns());
        Assert.DoesNotContain(right.Headers, h => h.Key == "Set-Cookie" && h.Value.Any(v => v.StartsWith("__Host-fitcore-session=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_lockout_holds_from_every_address()
    {
        await LockOutAsync();

        using var elsewhere = _app.Browser();
        using var response = await FitCoreApp.SignInAsync(elsewhere);

        await AssertRefusedAsync(response, Locked, SignInLimits.FirstLockout);
    }

    [Fact]
    public async Task A_lockout_holds_whatever_the_case_or_spacing_of_the_address()
    {
        await LockOutAsync();

        using var browser = _app.Browser();
        using var response = await FitCoreApp.SignInAsync(browser, email: "  MEMBER@Members.Test ");

        await AssertRefusedAsync(response, Locked, SignInLimits.FirstLockout);
    }

    [Fact]
    public async Task A_lockout_ends_after_15_minutes_and_the_next_one_lasts_twice_as_long()
    {
        await LockOutAsync();
        _app.Clock.Offset += SignInLimits.FirstLockout + TimeSpan.FromSeconds(1);

        using var browser = _app.Browser();
        for (var attempt = 1; attempt < SignInLimits.FailuresBeforeLockout; attempt++)
        {
            using var wrong = await FitCoreApp.SignInAsync(browser, WrongPassword);
            Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        }

        using var fifth = await FitCoreApp.SignInAsync(browser, WrongPassword);

        await AssertRefusedAsync(fifth, Locked, 2 * SignInLimits.FirstLockout);
    }

    [Fact]
    public async Task Signing_in_clears_earlier_wrong_passwords()
    {
        using var browser = _app.Browser();
        for (var attempt = 1; attempt < SignInLimits.FailuresBeforeLockout; attempt++)
        {
            using var wrong = await FitCoreApp.SignInAsync(browser, WrongPassword);
        }

        using (var right = await FitCoreApp.SignInAsync(browser))
        {
            Assert.Equal(HttpStatusCode.Redirect, right.StatusCode);
        }

        using var again = _app.Browser();
        for (var attempt = 1; attempt < SignInLimits.FailuresBeforeLockout; attempt++)
        {
            using var wrong = await FitCoreApp.SignInAsync(again, WrongPassword);
            Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        }
    }

    [Fact]
    public async Task One_address_gets_a_burst_of_20_attempts_then_one_every_6_seconds()
    {
        using var browser = _app.Browser();
        for (var attempt = 1; attempt <= SignInLimits.Burst; attempt++)
        {
            // A different account each time, so only the address limit applies.
            using var allowed = await FitCoreApp.SignInAsync(browser, WrongPassword, email: $"member{attempt}@members.test");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var refused = await FitCoreApp.SignInAsync(browser, email: "fresh@members.test");
        await AssertRefusedAsync(refused, TooMany, TimeSpan.FromSeconds(6));
        Assert.Equal(SignInLimits.Burst, PasswordSignIns());

        _app.Clock.Offset += TimeSpan.FromSeconds(6);
        using var refilled = await FitCoreApp.SignInAsync(browser, email: "fresh@members.test");
        Assert.Equal(HttpStatusCode.Redirect, refilled.StatusCode);
    }

    [Fact]
    public async Task Other_addresses_keep_their_own_attempts()
    {
        using var busy = _app.Browser();
        for (var attempt = 0; attempt <= SignInLimits.Burst; attempt++)
        {
            using var response = await FitCoreApp.SignInAsync(busy, WrongPassword, email: $"member{attempt}@members.test");
        }

        using var other = _app.Browser();
        using var signIn = await FitCoreApp.SignInAsync(other);

        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
    }

    [Fact]
    public async Task Sign_up_shares_the_address_limit_with_sign_in()
    {
        using var browser = _app.Browser();
        for (var attempt = 1; attempt <= SignInLimits.Burst; attempt++)
        {
            using var response = await FitCoreApp.SignInAsync(browser, WrongPassword, email: $"member{attempt}@members.test");
        }

        using var signUp = await SignUpAsync(browser);

        await AssertRefusedAsync(signUp, TooMany, TimeSpan.FromSeconds(6));
        Assert.DoesNotContain(_app.Supabase.Requests, r => r.Path == "/auth/v1/signup");
    }

    private async Task LockOutAsync()
    {
        using var browser = _app.Browser();
        for (var attempt = 1; attempt <= SignInLimits.FailuresBeforeLockout; attempt++)
        {
            using var wrong = await FitCoreApp.SignInAsync(browser, WrongPassword);
        }
    }

    private int PasswordSignIns() => _app.Supabase.Requests.Count(r => r.Path == "/auth/v1/token?grant_type=password");

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string message, TimeSpan wait)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var retryAfter = Assert.IsType<TimeSpan>(response.Headers.RetryAfter?.Delta);

        // The clock keeps moving while the test runs, so allow a few seconds less than the full wait.
        Assert.InRange(retryAfter, wait - TimeSpan.FromSeconds(5), wait);
        Assert.Contains(message, await response.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);
    }

    private static async Task<HttpResponseMessage> SignUpAsync(HttpClient browser)
    {
        var page = await browser.GetStringAsync("/account/sign-up", Cancel);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "sign-up",
            ["__RequestVerificationToken"] = FitCoreApp.AntiforgeryToken(page),
            ["Input.DisplayName"] = "Ada Member",
            ["Input.Email"] = "new@members.test",
            ["Input.Password"] = FakeSupabase.Password,
            ["Input.BirthDate"] = "1990-04-01",
            ["Input.TimeZone"] = "UTC",
        });
        return await browser.PostAsync("/account/sign-up", form, Cancel);
    }
}
