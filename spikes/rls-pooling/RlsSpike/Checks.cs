using Npgsql;

namespace RlsSpike;

/// <summary>Correctness checks: RLS identity is right for every request and never outlives it.</summary>
public static class Checks
{
    public sealed record Result(string Endpoint, string Check, bool Pass, string Detail);

    private static readonly Guid Member1 = Ids.Member(1);
    private static readonly Guid Member2 = Ids.Member(2);
    private static readonly Guid CoachActive = Ids.Professional(1);
    private static readonly Guid CoachRevoked = Ids.Professional(2);
    private static readonly Guid CoachOtherScope = Ids.Professional(3);

    public static async Task<List<Result>> RunAsync(Endpoint ep)
    {
        var results = new List<Result>();
        void Add(string check, bool pass, string detail) => results.Add(new(ep.Name, check, pass, detail));

        await using var ds = ep.Build();
        await using var admin = Endpoint.Admin();
        var ef = SpikeDb.Options(ds);

        // Actor matrix for each safe strategy.
        foreach (var s in new[] { Strategy.RoundTrips, Strategy.Batched, Strategy.SingleTrip })
        {
            var own = await RlsRequest.ReadAsync(ds, Member1, Member1, s);
            Add($"{s}: owner reads own diary", own.Count > 0 && own.All(r => r.MemberId == Member1 && r.Caller == Member1 && r.CurrentUser == "authenticated"), $"{own.Count} rows");
            var other = await RlsRequest.ReadAsync(ds, Member2, Member1, s);
            Add($"{s}: other member sees nothing", other.Count == 0, $"{other.Count} rows");
            var grantee = await RlsRequest.ReadAsync(ds, CoachActive, Member1, s);
            Add($"{s}: grantee in scope reads", grantee.Count == own.Count, $"{grantee.Count} rows");
            var outOfScope = await RlsRequest.ReadAsync(ds, CoachOtherScope, Member1, s);
            Add($"{s}: grantee out of scope sees nothing", outOfScope.Count == 0, $"{outOfScope.Count} rows");
            var revoked = await RlsRequest.ReadAsync(ds, CoachRevoked, Member1, s);
            Add($"{s}: revoked grantee sees nothing", revoked.Count == 0, $"{revoked.Count} rows");
            var inserted = await RlsRequest.InsertAsync(ds, Member2, Member2, s);
            Add($"{s}: member writes own diary", inserted.Count == 1 && inserted[0].Caller == Member2, $"{inserted.Count} rows");
            var forged = await ExpectSqlStateAsync(() => RlsRequest.InsertAsync(ds, Member2, Member1, s));
            Add($"{s}: write to another member's diary rejected", forged == "42501", $"SQLSTATE {forged}");
        }

        var efOwn = await EfCoreRls.ReadAsync(ef, Member1, Member1);
        Add("EfCore: owner reads own diary", efOwn.Count > 0 && efOwn.All(r => r.MemberId == Member1), $"{efOwn.Count} rows");
        var efOther = await EfCoreRls.ReadAsync(ef, Member2, Member1);
        Add("EfCore: other member sees nothing", efOther.Count == 0, $"{efOther.Count} rows");
        var efGrantee = await EfCoreRls.ReadAsync(ef, CoachActive, Member1);
        Add("EfCore: grantee in scope reads", efGrantee.Count == efOwn.Count, $"{efGrantee.Count} rows");
        var efOutOfScope = await EfCoreRls.ReadAsync(ef, CoachOtherScope, Member1);
        Add("EfCore: grantee out of scope sees nothing", efOutOfScope.Count == 0, $"{efOutOfScope.Count} rows");
        var efRevoked = await EfCoreRls.ReadAsync(ef, CoachRevoked, Member1);
        Add("EfCore: revoked grantee sees nothing", efRevoked.Count == 0, $"{efRevoked.Count} rows");
        var efInsert = await EfCoreRls.InsertAsync(ef, Member2, Member2);
        Add("EfCore: member writes own diary", efInsert.MemberId == Member2, "1 row");
        var efForged = await ExpectSqlStateAsync(() => EfCoreRls.InsertAsync(ef, Member2, Member1));
        Add("EfCore: write to another member's diary rejected", efForged == "42501", $"SQLSTATE {efForged}");
        var efNoTx = await ExpectSqlStateAsync(() => EfCoreRls.CountWithoutTransactionAsync(ef, Member1));
        Add("EfCore: query outside a transaction fails closed", efNoTx == "42501", $"SQLSTATE {efNoTx}");

        // Revocation takes effect on the next request (NFR-PRV-4).
        var before = await RlsRequest.ReadAsync(ds, CoachActive, Member1, Strategy.SingleTrip);
        await SetCoachStatusAsync(admin, CoachActive, "revoked");
        var after = await RlsRequest.ReadAsync(ds, CoachActive, Member1, Strategy.SingleTrip);
        await SetCoachStatusAsync(admin, CoachActive, "active");
        Add("Revocation applies on the next request", before.Count > 0 && after.Count == 0, $"{before.Count} rows, then {after.Count}");

        // A request with no identity never inherits one.
        var probe = await RlsRequest.ProbeSessionAsync(ds);
        Add("Identity is cleared after each request", probe is ("authenticator", null), $"role {probe.Role}, claims {probe.Claims ?? "none"}");

        // Soak: concurrent requests by random callers, interleaved with no-identity probes.
        var soak = await SoakAsync(ds, ef, workers: 64, perWorker: 150);
        Add("Concurrent soak: no cross-request leak", soak.Leaks == 0 && soak.StaleProbes == 0 && soak.Errors == 0,
            $"{soak.Requests} requests, {soak.Probes} probes, {soak.Leaks} leaks, {soak.StaleProbes} stale probes, {soak.Errors} errors");

        return results;
    }

