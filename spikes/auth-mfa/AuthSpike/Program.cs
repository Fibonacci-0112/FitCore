using System.Globalization;
using System.Text;
using AuthSpike;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
var results = await Checks.RunAsync();
results.AddRange(await LibraryCheck.RunAsync());

var md = new StringBuilder();
md.AppendLine("| Area | Check | Result | Detail |").AppendLine("|---|---|---|---|");
foreach (var r in results)
    md.AppendLine($"| {r.Area} | {r.Check} | {(r.Gap ? (r.Pass ? "fixed" : "**gap present**") : r.Pass ? "pass" : "FAIL")} | {r.Detail.Replace("|", "/")} |");
Console.Write(md.ToString());
return results.All(r => r.Pass || r.Gap) ? 0 : 1;
