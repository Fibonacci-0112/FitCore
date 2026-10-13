using System.Text.Json.Nodes;
using Npgsql;

namespace AuthSpike;

/// <summary>Database access as the API's own login role, with the caller's validated claims (rules from the RLS spike).</summary>
public static class Db
{
    private static readonly NpgsqlDataSource Api =
        NpgsqlDataSource.Create("Host=127.0.0.1;Port=54322;Username=fitcore_api;Password=local-spike-password;Database=postgres");

    private static readonly NpgsqlDataSource Admin =
        NpgsqlDataSource.Create("Host=127.0.0.1;Port=54322;Username=postgres;Password=postgres;Database=postgres");

    /// <summary>Diary rows the caller can see for a member: one round trip, identity first.</summary>
    public static async Task<int> CountDiaryAsync(JsonObject claims, Guid member)
    {
        await using var conn = await Api.OpenConnectionAsync();
        await using var batch = new NpgsqlBatch(conn)
        {
            BatchCommands =
            {
                new("set local role authenticated"),
                new("select set_config('request.jwt.claims', $1, true)") { Parameters = { new() { Value = claims.ToJsonString() } } },
                new("select count(*) from nutrition.diary_entries where member_id = $1") { Parameters = { new() { Value = member } } },
            },
        };
        await using var reader = await batch.ExecuteReaderAsync();
        while (reader.FieldCount != 1 || reader.GetName(0) != "count") await reader.NextResultAsync();
        await reader.ReadAsync();
        return (int)reader.GetInt64(0);
    }

    public static async Task SeedAsync(Guid member, Guid coach, Guid clinician)
    {
        await using var conn = await Admin.OpenConnectionAsync();
        await using var batch = new NpgsqlBatch(conn)
        {
            BatchCommands =
            {
                new("insert into nutrition.diary_entries (member_id, food_name) select $1, 'Food ' || g from generate_series(1, 5) g")
                    { Parameters = { new() { Value = member } } },
                new("insert into care.relationships (member_id, professional_id, role, scopes, status) values ($1, $2, 'coach', '{nutrition}', 'active'), ($1, $3, 'clinician', '{nutrition}', 'active')")
                    { Parameters = { new() { Value = member }, new() { Value = coach }, new() { Value = clinician } } },
            },
        };
        await batch.ExecuteNonQueryAsync();
    }
}