    /// <summary>Negative control: session-level SET ROLE / set_config leaks through a pool.</summary>
    public static async Task<Result> UnsafeControlAsync(Endpoint ep, bool noResetOnClose)
    {
        await using var ds = ep.Build(noResetOnClose: noResetOnClose, commandTimeout: 3);
        int stale = 0, probes = 0, errors = 0;
        // Each worker stops at its first error: a hung request costs the full timeout.
        await Parallel.ForAsync(0, 16, async (w, ct) =>
        {
            for (var i = 0; i < 25; i++)
            {
                try
                {
                    await RlsRequest.ReadAsync(ds, Ids.Member(1 + (w * 25 + i) % 2000), Ids.Member(1), Strategy.UnsafeSessionLevel, ct);
                    var p = await RlsRequest.ProbeSessionAsync(ds, ct);
                    Interlocked.Increment(ref probes);
                    if (p.Role != "authenticator" || p.Claims is not null) Interlocked.Increment(ref stale);
                }
                catch (Exception ex) when (ex is NpgsqlException or OperationCanceledException or NotSupportedException or InvalidOperationException)
                {
                    Interlocked.Increment(ref errors);
                    return;
                }
            }
        });
        await ResetAllAsync();
        return new(ep.Name, $"CONTROL unsafe session-level SET (No Reset On Close={noResetOnClose})", stale == 0 && errors == 0,
            $"{stale} of {probes} no-identity probes saw another caller's role or claims; {errors} errors");
    }

    /// <summary>
    /// Negative control: Npgsql's default reset-on-close through a pooler, with
    /// the safe per-request pattern. Counts hung requests, requests that found
    /// another client's open transaction, and no-identity probes that saw an identity.
    /// </summary>
    public static async Task<Result> ResetOnCloseControlAsync(Endpoint ep)
    {
        await using var ds = ep.Build(noResetOnClose: false, commandTimeout: 3);
        int requests = 0, timeouts = 0, foreignTx = 0, otherErrors = 0, stale = 0, probes = 0;
        await Parallel.ForAsync(0, 16, async (w, ct) =>
        {
            for (var i = 0; i < 40; i++)
            {
                var caller = Ids.Member(1 + (w * 40 + i) % 2000);
                try
                {
                    var rows = await RlsRequest.ReadAsync(ds, caller, caller, (Strategy)(i % 3), ct);
                    Interlocked.Increment(ref requests);
                    var p = await RlsRequest.ProbeSessionAsync(ds, ct);
                    Interlocked.Increment(ref probes);
                    if (p.Role != "authenticator" || p.Claims is not null) Interlocked.Increment(ref stale);
                }
                catch (Exception ex) when (ex is NpgsqlException or OperationCanceledException or NotSupportedException or InvalidOperationException)
                {
                    if (ex is NotSupportedException) Interlocked.Increment(ref foreignTx); // Npgsql: unexpected "in transaction" status
                    else if (ex.InnerException is TimeoutException || ex is OperationCanceledException) Interlocked.Increment(ref timeouts);
                    else Interlocked.Increment(ref otherErrors);
                    return; // a hung request costs the full timeout
                }
            }
        });
        await ResetAllAsync();
        var clean = timeouts == 0 && foreignTx == 0 && otherErrors == 0 && stale == 0;
        return new(ep.Name, "CONTROL Npgsql default reset-on-close", clean,
            $"{requests} ok, {timeouts} hung (timed out), {foreignTx} landed in another client's open transaction, {otherErrors} other errors, {stale} of {probes} probes saw an identity");
    }

