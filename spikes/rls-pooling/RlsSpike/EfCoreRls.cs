using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using NpgsqlTypes;

namespace RlsSpike;

public sealed class DiaryEntry
{
    public Guid Id { get; set; }
    public Guid MemberId { get; set; }
    public DateOnly DiaryDate { get; set; }
    public string FoodName { get; set; } = "";
    public decimal? EnergyKcal { get; set; }
    public long? CostMinor { get; set; }
    public string? Currency { get; set; }
}

/// <summary>The caller for the current unit of work. In the API this comes from the validated JWT.</summary>
public static class CurrentCaller
{
    private static readonly AsyncLocal<Guid?> Value = new();
    public static Guid? Id { get => Value.Value; set => Value.Value = value; }
}

/// <summary>
/// Applies the caller's identity whenever EF Core starts a transaction. Work
/// done outside a transaction runs as the authenticator role, which has no
/// grants on module tables, so a missing transaction fails closed.
/// </summary>
public sealed class RlsTransactionInterceptor : DbTransactionInterceptor
{
    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken ct = default)
    {
        var caller = CurrentCaller.Id ?? throw new InvalidOperationException("No caller for this unit of work.");
        await using var batch = new NpgsqlBatch((NpgsqlConnection)connection, (NpgsqlTransaction)result)
        {
            BatchCommands =
            {
                new(RlsRequest.SetRoleLocal),
                new(RlsRequest.SetClaimsLocal) { Parameters = { new() { Value = RlsRequest.Claims(caller), NpgsqlDbType = NpgsqlDbType.Text } } },
            },
        };
        await batch.ExecuteNonQueryAsync(ct);
        return result;
    }

    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result) =>
        throw new NotSupportedException("Use the async API.");
}

public sealed class SpikeDb(DbContextOptions<SpikeDb> options) : DbContext(options)
{
    public DbSet<DiaryEntry> DiaryEntries => Set<DiaryEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        var e = b.Entity<DiaryEntry>();
        e.ToTable("diary_entries", "nutrition");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.MemberId).HasColumnName("member_id");
        e.Property(x => x.DiaryDate).HasColumnName("diary_date");
        e.Property(x => x.FoodName).HasColumnName("food_name");
        e.Property(x => x.EnergyKcal).HasColumnName("energy_kcal");
        e.Property(x => x.CostMinor).HasColumnName("cost_minor");
        e.Property(x => x.Currency).HasColumnName("currency");
    }

    public static DbContextOptions<SpikeDb> Options(NpgsqlDataSource ds) =>
        new DbContextOptionsBuilder<SpikeDb>()
            .UseNpgsql(ds)
            .AddInterceptors(new RlsTransactionInterceptor())
            .Options;
}

public static class EfCoreRls
{
    /// <summary>A read as one unit of work: begin (with identity), query, commit. 3 round trips.</summary>
    public static async Task<List<DiaryEntry>> ReadAsync(DbContextOptions<SpikeDb> options, Guid caller, Guid member, CancellationToken ct = default)
    {
        CurrentCaller.Id = caller;
        await using var db = new SpikeDb(options);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7);
        var rows = await db.DiaryEntries.AsNoTracking()
            .Where(x => x.MemberId == member && x.DiaryDate >= since)
            .ToListAsync(ct);
        await tx.CommitAsync(ct);
        return rows;
    }

    /// <summary>A write as one unit of work: begin (with identity), insert, commit. 3 round trips.</summary>
    public static async Task<DiaryEntry> InsertAsync(DbContextOptions<SpikeDb> options, Guid caller, Guid member, CancellationToken ct = default)
    {
        CurrentCaller.Id = caller;
        await using var db = new SpikeDb(options);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var entry = new DiaryEntry
        {
            Id = Guid.CreateVersion7(),
            MemberId = member,
            DiaryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            FoodName = "Spike food (EF)",
            CostMinor = 250,
            Currency = "USD",
        };
        db.DiaryEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return entry;
    }

    /// <summary>A query with no transaction: the identity is never applied.</summary>
    public static async Task<int> CountWithoutTransactionAsync(DbContextOptions<SpikeDb> options, Guid member, CancellationToken ct = default)
    {
        await using var db = new SpikeDb(options);
        return await db.DiaryEntries.CountAsync(x => x.MemberId == member, ct);
    }
}
