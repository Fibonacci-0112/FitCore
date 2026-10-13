# CLAUDE.md

Guidance for Claude Code in this repository. Read the BRD and the NFRs before any non-trivial change. They are the source of truth for scope, roles, permissions, business rules, targets and release phases.

## Overview

FitCore is a health, fitness and nutrition app. Members plan targets and track nutrition (including estimated food cost), training, body metrics and wellness, with optional support from a coach or clinician. It's multi-user from day one, and every member's data is private by default.

The backend is a **modular monolith Web API** on ASP.NET Core, serving .NET MAUI XAML apps and a Blazor Web App. This is **proposed** in [ADR 0003](docs/decisions/0003-modular-monolith.md) and isn't yet accepted, so confirm with the product owner before scaffolding code.

The repository is at the requirements and architecture stage. It holds documentation only, and no application code exists yet.

| Path | Contents |
|---|---|
| `README.md` | Product overview, summarized from the BRD |
| `docs/BRD.md` | Business requirements: roles, permission matrix, features, business rules, compliance |
| `docs/NFR.md` | Measurable non-functional requirements, each with an ID such as `NFR-PRV-4` |
| `docs/architecture-options.md` | Three architecture options compared against the NFRs, with a cost check |
| `docs/decisions/` | Architecture decision records (ADRs), numbered `NNNN-title.md` |
| `.github/workflows/ci.yml` | CI: Markdown lint and link check |
| `.markdownlint-cli2.jsonc` | Markdown lint configuration (MD013 line length is off) |

## Commands

CI lints every Markdown file and checks every link, including links to headings. Run the same checks before declaring a task done:

```sh
npx --yes markdownlint-cli2@0.18.1 "**/*.md"   # the version CI's action uses
lychee --include-fragments '**/*.md'           # https://lychee.cli.rs
```

Use the pinned linter version: newer markdownlint releases add rules (such as MD060) that CI doesn't run, and that flag the existing files.

When code is added, add the commands to build, test and lint it here, and extend CI to run them (NFR-MNT-3).

## Tech Stack

