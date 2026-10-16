using FitCore.Platform.Data;
using Npgsql;

namespace FitCore.Platform.Tests;

/// <summary>Per-request identity through the unit of work (implementation plan PR 2, RLS spike §4).</summary>
[Collection(PlatformDatabaseGroup.Name)]
public sealed class UnitOfWorkTests(PlatformDatabase db)
{
    private const string Notes = PlatformDatabase.Schema + ".notes";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_query_without_an_identity_fails_with_42501()
    {
        await using var dataSource = db.ApiDataSource();
        await using var command = dataSource.CreateCommand($"select count(*) from {Notes}");

        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteScalarAsync(Cancel));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Fact]
    public async Task The_login_role_can_switch_only_to_authenticated()
    {
        await using var dataSource = db.ApiDataSource();
        await using var connection = await dataSource.OpenConnectionAsync(Cancel);
        await using var transaction = await connection.BeginTransactionAsync(Cancel);
        await using var command = new NpgsqlCommand("SET LOCAL ROLE service_role", connection, transaction);

        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Cancel));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Fact]
    public async Task A_unit_of_work_runs_as_the_caller()
    {
        var caller = new DbIdentity(Guid.NewGuid(), "aal2");
        await using var dataSource = db.ApiDataSource();

        await using var work = await PlatformDatabase.Database(dataSource).BeginAsync(caller, Cancel);
        var (role, uid, aal) = await SessionAsync(work);

        Assert.Equal("authenticated", role);
        Assert.Equal(caller.Subject, uid);
        Assert.Equal("aal2", aal);
    }

    [Fact]
    public async Task The_identity_holds_for_every_batch_in_the_unit()
    {
        var caller = new DbIdentity(Guid.NewGuid());
        await using var dataSource = db.ApiDataSource();

        await using var work = await PlatformDatabase.Database(dataSource).BeginAsync(caller, Cancel);
        await InsertNoteAsync(work, "first");
        await InsertNoteAsync(work, "second");

        Assert.Equal(2, await CountNotesAsync(work));
        Assert.Equal(caller.Subject, (await SessionAsync(work)).Uid);
    }

    [Fact]
    public async Task Row_level_security_applies_to_the_caller()
    {
        var owner = new DbIdentity(Guid.NewGuid());
        var other = new DbIdentity(Guid.NewGuid());
        await using var dataSource = db.ApiDataSource();
        var database = PlatformDatabase.Database(dataSource);

        await using (var work = await database.BeginAsync(owner, Cancel))
        {
            Assert.Equal(1, await InsertNoteAsync(work, "mine"));
            await work.CommitAsync(Cancel);
        }

        await using (var work = await database.BeginAsync(owner, Cancel))
        {
            Assert.Equal(1, await CountNotesAsync(work));
        }

        await using (var work = await database.BeginAsync(other, Cancel))
        {
            Assert.Equal(0, await CountNotesAsync(work));
        }
    }

    [Fact]
    public async Task A_write_for_another_member_fails_with_42501()
    {
        var caller = new DbIdentity(Guid.NewGuid());
        await using var dataSource = db.ApiDataSource();

        await using var work = await PlatformDatabase.Database(dataSource).BeginAsync(caller, Cancel);
        await using var batch = work.CreateBatch();
        batch.BatchCommands.Add(new NpgsqlBatchCommand($"insert into {Notes} (owner_id, body) values ($1, 'theirs')")
        {
            Parameters = { new NpgsqlParameter { Value = Guid.NewGuid() } },
        });

        var error = await Assert.ThrowsAsync<PostgresException>(() => work.ExecuteNonQueryAsync(batch, Cancel));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Fact]
    public async Task The_affected_row_count_leaves_out_reads()
    {
        var caller = new DbIdentity(Guid.NewGuid());
        await using var dataSource = db.ApiDataSource();

        await using var work = await PlatformDatabase.Database(dataSource).BeginAsync(caller, Cancel);
        await using var batch = work.CreateBatch();
        batch.BatchCommands.Add(new NpgsqlBatchCommand($"select count(*) from {Notes}"));
        batch.BatchCommands.Add(new NpgsqlBatchCommand($"insert into {Notes} (body) values ('one')"));

        Assert.Equal(1, await work.ExecuteNonQueryAsync(batch, Cancel));
    }

    [Fact]
    public async Task Disposing_without_commit_rolls_back()
    {
        var caller = new DbIdentity(Guid.NewGuid());
        await using var dataSource = db.ApiDataSource();
        var database = PlatformDatabase.Database(dataSource);

        await using (var work = await database.BeginAsync(caller, Cancel))
        {
            await InsertNoteAsync(work, "not kept");
        }

        await using (var work = await database.BeginAsync(caller, Cancel))
        {
            Assert.Equal(0, await CountNotesAsync(work));
        }
    }

    [Fact]
    public async Task A_unit_runs_only_its_own_batches_and_nothing_after_commit()
    {
        var caller = new DbIdentity(Guid.NewGuid());
        await using var dataSource = db.ApiDataSource();
        var database = PlatformDatabase.Database(dataSource);

        await using var first = await database.BeginAsync(caller, Cancel);
        await using var second = await database.BeginAsync(caller, Cancel);
        await using var foreign = second.CreateBatch();
        foreign.BatchCommands.Add(new NpgsqlBatchCommand("select 1"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => first.ExecuteReaderAsync(foreign, Cancel));

        await first.CommitAsync(Cancel);
        Assert.Throws<InvalidOperationException>(first.CreateBatch);
    }

    [Fact]
    public async Task A_single_trip_read_runs_as_the_caller()
    {
        var caller = new DbIdentity(Guid.NewGuid());
        await using var dataSource = db.ApiDataSource();

        var (role, uid) = await PlatformDatabase.Database(dataSource).ReadAsync(
            caller,
            batch => batch.BatchCommands.Add(new NpgsqlBatchCommand("select current_user::text, auth.uid()")),
            async (reader, ct) =>
            {
                await reader.ReadAsync(ct);
                return (reader.GetString(0), reader.GetGuid(1));
            },
            Cancel);

        Assert.Equal("authenticated", role);
        Assert.Equal(caller.Subject, uid);
    }

    [Theory]
    [InlineData("commit")]
    [InlineData("rollback")]
    [InlineData("error")]
    [InlineData("single trip")]
    public async Task The_identity_ends_with_the_unit_of_work(string ending)
    {
        // One pooled connection, and no DISCARD ALL when it returns to the pool, so the probe sees exactly the session
        // state the unit left behind. Session-level SET would show up here (RLS spike §3.2).
        await using var dataSource = db.ApiDataSource(maxPoolSize: 1, noResetOnClose: true);
        var database = PlatformDatabase.Database(dataSource);
        var caller = new DbIdentity(Guid.NewGuid());

        await EndUnitAsync(database, caller, ending);

        await using var probe = dataSource.CreateCommand(
            "select current_user::text, coalesce(current_setting('request.jwt.claims', true), '')");
        await using var reader = await probe.ExecuteReaderAsync(Cancel);
        await reader.ReadAsync(Cancel);
        Assert.Equal("fitcore_api", reader.GetString(0));
        Assert.Equal("", reader.GetString(1));
    }

    [Fact]
    public async Task Concurrent_units_never_see_another_callers_identity()
    {
        // Fewer connections than callers, and no reset between them, so connections are shared as much as possible.
        await using var dataSource = db.ApiDataSource(maxPoolSize: 4, noResetOnClose: true);
        var database = PlatformDatabase.Database(dataSource);

        var seen = await Task.WhenAll(Enumerable.Range(0, 400).Select(async i =>
        {
            var caller = new DbIdentity(Guid.NewGuid());
            if (i % 2 == 0)
            {
                await using var work = await database.BeginAsync(caller, Cancel);
                return (caller.Subject, (await SessionAsync(work)).Uid);
            }

            var uid = await database.ReadAsync(
                caller,
                batch => batch.BatchCommands.Add(new NpgsqlBatchCommand("select auth.uid()")),
                async (reader, ct) =>
                {
                    await reader.ReadAsync(ct);
                    return reader.GetGuid(0);
                },
                Cancel);
            return (caller.Subject, uid);
        }));

        Assert.All(seen, s => Assert.Equal(s.Item1, s.Item2));
    }

    private static async Task EndUnitAsync(FitCoreDatabase database, DbIdentity caller, string ending)
    {
        switch (ending)
        {
            case "commit":
                {
                    await using var work = await database.BeginAsync(caller, Cancel);
                    await SessionAsync(work);
                    await work.CommitAsync(Cancel);
                    break;
                }

            case "rollback":
                {
                    await using var work = await database.BeginAsync(caller, Cancel);
                    await SessionAsync(work);
                    break;
                }

            case "error":
                {
                    await using var work = await database.BeginAsync(caller, Cancel);
                    await using var batch = work.CreateBatch();
                    batch.BatchCommands.Add(new NpgsqlBatchCommand("select 1 / 0"));
                    await Assert.ThrowsAsync<PostgresException>(() => work.ExecuteReaderAsync(batch, Cancel));
                    break;
                }

            default:
                await database.ReadAsync(
                    caller,
                    batch => batch.BatchCommands.Add(new NpgsqlBatchCommand("select auth.uid()")),
                    (_, _) => Task.FromResult(0),
                    Cancel);
                break;
        }
    }

    private static async Task<(string Role, Guid Uid, string Aal)> SessionAsync(UnitOfWork work)
    {
        await using var batch = work.CreateBatch();
        batch.BatchCommands.Add(new NpgsqlBatchCommand("select current_user::text, auth.uid(), auth.jwt() ->> 'aal'"));
        await using var reader = await work.ExecuteReaderAsync(batch, Cancel);
        await reader.ReadAsync(Cancel);
        return (reader.GetString(0), reader.GetGuid(1), reader.GetString(2));
    }

    private static async Task<int> InsertNoteAsync(UnitOfWork work, string body)
    {
        await using var batch = work.CreateBatch();
        batch.BatchCommands.Add(new NpgsqlBatchCommand($"insert into {Notes} (body) values ($1)")
        {
            Parameters = { new NpgsqlParameter { Value = body } },
        });
        return await work.ExecuteNonQueryAsync(batch, Cancel);
    }

    private static async Task<long> CountNotesAsync(UnitOfWork work)
    {
        await using var batch = work.CreateBatch();
        batch.BatchCommands.Add(new NpgsqlBatchCommand($"select count(*) from {Notes}"));
        await using var reader = await work.ExecuteReaderAsync(batch, Cancel);
        await reader.ReadAsync(Cancel);
        return reader.GetInt64(0);
    }
}
