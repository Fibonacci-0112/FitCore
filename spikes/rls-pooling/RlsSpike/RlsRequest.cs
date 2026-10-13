using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace RlsSpike;

/// <summary>How the API applies the caller's identity to one unit of work.</summary>
public enum Strategy
{
    /// <summary>Explicit transaction; set role, set claims and the query are separate round trips. 4 round trips.</summary>
    RoundTrips,

    /// <summary>Explicit transaction; role, claims and the query go in one batch, then commit. 2 round trips.</summary>
    Batched,

    /// <summary>No explicit transaction; one batch runs as an implicit transaction. 1 round trip.</summary>
    SingleTrip,

    /// <summary>UNSAFE negative control: session-level SET ROLE and set_config(..., false), never reset.</summary>
    UnsafeSessionLevel,
}

public sealed record DiaryRow(Guid Id, Guid MemberId, Guid? Caller, string CurrentUser);

/// <summary>Per-request RLS identity with raw Npgsql, following architecture-options.md §B.3.</summary>
public static class RlsRequest
{
    public const string SetRoleLocal = "set local role authenticated";
    public const string SetClaimsLocal = "select set_config('request.jwt.claims', $1, true)";

    private const string ReadSql = """
        select id, member_id, auth.uid(), current_user
        from nutrition.diary_entries
        where member_id = $1 and diary_date >= current_date - 7
        """;

    private const string InsertSql = """
        insert into nutrition.diary_entries (member_id, diary_date, food_name, energy_kcal, cost_minor, currency)
        values ($1, current_date, 'Spike food', null, 250, 'USD')
        returning id, member_id, auth.uid(), current_user
        """;

    /// <summary>The claims a validated Supabase access token would carry. No health data.</summary>
    public static string Claims(Guid sub) =>
        JsonSerializer.Serialize(new { sub, role = "authenticated", aud = "authenticated" });

    public static Task<List<DiaryRow>> ReadAsync(NpgsqlDataSource ds, Guid caller, Guid member, Strategy s, CancellationToken ct = default) =>
        RunAsync(ds, caller, s, ReadSql, member, ct);

    public static Task<List<DiaryRow>> InsertAsync(NpgsqlDataSource ds, Guid caller, Guid member, Strategy s, CancellationToken ct = default) =>
        RunAsync(ds, caller, s, InsertSql, member, ct);

    private static async Task<List<DiaryRow>> RunAsync(NpgsqlDataSource ds, Guid caller, Strategy s, string sql, Guid member, CancellationToken ct)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        switch (s)
        {
            case Strategy.RoundTrips:
            {
                await using var tx = await conn.BeginTransactionAsync(ct);
                await using (var role = new NpgsqlCommand(SetRoleLocal, conn, tx))
                    await role.ExecuteNonQueryAsync(ct);
                await using (var claims = new NpgsqlCommand(SetClaimsLocal, conn, tx))
                {
                    claims.Parameters.Add(new() { Value = Claims(caller), NpgsqlDbType = NpgsqlDbType.Text });
                    await claims.ExecuteNonQueryAsync(ct);
                }
                await using var cmd = new NpgsqlCommand(sql, conn, tx);
                cmd.Parameters.Add(new() { Value = member });
                var rows = await ReadRowsAsync(cmd.ExecuteReaderAsync(ct), ct);
                await tx.CommitAsync(ct);
                return rows;
            }
            case Strategy.Batched:
            {
                await using var tx = await conn.BeginTransactionAsync(ct);
                await using var batch = IdentityBatch(conn, caller, sql, member);
                batch.Transaction = tx;
                var rows = await ReadLastResultAsync(batch, ct);
                await tx.CommitAsync(ct);
                return rows;
            }
            case Strategy.SingleTrip:
            {
                await using var batch = IdentityBatch(conn, caller, sql, member);
                return await ReadLastResultAsync(batch, ct);
            }
            case Strategy.UnsafeSessionLevel:
            {
                await using var batch = new NpgsqlBatch(conn)
                {
                    BatchCommands =
                    {
                        new("set role authenticated"),
                        new("select set_config('request.jwt.claims', $1, false)") { Parameters = { new() { Value = Claims(caller), NpgsqlDbType = NpgsqlDbType.Text } } },
                        new(sql) { Parameters = { new() { Value = member } } },
                    },
                };
                return await ReadLastResultAsync(batch, ct);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(s));
        }
    }

    private static NpgsqlBatch IdentityBatch(NpgsqlConnection conn, Guid caller, string sql, Guid member) => new(conn)
    {
        BatchCommands =
        {
            new(SetRoleLocal),
            new(SetClaimsLocal) { Parameters = { new() { Value = Claims(caller), NpgsqlDbType = NpgsqlDbType.Text } } },
            new(sql) { Parameters = { new() { Value = member } } },
        },
    };

    private static async Task<List<DiaryRow>> ReadLastResultAsync(NpgsqlBatch batch, CancellationToken ct)
    {
        // The last command's result set is the one with the row columns.
        // SET returns no result set; set_config returns one column.
        await using var reader = await batch.ExecuteReaderAsync(ct);
        while (reader.FieldCount != 4)
            if (!await reader.NextResultAsync(ct))
                throw new InvalidOperationException("The batch returned no rows result set.");
        return await ReadRowsAsync(Task.FromResult(reader), ct, dispose: false);
    }

    private static async Task<List<DiaryRow>> ReadRowsAsync(Task<NpgsqlDataReader> readerTask, CancellationToken ct, bool dispose = true)
    {
        var reader = await readerTask;
        try
        {
            var rows = new List<DiaryRow>();
            while (await reader.ReadAsync(ct))
                rows.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.GetString(3)));
            return rows;
        }
        finally
        {
            if (dispose) await reader.DisposeAsync();
        }
    }

    /// <summary>What a request with no identity sees: the session's role and claims.</summary>
    public static async Task<(string Role, string? Claims)> ProbeSessionAsync(NpgsqlDataSource ds, CancellationToken ct = default)
    {
        await using var cmd = ds.CreateCommand("select current_user, nullif(current_setting('request.jwt.claims', true), '')");
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }
}
