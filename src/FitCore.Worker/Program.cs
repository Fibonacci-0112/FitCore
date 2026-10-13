using FitCore.Modules;
using FitCore.Platform.Modules;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddModules(builder.Configuration, ModuleCatalog.All);

var host = builder.Build();
await host.RunAsync();
