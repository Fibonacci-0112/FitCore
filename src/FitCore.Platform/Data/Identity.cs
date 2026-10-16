using Npgsql;
using NpgsqlTypes;

namespace FitCore.Platform.Data;

/// <summary>
/// The two statements that apply a caller's identity, both scoped to the current transaction (RLS spike §4 rule 1).
/// Nothing else in FitCore sets the role or the claims.
/// </summary>
internal static class Identity
{
    private const string SetRole = "SET LOCAL ROLE authenticated";
    private const string SetClaims = "SELECT set_config('request.jwt.claims', $1, true)";

    /// <summary>Puts the identity ahead of the batch's own commands, so it goes in the same round trip (rule 2).</summary>
    public static void Prepend(NpgsqlBatch batch, DbIdentity identity)
    {
        batch.BatchCommands.Insert(0, new NpgsqlBatchCommand(SetRole));
        batch.BatchCommands.Insert(1, new NpgsqlBatchCommand(SetClaims)
        {
            Parameters = { new NpgsqlParameter { Value = identity.ToClaimsJson(), NpgsqlDbType = NpgsqlDbType.Text } },
        });
    }

    /// <summary>
    /// Moves a reader past the identity's one result set, the row <c>set_config</c> returns, to the first result set
    /// of the batch's own commands. <c>SET</c> returns none.
    /// </summary>
    public static async Task<NpgsqlDataReader> SkipAsync(NpgsqlDataReader reader, CancellationToken cancellationToken)
    {
        try
        {
            await reader.NextResultAsync(cancellationToken);
            return reader;
        }
        catch
        {
            await reader.DisposeAsync();
            throw;
        }
    }

    /// <summary>The rows the batch's own commands affected, leaving out the identity's two.</summary>
    public static int RecordsAffected(NpgsqlBatch batch, bool prepended)
    {
        var total = 0L;
        for (var i = prepended ? 2 : 0; i < batch.BatchCommands.Count; i++)
        {
            total += batch.BatchCommands[i].RecordsAffected;
        }

        return checked((int)total);
    }
}
