using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FitCore.Web.Tests;

/// <summary>The web host's pipeline: API errors stay JSON, pages are never cached, and pages prerender.</summary>
public sealed class PipelineTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client = factory.CreateClient(new() { AllowAutoRedirect = false });

    [Fact]
    public async Task Unknown_API_route_returns_problem_details_not_a_page()
    {
        using var response = await client.GetAsync("/v1/does-not-exist", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unknown_page_shows_the_not_found_page_and_is_not_cached()
    {
        using var response = await client.GetAsync("/does-not-exist", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore, "Pages can carry health data, so they must not be stored.");
        Assert.Contains("<h1>Not found</h1>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Home_prerenders_for_WebAssembly_and_is_not_cached()
    {
        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore, "Pages can carry health data, so they must not be stored.");
        Assert.Contains("<h1>FitCore</h1>", html, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"webassembly\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_reports_healthy()
    {
        var body = await client.GetStringAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal("Healthy", body);
    }
}
