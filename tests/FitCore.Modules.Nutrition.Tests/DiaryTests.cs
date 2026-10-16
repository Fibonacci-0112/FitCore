using FitCore.Platform.Data;
using FitCore.Testing;
using Npgsql;

namespace FitCore.Modules.Nutrition.Tests;

/// <summary>
/// The nutrition schema through the real unit of work, as the API will use it (implementation plan §3.7, R1 PR 3).
/// The pgTAP suite covers every actor; these tests cover what only shows across separate units of work.
/// </summary>
[Collection(LocalDatabaseGroup.Name)]
public sealed class DiaryTests(LocalDatabase db)
{
    private const string FoodName = "Oats, rolled";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_retried_save_adds_one_entry_and_an_edit_bumps_its_version()
    {
        var member = await db.CreateMemberAsync();
        var entry = Guid.NewGuid();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var work = await db.Database.BeginAsync(member, Cancel);
            await InsertEntryAsync(work, entry, foodId: null, kcal: 150m);
            await work.CommitAsync(Cancel);
        }

        await using (var work = await db.Database.BeginAsync(member, Cancel))
        {
            await using var batch = work.CreateBatch();
            batch.BatchCommands.Add(new NpgsqlBatchCommand("update nutrition.diary_entries set amount = 60 where id = $1")
            {
                Parameters = { new NpgsqlParameter { Value = entry } },
            });
            Assert.Equal(1, await work.ExecuteNonQueryAsync(batch, Cancel));
            await work.CommitAsync(Cancel);
        }

        var (count, version) = await ReadEntriesAsync(member);
        Assert.Equal(1, count);
        Assert.Equal(2, version);
    }

    [Fact]
    public async Task A_member_reads_only_their_own_diary()
    {
        var owner = await db.CreateMemberAsync();
        var other = await db.CreateMemberAsync();

        await using (var work = await db.Database.BeginAsync(owner, Cancel))
        {
            await InsertEntryAsync(work, Guid.NewGuid(), foodId: null, kcal: 150m);
            await work.CommitAsync(Cancel);
        }

        Assert.Equal(1, (await ReadEntriesAsync(owner)).Count);
        Assert.Equal(0, (await ReadEntriesAsync(other)).Count);
    }

    [Fact]
    public async Task Revoking_a_grant_takes_effect_on_the_next_unit_of_work()
    {
        // NFR-PRV-4: the grant check reads the live table, so nothing the grantee's last request saw carries over.
        var owner = await db.CreateMemberAsync();
        var coach = await db.CreateMemberAsync();
        await using (var work = await db.Database.BeginAsync(owner, Cancel))
        {
            await InsertEntryAsync(work, Guid.NewGuid(), foodId: null, kcal: 150m);
            await work.CommitAsync(Cancel);
        }

        await db.ExecuteAsync(
            """
            insert into care.relationships (member_id, professional_id, role, scopes, status, granted_at)
            values ($1, $2, 'coach', '{nutrition}', 'active', now())
            """,
            owner.Subject,
            coach.Subject);
        Assert.Equal(1, (await ReadEntriesAsync(coach)).Count);

        await db.ExecuteAsync(
            "update care.relationships set status = 'revoked', revoked_at = now() where member_id = $1 and professional_id = $2",
            owner.Subject,
            coach.Subject);
        Assert.Equal(0, (await ReadEntriesAsync(coach)).Count);
    }

    [Fact]
    public async Task A_logged_entry_keeps_its_snapshot_when_the_food_changes()
    {
        // BRD §6.1 and NFR-REL-4: editing a food never rewrites history.
        var member = await db.CreateMemberAsync();
        var food = Guid.NewGuid();
        await db.ExecuteAsync(
            """
            insert into catalog.foods (id, owner_id, source, name, nutrient_basis, energy_kcal, protein_g, carbs_g, fat_g)
            values ($1, $2, 'custom', $3, 'per_100g', 379, 13.2, 67.7, 6.5)
            """,
            food,
            member.Subject,
            FoodName);

        await using (var work = await db.Database.BeginAsync(member, Cancel))
        {
            await InsertEntryAsync(work, Guid.NewGuid(), food, kcal: 189.5m);
            await work.CommitAsync(Cancel);
        }

        await using (var work = await db.Database.BeginAsync(member, Cancel))
        {
            await using var batch = work.CreateBatch();
            batch.BatchCommands.Add(new NpgsqlBatchCommand("update catalog.foods set name = 'Renamed', energy_kcal = 999 where id = $1")
            {
                Parameters = { new NpgsqlParameter { Value = food } },
            });
            Assert.Equal(1, await work.ExecuteNonQueryAsync(batch, Cancel));
            await work.CommitAsync(Cancel);
        }

        var snapshot = await db.Database.ReadAsync(
            member,
            batch => batch.BatchCommands.Add(new NpgsqlBatchCommand("select food_name, energy_kcal from nutrition.diary_entries")),
            async (reader, ct) =>
            {
                Assert.True(await reader.ReadAsync(ct));
                return (reader.GetString(0), reader.GetDecimal(1));
            },
            Cancel);

        Assert.Equal((FoodName, 189.5m), snapshot);
    }

    private static async Task InsertEntryAsync(UnitOfWork work, Guid id, Guid? foodId, decimal kcal)
    {
        await using var batch = work.CreateBatch();
        batch.BatchCommands.Add(new NpgsqlBatchCommand(
            """
            insert into nutrition.diary_entries (id, diary_date, meal, food_id, amount, unit, food_name, grams, energy_kcal, cost_status)
            values ($1, date '2026-10-15', 'breakfast', $2, 50, 'g', $3, 50, $4, 'no_price')
            on conflict (id) do nothing
            """)
        {
            Parameters =
            {
                new NpgsqlParameter { Value = id },
                new NpgsqlParameter { Value = (object?)foodId ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid },
                new NpgsqlParameter { Value = FoodName },
                new NpgsqlParameter { Value = kcal },
            },
        });
        await work.ExecuteNonQueryAsync(batch, Cancel);
    }

    private Task<(int Count, int Version)> ReadEntriesAsync(DbIdentity caller) =>
        db.Database.ReadAsync(
            caller,
            batch => batch.BatchCommands.Add(new NpgsqlBatchCommand(
                "select count(*)::int, coalesce(max(version), 0) from nutrition.diary_entries")),
            async (reader, ct) =>
            {
                await reader.ReadAsync(ct);
                return (reader.GetInt32(0), reader.GetInt32(1));
            },
            Cancel);
}

[CollectionDefinition(Name)]
public sealed class LocalDatabaseGroup : ICollectionFixture<LocalDatabase>
{
    public const string Name = "Local database";
}
