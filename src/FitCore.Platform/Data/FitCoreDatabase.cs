using Npgsql;

namespace FitCore.Platform.Data;

/// <summary>
/// The only way FitCore code runs SQL as a caller (implementation plan §3.2, RLS spike §4). The API and the worker
/// log in as <c>fitcore_api</c>, which has no table grants, and every statement runs inside a transaction that first
/// switches to <c>authenticated</c> with the caller's claims. Work without an identity fails with 42501.
/// </summary>
public sealed class FitCoreDatabase
{
    private readonly NpgsqlDataSource _dataSource;

    internal FitCoreDatabase(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    /// <summary>Opens a transaction that runs as <paramref name="identity"/>. Use it for writes and for reads of several statements.</summary>
    public Task<UnitOfWork> BeginAsync(DbIdentity identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return UnitOfWork.BeginAsync(_dataSource, identity, cancellationToken);
    }

    /// <summary>
    /// Runs a read in one round trip (RLS spike §4 rule 2): the identity and the batch's commands go to Postgres
    /// together, and run as one implicit transaction.
    /// </summary>
    /// <param name="identity">The caller.</param>
    /// <param name="build">Adds the read's commands to the batch.</param>
    /// <param name="read">Reads the results, starting at the first result set of the batch's own commands.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<T> ReadAsync<T>(
        DbIdentity identity,
        Action<NpgsqlBatch> build,
        Func<NpgsqlDataReader, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(read);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        // Without error barriers Npgsql ends the batch with one Sync, so Postgres runs it as one implicit transaction
        // and SET LOCAL holds for every command in it. If that ever changed, the commands would run as the login role
        // and fail closed.
        await using var batch = new NpgsqlBatch(connection) { EnableErrorBarriers = false };
        build(batch);
        if (batch.BatchCommands.Count == 0)
        {
            throw new InvalidOperationException("The batch has no commands.");
        }

        Identity.Prepend(batch, identity);
        await using var reader = await Identity.SkipAsync(await batch.ExecuteReaderAsync(cancellationToken), cancellationToken);
        return await read(reader, cancellationToken);
    }
}
