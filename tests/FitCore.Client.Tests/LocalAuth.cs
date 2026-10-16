using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FitCore.Client.Auth;
using FitCore.Testing;

namespace FitCore.Client.Tests;

/// <summary>
/// The local Supabase stack's Auth, Mailpit and database (`npx supabase start`), reached the way the device apps reach
/// a hosted project: with the publishable key. Every member these tests sign up uses an <c>@client.test</c> address and
/// is deleted afterwards. Override the addresses with FITCORE_TEST_SUPABASE_URL, FITCORE_TEST_PUBLISHABLE_KEY and
/// FITCORE_TEST_MAILPIT.
/// </summary>
public sealed class LocalAuth : IAsyncLifetime
{
    public const string Password = "correct horse battery staple";

    // The local stack's fixed publishable key, the same one FitCore.Maui builds with by default.
    private const string LocalPublishableKey = "sb_publishable_ACJWlzQHlZjBrEguHvfOxg_3BJgxAaH";

    private readonly HttpClient _mail = new()
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("FITCORE_TEST_MAILPIT") ?? "http://127.0.0.1:54324/"),
    };

    public static SupabaseAuthOptions Options { get; } = new(
        new Uri(Environment.GetEnvironmentVariable("FITCORE_TEST_SUPABASE_URL") ?? "http://127.0.0.1:54321"),
        Environment.GetEnvironmentVariable("FITCORE_TEST_PUBLISHABLE_KEY") ?? LocalPublishableKey);

    public SupabaseMemberAccounts Accounts { get; } = new(Options);

    public LocalDatabase Database { get; } = new();

    public static string NewEmail() => $"{Guid.NewGuid():N}@client.test";

    public ValueTask InitializeAsync() => Database.InitializeAsync();

    public async ValueTask DisposeAsync()
    {
        await Database.ExecuteAsync("delete from auth.users where email like '%@client.test'");
        await Database.DisposeAsync();
        Accounts.Dispose();
        _mail.Dispose();
    }

    /// <summary>Whether Supabase Auth emailed <paramref name="email"/>, waiting up to 10 seconds for the message.</summary>
    public async Task<bool> MailReachedAsync(string email)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var search = await _mail.GetFromJsonAsync<JsonObject>(
                $"api/v1/search?query={Uri.EscapeDataString("to:" + email)}", TestContext.Current.CancellationToken);
            if (search?["messages"]?.AsArray() is [_, ..])
            {
                return true;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        return false;
    }
}
