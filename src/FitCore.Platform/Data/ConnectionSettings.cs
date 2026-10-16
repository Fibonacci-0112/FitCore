using Npgsql;

namespace FitCore.Platform.Data;

/// <summary>Rejects connection settings that the RLS spike showed to be unsafe (RLS spike §3.2 and §4 rule 5).</summary>
internal static class ConnectionSettings
{
    // Supavisor and the dedicated pooler run transaction mode on 6543; session mode and direct connections use 5432.
    // The port alone decides, so a pooler reached by IP address, private link or a custom DNS name is refused too.
    private const int TransactionModePort = 6543;

    public static string Check(string connectionString)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString);

        if (settings.Port == TransactionModePort)
        {
            throw new InvalidOperationException(
                "The database connection uses the Supabase pooler's transaction mode (port 6543). Use session mode (5432) or a direct connection (RLS spike §4 rule 5).");
        }

        if (settings.NoResetOnClose)
        {
            throw new InvalidOperationException(
                "No Reset On Close must stay off: Npgsql's DISCARD ALL is the second safety net against a leaked identity (RLS spike §4 rule 5).");
        }

        return connectionString;
    }
}