    /// <summary>Do auto-prepared statements survive transaction-mode pooling?</summary>
    public static async Task<Result> AutoPrepareAsync(Endpoint ep)
    {
        await using var ds = ep.Build(autoPrepare: true);
        var soak = await SoakAsync(ds, null, workers: 32, perWorker: 100);
        return new(ep.Name, "CONTROL auto-prepare (Max Auto Prepare=20) under concurrency", soak.Errors == 0 && soak.Leaks == 0,
            $"{soak.Requests} requests, {soak.Errors} errors{(soak.FirstError is null ? "" : $": {soak.FirstError}")}");
    }

    private sealed record SoakResult(int Requests, int Probes, int Leaks, int StaleProbes, int Errors, string? FirstError);

    private static async Task<SoakResult> SoakAsync(NpgsqlDataSource ds, Microsoft.EntityFrameworkCore.DbContextOptions<SpikeDb>? ef, int workers, int perWorker)
    {
        int requests = 0, probes = 0, leaks = 0, stale = 0, errors = 0;
        string? firstError = null;
        var strategies = new[] { Strategy.RoundTrips, Strategy.Batched, Strategy.SingleTrip };
        await Parallel.ForAsync(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, async (w, ct) =>
        {
            var rng = new Random(w);
            for (var i = 0; i < perWorker; i++)
            {
                var caller = Ids.Member(rng.Next(1, 2001));
                try
                {
                    if (ef is not null && i % 4 == 3)
                    {
                        var rows = await EfCoreRls.ReadAsync(ef, caller, caller, ct);
                        if (rows.Count == 0 || rows.Any(r => r.MemberId != caller)) Interlocked.Increment(ref leaks);
                    }
                    else
                    {
                        var rows = await RlsRequest.ReadAsync(ds, caller, caller, strategies[i % 3], ct);
                        if (rows.Count == 0 || rows.Any(r => r.MemberId != caller || r.Caller != caller)) Interlocked.Increment(ref leaks);
                    }
                    Interlocked.Increment(ref requests);
                    if (i % 5 == 0)
                    {
                        var p = await RlsRequest.ProbeSessionAsync(ds, ct);
                        Interlocked.Increment(ref probes);
                        if (p.Role != "authenticator" || p.Claims is not null) Interlocked.Increment(ref stale);
                    }
                }
                catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
                {
                    Interlocked.Increment(ref errors);
                    Interlocked.CompareExchange(ref firstError, ex.Message.Split('\n')[0], null);
                }
            }
        });
        return new(requests, probes, leaks, stale, errors, firstError);
    }

    private static async Task<string?> ExpectSqlStateAsync(Func<Task> action)
    {
        try { await action(); return "none (succeeded)"; }
        catch (PostgresException ex) { return ex.SqlState; }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (ex.InnerException is PostgresException pg) { return pg.SqlState; }
    }

    private static async Task SetCoachStatusAsync(NpgsqlDataSource admin, Guid coach, string status)
    {
        await using var cmd = admin.CreateCommand("update care.relationships set status = $2, revoked_at = case when $2 = 'revoked' then now() end where professional_id = $1");
        cmd.Parameters.Add(new() { Value = coach });
        cmd.Parameters.Add(new() { Value = status });
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Clears leaked session state from every server connection after the unsafe control.</summary>
    private static async Task ResetAllAsync()
    {
        await using var admin = Endpoint.Admin();
        await using var cmd = admin.CreateCommand("select pg_terminate_backend(pid) from pg_stat_activity where usename = 'authenticator'");
        await cmd.ExecuteNonQueryAsync();
        NpgsqlConnection.ClearAllPools();
    }
}
