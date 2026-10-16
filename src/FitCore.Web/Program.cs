using FitCore.Contracts;
using FitCore.Modules;
using FitCore.Platform.Auth;
using FitCore.Platform.Data;
using FitCore.Platform.Http;
using FitCore.Platform.Modules;
using FitCore.Web.Components;
using Microsoft.AspNetCore.Antiforgery;

var builder = WebApplication.CreateBuilder(args);

// WebAssembly only: no Interactive Server or Auto render mode (CLAUDE.md, Web rendering).
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents()
    // WebAssembly learns only who is signed in: the user id, never a token or health data (ADR 0005 decision 6).
    .AddAuthenticationStateSerialization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddFitCoreAuthentication(builder.Configuration, ApiRoutes.V1);
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
builder.Services.AddFitCoreDatabase(builder.Configuration);
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
app.UseAuthentication();
app.UseWhen(IsApi, api => api.UseCrossSiteWriteProtection());
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/health");
app.MapGroup(ApiRoutes.V1).MapModules();
// The antiforgery middleware only records the token check; an endpoint without form binding must act on it itself.
// Without a session there's nothing to protect, so a second tab's sign-out after the first just lands home.
app.MapPost("/account/sign-out", async (HttpContext context, IAntiforgery antiforgery, MemberSessions sessions) =>
{
    if (sessions.HasSession(context) && context.User.Identity?.IsAuthenticated == true && !await antiforgery.IsRequestValidAsync(context))
    {
        return Results.BadRequest();
    }

    return await sessions.EndAsync(context) ? Results.Redirect("/") : Results.Redirect("/account/sign-out-failed");
});
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(FitCore.Web.Client._Imports).Assembly);

await app.RunAsync();

static bool IsApi(HttpContext context) => context.Request.Path.StartsWithSegments(ApiRoutes.V1);
