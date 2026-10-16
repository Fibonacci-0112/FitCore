using System.Text.RegularExpressions;

namespace FitCore.Architecture.Tests;

/// <summary>
/// User-visible text comes from resource files, so another language needs no code change (NFR-L10N-2, implementation
/// plan §3.6). The scan covers what Razor shows, the attributes screen readers and browsers read out, and validation
/// messages. Text a C# method returns isn't scanned; it goes through the same resource classes by review.
/// </summary>
public sealed partial class UserVisibleTextTests
{
    private static readonly string[] RazorPaths = [.. Files("src", "*.razor")];

    private static readonly string[] CodePaths = [.. Files("src", "*.cs"), .. RazorPaths];

    public static TheoryData<string> Razor => [.. RazorPaths];

    public static TheoryData<string> Code => [.. CodePaths];

    [Fact]
    public void The_scan_finds_the_pages()
    {
        Assert.Contains(RazorPaths, path => path.EndsWith("Account/SignUp.razor", StringComparison.Ordinal));
        Assert.Contains(RazorPaths, path => path.EndsWith("Layout/MainLayout.razor", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Razor))]
    public void Razor_shows_only_resource_text(string path)
    {
        Assert.Empty(HardCodedText(File.ReadAllText(Path.Combine(Solution.Root, path))));
    }

    [Theory]
    [MemberData(nameof(Code))]
    public void Validation_messages_come_from_resources(string path)
    {
        Assert.DoesNotMatch(LiteralErrorMessage(), File.ReadAllText(Path.Combine(Solution.Root, path)));
    }

    [Theory]
    [InlineData("<p>Hello</p>")]
    [InlineData("<button type=\"submit\">Sign in</button>")]
    [InlineData("<p>Already have an account? <a href=\"account/sign-in\">@AccountStrings.SignInLink</a></p>")]
    [InlineData("<PageTitle>Sign in · FitCore</PageTitle>")]
    [InlineData("<nav aria-label=\"Account\">")]
    [InlineData("<input placeholder=\"you@example.com\" />")]
    [InlineData("@if (error is not null)\n{\n    <p>Try again</p>\n}")]
    [InlineData("Loose text at the start\n<p>@X</p>")]
    public void The_scan_catches_hard_coded_text(string razor)
    {
        Assert.NotEmpty(HardCodedText(razor));
    }

    [Theory]
    [InlineData("<p>@AccountStrings.CheckEmailSent</p>")]
    [InlineData("<PageTitle>@TextFormat.PageTitle(AccountStrings.SignInHeading)</PageTitle>")]
    [InlineData("<nav aria-label=\"@AccountStrings.AccountNavLabel\"><a href=\"account/sign-in\">@AccountStrings.SignInLink</a></nav>")]
    [InlineData("@page \"/account/sign-in\"\n@attribute [ExcludeFromInteractiveRouting]\n@inject MemberAuth Auth\n<h1>@AccountStrings.SignInHeading</h1>")]
    [InlineData("@if (error is not null)\n{\n    <p role=\"alert\">@error</p>\n}\nelse\n{\n    <p>@(Format[..at])</p>\n}")]
    [InlineData("@* A comment may say anything. *@\n<!-- So may this. -->\n<p>@X</p>")]
    [InlineData("<p>@X</p>\n@code {\n    private string Text => \"<p>Not markup</p>\";\n}")]
    [InlineData("<p>@ShellStrings.AppName&nbsp;@@</p>")]
    public void The_scan_allows_resource_text(string razor)
    {
        Assert.Empty(HardCodedText(razor));
    }

    [Theory]
    [InlineData("[Required(ErrorMessage = \"Enter your name.\")]")]
    [InlineData("[StringLength(80, ErrorMessage=\"Too long\")]")]
    public void The_scan_catches_literal_validation_messages(string code)
    {
        Assert.Matches(LiteralErrorMessage(), code);
    }

    [Fact]
    public void The_scan_allows_resource_validation_messages()
    {
        Assert.DoesNotMatch(
            LiteralErrorMessage(),
            "[Required(ErrorMessageResourceType = typeof(AccountStrings), ErrorMessageResourceName = nameof(AccountStrings.NameRequired))]");
    }

