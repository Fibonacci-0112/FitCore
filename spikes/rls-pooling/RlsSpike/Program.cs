using System.Globalization;
using System.Text;
using RlsSpike;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
var mode = args.FirstOrDefault() ?? "all";
var concurrency = int.Parse(Environment.GetEnvironmentVariable("SPIKE_CONCURRENCY") ?? "32");
var requests = int.Parse(Environment.GetEnvironmentVariable("SPIKE_REQUESTS") ?? "3000");
var md = new StringBuilder();
var failed = false;

if (mode is "all" or "verify")
{
    md.AppendLine("## Correctness").AppendLine();
    md.AppendLine("| Endpoint | Check | Result | Detail |").AppendLine("|---|---|---|---|");
    var results = new List<Checks.Result>();
    foreach (var ep in Endpoint.All)
        results.AddRange(await Checks.RunAsync(ep));
    foreach (var ep in Endpoint.All)
        foreach (var noReset in new[] { false, true })
            results.Add(await Checks.UnsafeControlAsync(ep, noReset));
    foreach (var ep in Endpoint.All)
        results.Add(await Checks.ResetOnCloseControlAsync(ep));
    results.Add(await Checks.AutoPrepareAsync(Endpoint.Transaction()));
    foreach (var r in results)
    {
        // Controls show what goes wrong without the rules; they may fail.
        var control = r.Check.StartsWith("CONTROL", StringComparison.Ordinal);
        var verdict = r.Pass ? (control ? "clean" : "pass") : control ? "**breaks**" : "FAIL";
        if (!r.Pass && !control) failed = true;
        md.AppendLine($"| {r.Endpoint} | {r.Check} | {verdict} | {r.Detail} |");
    }
    md.AppendLine();
}

if (mode is "all" or "bench")
{
    md.AppendLine($"## Latency ({concurrency} concurrent callers, {requests} requests per row, ms)").AppendLine();
    md.AppendLine("| Endpoint | Strategy | Op | Round trips | p50 | p95 | p99 | Requests/s |").AppendLine("|---|---|---|---|---|---|---|---|");
    foreach (var ep in Endpoint.All)
        foreach (var r in await Bench.RunAsync(ep, concurrency, requests, warmup: 300))
            md.AppendLine($"| {r.Endpoint} | {r.Strategy} | {r.Op} | {r.RoundTrips} | {r.P50:F2} | {r.P95:F2} | {r.P99:F2} | {r.PerSecond:F0} |");
    md.AppendLine();
}

Console.Write(md.ToString());
return failed ? 1 : 0;
