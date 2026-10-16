using Npgsql;

namespace FitCore.Platform.Tests;

/// <summary>
/// The local Supabase database, with migrations and seed applied: `npx supabase start` or `npx supabase db start`.
/// CI starts it the same way. Override the connection strings with FITCORE_TEST_ADMIN_DB and FITCORE_TEST_API_DB.
/// </summary>
/// <remarks>
/// The fixture adds a scratch schema with one RLS table, because the R1 tables arrive in PR 3. The table has RLS on
/// and grants nothing to anon, so it passes the pgTAP coverage test even if a run leaves it behind.
/// </remarks>
public sealed class LocalDatabase : IAsyncLifetime
{
    public const string Schema = "platform_tests";

    private const string LocalAdmin = "Host=127.0.0.1;Port=54322;Database=postgres;Username=postgres;Password=postgres";

    // The local-only password from supabase/seed.sql.
    private const string LocalApi = "Host=127.0.0.1;Port=54322;Database=postgres;Username=fitcore_api;Password=fitcore-local-only";

    public string AdminConnectionString { get; } = Environment.GetEnvironmentVariable("FITCORE_TEST_ADMIN_DB") ?? LocalAdmin;

    public string ApiConnectionString { get; } = Environment.GetEnvironmentVariable("FITCORE_TEST_API_DB") ?? LocalApi;

    public async ValueTask InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        try
        {
            await connection.OpenAsync();
        }
        catch (NpgsqlException e)
        {
            throw new InvalidOperationException(
                "The local Supabase database isn't running. Start it with `npx supabase db start` (supabase/README.md).", e);
        }

        await using var command = new NpgsqlCommand($"""
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
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"drop schema if exists {Schema} cascade", connection);
        await command.ExecuteNonQueryAsync();
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

    /// <summary>The database as the API sees it, built the way <c>AddFitCoreDatabase</c> builds it.</summary>
    public static FitCore.Platform.Data.FitCoreDatabase Database(NpgsqlDataSource dataSource) => new(dataSource);
}

[CollectionDefinition(Name)]
public sealed class LocalDatabaseGroup : ICollectionFixture<LocalDatabase>
{
    public const string Name = "Local database";
}
