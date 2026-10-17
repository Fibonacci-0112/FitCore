# CLAUDE.md

Guidance for Claude Code in this repository. Read the BRD and the NFRs before any non-trivial change. They are the source of truth for scope, roles, permissions, business rules, targets and release phases.

## Project

FitCore is a health, fitness and nutrition app. Members plan targets and track nutrition (including estimated food cost), training, body metrics and wellness, with optional support from a coach or clinician. It's multi-user from day one, and every member's data is private by default.

The repository holds the requirements and architecture documents, throwaway spike code under `spikes/`, and the start of the application: the solution scaffold from [`architecture-options.md` §B.2](docs/architecture-options.md#b2-project-structure-and-boundaries). The projects build and the tests run. `FitCore.Domain` holds the BRD §6 rules, `supabase/` holds the R1 tables with their RLS policies, audit triggers and pgTAP tests, and `FitCore.Platform` holds the unit of work and member authentication. The first feature is email sign-up, sign-in and sign-out on the web, with the 18+ gate (R1 PR 4, first part), and the MAUI app's shell with email sign-up (second part).

| Path | Contents |
|---|---|
| `README.md` | Product overview, summarized from the BRD |
| `docs/BRD.md` | Business requirements: roles, permission matrix, features, business rules, compliance |
| `docs/NFR.md` | Measurable non-functional requirements, each with an ID such as `NFR-PRV-4` |
| `docs/architecture-options.md` | Three architecture options compared against the NFRs, with a cost check |
| `docs/implementation-plan.md` | Build order: the R1 PRs with acceptance criteria, later phases, open decisions |
| `docs/decisions/` | Architecture decision records (ADRs), numbered `NNNN-title.md` |
| `docs/spikes/` | Spike findings, one file per spike |
| `docs/diagrams/` | Excalidraw diagrams of the ADR 0003 design (architecture, target workflow, data flow), listed in its README. Update a diagram when the design it shows changes |
| `spikes/` | Throwaway spike code (.NET 10, Docker Compose or the Supabase CLI, Node for browser checks). Each spike has a README with run instructions. Not production code |
| `supabase/` | The only source of schema: `config.toml` for the local stack, forward-only `migrations/` (the foundation, then one per module for the R1 tables), the local-only `seed.sql`, Auth's email `templates/`, and the pgTAP suite in `tests/` with the six-actor helper. See its README |
| `FitCore.slnx` | The solution: everything under `src/` and `tests/` |
| `src/FitCore.Domain/` | Shared rules (BRD §6): quantities and units, nutrient totals, money and estimated cost, diary snapshots, local dates, targets and guardrails. No I/O and no references |
| `src/FitCore.Contracts/` | Versioned DTOs and the typed API client (`/v1`) |
| `src/FitCore.Client.Core/` | View models, offline store and sync engine, shared by the MAUI app and the Blazor client. `Resources/` holds the text both UIs show (NFR-L10N-2); `Accounts/` holds the sign-up view model and the `IMemberAccounts` interface |
| `src/FitCore.Client.Auth/` | Supabase Auth for the device apps: `Supabase.Gotrue` behind a thin wrapper, called with the project's publishable key. Only `FitCore.Maui` references it; the browser never gets it (ADR 0005) |
| `src/FitCore.AppHost/` | .NET Aspire AppHost for local development: starts the Supabase CLI stack, then `fitcore-app` and `fitcore-worker`. Never deployed |
| `src/FitCore.Platform/` | Cross-cutting server code: the `IModule` host, the database unit of work (`Data/`, the only way to run SQL as a caller), and member authentication (`Auth/`: the Supabase Auth client, token validation, the session cookie and the cross-site write checks of ADR 0005). Rate limits and redacted logging come later |
| `src/FitCore.Web/` | `fitcore-app`: Blazor Web App host, `/v1` API and `/health` |
| `src/FitCore.Web.Client/` | Blazor WebAssembly pages, prerendered by `FitCore.Web` |
| `src/FitCore.Maui/` | The .NET MAUI XAML app for Android, iOS, macOS and Windows: the shell and email sign-up so far. The Supabase URL and publishable key are build properties that default to the local stack |
| `src/FitCore.Worker/` | `fitcore-worker`: background host |
| `src/Modules/` | One project per module in ADR 0003, each with a `<Name>Module` entry point. `Modules.targets` and `ModuleCatalog.cs` load every module into both hosts |
| `tests/FitCore.Architecture.Tests/` | Module boundaries and project layering (NFR-MNT-4), including that browser code never reaches `Supabase.Gotrue`, and no hard-coded text in Razor, XAML or validation messages (NFR-L10N-2) |
| `tests/FitCore.Client.Tests/` | The sign-up view model, and `FitCore.Client.Auth` against the local Supabase stack with the publishable key: the profile, the 18+ gate, Auth's own refusals and the confirmation email |
| `tests/FitCore.Domain.Tests/` | Table tests for every BRD §6 rule in `FitCore.Domain`, with a 90% line-coverage gate (NFR-MNT-1) |
| `tests/coverage-gate.py` | Fails CI when a package's line coverage in a Cobertura report is below a minimum |
| `tests/FitCore.Testing/` | Shared fixture for the database test projects: the local Supabase database, the API's `FitCoreDatabase`, and test members that are deleted afterwards |
| `tests/FitCore.Platform.Tests/` | The unit of work against the local Supabase database: no identity fails with 42501, RLS applies to the caller, and no identity outlives its transaction |
| `tests/FitCore.Modules.<Name>.Tests/` | A module's schema and code against the local Supabase stack, through the real unit of work. `FitCore.Modules.Identity.Tests` drives sign-up, the 18+ gate, sign-in and sign-out through fitcore-app against the local Auth and its mail catcher |
| `tests/FitCore.Web.Tests/` | The web host's pipeline, in memory: API errors as problem details, `no-store` pages, prerendering, and the web session against a fake Supabase Auth (cookie, refresh, outages, cross-site writes, sign-out) |
| `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props` | Shared build settings (warnings are errors), the generated class for each `Resources/*.resx`, and central package versions. `spikes/` opts out of all three |
| `.github/workflows/ci.yml` | CI: Markdown lint, link check, solution format check, build (with the MAUI app for Android) and test (against a local Supabase database), pgTAP, and spike build |
| `.github/skills/code-review/SKILL.md` | GitHub Copilot code-review skill: checklists drawn from the BRD, NFRs, ADRs and spike rules. Update it when those rules change. It is also the checklist for the Claude Code and Codex skills below |
| `.claude/skills/fitcore-review/SKILL.md` | Claude Code review skill (`/fitcore-review`): runs the Copilot skill's checklist with local checks and verified findings |
| `.agents/skills/fitcore-review/SKILL.md` | Codex review skill (`$fitcore-review`): the same runner for Codex |
| `.markdownlint-cli2.jsonc` | Markdown lint configuration (MD013 line length is off) |

## Commands

CI lints every Markdown file and checks every link, including links to headings. Run the same checks before declaring a task done:

```sh
npx --yes markdownlint-cli2@0.18.1 "**/*.md"   # the version CI's action uses
lychee --include-fragments '**/*.md' \
  --remap 'https://github\.com/([^/]+)/([^/]+)/blob/(.+) https://raw.githubusercontent.com/$1/$2/$3'   # https://lychee.cli.rs, as CI runs it
```

Use the pinned linter version: newer markdownlint releases add rules (such as MD060) that CI doesn't run, and that flag the existing files.

The solution needs the .NET 10 SDK (`global.json`) and the MAUI Android workload, which builds on any OS. Android also needs JDK 17 or later and the Android SDK; iOS and macOS build only on a Mac, and Windows only on Windows. `FitCore.Platform.Tests`, `FitCore.Client.Tests`, the module test projects and the pgTAP suite also need the local Supabase stack, which needs Docker. CI runs these, and a PR must pass them:

```sh
dotnet workload install maui-android             # once per SDK; FitCore.Maui is in the solution
dotnet build src/FitCore.Maui -t:InstallAndroidDependencies -f net10.0-android \
  -p:AndroidSdkDirectory=$HOME/android-sdk -p:AcceptAndroidSDKLicenses=True   # once, if you have no Android SDK yet; then set ANDROID_HOME to it
npx --yes supabase@2.121.0 start                 # the local stack: Postgres on 54322, Auth on 54321, mail on 54324
export Supabase__SecretKey=$(npx --yes supabase@2.121.0 status -o env | sed -n 's/^SECRET_KEY="\(.*\)"$/\1/p')   # fitcore-app's Auth key, never committed
npx --yes supabase@2.121.0 test db               # the pgTAP suite (NFR-MNT-2)
dotnet format FitCore.slnx --verify-no-changes   # formatting and code style (.editorconfig)
dotnet build FitCore.slnx                        # warnings are errors
dotnet test --solution FitCore.slnx              # Microsoft.Testing.Platform runner, set in global.json
dotnet test --project tests/FitCore.Domain.Tests --coverage --coverage-output-format cobertura \
  --coverage-output domain.cobertura.xml --results-directory TestResults
python3 tests/coverage-gate.py TestResults/domain.cobertura.xml FitCore.Domain 90   # NFR-MNT-1
dotnet run --project src/FitCore.Web             # the web app and API, https://localhost:7180
dotnet run --project src/FitCore.AppHost         # or everything: the Supabase stack, the app and the worker
dotnet build src/FitCore.Maui -t:Run -f net10.0-android   # the device app on a running emulator, against the local stack
```

Debug builds of `FitCore.Maui` sign members in against the local stack. Any other configuration, including a Release build of the solution, must pass `-p:SupabaseUrl=…` and `-p:SupabasePublishableKey=…`, or the build fails, and a non-Debug app refuses to start unless the URL is HTTPS (NFR-SEC-6). Never pass a secret key to a client build.

After changing a migration or `seed.sql`, run `npx --yes supabase@2.121.0 db reset`. Use the same pinned CLI version as CI.

Spike code also needs the .NET 10 SDK. CI builds it. Running the RLS and auth spikes needs Docker; the Blazor spike needs Node 22 and a Chromium build instead (see each README):

```sh
dotnet build spikes/rls-pooling/RlsSpike
dotnet build spikes/auth-mfa/AuthSpike
dotnet build spikes/blazor-web-vitals/BlazorSpike
```

## Platform and architecture

- **Clients:** .NET MAUI apps with native XAML UI on iOS, Android, Windows and macOS, and a Blazor Web App on the web ([BRD §5](docs/BRD.md#5-platform-and-technology-constraints)). The two UIs are built separately and share view models, service clients and the domain library. Every feature ships on both (NFR-CMP-3).
- **Domain logic:** one shared C# library for units, nutrition totals, estimated cost, targets and guardrails, with table tests and at least 90% coverage (NFR-MNT-1). Business logic never lives in UI code (NFR-MNT-4).
- **Backend:** a modular monolith on ASP.NET Core (.NET 10 LTS), with Supabase for Postgres, Auth, Storage and RLS. This is **accepted** in [ADR 0003](docs/decisions/0003-modular-monolith.md) (2026-10-13). Its follow-up spikes come before the solution scaffold.
- **Database identity:** each unit of work applies the caller's identity with `SET LOCAL ROLE authenticated` and `set_config('request.jwt.claims', …, true)` only, never session-level `SET`. The API connects through Supavisor session mode or directly, not transaction mode ([spike findings](docs/spikes/rls-connection-pooling.md#4-rules-for-the-api)).
- **Web rendering:** every Blazor page prerenders on the server and hydrates with WebAssembly, with prerendered data carried by `[PersistentState]`. No WebAssembly-only routes, no Interactive Server or Auto. View-model commands `await Task.Yield()` after the optimistic update and before calling the API ([spike findings](docs/spikes/blazor-prerendering-web-vitals.md#5-rules-for-the-build)).
- **Sign-in:** email/password, Google, Microsoft and Sign in with Apple, through Supabase Auth, every method on every platform. Hosted Auth settings are in [`supabase/README.md`](supabase/README.md#auth-settings). The MAUI app calls Supabase Auth itself through `FitCore.Client.Auth`, with the project's publishable key; the web signs in on the server (below).
- **Web session:** the server signs the member in and keeps the Supabase session in an encrypted `__Host-` cookie (HttpOnly, Secure, `SameSite=Lax`). No token reaches WebAssembly, JavaScript or browser storage. Bearer (MAUI) and cookie (web) callers get the same identity and RLS claims. Every cookie-authenticated `/v1` write needs an antiforgery header and a same-origin `Origin`; `GET` never changes state, and `/v1` sends no CORS headers ([ADR 0005](docs/decisions/0005-web-session.md)).
- **MFA:** TOTP, offered to everyone and required for clinicians and admins. "Required" is enforced in the API and in RLS by checking `aal2`, since Supabase has no per-role switch. Passkey sign-in counts as a single factor ([spike findings](docs/spikes/apple-sign-in-and-mfa.md#4-rules-for-the-build)).

### Solution rules

- **Modules** expose only their `<Name>Module` class (an `IModule`) and types in the `FitCore.Modules.<Name>.Contracts` namespace. Everything else is `internal`. `InternalsVisibleTo` is allowed only for the module's own test project. The architecture tests enforce this.
- **A new module** needs an ADR change first, then a project under `src/Modules/` and an entry in `ModuleCatalog.cs`. Its schema is its name in snake case.
- **Client projects** (`FitCore.Domain`, `FitCore.Contracts`, `FitCore.Client.Core`, `FitCore.Client.Auth`, `FitCore.Web.Client`, `FitCore.Maui`) never reference modules, `FitCore.Platform`, the hosts or Npgsql.
- **`Supabase.Gotrue`** lives only in `FitCore.Client.Auth`, which only `FitCore.Maui` references. Nothing the Blazor client is built from may reach either (ADR 0005).
- **Device app XAML** binds with `x:DataType`, and takes its text from the same resource classes with `x:Static`.
- **Package versions** go in `Directory.Packages.props`, never in a project file.
- **User-visible text** goes in a project's `Resources/*.resx`, never in Razor markup, XAML, C# or validation attributes (NFR-L10N-2). Text both UIs show lives in `FitCore.Client.Core/Resources`; use the generated class (`AccountStrings.SignInButton`, or `{x:Static strings:AccountStrings.SignUpTitle}` in XAML), `ErrorMessageResourceType` on validation attributes, and `LinkedText` for a sentence with a link in it. An architecture test fails on text written into Razor or XAML.
- **Database access** goes through `FitCoreDatabase` in `FitCore.Platform`: a `UnitOfWork` for writes and multi-statement reads, `ReadAsync` for single-trip reads. Never set the role or claims yourself; an architecture test fails on any session-level `SET ROLE` or `set_config(…, false)` under `src/` or `supabase/`.

When code is added, keep this file's layout and commands current, and extend CI to run any new checks.

## Business rules (do not break)

Summarized from [BRD §6](docs/BRD.md#6-business-rules). Follow them in domain logic, SQL and UI.

- **Unknown is not zero.** Missing nutrient values stay `null`, never `0`. Totals show as a lower bound with a count of incomplete items.
- **Days without a diary record are unknown**, never zero intake. Only days marked complete feed adaptive targets.
- **Money** is integer minor units plus an ISO 4217 code. Never use floating-point money.
- **"Estimated food cost"** is consumption cost, always labelled as an estimate and never as "spend". Actual spend comes only from purchase records.
- **Units:** `g` and `ml` are canonical. Standard mass and volume units (mg, kg, oz, lb; l, tsp, tbsp, fl oz, cup, pint, quart, gallon) convert exactly to them; a cup is the US customary 236.6 ml. Food-specific measures ("1 slice", "1 cup, chopped = 128 g") are servings. Mass↔volume conversion needs a known density. Reject an impossible conversion; never guess.
- **Snapshots:** a log entry keeps its nutrients and cost from log time. Editing a food or price never rewrites history.
- **Dates:** diary dates are the member's local calendar date.
- **Targets:** Mifflin-St Jeor × activity factor. Soft limits (loss ≤ 1% of body weight per week, ≥ 1,200 kcal/day, protein 1.6 g/kg by default) can be overridden by the member after a warning, and the override is logged. The hard floor (initially < 800 kcal/day or a goal BMI < 18.5) blocks members and coaches; only a clinician can go past it, and the member is told. A coach target below a soft limit stays pending until the member accepts it.
- **Safety:** 18+ only. AI food estimates are saved only after the member confirms them. The app never generates medical advice. Adaptive targets are never applied without the member accepting them.

## Privacy and security rules

- **RLS on every private table**, with pgTAP tests for each actor: owner, other member, grantee in scope, grantee out of scope, revoked grantee and admin (NFR-PRV-1, NFR-PRV-2). Grant-aware policies call `care.has_grant(member_id, scope)`; in R1 the `nutrition` scope covers the diary and targets, and `body_metrics` covers weigh-ins.
- **Shared catalog rows** (`owner_id` null) are written only by the `fitcore_catalog_import` login role, never by `fitcore_api` or `authenticated`.
- **Professionals reach member data only through active grants**, exactly as the [permission matrix](docs/BRD.md#32-permission-matrix) says. They write only targets, assigned plans, comments, messages and check-in responses. Revocation takes effect on the next request (NFR-PRV-4).
- **Audit** every grant change, professional edit, guardrail override and admin action in an append-only log (NFR-AUD-1..3).
- **Admins never read member diaries**, measurements, photos, wellness data or messages (NFR-PRV-5).
- **No health data** in analytics events, logs, crash reports, error messages or push payloads (NFR-PRV-6).
- **Secrets** live only server-side. Never commit them.
- Show the attribution "Data: USDA FoodData Central" wherever USDA foods appear.

## Conventions

- Write documentation in plain, direct US English, with one paragraph or table row per line.
- Cite requirements by section (`BRD §6.2`) or ID (`NFR-SCL-5`) so changes can be traced.
- Heading text must be unique within a file (markdownlint MD024). Prefix repeated subsections, for example `A.1`, `B.1`.
- Link to removed files at a commit permalink, never with a dead relative link.
- Record architecture decisions as new ADRs in `docs/decisions/` (NFR-MNT-5). Continue the numbering: 0001 and 0002 exist in git history.
- When a change contradicts the BRD, the NFRs or an accepted ADR, update that document in the same PR or raise an open question. Don't diverge silently.
- Keep PRs small and scoped to one roadmap item. Don't build features the BRD places in a later phase than the one being worked on.
