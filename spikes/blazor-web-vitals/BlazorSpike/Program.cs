using BlazorSpike;
using BlazorSpike.Components;
using BlazorSpike.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddInteractiveServerComponents(); // only for the /server reference variant
builder.Services.AddSingleton<DiaryStore>();
builder.Services.AddScoped<IDiaryClient, InProcessDiaryClient>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, DiaryJson.Default));

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
api.MapGet("/_stats", (DiaryStore s) => new { todayReads = s.TodayReads });
api.MapPost("/_reset", (DiaryStore s) => s.Reset());

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(BlazorSpike.Client._Imports).Assembly);

app.Run();
