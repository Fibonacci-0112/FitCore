using BlazorSpike;
using BlazorSpike.Components;
using BlazorSpike.Core;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddInteractiveServerComponents(); // for /server and /auto
builder.Services.AddSingleton(new DiaryStore(Environment.GetEnvironmentVariable("SPIKE_STORE_FILE")));
builder.Services.AddScoped<IDiaryClient, InProcessDiaryClient>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, DiaryJson.Default));
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<CircuitHandler, CircuitCounter>());

// Circuit settings for the resilience runs. The defaults keep a disconnected circuit for 3 minutes and then
// persist its [PersistentState] in memory for 2 hours; a Redis-backed HybridCache shares persisted state
// between server instances and restarts.
if (int.TryParse(Environment.GetEnvironmentVariable("SPIKE_CIRCUIT_RETENTION_SECONDS"), out var retention))
{
    builder.Services.Configure<CircuitOptions>(o => o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromSeconds(retention));
}
if (Environment.GetEnvironmentVariable("SPIKE_REDIS") is { Length: > 0 } redis)
{
    builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redis);
    builder.Services.AddHybridCache();
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// Pages carry a member's health data, so no browser or proxy may store them.
app.Use((ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        if (ctx.Response.ContentType?.StartsWith("text/html") == true)
            ctx.Response.Headers.CacheControl = "no-store";
        return Task.CompletedTask;
    });
    return next();
});

app.UseAntiforgery();
app.MapStaticAssets();

var api = app.MapGroup("/api");
api.MapGet("/diary/today", (DiaryStore s) => s.GetToday());
api.MapPost("/diary/{date}", (DateOnly date, DiaryEntryDto entry, DiaryStore s) => s.AddAsync(date, entry));
// Measurement hooks: how many times "today" was read, and a reset between runs.
api.MapGet("/_stats", (DiaryStore s) => new
{
    todayReads = s.TodayReads,
    added = s.AddedCount,
    circuits = CircuitCounter.Open,
    connectedCircuits = CircuitCounter.Connected,
});
api.MapPost("/_reset", (DiaryStore s) => s.Reset());
// Server memory after a full, compacting collection, for the memory-per-circuit script.
api.MapGet("/_memory", () =>
{
    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    GC.WaitForPendingFinalizers();
    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    return new
    {
        circuits = CircuitCounter.Open,
        managedBytes = GC.GetTotalMemory(forceFullCollection: false),
        workingSetBytes = Environment.WorkingSet,
    };
});

// The islands page's save: stores the entry and returns the new totals and row as HTML.
app.MapPost("/islands/diary/{date}", async (DateOnly date, DiaryEntryDto entry, DiaryStore s) =>
{
    var saved = await s.AddAsync(date, entry);
    return new RazorComponentResult<IslandUpdate>(new { Day = s.Snapshot(), Added = saved });
});

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(BlazorSpike.Client._Imports).Assembly);

app.Run();
