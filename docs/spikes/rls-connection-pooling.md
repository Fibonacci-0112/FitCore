# Spike: Per-Request RLS Claims Through Connection Pooling

- **Status:** Done. Findings feed the Platform module's database session code and the beta migrations.
- **Date:** 2026-10-13
- **Decision record:** [ADR 0003](../decisions/0003-modular-monolith.md), follow-up 1
- **Code and raw results:** [`spikes/rls-pooling/`](../../spikes/rls-pooling/README.md)

## 1. Question

ADR 0003 keeps Postgres RLS on for API traffic: each request runs as the `authenticated` role with the caller's JWT claims ([architecture-options §B.3](../architecture-options.md#b3-database-and-schema-approach)). The API reuses database connections through Npgsql's pool and, optionally, Supabase's Supavisor pooler. The spike answers three questions:

1. Can the caller's identity be applied per request so that it never reaches another request, through Npgsql and EF Core, directly and through Supavisor session and transaction modes?
2. Which client and pooler settings are required, and which ones break it?
3. What does applying the identity cost against NFR-PRF-1 (reads p95 ≤ 300 ms) and NFR-PRF-2 (writes p95 ≤ 500 ms)?

## 2. Setup

| Component | Version |
|---|---|
| Postgres | `supabase/postgres:17.6.1.016`, with the `auth.uid()` that hosted projects use |
| Pooler | Supavisor 2.9.13 (current release); 2.5.7 for comparison |
| Client | .NET 10, Npgsql 10.0.3, EF Core 10 with the Npgsql provider 10.0.3 |
| Schema | `care.relationships`, `care.has_grant(member_id, scope)` and `nutrition.diary_entries` with grant-aware RLS; 2,000 members and 120,000 diary rows |

The API logs in as a role without table grants and switches to `authenticated` per request. Four ways of applying the identity were tested:

| Strategy | What the API sends | Round trips per read |
|---|---|---|
| Round trips | `BEGIN`; `SET LOCAL ROLE authenticated`; `set_config('request.jwt.claims', $1, true)`; query; `COMMIT`, each awaited separately | 4 |
| Batched | Explicit transaction; role, claims and query in one `NpgsqlBatch`; then `COMMIT` | 2 |
| Single trip | Role, claims and query in one `NpgsqlBatch` with no explicit transaction, so Postgres runs it as one implicit transaction | 1 |
| EF Core interceptor | A `DbTransactionInterceptor` sends role and claims when EF Core starts a transaction; then the query and `COMMIT` | 3 |

## 3. Results

Full output: [verify](../../spikes/rls-pooling/results/verify-supavisor-2.9.13.md) and [benchmark](../../spikes/rls-pooling/results/bench-supavisor-2.9.13.md).

### 3.1 Correctness with the rules in section 4

All four strategies passed every check on all three endpoints (direct, Supavisor session mode, Supavisor transaction mode):

- The owner reads their own diary. Another member, a grantee whose grant is out of scope and a revoked grantee see nothing. A grantee in scope sees the owner's rows.
- A member can write their own diary. A write to another member's diary is rejected with SQLSTATE 42501.
- Revoking a grant applies on the very next request (NFR-PRV-4).
- After each request the connection is back to the login role with no claims.
- A concurrent soak of 9,600 requests per endpoint from random members, with 1,920 no-identity probes in between, saw 0 cross-request leaks and 0 errors.
- An EF Core query outside a transaction fails closed with SQLSTATE 42501, because the login role has no table grants.

### 3.2 What breaks

The controls run the same workload without one of the rules. Their counts vary between runs; the table shows the published run.

| Control | Direct | Session mode | Transaction mode |
|---|---|---|---|
| Session-level `SET ROLE` and `set_config(..., false)`, with Npgsql's default reset | Clean: Npgsql's `DISCARD ALL` clears it | Clean | **Breaks**: 16 requests hung, and 3 of 9 probes saw another caller's identity |
| Session-level `SET`, with `No Reset On Close=true` | **Leaks**: 400 of 400 probes saw another caller's identity | **Leaks**: 400 of 400 | **Leaks**: 400 of 400 |
| Safe pattern, with Npgsql's default reset-on-close | Clean | Clean | Breaks: 13 of 40 requests hung and 3 failed |
| Npgsql auto-prepare (`Max Auto Prepare=20`) | Not tested | Not tested | Breaks: 1,097 of 2,191 requests failed with `42P05 prepared statement "_auto0" already exists` |

Two Supavisor transaction-mode findings matter beyond hangs:

1. **A hung request can hand its open transaction to another client.** When Npgsql reuses a pooled connection it prepends `DISCARD ALL` to the next command. The hangs happen only with that reset on, so the likely cause is that Supavisor takes the reply to `DISCARD ALL` as the end of the transaction and loses track of the rest of the pipeline. The client hangs, and the server connection can go back to the pool still inside the hung request's transaction. A later client was then handed that connection on both 2.5.7 and 2.9.13. Npgsql noticed the unexpected transaction status and threw, so no data leaked to it, but any statements the hung request had already run, including `SET LOCAL` claims, sat inside a transaction another client would commit.
2. **On Supavisor 2.5.7 a killed client leaks its identity** ([`killed-client-test.sh`](../../spikes/rls-pooling/killed-client-test.sh)). A client that set claims inside a transaction and then died left the transaction open. The next client (psql, which doesn't check transaction status) ran inside it as `authenticated` with the dead client's claims and read that member's 60 diary rows. Supavisor 2.9.13 closed the server connection instead in 3 of 3 trials.

### 3.3 Latency

p95 in milliseconds at 8 concurrent callers, 3,000 requests per cell, with each endpoint's recommended settings: Npgsql's default reset on direct and session, `No Reset On Close=true` on transaction mode. Everything ran on one 4-core host, so these numbers mostly show client and pooler overhead; network cost is the round-trip count times the API-to-database round-trip time.

| Strategy | Round trips | Direct read / write | Session read / write | Transaction read / write |
|---|---|---|---|---|
| `select 1` baseline | 1 | 1.3 | 3.2 | 2.2 |
| Round trips | 4 | 5.8 / 7.3 | 10.6 / 7.8 | 8.4 / 7.7 |
| Batched | 2 | 3.8 / 4.5 | 7.1 / 7.2 | 6.9 / 6.0 |
| Single trip | 1 | 3.4 / 5.0 | 7.7 / 7.4 | 6.2 / 7.5 |
| EF Core interceptor | 3 | 7.5 / 11.4 | 8.9 / 9.0 | 7.7 / 9.0 |

Applying the identity costs one extra statement in the same round trip (single trip), or one extra round trip (batched, EF Core). Even at a 2 ms round-trip time between Fargate and Supabase, the EF Core pattern adds about 4 ms over a bare query, against budgets of 300 ms for reads and 500 ms for writes. Identity handling isn't a latency risk for NFR-PRF-1 or NFR-PRF-2. The 3× load test (NFR-SCL-1) still has to confirm it against the real Supabase project.

## 4. Rules for the API

These rules go into `FitCore.Platform`'s database session code and its tests.

1. **Only transaction-scoped identity.** Use `SET LOCAL ROLE authenticated` and `set_config('request.jwt.claims', $claims, true)` inside the unit of work's transaction. Never use session-level `SET`, `SET ROLE` or `set_config(..., false)` on an API connection.
2. **The identity goes first, in the same round trip.** Reads send role, claims and query as one batch (single trip). Units of work with several statements open a transaction and send role and claims in the same batch as the first statement.
3. **EF Core work always runs in a transaction**, with the claims interceptor attached. Work outside a transaction fails closed, and that is intended.
4. **The API's login role has no table grants.** It can only switch to `authenticated`. A missing identity then fails with 42501 instead of reading data.
5. **Connect through Supavisor session mode or directly, not transaction mode.** The API already pools in Npgsql, so transaction mode adds little at the launch scale and brings the failures in section 3.2. Session mode and direct connections keep Npgsql's `DISCARD ALL` reset as a second safety net, and allow prepared statements. Size `Max Pool Size` so that pool size × app and worker instances stays within the pooler's pool size or the compute size's connection limit.
6. **If transaction mode is ever needed** (connection limits at 10× scale), set `No Reset On Close=true`, keep `Max Auto Prepare=0`, and allow no session state at all: no `LISTEN`, session advisory locks, temporary tables or session `SET`. Rerun this spike's checks against the pooler version in use before switching.
7. **Writes are idempotent by client UUID.** A request that times out may still commit, so retries must not create duplicates. Diary entries already carry a client UUID ([architecture-options §B.3](../architecture-options.md#b3-database-and-schema-approach)).

## 5. Open points

- **Which role the API logs in as on hosted Supabase.** The spike used `authenticator`, whose password hosted projects don't expose. The beta migrations should create a dedicated login role (for example `fitcore_api`, `NOINHERIT`, granted `authenticated` only) and the pgTAP suite should check that it has no table grants.
- **Hosted Supavisor version and behavior.** The spike ran Supavisor locally. Rule 5 avoids transaction mode, but the Supavisor version on the hosted project and its handling of a client that disconnects mid-transaction should be confirmed with Supabase before the Public launch.
- **Direct connections and IPv6.** Supabase direct connections are IPv6 unless the IPv4 add-on is bought, and session mode through Supavisor works over IPv4. The Fargate network design must pick one; this affects NFR-CST-1 slightly.
- **Load test.** NFR-SCL-1's 3× load test must include identity handling, pool sizing and a mid-transaction client kill.
