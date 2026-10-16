using FitCore.Platform.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace FitCore.Testing;

/// <summary>
/// The local Supabase database, with migrations and seed applied: `npx supabase start` or `npx supabase db start`.
/// CI starts it the same way. Override the connection strings with FITCORE_TEST_ADMIN_DB and FITCORE_TEST_API_DB.
/// </summary>
/// <remarks>
/// Use it as a collection fixture. <see cref="Database"/> is built the way the hosts build it, with
/// <c>AddFitCoreDatabase</c>, and connects as <c>fitcore_api</c>. Fixtures are written as <c>postgres</c> through
/// <see cref="ExecuteAsync"/>. Members created with <see cref="CreateMemberAsync"/> are deleted on disposal, and their
/// rows go with them; the audit entries their fixtures caused stay, because the audit log is append-only.
/// </remarks>
public sealed class LocalDatabase : IAsyncLifetime
{
    private const string LocalAdmin = "Host=127.0.0.1;Port=54322;Database=postgres;Username=postgres;Password=postgres";

    // The local-only password from supabase/seed.sql.
    private const string LocalApi = "Host=127.0.0.1;Port=54322;Database=postgres;Username=fitcore_api;Password=fitcore-local-only";

    private readonly List<Guid> _members = [];
    private ServiceProvider? _services;

    public string AdminConnectionString { get; } = Environment.GetEnvironmentVariable("FITCORE_TEST_ADMIN_DB") ?? LocalAdmin;

    public string ApiConnectionString { get; } = Environment.GetEnvironmentVariable("FITCORE_TEST_API_DB") ?? LocalApi;

    /// <summary>The database as the API sees it.</summary>
    public FitCoreDatabase Database =>
        _services?.GetRequiredService<FitCoreDatabase>() ?? throw new InvalidOperationException("The fixture isn't initialized.");

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

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{DatabaseServiceExtensions.ConnectionStringName}"] = ApiConnectionString,
            })
            .Build();
        _services = new ServiceCollection().AddFitCoreDatabase(configuration).BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        if (_members.Count > 0)
        {
            // Deleting the user cascades to every row that references it.
            await ExecuteAsync("delete from auth.users where id = any ($1)", _members.ToArray());
        }

        if (_services is not null)
        {
            await _services.DisposeAsync();
        }
    }

    /// <summary>Runs SQL as <c>postgres</c>, outside row-level security, to write or check fixtures.</summary>
    public async Task<int> ExecuteAsync(string sql, params object[] parameters)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = parameter });
        }

        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>Runs a query as <c>postgres</c>, outside row-level security, and returns the first column of the first row.</summary>
    public async Task<T> ScalarAsync<T>(string sql, params object[] parameters)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = parameter });
        }

        return (T)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Adds a user to <c>auth.users</c> and returns the identity a unit of work runs as. The user signed up with
    /// Google, so it starts without a profile: an email sign-up needs a birth date and gets one.
    /// </summary>
    public async Task<DbIdentity> CreateMemberAsync(string assuranceLevel = "aal1")
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            """
            insert into auth.users (id, aud, role, email, raw_app_meta_data)
            values ($1, 'authenticated', 'authenticated', $2, '{"provider": "google", "providers": ["google"]}')
            """,
            id,
            $"{id:N}@members.test");
        _members.Add(id);
        return new DbIdentity(id, assuranceLevel);
    }
}