    /// <summary>The text nodes and user-visible attribute values in a Razor file that aren't Razor expressions.</summary>
    private static List<string> HardCodedText(string razor)
    {
        var markup = Comments().Replace(razor, "");
        markup = WithoutBlocks(markup, "@code");
        markup = WithoutBlocks(markup, "@functions");
        markup = WithoutBlocks(markup, "@{");
        markup = Directives().Replace(markup, "");
        markup = ControlFlow().Replace(markup, "");

        var found = new List<string>();
        foreach (Match node in TextNode().Matches(">" + markup + "<"))
        {
            var text = Expressions().Replace(node.Groups[1].Value, "");
            text = BlockSyntax().Replace(text, "");
            if (Letter().IsMatch(text))
            {
                found.Add(node.Groups[1].Value.Trim());
            }
        }

        foreach (Match attribute in VisibleAttribute().Matches(markup))
        {
            var value = attribute.Groups[2].Value;
            if (!value.StartsWith('@') && Letter().IsMatch(value))
            {
                found.Add(attribute.Value);
            }
        }

        return found;
    }

    /// <summary>Removes each <paramref name="start"/> block up to its matching closing brace.</summary>
    private static string WithoutBlocks(string text, string start)
    {
        for (var at = text.IndexOf(start, StringComparison.Ordinal); at >= 0; at = text.IndexOf(start, at, StringComparison.Ordinal))
        {
            var open = text.IndexOf('{', at);
            if (open < 0)
            {
                break;
            }

            var depth = 0;
            var end = open;
            for (; end < text.Length; end++)
            {
                depth += text[end] switch { '{' => 1, '}' => -1, _ => 0 };
                if (depth == 0)
                {
                    break;
                }
            }

            text = text[..at] + text[Math.Min(end + 1, text.Length)..];
        }

        return text;
    }

    private static IEnumerable<string> Files(string folder, string pattern) =>
        Directory.EnumerateFiles(Path.Combine(Solution.Root, folder), pattern, SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Solution.Root, path).Replace('\\', '/'))
            .Where(path => !path.Contains("/bin/", StringComparison.Ordinal) && !path.Contains("/obj/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    // @* Razor comments *@ and <!-- HTML comments -->, which no one sees.
    [GeneratedRegex(@"@\*.*?\*@|<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comments();

    // Directive lines, such as @page "/account/sign-in" or @inject MemberAuth Auth.
    [GeneratedRegex(@"^\s*@(?:page|attribute|inject|using|inherits|layout|implements|rendermode|namespace|typeparam|preservewhitespace)\b.*$", RegexOptions.Multiline)]
    private static partial Regex Directives();

    // @if (...), @foreach (...), else if (...) and the like, with one level of nested parentheses.
    [GeneratedRegex(@"@?(?:\belse\s+)?\b(?:if|foreach|for|while|switch)\s*\((?:[^()]|\([^()]*\))*\)")]
    private static partial Regex ControlFlow();

    // A text node: whatever sits between one tag and the next.
    [GeneratedRegex(@">([^<>]*)<")]
    private static partial Regex TextNode();

    // @(expression), @Name.Member(args)[index], @@ (a literal @) and HTML entities such as &nbsp;.
    [GeneratedRegex(@"@\((?:[^()]|\([^()]*\))*\)|@[A-Za-z_][\w.]*(?:\((?:[^()]|\([^()]*\))*\))?(?:\[[^\]]*\])?|@@|&#?\w+;")]
    private static partial Regex Expressions();

    // What's left of Razor blocks once their conditions are gone: braces and else.
    [GeneratedRegex(@"[{}]|\belse\b")]
    private static partial Regex BlockSyntax();

    [GeneratedRegex(@"\p{L}")]
    private static partial Regex Letter();

    // Attributes a browser or screen reader shows or reads out.
    [GeneratedRegex(@"\b(aria-label|aria-description|aria-placeholder|placeholder|title|alt)\s*=\s*""([^""]*)""")]
    private static partial Regex VisibleAttribute();

    // ErrorMessage = "...": a validation message written into the code.
    [GeneratedRegex("""\bErrorMessage\s*=\s*\$?@?"[^"]""")]
    private static partial Regex LiteralErrorMessage();
}
