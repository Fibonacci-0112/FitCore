# Implementation plan: FitCore

- **Status:** Draft v1, for product owner review.
- **Date:** 2026-10-14
- **Related:** [`BRD.md`](BRD.md), [`NFR.md`](NFR.md), [ADR 0003](decisions/0003-modular-monolith.md) and [`architecture-options.md`](architecture-options.md) (Option B).
- **Replaces:** the plan for the Expo prototype, [`docs/PLAN.md` at `7149e58`](https://github.com/Fibonacci-0112/FitCore/blob/7149e58a9b1397b95eb7c1426b90425f09903b6b/docs/PLAN.md). Its data contracts carry over. Its stack, layout and PR list don't.

This plan turns the requirements and the accepted architecture into a build order. It covers the private beta (R1) in detail, as a sequence of small PRs with acceptance criteria, and the later phases at milestone level. It doesn't change any requirement or decision. Where it needs a new decision, it lists it in [section 8](#8-decisions-still-needed).

---

## 1. Sources

| Topic | Source |
|---|---|
| Scope and phases | [BRD §4.1](BRD.md#41-release-phases), [§4.2](BRD.md#42-feature-list), [§4.3](BRD.md#43-key-user-journeys) |
| Business rules | [BRD §6](BRD.md#6-business-rules) |
| Targets, each tagged with the phase it applies from | [`NFR.md`](NFR.md) |
| Architecture | [ADR 0003](decisions/0003-modular-monolith.md) and [`architecture-options.md` §B.1–§B.6](architecture-options.md#3-option-b-modular-monolith-on-aspnet-core-with-supabase-as-the-data-platform) |
| Database identity and pooling | [RLS spike §4](spikes/rls-connection-pooling.md#4-rules-for-the-api) |
| Sign-in and MFA | [Auth spike §4](spikes/apple-sign-in-and-mfa.md#4-rules-for-the-build) |
| Web rendering | [Blazor spike §5](spikes/blazor-prerendering-web-vitals.md#5-rules-for-the-build) and [render-mode comparison §5](spikes/web-render-modes.md#5-what-this-changes) |

## 2. Where we are

- **Done:** ADR 0003; the RLS and Blazor spikes; the render-mode comparison; the solution scaffold (`FitCore.slnx`, 12 empty modules, both hosts, architecture tests and pipeline tests in CI).
- **Partly done:** the auth spike. The on-device Sign in with Apple test is still to run ([auth spike §5](spikes/apple-sign-in-and-mfa.md#5-device-test-plan-for-sign-in-with-apple)).
- **Not started:** the cost model (ADR 0003 follow-up 4). Still missing from the scaffold (follow-up 5): the module test projects other than Nutrition's and Identity's, and `FitCore.Load.Tests`.
- **R1 progress:** [PR 1](#pr-1-domain-library), the domain library with `FitCore.Domain.Tests` and its coverage gate, [PR 2](#pr-2-database-foundation), the database foundation, and [PR 3](#pr-3-r1-schema-and-rls), the R1 schema and RLS, are done. [PR 4](#pr-4-sign-in-profile-and-app-shells) is in progress, in parts. The first part covers the web session of [ADR 0005](decisions/0005-web-session.md), email sign-up with confirmation, sign-in and sign-out on the web, the 18+ gate in the database, and `GET /v1/profile`. The second part adds `FitCore.Maui` with its shell and email sign-up, which calls Supabase Auth through `FitCore.Client.Auth` with the publishable key, and builds the Android app in CI. Until the app handles links, the confirmation link opens fitcore-app's confirm page in the browser. Still to come: MAUI sign-in, the session in the secure store and sign-out, Google, Microsoft and Apple sign-in, TOTP step-up, and sign-in rate limits with Valkey.
- **Environments:** the first hosted Supabase project exists: `FitCore-Dev-DB` in the `Fibonacci-0112` organization (`https://apauhqfldklnmqthubno.supabase.co`, Postgres 17, `us-west-2`). It is the shared development and staging database for R1. Production is a separate project, created in [PR 9](#pr-9-beta-environments-and-release).

## 3. Foundations

Every feature PR builds on these. Each is introduced by the first PR that needs it, not all at once.

### 3.1 Database

- `supabase/migrations/` is the only source of schema. Migrations are forward-only and expand-then-contract, so the previous release still runs after a rollback (NFR-OPS-6).
- Each module owns one schema, named after the module in snake case. The Data API doesn't expose module schemas; the `/v1` API is the only way in ([§B.3](architecture-options.md#b3-database-and-schema-approach)).
- Every migration enables RLS explicitly on every table it creates. FitCore doesn't use Supabase's "Enable automatic RLS" option: it covers only the `public` schema, and it adds database state that isn't in the migrations.
- `supabase/tests/` holds the pgTAP suite. It starts with three shared tests:
  - a test that fails if any table in a FitCore module schema lacks RLS (NFR-PRV-1);
  - a six-actor helper for owner, other member, grantee in scope, grantee out of scope, revoked grantee and admin (NFR-PRV-2);
  - a test that the `fitcore_api` login role has no table grants and can only switch to `authenticated` ([RLS spike §5](spikes/rls-connection-pooling.md#5-open-points)).
- Every migration ships with its pgTAP tests (NFR-MNT-2).
- A module writes only its own schema. Policies may read another schema only to decide visibility: `care.has_grant()` for grants, and Nutrition's check that a food is one the member can see in `catalog`. Foreign keys may point into another schema, as diary entries and prices point to `catalog.foods`. Audit triggers write `audit.events` only through `audit.append()`.

### 3.2 Database identity

- `FitCore.Platform` owns one `NpgsqlDataSource` and a unit-of-work wrapper. Each unit runs in a transaction and sends `SET LOCAL ROLE authenticated` and `set_config('request.jwt.claims', …, true)` in the same batch as its first statement. EF Core work gets the claims interceptor. Work outside a transaction fails closed.
- The API and worker connect as `fitcore_api` through Supavisor session mode or directly, never transaction mode. Pool size × instances stays within the pooler limit ([RLS spike §4](spikes/rls-connection-pooling.md#4-rules-for-the-api)).
- Jobs that write outside any caller's identity use their own least-privilege login roles and data sources, never `fitcore_api` ([§B.3](architecture-options.md#b3-database-and-schema-approach)). R1 has one: `fitcore_catalog_import`, which can read and write only the shared rows of `catalog` (`owner_id` is null). Row policies keep it away from members' custom foods and servings in the same schema, and it has no access to any member schema. Webhooks, deletion and export get their own roles in later phases.
- Writes are idempotent by client-generated UUID.

### 3.3 Authentication

- The API validates Supabase JWTs against the project's signing keys. Clinician and admin endpoints require `aal2` (NFR-SEC-3; [auth spike §4](spikes/apple-sign-in-and-mfa.md#4-rules-for-the-build)).
- **Web:** a server-side session. The Supabase session is held in an HttpOnly, Secure, SameSite cookie, and the browser calls the same-origin `/v1` API. Every cookie-authenticated `/v1` endpoint that changes state requires both antiforgery validation and a same-origin `Origin` or `Sec-Fetch-Site` check. Tokens never reach WebAssembly or local storage. The details are in [ADR 0005](decisions/0005-web-session.md).
- **MAUI:** `Supabase.Gotrue` behind a thin wrapper, `FitCore.Client.Auth`, called with the project's publishable key, with tokens in the platform secure store (NFR-SEC-8). Nothing the Blazor client is built from references it, so it never reaches the browser (ADR 0005). On iOS, Sign in with Apple uses the native provider with a hashed nonce.
- Sign-in methods for R1: email and password, Google, Microsoft and Apple ([BRD §4.2](BRD.md#42-feature-list)), every method on every platform. Apple uses the native flow on iOS and macOS and Apple's web flow elsewhere. Passwords follow NFR-SEC-2; sign-in rate limits and lockout follow NFR-SEC-4.

### 3.4 API conventions

- Minimal APIs under `/v1`, one route group per module. Errors are problem details; responses never echo health data (NFR-PRV-6).
- Guardrails: a soft-limit breach returns 422 with a warning ID; the client resends with `acknowledgedWarningId`, and the override is audited. A hard-floor breach returns 403 ([§B.4](architecture-options.md#b4-api-design)).
- Every list is paginated with a maximum page size, and every query filters on an indexed member or grant key (NFR-SCL-3).
- DTOs live in `FitCore.Contracts` with source-generated JSON contexts, shared by the server and both clients.
- Token-bucket rate limits per NFR-SCL-5. Counters live in Valkey once deployed and in memory locally.

### 3.5 Cross-cutting services

- **Observability:** OpenTelemetry traces and structured logs with a correlation ID. A redaction allow-list runs before anything leaves the process (NFR-OPS-1, NFR-OPS-2, NFR-PRV-6).
- **Outbox:** each module that publishes events writes them to its own outbox table in the same transaction. `FitCore.Worker` dispatches them.
- **Audit:** `audit.events` is written by triggers, through `audit.append()`, a security definer function no app role can call. App roles get SELECT only, and a trigger rejects updates, deletes and truncation from any role, so no one can edit or delete an entry (NFR-AUD-3). R1 audits grant changes, professional writes to targets, guardrail overrides and target acceptances; admin actions join in later phases.
- **Configuration and secrets:** settings come from `appsettings`, then user secrets locally, then the deployed secret store. No secret is ever committed (NFR-SEC-5).

### 3.6 Clients

- `FitCore.Client.Core` holds the view models (CommunityToolkit.Mvvm 8.4.2 or later) and the typed API client. Commands update the screen optimistically, `await Task.Yield()`, then call the API ([Blazor spike §5](spikes/blazor-prerendering-web-vitals.md#5-rules-for-the-build)). No business logic lives in UI code (NFR-MNT-4).
- `FitCore.Web.Client` pages prerender on the server and hydrate with WebAssembly. Prerendered data travels in `[PersistentState]`, and actions stay disabled until `RendererInfo.IsInteractive`. Member pages send `Cache-Control: no-store`.
- `FitCore.Maui` is the XAML app for iOS, Android, Windows and macOS (NFR-CMP-1). The Supabase URL and publishable key are build properties, defaulting to the local stack.
- Strings live in resource files (NFR-L10N-2). Units, currency and time zone come from the profile (NFR-L10N-3).
- Accessibility is built in from the first screen: WCAG 2.2 AA on the web, screen readers, 200% text and reduce-motion everywhere (NFR-ACC-1 to NFR-ACC-4, NFR-ACC-6).

### 3.7 Local development and tests

- A .NET Aspire AppHost starts the Supabase CLI stack, Valkey, the web host and the worker ([§B.6](architecture-options.md#b6-deployment-model)).
- Test projects:
  - `FitCore.Domain.Tests`: table tests for every BRD §6 rule, with a 90% line-coverage gate in CI (NFR-MNT-1).
  - `FitCore.Modules.<Name>.Tests`: each module against the local Supabase stack, through the real unit of work. They share the database fixture in `FitCore.Testing` with `FitCore.Platform.Tests`.
  - `FitCore.Architecture.Tests`: extended as projects are added.
  - `FitCore.Client.Tests`: the shared view models, and `FitCore.Client.Auth` against the local Supabase stack.
  - `FitCore.Load.Tests`: a beta-sized run in [PR 9](#pr-9-beta-environments-and-release) for the API latency targets (NFR-PRF-1, NFR-PRF-2), then the 3× peak run before the public launch (NFR-SCL-1).
- CI gains jobs as their first content lands: pgTAP, dependency and secret scanning plus SAST (NFR-SEC-9), a MAUI build, and Lighthouse with the Playwright tap tests and a payload budget. Every job gates merging to `main` (NFR-MNT-3).

## 4. R1 data model

The R1 tables port the prototype's schema ([`r1_schema.sql` at `7149e58`](https://github.com/Fibonacci-0112/FitCore/blob/7149e58a9b1397b95eb7c1426b90425f09903b6b/supabase/migrations/20261008120000_r1_schema.sql)) into module schemas, with its RLS policies and pgTAP tests as the starting point.

| Schema | Tables | Notes |
|---|---|---|
| `identity` | `profiles` | Birth date for the 18+ gate, sex, height, activity level, time zone, units, currency. Private to the member in R1: RLS can't limit a grantee to the name and goals the [permission matrix](BRD.md#32-permission-matrix) allows, so that read arrives as a column-limited path with the Coach/Pro roster |
| `targets` | `targets` | kcal and macros, `source` (estimated or manual), `set_by`, `state`, `below_soft_limit`, `below_hard_floor`, `effective_from` |
| `catalog` | `foods`, `food_servings` | Nutrients only, never prices. `owner_id` is null for shared rows, which only the service writes ([BRD §6.1](BRD.md#61-data-contracts)). Unique `(source, source_id)` for imports |
| `nutrition` | `food_prices`, `diary_entries`, `diary_days` | Prices are private per member. Entries keep nutrient and cost snapshots, a client UUID and a `version`. Days carry the "complete" flag |
| `progress` | `body_weights` | One row per weigh-in, on the member's local date |
| `care` | `relationships`, `has_grant()` | Created now, so every RLS policy is grant-aware from the start ([§7.4](architecture-options.md#74-next-steps)). No grant UI until Coach/Pro. In R1 the `nutrition` scope covers the diary, diary days and targets, and `body_metrics` covers weigh-ins |
| `audit` | `events` | Trigger-written, append-only |

The data contracts from [BRD §6.1](BRD.md#61-data-contracts) apply throughout:

- Every nutrient column is nullable. `null` means unknown, never zero, and totals report a lower bound plus a count of incomplete items.
- Money is `bigint` minor units plus a `char(3)` ISO 4217 code. A price in another currency than the profile's leaves the entry unpriced; R1 does no conversion.
- Amounts can be entered in any standard mass unit (mg, g, kg, oz, lb) or volume unit (ml, l, tsp, tbsp, fl oz, cup, pint, quart, gallon). Each converts exactly to `g` or `ml`, the canonical units. Food-specific measures, such as "1 slice" or a label's "1 cup = 128 g", are `food_servings` rows. Mass↔volume conversion needs `density_g_per_ml`; anything else is rejected.
- A diary entry's price is the member's price for that food, in the profile currency, with the latest `effective_date` on or before the diary date. When two prices share that date, the most recently created one wins, so the choice is deterministic.
- Snapshots are immutable. Editing a food or price never changes a logged entry (NFR-REL-4).
- `diary_date` is a `date` in the member's local calendar.

## 5. R1 delivery

Nine PRs, each scoped to one item and merged green. A feature PR may land its web screen and its MAUI screen separately, but the beta ships only when both UIs have every R1 feature (NFR-CMP-3).

### PR 1: Domain library

- **Scope:** `FitCore.Domain` and `FitCore.Domain.Tests`. Quantity resolver (units, servings, density), nutrient totals with an incomplete count, money and estimated cost, price selection by date and currency, Mifflin-St Jeor with activity factor, weekly rate ↔ goal date, soft limits and hard floor, local-date helpers.
- **Accept:**
  - Table tests for every BRD §6 rule, including an impossible conversion rejected, `null` kept apart from 0, a price effective-date boundary, a wrong-currency price leaving an entry unpriced, and an aggressive goal date producing a warning.
  - The coverage gate (≥ 90%) runs in CI.

### PR 2: Database foundation

- **Scope:** `supabase/` (config, the module schemas, the `fitcore_api` role, the shared pgTAP tests in [section 3.1](#31-database)), a pgTAP CI job, the Platform unit of work, the Aspire AppHost and the first database test project, `FitCore.Platform.Tests`.
- **Deferred:** the first module test project moves to PR 3, because a module test needs the module's tables; PR 3 also moves the database fixture into a shared place for the module tests. Valkey joins the AppHost with the rate limits that use it, and the EF Core claims interceptor with the first module that uses EF Core ([section 3](#3-foundations): each foundation arrives with the first PR that needs it).
- **Accept:**
  - A query without an identity fails with 42501.
  - No code path issues a session-level `SET`.
  - The RLS-coverage and role-grant tests pass in CI.

### PR 3: R1 schema and RLS

- **Scope:** the tables in [section 4](#4-r1-data-model), their policies and audit triggers, the `fitcore_catalog_import` role, and the first module test project, `FitCore.Modules.Nutrition.Tests`, with the database fixture moved to `FitCore.Testing` so every database test project shares it.
- **Accept:**
  - Six-actor pgTAP tests for every private table. Grantee actors run against `care.relationships` rows inserted by the test.
  - A cross-member read or write succeeds only through an active grant whose scope covers the data, and only as the [permission matrix](BRD.md#32-permission-matrix) allows. Other members, out-of-scope grantees and revoked grantees are denied.
  - A member's write to a shared catalog row is denied, and so is any anonymous access.
  - `fitcore_api` and `authenticated` can't write shared catalog rows.
  - `fitcore_catalog_import` can insert and update only shared catalog rows. It can't select, insert as, update or delete members' custom foods or servings, and it can't read or write any member schema.

### PR 4: Sign-in, profile and app shells

- **Scope:** Platform authentication, the web session cookie, sign-in rate limits, the Identity module (profile and the 18+ gate), the `FitCore.Maui` project, and both UI shells with all four sign-in methods.
- **Accept:**
  - Sign up, sign in and sign out on the web and on Android in CI. Signing out clears every cached member record.
  - A user under 18 can't complete sign-up.
  - The native smoke checklist (sign-in redirect, session survives an app kill, tokens in the platform secure store, offline save error) passes on a physical iPhone and Android device and on Windows and macOS. The on-device Apple test from the auth spike runs here.
  - A cross-site request that changes state through the web session is rejected.

### PR 5: Targets

- **Scope:** the Targets module and screens on both UIs: estimate, weekly rate or goal date, manual override, soft-limit warning and acknowledgement, hard-floor block.
- **Accept:**
  - An override past a soft limit needs the acknowledgement round trip and writes an audit event.
  - A member can't save a target past the hard floor.
  - The newest accepted target wins. A member can replace it with a manual target, unless a clinician set it below the hard floor; ending that one takes the member revoking the grant or the clinician changing it ([BRD §6.2](BRD.md#62-targets-and-safety) precedence).

### PR 6: Custom foods, servings and prices

- **Scope:** custom foods and servings in Catalog, prices in Nutrition, screens on both UIs.
- **Accept:**
  - A per-100 g food with a "1 slice" serving and a price can be created.
  - A custom food without kcal or any macro is rejected.

### PR 7: USDA foods

- **Scope:** a worker job that loads the USDA FoodData Central bulk download into `catalog` as `fitcore_catalog_import`, full-text and trigram search, the food search screens and the "Data: USDA FoodData Central" attribution. Every USDA call has a timeout, retry and circuit breaker, and search reads only the local catalog.
- **Accept:**
  - First results within NFR-PRF-3 (p95 ≤ 800 ms) on the staging database.
  - Re-running the import creates no duplicate rows.
  - Search is limited to 60 requests per minute per member (NFR-SCL-5).
  - A member can price a USDA food without changing the shared row.
  - A fault-injection test with USDA unreachable: the import fails cleanly and retries later, while search over imported foods, custom foods and diary logging keep working (NFR-AVL-6).

### PR 8: Diary, Today and weight

- **Scope:** diary entries with snapshots, marking a day complete, the Today screen (calories, macros, estimated food cost, incomplete and unpriced flags), weight logging, and the Lighthouse and Playwright tap-test CI jobs.
- **Accept:**
  - The full R1 journey passes as an end-to-end test on the web: sign up → profile → accept or override targets → custom food with a price → USDA food with a price → log both in g, ml and servings → Today matches hand-computed totals and flags → log weight → a second member sees none of it.
  - The same journey passes on iOS, Android, Windows and macOS, with a recorded result for each platform: automated where the platform allows, otherwise run by hand from a checklist.
  - Today meets NFR-PRF-5, and lab Core Web Vitals meet NFR-PRF-8.
  - A failed save keeps the form and offers retry (NFR-REL-1).

### PR 9: Beta environments and release

- **Scope:**
  - Infrastructure as code for ECS Fargate, the load balancer, Valkey, S3 and secrets in `us-west-2`.
  - The production Supabase project, the build-once deploy pipeline with rollback (NFR-OPS-6), daily backups (NFR-AVL-3), error tracking, uptime checks and billing alerts (NFR-CST-2).
  - Real-user Core Web Vitals reporting, and tests of the start-up INP mitigations ([NFR open item 5](NFR.md#17-open-items)).
  - Store builds for testers: TestFlight, a Google Play internal track and sideloaded Windows and macOS builds.
  - `FitCore.Load.Tests` with a beta-sized load test against staging, including identity handling and pool sizing ([RLS spike §5](spikes/rls-connection-pooling.md#5-open-points)).
- **Accept:**
  - A deploy and a rollback both run from CI, the rollback finishes within 30 minutes (NFR-OPS-6), and a restore from backup has been rehearsed once.
  - API reads meet NFR-PRF-1 (p95 ≤ 300 ms) and writes meet NFR-PRF-2 (p95 ≤ 500 ms) under the beta-sized load test.

## 6. R1 exit criteria

- The R1 parts of [BRD §4.3](BRD.md#43-key-user-journeys) journeys 1 and 2 work on the web, iOS, Android, Windows and macOS. Barcode, AI, saved meals, workouts, wellness and ED screening come later.
- Every NFR tagged Beta is met or has a dated exception.
- pgTAP proves no member can see or change another member's data.

## 7. Later phases

Each phase starts with its prerequisites and ends when its NFRs are met. Each gets its own detailed plan before it starts.

### 7.1 Public member app

- **Prerequisites:** the cost model ([NFR open item 2](NFR.md#17-open-items)), legal and FDA review, the Open Food Facts licensing review, and the commercial food database and AI vendor choices ([BRD §10](BRD.md#10-open-questions-and-decisions-needed)).
- **Work:**
  - Sync module: offline outbox, `POST /v1/sync`, conflicts (NFR-REL-2, NFR-REL-3, NFR-PRF-10).
  - Training module: exercise library, templates, logging, PRs, programs, calendar.
  - Nutrition: recipes, saved meals, meal planner, grocery list, purchases and budget.
  - Barcode scanning and the extra food sources.
  - Progress: measurements, private photos with S3 backup, wellness, adaptive targets.
  - Integrations: HealthKit, Health Connect, wearables, AI logging with quotas (NFR-CST-3), push and email.
  - Safety: ED screening, hide-numbers mode (NFR-ACC-5) and support resources.
  - DataRights: export, import and deletion (NFR-DAT-1, NFR-DAT-2).
  - Admin tooling with MFA, consent records, the status page and on-call.
- **Gates:** the load test at 3× peak (NFR-SCL-1), a pen test (NFR-SEC-11) and a restore drill (NFR-AVL-5).

### 7.2 Coach/Pro tier

- **Work:**
  - Care module: invites, grants and scopes in the UI, roster, plan and target assignment, comments, check-ins.
  - Messaging over SignalR with the Valkey backplane.
  - Grant leases and cache purge on revocation (NFR-PRV-3, NFR-PRV-4).
  - The member-visible audit log (NFR-AUD-4).
  - Stripe Connect coach billing (NFR-REL-9).
- **Gates:** six-actor pgTAP for every grant path, and a pen test.

### 7.3 Clinic phase

- **Work:** the clinic cell (a second deployment on a Supabase project and compute covered by BAAs), clinician credential verification, and clinician-only targets past the hard floor at `aal2`.

## 8. Decisions still needed

| Decision | Needed by | Notes |
|---|---|---|
| ADR 0004: hosting region and infrastructure-as-code tool | PR 9 | The region is `us-west-2`, fixed by the Supabase project ([§B.6](architecture-options.md#b6-deployment-model)). The tool is open |
| ADR 0005: web session design | PR 4 | Accepted on 2026-10-16: [ADR 0005](decisions/0005-web-session.md). The access-token lifetime and the session time box or inactivity timeout it asks for are set with the auth settings below |
| Secret store | PR 9 | AWS Secrets Manager or SSM Parameter Store |
| Error-tracking vendor | PR 9 | Must meet NFR-PRV-6 scrubbing |
| Transactional-email vendor | Decided 2026-10-16 | Amazon SES in `us-west-2`, as Supabase Auth's custom SMTP: the cheapest option, on the existing AWS account, and HIPAA-eligible under the AWS BAA for the Clinic phase (NFR-PRV-10). Open and click tracking stay off, and emails carry no health data (NFR-PRV-6). Request production access early, since new SES accounts start in a sandbox. Until then, development uses the local stack's mail catcher |
| Supabase plan and connection mode | PR 9 | Pro for production: PITR, leaked-password protection and the session time-box and inactivity timeout all need it. The auth settings were decided on 2026-10-16 and are listed in [`supabase/README.md`](../supabase/README.md#auth-settings). Choose Supavisor session mode or the IPv4 add-on. PR 2's unit of work accepts either and refuses transaction mode, and development uses session mode, which works over IPv4 ([`supabase/README.md`](../supabase/README.md#hosted-projects)) |
| Sign in with Apple on Android, Windows and web | Decided 2026-10-16 | Offered on every platform, so a member who signed up on iOS with Hide My Email can still sign in elsewhere. The web flow's client secret expires every 6 months and must be renewed from the signing key ([`supabase/README.md`](../supabase/README.md#auth-settings)) |
| Open items carried over | Various | [NFR §17](NFR.md#17-open-items) items 1–5 and [BRD §10](BRD.md#10-open-questions-and-decisions-needed) |

## 9. Inputs from the product owner

| Input | Status |
|---|---|
| Supabase organization and development project | Provided: `FitCore-Dev-DB` in `Fibonacci-0112`. Keep "Enable automatic RLS" off, as [section 3.1](#31-database) explains |
| USDA FoodData Central API key | Provided. Stored as the setting `Usda:ApiKey`: user secrets locally, the GitHub Actions secret `USDA_API_KEY` in CI, the secret store once deployed. Never committed. The R1 import uses the bulk download, which needs no key; the key is for single-food lookups |
| Supabase production project | Needed by PR 9 |
| AWS account | Needed by PR 9 |
| Google, Microsoft and Apple sign-in apps (Apple needs a developer account and a `.p8` key) | Needed by PR 4 |
| Test devices: an iPhone, an Android phone, and Windows and macOS machines | Needed by PR 4 |
