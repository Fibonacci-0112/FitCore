using FitCore.Client.Core.Resources;

namespace FitCore.Web.Tests;

/// <summary>
/// The account pages show their text from the resource files (NFR-L10N-2): labels, validation messages, and sentences
/// with a link in them. The architecture tests stop text being written into the pages; these check it still arrives.
/// </summary>
public sealed class AccountTextTests(FitCoreApp app) : IClassFixture<FitCoreApp>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_sign_in_page_shows_its_resource_text()
    {
        var page = Decode(await app.Browser().GetStringAsync("/account/sign-in", Cancel));

        Assert.Contains($"<title>{TextFormat.PageTitle(AccountStrings.SignInHeading)}</title>", page, StringComparison.Ordinal);
        Assert.Contains($">{AccountStrings.EmailLabel}</label>", page, StringComparison.Ordinal);
        Assert.Contains($">{AccountStrings.SignInButton}</button>", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sentence_keeps_its_link_in_the_place_the_resource_gives()
    {
        var page = Decode(await app.Browser().GetStringAsync("/account/sign-in", Cancel));

        // "New to FitCore? {0}" with the link where {0} was, and nothing split off around it.
        var expected = AccountStrings.NewToFitCore.Replace("{0}", $"<a href=\"account/sign-up\">{AccountStrings.CreateAccountLink}</a>", StringComparison.Ordinal);
        Assert.Contains(expected, page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validation_messages_come_from_the_resources()
    {
        using var browser = app.Browser();
        using var response = await FitCoreApp.SignInAsync(browser, password: "", email: "");

        var page = Decode(await response.Content.ReadAsStringAsync(Cancel));
        Assert.Contains(AccountStrings.EmailRequired, page, StringComparison.Ordinal);
        Assert.Contains(AccountStrings.CurrentPasswordRequired, page, StringComparison.Ordinal);
    }

    private static string Decode(string html) => System.Net.WebUtility.HtmlDecode(html);
}
