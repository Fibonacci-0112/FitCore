using Npgsql;

namespace RlsSpike;

/// <summary>The three ways the API can reach Postgres, all as the authenticator login.</summary>
/// <param name="NoResetOnClose">
/// Npgsql's default (false) prepends DISCARD ALL to the next command when a
/// pooled connection is reused. Through Supavisor transaction mode that
/// desynchronizes the pooler and can hand an open transaction to another client,
/// so transaction mode must set it to true.
/// </param>
public sealed record Endpoint(string Name, string ConnectionString, int MaxPoolSize, bool NoResetOnClose = false)
{
    private const string Common = "Database=postgres;Password=spike-password;Timeout=15;Command Timeout=10";

    public static Endpoint Direct(int maxPool = 32) =>
        new("direct", $"Host=localhost;Port=54322;Username=authenticator;{Common}", maxPool);

    // Session mode pins one server connection per client connection, so the
    // client pool must not exceed the Supavisor pool size (10 here).
    public static Endpoint Session(int maxPool = 10) =>
        new("supavisor-session", $"Host=localhost;Port=54329;Username=authenticator.fitcore;{Common}", maxPool);

    public static Endpoint Transaction(int maxPool = 32) =>
        new("supavisor-transaction", $"Host=localhost;Port=54330;Username=authenticator.fitcore;{Common}", maxPool, NoResetOnClose: true);

    public static IReadOnlyList<Endpoint> All => [Direct(), Session(), Transaction()];

    public NpgsqlDataSource Build(bool autoPrepare = false, bool? noResetOnClose = null, int? commandTimeout = null)
    {
        var b = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            MaxPoolSize = MaxPoolSize,
            MaxAutoPrepare = autoPrepare ? 20 : 0,
            NoResetOnClose = noResetOnClose ?? NoResetOnClose,
        };
        if (commandTimeout is { } t) b.CommandTimeout = t;
        return NpgsqlDataSource.Create(b.ConnectionString);
    }

    /// <summary>Superuser connection used only by the harness to change grants.</summary>
    public static NpgsqlDataSource Admin() =>
        NpgsqlDataSource.Create("Host=localhost;Port=54322;Username=supabase_admin;Password=spike-password;Database=postgres");
}

public static class Ids
{
    public static Guid Member(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");
    public static Guid Professional(int n) => Guid.Parse($"00000000-0000-0000-0001-{n:D12}");
}
