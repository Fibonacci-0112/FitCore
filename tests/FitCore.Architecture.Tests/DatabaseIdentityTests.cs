using System.Text.RegularExpressions;

namespace FitCore.Architecture.Tests;

/// <summary>
/// No code path sets the database role or claims for the session (implementation plan PR 2, RLS spike §4 rule 1).
/// Session-level state outlives the request and, with a transaction-mode pooler, reaches other callers.
/// </summary>
public sealed partial class DatabaseIdentityTests
{
    private static readonly string[] SourcePaths =
    [
        .. Files("src", "*.cs"),
        .. Files("supabase", "*.sql"),
        .. Files("supabase", "*.psql"),
    ];

    public static TheoryData<string> Sources => [.. SourcePaths];

    [Fact]
    public void The_scan_finds_the_sources()
    {
        Assert.Contains(SourcePaths, path => path.EndsWith("Identity.cs", StringComparison.Ordinal));
        Assert.Contains(SourcePaths, path => path.EndsWith("_database_foundation.sql", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void Source_sets_role_and_claims_only_for_the_transaction(string path)
    {
        // Comments may name the statements they rule out, so only code is scanned.
        var comment = path.EndsWith(".cs", StringComparison.Ordinal) ? "//" : "--";
        var code = File.ReadLines(Path.Combine(Solution.Root, path))
            .Select(line => line.IndexOf(comment, StringComparison.Ordinal) is var at and >= 0 ? line[..at] : line);

        Assert.Empty(SessionLevelSet().Matches(string.Join('\n', code)).Select(m => m.Value));
    }

    [Theory]
    [InlineData("SET ROLE authenticated")]
    [InlineData("set session role authenticated")]
    [InlineData("SET SESSION AUTHORIZATION fitcore_api")]
    [InlineData("set request.jwt.claims = '{}'")]
    [InlineData("set session \"request.jwt.claims\" to '{}'")]
    [InlineData("select set_config('request.jwt.claims', $1, false)")]
    [InlineData("SELECT set_config('role', 'authenticated', FALSE)")]
    public void The_scan_catches_session_level_statements(string sql)
    {
        Assert.Matches(SessionLevelSet(), sql);
    }

    [Theory]
    [InlineData("SET LOCAL ROLE authenticated")]
    [InlineData("SELECT set_config('request.jwt.claims', $1, true)")]
    [InlineData("create function f() returns void language sql set search_path = '' as $$ select 1 $$")]
    public void The_scan_allows_transaction_level_statements(string sql)
    {
        Assert.DoesNotMatch(SessionLevelSet(), sql);
    }

    private static IEnumerable<string> Files(string folder, string pattern) =>
        Directory.EnumerateFiles(Path.Combine(Solution.Root, folder), pattern, SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Solution.Root, path).Replace('\\', '/'))
            .Where(path => !path.Contains("/bin/", StringComparison.Ordinal) && !path.Contains("/obj/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    // SET [SESSION] ROLE, SET SESSION AUTHORIZATION, SET [SESSION] request.jwt.*, and set_config(..., false).
    [GeneratedRegex(
        """\bset\s+(?:session\s+)?(?:role\b|authorization\b|"?request\.)|\bset_config\s*\([^;]*?,\s*false\s*\)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex SessionLevelSet();
}
