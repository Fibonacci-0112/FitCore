var builder = DistributedApplication.CreateBuilder(args);

// The repository root, where supabase/ lives.
var root = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));

// `supabase start` runs the local stack in Docker, applies supabase/migrations and supabase/seed.sql on first start,
// and exits once the stack is up. The stack keeps running after the AppHost stops; `npx supabase stop` ends it.
var supabase = builder.AddExecutable("supabase", "npx", root, "--yes", "supabase@2.121.0", "start");

// The API's login role on the local stack, with the local-only password from supabase/seed.sql. Never a hosted
// project's: those come from the secret store (NFR-SEC-5).
const string database = "Host=127.0.0.1;Port=54322;Database=postgres;Username=fitcore_api;Password=fitcore-local-only";

builder.AddProject<Projects.FitCore_Web>("fitcore-app")
    .WithEnvironment("ConnectionStrings__Fitcore", database)
    .WaitForCompletion(supabase);

builder.AddProject<Projects.FitCore_Worker>("fitcore-worker")
    .WithEnvironment("ConnectionStrings__Fitcore", database)
    .WaitForCompletion(supabase);

await builder.Build().RunAsync();