- **Clients:** .NET MAUI apps with native XAML UI on iOS, Android, Windows and macOS, and a Blazor Web App (static SSR and prerendering, then WebAssembly; no Interactive Server mode) on the web ([BRD §5](docs/BRD.md#5-platform-and-technology-constraints), NFR-PRF-8, NFR-SCL-2). The two UIs share view models, service clients and the domain library. Every feature ships on both (NFR-CMP-3).
- **Server:** .NET 10 LTS, ASP.NET Core minimal APIs, REST versioned under `/v1` (NFR-CMP-5).
- **Data:** Supabase Postgres, Auth and Storage. EF Core 10 with Npgsql and snake_case naming, one schema per module. The schema itself comes only from Supabase CLI SQL migrations, never EF migrations (see [Database](#database)).
- **Validation:** FluentValidation, one validator per use case.
- **Errors:** `Result<T>` for business outcomes, mapped to `ProblemDetails`. No exceptions for business logic.
- **Authentication:** Supabase Auth issues the tokens (email/password, Google, Microsoft, Sign in with Apple; access tokens ≤ 1 hour, rotating refresh tokens, NFR-SEC-4). The API only validates the JWTs against the project's JWKS. It never issues its own tokens or refresh tokens.
- **Observability:** OpenTelemetry traces, metrics and structured logs through `ILogger`, with a correlation ID per request. A redaction allow-list runs before anything leaves the process (NFR-OPS-1, NFR-PRV-6).
- **API documentation:** OpenAPI documents from `Microsoft.AspNetCore.OpenApi`.
- **Resilience:** `Microsoft.Extensions.Http.Resilience` (timeout, retry, circuit breaker) on every outbound call (NFR-AVL-6).
- **Shared state:** Valkey for rate-limit and AI-quota counters and the SignalR backplane (NFR-SCL-5, NFR-CST-3).
- **Containers:** one Docker image, run as two process types, `fitcore-app` and `fitcore-worker`, on AWS ECS Fargate in the Supabase project's region. Local development uses .NET Aspire and the Supabase CLI.

## Architecture

- **Modular monolith with vertical slices.** Modules: Identity, Catalog, Nutrition, Training, Progress, Targets, Care, Sync, Billing, Integrations, DataRights and Audit ([architecture-options.md §B.2](docs/architecture-options.md#b2-project-structure-and-boundaries)).
- **One project per module**, split into `Domain/`, `Features/`, `Infrastructure/` and `PublicApi/` folders. Architecture tests enforce the boundaries between folders and modules.
- **Clean dependencies:** a module's `Domain/` has no I/O and no reference to EF Core or ASP.NET Core. Shared rules (units, totals, cost, targets, guardrails) live in `FitCore.Domain`, which also runs on devices for previews (NFR-MNT-1, NFR-MNT-4).
- **Module isolation:** a module reads and writes only its own schema, and calls another module only through that module's `PublicApi/`, in process. Cross-module side effects go through the transactional outbox, which the worker dispatches.
- **CQRS with plain handlers:** one handler class per command or query, with no mediator and no handler interfaces beyond the `IHandler` marker.
- **Manual mapping** with extension methods between module entities and `FitCore.Contracts` DTOs.
- **The API is the only way in.** The Supabase Data API is off for module schemas.

## Project Structure

- `src/FitCore.Domain/`: shared rules with no I/O, plus `Result<T>` and the error types. Used by clients and the server.
- `src/FitCore.Contracts/`: versioned `/v1` request and response DTOs and the typed API client, shared by both UIs.
- `src/FitCore.Client.Core/`: view models (MVVM), encrypted offline store, sync engine.
- `src/FitCore.Maui/`: XAML app.
- `src/FitCore.Web/`: Blazor Web App host. Also hosts the API: `Program.cs` and DI composition.
- `src/FitCore.Web.Client/`: Blazor WebAssembly interactive components.
- `src/FitCore.Worker/`: background host for the outbox dispatcher, export, imports, sync and photo backup.
- `src/FitCore.Platform/`: cross-cutting code: `IHandler`, `IApiEndpoint`, endpoint and handler registration, `Result<T>` to `ProblemDetails` mapping, JWT validation, the per-request RLS session, rate limits, redacted logging, resilience.
- `src/Modules/FitCore.Modules.<Name>/`:
  - `Domain/`: entities, value objects, enums for this module.
  - `Features/`: endpoints, handlers, validators and mappings, one folder per use case.
  - `Infrastructure/`: the module's `DbContext` and EF Core configurations. No migrations.
  - `PublicApi/`: the contracts other modules may use, and integration events.
- `supabase/migrations/`: SQL migrations, forward-only and expand-then-contract (NFR-OPS-6).
- `supabase/tests/`: pgTAP tests.
- `tests/`: see [Testing](#testing).

## File Naming

Inside each module project:

- `Features/<UseCase>/<UseCase>.Endpoint.cs`
- `Features/<UseCase>/<UseCase>.Handler.cs`
- `Features/<UseCase>/<UseCase>.Validator.cs`
- `Features/<UseCase>/<UseCase>.Mapping.cs` (only when the use case has two or more mappings)
- `Features/Shared/Routes/RouteConsts.cs`: every route for the module, under `/v1`
- `Features/Shared/Errors/<Module>Errors.cs`: error codes such as `target.below_soft_limit`

Request and response DTOs live in `FitCore.Contracts`, grouped by module, because the clients use them too.

## Code Conventions

- Positional records for request and response DTOs.
- File-scoped namespaces.
- Primary constructors for dependency injection.
- Sealed classes for implementations.
- `internal` by default. Only `PublicApi/` types, `FitCore.Contracts` and `FitCore.Domain` are public.
- Money is a value type of `long` minor units plus an ISO 4217 code. Never `double`, `float` or `decimal` money (NFR-REL-6).
- Unknown nutrient values are nullable, never defaulted to `0` (NFR-REL-5).
- User-visible strings go in resource files (NFR-L10N-2).

## Patterns We Use

- `Result<T>` as the return type of every handler. Endpoints map failures to `ProblemDetails`, for example `422` with `target.below_soft_limit` and a warning ID, and `403` for the hard floor ([architecture-options.md §B.4](docs/architecture-options.md#b4-api-design)).
- The `IHandler` marker interface for auto-registration.
- `IApiEndpoint` for endpoint registration.
- `RouteConsts` for centralized route definitions.
- A FluentValidation validator per use case.
- The module's EF Core `DbContext` directly in handlers.
- Pagination with a maximum page size on every list endpoint (NFR-SCL-3).
- Idempotency keys: client UUIDs for synced records, and an inbox table with unique event IDs for webhooks (NFR-REL-3, NFR-REL-9).

## Patterns We Do NOT Use

- The repository pattern.
- AutoMapper or any other mapping library.
- MediatR or any other mediator library.
- Exceptions for business logic flow.
- The `[FromServices]` attribute.
- EF Core migrations, or `EnsureCreated`.
- Business logic in XAML pages, Razor components or view models (NFR-MNT-4).
- Blazor Interactive Server mode, or any other instance-local state (NFR-SCL-2).

## Database

- One Supabase Postgres database with one schema per module, named in snake case: `identity`, `catalog`, `nutrition`, `training`, `progress`, `targets`, `care`, `sync`, `billing`, `integrations`, `data_rights`, `audit`.
- Each module's `DbContext` sets its default schema and uses snake_case naming, so the EF model matches the SQL migrations.
- Every schema change is a SQL migration under `supabase/migrations/` and ships with pgTAP tests (NFR-MNT-2).
- **RLS stays on for API traffic.** Each request runs in a transaction that switches to the `authenticated` role and sets `request.jwt.claims` from the caller's validated token with `set local`. `FitCore.Platform` provides this. Only catalog import, webhooks, deletion and export use privileged credentials.
- The ADR 0003 spike measures this per-request RLS setup through EF Core against NFR-PRF-1 and NFR-PRF-2.

## Testing

- **xUnit** as the test framework, with `[Theory]` table tests for every rule in [BRD §6](docs/BRD.md#6-business-rules).
- `tests/FitCore.Domain.Tests/`: at least 90% line coverage of `FitCore.Domain` (NFR-MNT-1).
- `tests/FitCore.Modules.<Name>.Tests/`: tests against the local Supabase stack (`supabase start`), with **Respawn** resetting data between tests.
- `tests/FitCore.Architecture.Tests/`: **NetArchTest.Rules** for module boundaries, folder dependencies and no business logic in UI projects.
- `tests/FitCore.Load.Tests/`: the 3× peak load test (NFR-SCL-1).
- `supabase/tests/`: pgTAP RLS tests for every private table and each actor (NFR-PRV-1, NFR-PRV-2).
- **Moq** to stub external integrations (USDA, AI provider, RevenueCat, Stripe, push, email). Don't mock the database.
- **Bogus** for test data. Generated data must keep the business rules: `null` for unknown nutrients, integer minor-unit money.
- Test names follow `Method_Scenario_ExpectedResult`.

## DI Registration

- Each module exposes `Add<Module>Module(this IServiceCollection services, IConfiguration configuration)`. `FitCore.Web` and `FitCore.Worker` call it.
- Handlers are scanned with `RegisterHandlersFromAssemblyContaining<T>()`.
- Validators are scanned with `AddValidatorsFromAssembly`.
- Endpoints are scanned with `RegisterApiEndpointsFromAssemblyContaining<T>()`, in `FitCore.Web` only.

## Business rules (do not break)

Summarized from [BRD §6](docs/BRD.md#6-business-rules). Follow them in domain logic, SQL and UI.

- **Unknown is not zero.** Missing nutrient values stay `null`, never `0`. Totals show as a lower bound with a count of incomplete items.
- **Days without a diary record are unknown**, never zero intake. Only days marked complete feed adaptive targets.
- **Money** is integer minor units plus an ISO 4217 code. Never use floating-point money.
- **"Estimated food cost"** is consumption cost, always labelled as an estimate and never as "spend". Actual spend comes only from purchase records.
- **Units:** `g` and `ml` are canonical; household measures are servings. Mass↔volume conversion needs a known density. Reject an impossible conversion; never guess.
- **Snapshots:** a log entry keeps its nutrients and cost from log time. Editing a food or price never rewrites history.
- **Dates:** diary dates are the member's local calendar date.
- **Targets:** Mifflin-St Jeor × activity factor. Soft limits (loss ≤ 1% of body weight per week, ≥ 1,200 kcal/day, protein 1.6 g/kg by default) can be overridden by the member after a warning, and the override is logged. The hard floor (initially < 800 kcal/day or a goal BMI < 18.5) blocks members and coaches; only a clinician can go past it, and the member is told. A coach target below a soft limit stays pending until the member accepts it.
- **Safety:** 18+ only. AI food estimates are saved only after the member confirms them. The app never generates medical advice. Adaptive targets are never applied without the member accepting them.

## Privacy and security rules

- **RLS on every private table**, with pgTAP tests for each actor: owner, other member, grantee in scope, grantee out of scope, revoked grantee and admin (NFR-PRV-1, NFR-PRV-2).
- **Professionals reach member data only through active grants**, exactly as the [permission matrix](docs/BRD.md#32-permission-matrix) says. They write only targets, assigned plans, comments, messages and check-in responses. Grant checks read `care.relationships` live, without caching, so revocation takes effect on the next request (NFR-PRV-4).
- **Audit** every grant change, professional edit, guardrail override and admin action in an append-only log (NFR-AUD-1..3).
- **Admins never read member diaries**, measurements, photos, wellness data or messages (NFR-PRV-5).
- **No health data** in analytics events, logs, traces, crash reports, error messages, `ProblemDetails` or push payloads (NFR-PRV-6).
- **Secrets** live only server-side. Never commit them (NFR-SEC-5).
- Show the attribution "Data: USDA FoodData Central" wherever USDA foods appear.

## Conventions

- Write documentation in plain, direct US English, with one paragraph or table row per line.
- Cite requirements by section (`BRD §6.2`) or ID (`NFR-SCL-5`) so changes can be traced.
- Heading text must be unique within a file (markdownlint MD024). Prefix repeated subsections, for example `A.1`, `B.1`.
- Link to removed files at a commit permalink, never with a dead relative link.
- Record architecture decisions as new ADRs in `docs/decisions/` (NFR-MNT-5). Continue the numbering: 0001 and 0002 exist in git history.
- When a change contradicts the BRD, the NFRs or an accepted ADR, update that document in the same PR or raise an open question. Don't diverge silently.
- Keep PRs small and scoped to one roadmap item. Don't build features the BRD places in a later phase than the one being worked on.
