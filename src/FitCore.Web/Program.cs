using FitCore.Contracts;
using FitCore.Modules;
using FitCore.Platform.Http;
using FitCore.Platform.Modules;
using FitCore.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// WebAssembly only: no Interactive Server or Auto render mode (CLAUDE.md, Web rendering).
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
builder.Services.AddModules(builder.Configuration, ModuleCatalog.All);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// API clients get problem details (RFC 9457) for errors and unknown routes; browsers get the HTML pages.
app.UseWhen(IsApi, api =>
{
    api.UseExceptionHandler();
    api.UseStatusCodePages();
});
app.UseWhen(context => !IsApi(context), web =>
{
    if (!app.Environment.IsDevelopment())
    {
        web.UseExceptionHandler("/Error", createScopeForErrors: true);
    }

    web.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
});
app.UseHttpsRedirection();
app.UseNoStoreHtml();
// Routing runs after the error handling, so a re-executed /Error or /not-found request is routed again.
app.UseRouting();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/health");
app.MapGroup(ApiRoutes.V1).MapModules();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(FitCore.Web.Client._Imports).Assembly);

await app.RunAsync();

static bool IsApi(HttpContext context) => context.Request.Path.StartsWithSegments(ApiRoutes.V1);
