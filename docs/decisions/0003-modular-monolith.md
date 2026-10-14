# 0003: Build the backend as a modular monolith on ASP.NET Core with Supabase

- **Status:** Accepted. Approved by the product owner on 2026-10-13.
- **Date:** 2026-10-13
- **Deciders:** Product owner
- **Related:** [`docs/architecture-options.md`](../architecture-options.md) (the full comparison), [`docs/BRD.md`](../BRD.md) and [`docs/NFR.md`](../NFR.md). Earlier decision records are in git history: [0001](https://github.com/Fibonacci-0112/FitCore/blob/7149e58a9b1397b95eb7c1426b90425f09903b6b/docs/decisions/0001-windows-desktop-packaging.md) and [0002](https://github.com/Fibonacci-0112/FitCore/blob/7149e58a9b1397b95eb7c1426b90425f09903b6b/docs/decisions/0002-dotnet-platform.md).

## Context

The BRD fixes the platform: .NET MAUI XAML apps and a Blazor Web App on the client side, a shared C# domain library, Supabase (Postgres, Auth, Storage, row-level security) and ASP.NET Core services ([BRD §5](../BRD.md#5-platform-and-technology-constraints)). It doesn't say how the server side is structured, or whether clients reach Postgres directly or through our own API.

The requirements that decide this are:

- **Safety rules enforced on the server.** Soft limits, the clinician-only hard floor, pending coach targets and logged overrides ([BRD §6.2](../BRD.md#62-targets-and-safety)) can't be trusted to clients.
- **Grant-aware privacy.** RLS on every table that holds member data, revocation that takes effect on the next request, and a 24-hour limit on member data cached on a professional's device (NFR-PRV-1..4).
- **Cross-cutting limits.** Per-member rate limits and AI quotas (NFR-SCL-5, NFR-CST-3), and no health data in logs (NFR-PRV-6).
- **Stable contracts for released apps.** Old app versions keep working for 90 days (NFR-CMP-5).
- **One implementation of the rules** (NFR-MNT-1, NFR-MNT-4).
- **Budget and team.** At most $500/month at launch (NFR-CST-1), a small team, and a 1-hour on-call response (NFR-OPS-4).
- **Headroom.** 10× the launch load without a redesign ([NFR §1.1](../NFR.md#11-sizing-assumptions)).

## Decision

Build the backend as a **modular monolith on ASP.NET Core (.NET 10 LTS)**, with Supabase as the data, authentication and storage platform.

1. **One deployable, two process types.** A single container image runs as `fitcore-app` (REST API, Blazor host, SignalR; at least two instances) and `fitcore-worker` (outbox dispatch, export, imports, sync, backups). It runs on AWS ECS Fargate in the same region as the Supabase project.
2. **Strict modules.** Identity, Catalog, Nutrition, Training, Progress, Targets, Care, Sync, Billing, Integrations, DataRights and Audit. Each module owns one Postgres schema of the same name in snake case (`identity`, `catalog`, `nutrition`, `training`, `progress`, `targets`, `care`, `sync`, `billing`, `integrations`, `data_rights`, `audit`) and exposes only a contracts surface. Cross-module side effects go through a transactional outbox. Architecture tests enforce the boundaries.
3. **Shared rules run on both sides.** `FitCore.Domain` runs on devices for previews and in the API for enforcement.
4. **The API is the only way in.** Clients use versioned REST (`/v1`) through one typed C# client. The Supabase Data API is disabled for module schemas.
5. **Two authorization layers.** API policies check roles, entitlements and live grants. Postgres RLS checks again: each request runs as the `authenticated` role with the caller's JWT claims. pgTAP covers the six actors in NFR-PRV-2.
6. **Supabase stays the platform.** Supabase Auth provides sign-in (email/password, Google, Microsoft, Apple) and MFA, and the API validates its JWTs. The database schema lives in SQL migrations under `supabase/`, forward-only and expand-then-contract. Point-in-time recovery covers the database. A versioned S3 copy backs up Storage objects.
7. **Shared state for scale-out.** A small Valkey node holds rate-limit and quota counters and is the SignalR backplane, so app instances stay stateless (NFR-SCL-2).

The design details (schemas, the sync protocol, the offline grant lease, deletion of backups, the cost model) are in [`architecture-options.md` §3](../architecture-options.md#3-option-b-modular-monolith-on-aspnet-core-with-supabase-as-the-data-platform).

## Options considered

| Option | Why it wasn't chosen |
|---|---|
| **A: Supabase-first serverless.** Clients call PostgREST, rules live in PL/pgSQL, and .NET functions handle integrations. | The cheapest option, but the safety rules would exist in both C# and SQL (NFR-MNT-4). It has no per-member rate limiting on the data path (NFR-SCL-5). The schema is the public API, so schema changes break released apps (NFR-CMP-5). A 15-minute export would hit Lambda's 15-minute limit (NFR-PRF-12). |
| **C: Microservices.** About 12 services with a gateway, gRPC and a broker. | Estimated at $630–1,030/month, against a $500 budget (NFR-CST-1). Grant checks spread across services conflict with revocation on the next request (NFR-PRV-4). Too much to operate for a small team. |

## Consequences

### Positive

- Each safety rule has one implementation and one test suite, the code that actually runs in production.
- Privacy holds even if one layer has a bug, because the API and RLS each enforce it.
- Rate limits, quotas, redaction, consent checks and resilience live in one request pipeline.
- Two process types to deploy, roll back and be on call for.
- Reaches 10× by adding app instances and a larger Supabase compute size. Modules with clean contracts can be extracted into services later if one needs to scale on its own.

### Negative

- About $400–535/month: inside the $500 budget only at the lean end. It needs Small database compute until a load test says otherwise, free-tier monitoring, and the AI cap as the main lever.
- Every client request goes through the API, so it needs at least two instances and health checks.
- Module boundaries only hold if the architecture tests and reviews enforce them.
- Setting RLS claims on every request adds a little latency and needs care with connection pooling.

### Risks to track

- The Supabase plan this budget allows has no contractual SLA for 99.5% availability (NFR-AVL-1, [NFR open item 3](../NFR.md#17-open-items)).
- Passkey MFA depends on Supabase Auth support (NFR-SEC-3).
- Cost estimates are from list prices and need current vendor quotes before the Public launch.

## Revisit if

- The 3× peak load test (NFR-SCL-1) can't meet the NFR-PRF targets by adding instances and database compute.
- A module needs independent scaling or fault isolation that bulkheads inside the monolith can't give, for example AI logging or health-platform sync. Extract that module only.
- Paying coaches or clinics need a contractual SLA above 99.5%.
- The Clinic phase needs a HIPAA boundary that a separate deployment cell from the same image can't provide.

## Follow-up

1. Spike per-request RLS claims through Npgsql or EF Core with Supavisor, measured against NFR-PRF-1 and NFR-PRF-2. Done: see the [spike findings](../spikes/rls-connection-pooling.md). The API connects through Supavisor session mode or directly, not transaction mode, and applies the identity with `SET LOCAL` only.
2. Spike Sign in with Apple and MFA from MAUI to Supabase, and check passkey support. Partly done: see the [spike findings](../spikes/apple-sign-in-and-mfa.md). TOTP meets the MFA need, passkey sign-in is beta and single-factor, and the backend must revoke Apple tokens on account deletion. The on-device Apple test is still to run.
3. Spike Blazor prerendering with WebAssembly against NFR-PRF-8. Done: see the [spike findings](../spikes/blazor-prerendering-web-vitals.md). In lab runs, prerendering kept LCP and CLS well inside the targets on every profile; WebAssembly only didn't. Taps after start met INP once view-model commands yield before calling the API. Taps while WebAssembly starts wait 0.5–1.2 s on the mobile profiles, tracked as [NFR open item 5](../NFR.md#17-open-items). Field acceptance of NFR-PRF-8 needs real-user data from the beta.
4. Build a cost model from vendor quotes.
5. Scaffold the solution layout in [`architecture-options.md` §B.2](../architecture-options.md#b2-project-structure-and-boundaries) and extend CI to build and test it. Started: the server, web and shared projects, the 12 module projects and the architecture tests build and run in CI. `FitCore.Domain.Tests` and its coverage gate (NFR-MNT-1) run in CI too. Still to add: `FitCore.Maui`, `supabase/` with the pgTAP harness, the module test projects and `FitCore.Load.Tests`. The [implementation plan](../implementation-plan.md) schedules them.
