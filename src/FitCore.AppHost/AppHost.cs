var builder = DistributedApplication.CreateBuilder(args);

// The repository root, where supabase/ lives.
var root = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));

// `supabase start` runs the local stack in Docker, applies supabase/migrations and supabase/seed.sql on first start,
// and exits once the stack is up. The stack keeps running after the AppHost stops; `npx supabase stop` ends it.
var supabase = builder.AddExecutable("supabase", "npx", root, "--yes", "supabase@2.121.0", "start");

// The API's login role on the local stack, with the local-only password from supabase/seed.sql. Never a hosted
// project's: those come from the secret store (NFR-SEC-5).
const string database = "Host=127.0.0.1;Port=54322;Database=postgres;Username=fitcore_api;Password=fitcore-local-only";

// The local stack's Auth. Its secret key is a parameter, from user secrets or the dashboard's prompt, never a committed
// file (NFR-SEC-5): `npx supabase status` prints it. Hosted keys come from the secret store.
const string supabaseUrl = "http://127.0.0.1:54321";
var supabaseSecretKey = builder.AddParameter("supabase-secret-key", secret: true);

// The shared rate-limit counters (ADR 0005 decision 9). fitcore-app gets ConnectionStrings__valkey from the reference.
var valkey = builder.AddValkey("valkey");

builder.AddProject<Projects.FitCore_Web>("fitcore-app")
    .WithEnvironment("ConnectionStrings__Fitcore", database)
    .WithEnvironment("Supabase__Url", supabaseUrl)
    .WithEnvironment("Supabase__SecretKey", supabaseSecretKey)
    .WithReference(valkey)
    .WaitFor(valkey)
    .WaitForCompletion(supabase);

builder.AddProject<Projects.FitCore_Worker>("fitcore-worker")
    .WithEnvironment("ConnectionStrings__Fitcore", database)
    .WaitForCompletion(supabase);

await builder.Build().RunAsync();
