using Npgsql;

namespace FitCore.Platform.Data;

/// <summary>
/// One transaction run as one caller (implementation plan §3.2). The first batch carries <c>SET LOCAL ROLE
/// authenticated</c> and the caller's claims ahead of its own commands, so the identity costs no extra round trip
/// and ends with the transaction. Disposing without <see cref="CommitAsync"/> rolls back.
/// </summary>
/// <remarks>
/// Statements run only through the batches this unit creates. The connection is never handed out, because a
/// statement sent outside the transaction would run as the login role, which can read nothing.
/// </remarks>
public sealed class UnitOfWork : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private readonly DbIdentity _identity;
    private bool _identityApplied;
    private bool _completed;

    private UnitOfWork(NpgsqlConnection connection, NpgsqlTransaction transaction, DbIdentity identity)
    {
        _connection = connection;
        _transaction = transaction;
        _identity = identity;
    }

    internal static async Task<UnitOfWork> BeginAsync(NpgsqlDataSource dataSource, DbIdentity identity, CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            return new UnitOfWork(connection, transaction, identity);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>A batch in this unit's transaction. Add its commands, then run it once with this unit.</summary>
    public NpgsqlBatch CreateBatch()
    {
        ThrowIfCompleted();
        return new NpgsqlBatch(_connection, _transaction);
    }

    /// <summary>Runs a batch and returns a reader at the first result set of the batch's own commands.</summary>
    public async Task<NpgsqlDataReader> ExecuteReaderAsync(NpgsqlBatch batch, CancellationToken cancellationToken = default)
    {
        var prepended = Prepare(batch);
        var reader = await batch.ExecuteReaderAsync(cancellationToken);
        return prepended ? await Identity.SkipAsync(reader, cancellationToken) : reader;
    }

    /// <summary>Runs a batch and returns the number of rows its own commands inserted, updated or deleted.</summary>
    public async Task<int> ExecuteNonQueryAsync(NpgsqlBatch batch, CancellationToken cancellationToken = default)
    {
        var prepended = Prepare(batch);
        await batch.ExecuteNonQueryAsync(cancellationToken);
        return Identity.RecordsAffected(batch, prepended);
    }

    /// <summary>Commits the unit. Nothing more can run in it.</summary>
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfCompleted();
        _completed = true;
        await _transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Rolls back unless the unit committed, and returns the connection to the pool.</summary>
    public async ValueTask DisposeAsync()
    {
        _completed = true;
        try
        {
            // Npgsql rolls back a transaction that is disposed before it commits.
            await _transaction.DisposeAsync();
        }
        finally
        {
            await _connection.DisposeAsync();
        }
    }

    private bool Prepare(NpgsqlBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ThrowIfCompleted();

        if (batch.Connection != _connection || batch.Transaction != _transaction)
        {
            throw new InvalidOperationException("Run only batches this unit of work created.");
        }

        if (batch.BatchCommands.Count == 0)
        {
            throw new InvalidOperationException("The batch has no commands.");
        }

        if (_identityApplied)
        {
            return false;
        }

        Identity.Prepend(batch, _identity);
        _identityApplied = true;
        return true;
    }

    private void ThrowIfCompleted()
    {
        if (_completed)
        {
            throw new InvalidOperationException("The unit of work has already committed or rolled back.");
        }
    }
}
