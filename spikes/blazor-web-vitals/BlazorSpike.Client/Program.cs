using BlazorSpike.Client;
using BlazorSpike.Core;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<IDiaryClient, HttpDiaryClient>();

await builder.Build().RunAsync();
