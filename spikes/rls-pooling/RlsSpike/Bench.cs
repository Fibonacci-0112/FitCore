using System.Diagnostics;
using Npgsql;

namespace RlsSpike;

/// <summary>Latency of one unit of work under concurrency, per endpoint and strategy.</summary>
public static class Bench
{
    public sealed record Row(string Endpoint, string Strategy, string Op, int RoundTrips, int Requests, double P50, double P95, double P99, double PerSecond);

    public static async Task<List<Row>> RunAsync(Endpoint ep, int concurrency, int requests, int warmup)
    {
        await using var ds = ep.Build();
        var ef = SpikeDb.Options(ds);
        var rows = new List<Row>();

        rows.Add(await MeasureAsync(ep, "select 1 (RTT baseline)", "-", 1, concurrency, requests, warmup, async (_, ct) =>
        {
            await using var cmd = ds.CreateCommand("select 1");
            await cmd.ExecuteScalarAsync(ct);
        }));

        foreach (var (name, trips, read, write) in new (string, int, Func<Guid, CancellationToken, Task>, Func<Guid, CancellationToken, Task>)[]
        {
            ("Npgsql RoundTrips", 4, (m, ct) => RlsRequest.ReadAsync(ds, m, m, Strategy.RoundTrips, ct), (m, ct) => RlsRequest.InsertAsync(ds, m, m, Strategy.RoundTrips, ct)),
            ("Npgsql Batched", 2, (m, ct) => RlsRequest.ReadAsync(ds, m, m, Strategy.Batched, ct), (m, ct) => RlsRequest.InsertAsync(ds, m, m, Strategy.Batched, ct)),
            ("Npgsql SingleTrip", 1, (m, ct) => RlsRequest.ReadAsync(ds, m, m, Strategy.SingleTrip, ct), (m, ct) => RlsRequest.InsertAsync(ds, m, m, Strategy.SingleTrip, ct)),
            ("EF Core interceptor", 3, (m, ct) => EfCoreRls.ReadAsync(ef, m, m, ct), (m, ct) => EfCoreRls.InsertAsync(ef, m, m, ct)),
        })
        {
            rows.Add(await MeasureAsync(ep, name, "read", trips, concurrency, requests, warmup, read));
            rows.Add(await MeasureAsync(ep, name, "write", trips, concurrency, requests, warmup, write));
        }
        return rows;
    }

    private static async Task<Row> MeasureAsync(Endpoint ep, string strategy, string op, int trips, int concurrency, int requests, int warmup,
        Func<Guid, CancellationToken, Task> unit)
    {
        await RunAsync(warmup, concurrency, unit, null);
        var samples = new double[requests];
        var wall = Stopwatch.StartNew();
        await RunAsync(requests, concurrency, unit, samples);
        wall.Stop();
        Array.Sort(samples);
        return new(ep.Name, strategy, op, trips, requests, Pct(samples, 0.50), Pct(samples, 0.95), Pct(samples, 0.99), requests / wall.Elapsed.TotalSeconds);
    }

    private static Task RunAsync(int count, int concurrency, Func<Guid, CancellationToken, Task> unit, double[]? samples)
    {
        var next = -1;
        return Parallel.ForAsync(0, concurrency, new ParallelOptions { MaxDegreeOfParallelism = concurrency }, async (_, ct) =>
        {
            int i;
            while ((i = Interlocked.Increment(ref next)) < count)
            {
                var member = Ids.Member(1 + i % 2000);
                var start = Stopwatch.GetTimestamp();
                await unit(member, ct);
                if (samples is not null) samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
        });
    }

    private static double Pct(double[] sorted, double p) => sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)];
}
