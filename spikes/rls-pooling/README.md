# Spike: per-request RLS claims through Supavisor

Throwaway code for [ADR 0003](../../docs/decisions/0003-modular-monolith.md) follow-up 1. It checks that the API can set the caller's identity for Postgres row-level security on every request, through Npgsql and EF Core, behind Supabase's Supavisor pooler, and measures what that costs. The findings are in [`docs/spikes/rls-connection-pooling.md`](../../docs/spikes/rls-connection-pooling.md).

Nothing here is production code. The real modules, migrations and pgTAP tests will be written from the findings.

## Contents

| Path | Contents |
|---|---|
| `docker-compose.yml` | Supabase Postgres 17 and Supavisor (session mode on port 54329, transaction mode on 54330); direct Postgres on 54322 |
| `db/` | Init scripts: Supavisor metadata database, the current `auth.uid()`, and a small `care` + `nutrition` schema with grant-aware RLS and 120,000 seeded diary rows |
| `pooler/pooler.exs` | Creates the Supavisor tenant `fitcore`, with `authenticator` as the pooled login |
| `RlsSpike/` | .NET 10 console app: correctness checks (`verify`) and a latency benchmark (`bench`) |
| `killed-client-test.sh` | Kills a client mid-transaction in transaction mode and shows what the next clients see (needs `psql`) |
| `results/` | Raw output of the runs the findings quote |

All credentials are local-only placeholders.

## Run it

Needs Docker and the .NET 10 SDK.

```sh
cd spikes/rls-pooling
docker compose up -d                  # SUPAVISOR_VERSION=2.5.7 to test an older pooler
dotnet run --project RlsSpike -- verify  # correctness checks; exit code 1 on any failure
dotnet run --project RlsSpike -- bench   # latency table (SPIKE_CONCURRENCY, SPIKE_REQUESTS)
docker compose down -v
```

`verify` also runs controls that show what breaks without the rules: session-level `SET`, and Npgsql's default reset-on-close through transaction mode. Controls are expected to break and don't fail the run.
