using FitCore.Testing;
using Npgsql;

namespace FitCore.Platform.Tests;

/// <summary>
/// The shared local database fixture, plus a scratch schema with one RLS table, so the unit of work is tested apart
/// from any module's tables. The table has RLS on and grants nothing to anon, so it passes the pgTAP coverage test
/// even if a run leaves it behind.
/// </summary>
public sealed class PlatformDatabase : IAsyncLifetime
{
    public const string Schema = "platform_tests";

    private readonly LocalDatabase _local = new();

    public string ApiConnectionString => _local.ApiConnectionString;

    public async ValueTask InitializeAsync()
    {
        await _local.InitializeAsync();
        await _local.ExecuteAsync($"""
            drop schema if exists {Schema} cascade;
            create schema {Schema};
            create table {Schema}.notes (
              id uuid primary key default gen_random_uuid(),
              owner_id uuid not null default auth.uid(),
              body text not null
            );
            alter table {Schema}.notes enable row level security;
            create policy notes_owner_select on {Schema}.notes for select to authenticated
              using (owner_id = (select auth.uid()));
            create policy notes_owner_insert on {Schema}.notes for insert to authenticated
              with check (owner_id = (select auth.uid()));
            grant usage on schema {Schema} to authenticated;
            grant select, insert on {Schema}.notes to authenticated;
            """);
    }

    public async ValueTask DisposeAsync()
    {
        await _local.ExecuteAsync($"drop schema if exists {Schema} cascade");
        await _local.DisposeAsync();
    }

    /// <summary>
    /// A data source for fitcore_api. A small pool with no reset on close shares connections between units of work as
    /// much as possible and keeps whatever session state they leave, so a leaked identity would show.
    /// </summary>
    public NpgsqlDataSource ApiDataSource(int? maxPoolSize = null, bool noResetOnClose = false)
    {
        var builder = new NpgsqlDataSourceBuilder(ApiConnectionString);
        builder.ConnectionStringBuilder.NoResetOnClose = noResetOnClose;
        if (maxPoolSize is { } size)
        {
            builder.ConnectionStringBuilder.MaxPoolSize = size;
        }

        return builder.Build();
    }

    /// <summary>
    /// The database over <paramref name="dataSource"/>. <c>AddFitCoreDatabase</c> refuses the unsafe settings these
    /// tests need to expose a leak, so they build it directly.
    /// </summary>
    public static FitCore.Platform.Data.FitCoreDatabase Database(NpgsqlDataSource dataSource) => new(dataSource);
}

[CollectionDefinition(Name)]
public sealed class PlatformDatabaseGroup : ICollectionFixture<PlatformDatabase>
{
    public const string Name = "Local database";
}
