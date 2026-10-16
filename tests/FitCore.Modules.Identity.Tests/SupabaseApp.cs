using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FitCore.Testing;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FitCore.Modules.Identity.Tests;

/// <summary>
/// fitcore-app against the local Supabase stack (`npx supabase start`): its Development settings point at the local
/// Auth and database. Every member these tests sign up uses an <c>@identity.test</c> address and is deleted afterwards.
/// </summary>
public sealed partial class SupabaseApp : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "correct horse battery staple";

    /// <summary>The local stack's mail catcher. Override with FITCORE_TEST_MAILPIT.</summary>
    private static readonly Uri Mailpit = new(Environment.GetEnvironmentVariable("FITCORE_TEST_MAILPIT") ?? "http://127.0.0.1:54324/");

    private readonly HttpClient _mail = new() { BaseAddress = Mailpit };

    public LocalDatabase Database { get; } = new();

    public static string NewEmail() => $"{Guid.NewGuid():N}@identity.test";

    public HttpClient Browser() => CreateClient(new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    public ValueTask InitializeAsync() => Database.InitializeAsync();

    public override async ValueTask DisposeAsync()
    {
        await Database.ExecuteAsync("delete from auth.users where email like '%@identity.test'");
        await Database.DisposeAsync();
        _mail.Dispose();
        await base.DisposeAsync();
    }

    /// <summary>Fills in and posts a server-rendered form, with the antiforgery token the page carries.</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(HttpClient browser, string path, string handler, Dictionary<string, string> fields)
    {
        var page = await browser.GetStringAsync(path, TestContext.Current.CancellationToken);
        fields["_handler"] = handler;
        fields["__RequestVerificationToken"] = AntiforgeryToken(page);
        using var form = new FormUrlEncodedContent(fields);
        return await browser.PostAsync(path, form, TestContext.Current.CancellationToken);
    }

    public static string AntiforgeryToken(string html) =>
        RequestToken().Match(html) is { Success: true } match ? match.Groups[1].Value : throw new InvalidOperationException("The page has no antiforgery token.");

    /// <summary>The confirmation link Supabase Auth emailed to <paramref name="email"/>, as a local path.</summary>
    public async Task<string> ConfirmationLinkAsync(string email)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var search = await _mail.GetFromJsonAsync<JsonObject>($"api/v1/search?query={Uri.EscapeDataString("to:" + email)}", TestContext.Current.CancellationToken);
            if (search?["messages"]?.AsArray() is [{ } first, ..])
            {
                var message = await _mail.GetFromJsonAsync<JsonObject>($"api/v1/message/{first["ID"]}", TestContext.Current.CancellationToken);
                var link = Link().Match((string?)message?["HTML"] ?? "").Groups[1].Value.Replace("&amp;", "&", StringComparison.Ordinal);
                return new Uri(link).PathAndQuery;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        throw new InvalidOperationException($"No confirmation email reached {email}.");
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"", RegexOptions.None, "en-US")]
    private static partial Regex RequestToken();

    [GeneratedRegex("href=\"([^\"]+/account/confirm[^\"]+)\"", RegexOptions.None, "en-US")]
    private static partial Regex Link();
}
