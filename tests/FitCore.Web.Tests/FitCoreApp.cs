using System.Net;
using System.Text.RegularExpressions;
using FitCore.Platform.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FitCore.Web.Tests;

/// <summary>fitcore-app in memory, signing members in against <see cref="FakeSupabase"/>.</summary>
public sealed partial class FitCoreApp : WebApplicationFactory<Program>
{
    /// <summary>The address every test request comes from. The test server sets none, unlike Kestrel.</summary>
    public static readonly IPAddress MemberIp = IPAddress.Parse("203.0.113.7");

    public FakeSupabase Supabase { get; } = new();

    /// <summary>The app's clock: real time, which a test can move forward.</summary>
    public TestClock Clock { get; } = new();

    /// <summary>A browser: HTTPS, so the <c>__Host-</c> cookie is kept, with cookies and without following redirects.</summary>
    public HttpClient Browser() => CreateClient(new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    /// <summary>Signs in through the sign-in page, as a member would.</summary>
    public static async Task<HttpResponseMessage> SignInAsync(HttpClient browser, string password = FakeSupabase.Password, string returnUrl = "")
    {
        var path = "/account/sign-in" + (returnUrl.Length > 0 ? "?returnUrl=" + Uri.EscapeDataString(returnUrl) : "");
        var page = await browser.GetStringAsync(path, TestContext.Current.CancellationToken);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "sign-in",
            ["__RequestVerificationToken"] = AntiforgeryToken(page),
            ["Input.Email"] = "member@members.test",
            ["Input.Password"] = password,
        });
        return await browser.PostAsync(path, form, TestContext.Current.CancellationToken);
    }

    /// <summary>The antiforgery request token a rendered form carries.</summary>
    public static string AntiforgeryToken(string html) =>
        RequestToken().Match(html) is { Success: true } match ? match.Groups[1].Value : throw new InvalidOperationException("The page has no antiforgery token.");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Supabase:Url", FakeSupabase.Url.ToString());
        builder.UseSetting("Supabase:SecretKey", FakeSupabase.SecretKey);
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(MemberAuth.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Supabase);
            services.AddTransient<IStartupFilter, MemberIpFilter>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    private sealed class MemberIpFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress ??= MemberIp;
                return nextMiddleware();
            });
            next(app);
        };
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"", RegexOptions.None, "en-US")]
    private static partial Regex RequestToken();
}

/// <summary>Real time plus an offset a test sets.</summary>
public sealed class TestClock : TimeProvider
{
    public TimeSpan Offset { get; set; }

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Offset;
}
