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
builder.Services.AddModules(builder.Configuration, ModuleCatalog.All);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseNoStoreHtml();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/health");
app.MapGroup(ApiRoutes.V1).MapModules();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(FitCore.Web.Client._Imports).Assembly);

await app.RunAsync();
