using FitCore.Modules;
using FitCore.Platform.Data;
using FitCore.Platform.Modules;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddFitCoreDatabase(builder.Configuration);
builder.Services.AddModules(builder.Configuration, ModuleCatalog.All);

var host = builder.Build();
await host.RunAsync();
