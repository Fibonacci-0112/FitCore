# CLAUDE.md

Guidance for Claude Code in this repository. Read the BRD and the NFRs before any non-trivial change. They are the source of truth for scope, roles, permissions, business rules, targets and release phases.

## Project

FitCore is a health, fitness and nutrition app. Members plan targets and track nutrition (including estimated food cost), training, body metrics and wellness, with optional support from a coach or clinician. It's multi-user from day one, and every member's data is private by default.

The repository holds the requirements and architecture documents, throwaway spike code under `spikes/`, and the start of the application: the solution scaffold from [`architecture-options.md` §B.2](docs/architecture-options.md#b2-project-structure-and-boundaries). The projects build and the tests run. `FitCore.Domain` holds the BRD §6 rules, but no features exist yet.

| Path | Contents |
|---|---|
| `README.md` | Product overview, summarized from the BRD |
| `docs/BRD.md` | Business requirements: roles, permission matrix, features, business rules, compliance |
| `docs/NFR.md` | Measurable non-functional requirements, each with an ID such as `NFR-PRV-4` |
| `docs/architecture-options.md` | Three architecture options compared against the NFRs, with a cost check |
| `docs/decisions/` | Architecture decision records (ADRs), numbered `NNNN-title.md` |
| `docs/spikes/` | Spike findings, one file per spike |
| `spikes/` | Throwaway spike code (.NET 10, Docker Compose or the Supabase CLI, Node for browser checks). Each spike has a README with run instructions. Not production code |
| `FitCore.slnx` | The solution: everything under `src/` and `tests/` |
| `src/FitCore.Domain/` | Shared rules (BRD §6): quantities and units, nutrient totals, money and estimated cost, diary snapshots, local dates, targets and guardrails. No I/O and no references |
| `src/FitCore.Contracts/` | Versioned DTOs and the typed API client (`/v1`) |
| `src/FitCore.Client.Core/` | View models, offline store and sync engine, shared by the MAUI app and the Blazor client |
| `src/FitCore.Platform/` | Cross-cutting server code: the `IModule` host, and later auth, the RLS session, rate limits and redacted logging |
| `src/FitCore.Web/` | `fitcore-app`: Blazor Web App host, `/v1` API and `/health` |
| `src/FitCore.Web.Client/` | Blazor WebAssembly pages, prerendered by `FitCore.Web` |
| `src/FitCore.Worker/` | `fitcore-worker`: background host |
| `src/Modules/` | One project per module in ADR 0003, each with a `<Name>Module` entry point. `Modules.targets` and `ModuleCatalog.cs` load every module into both hosts |
| `tests/FitCore.Architecture.Tests/` | Module boundaries and project layering (NFR-MNT-4) |
| `tests/FitCore.Domain.Tests/` | Table tests for every BRD §6 rule in `FitCore.Domain`, with a 90% line-coverage gate (NFR-MNT-1) |
| `tests/coverage-gate.py` | Fails CI when a package's line coverage in a Cobertura report is below a minimum |
| `tests/FitCore.Web.Tests/` | The web host's pipeline, in memory: API errors as problem details, `no-store` pages, prerendering |
| `Directory.Build.props`, `Directory.Packages.props` | Shared build settings (warnings are errors) and central package versions. `spikes/` opts out of both |
| `.github/workflows/ci.yml` | CI: Markdown lint, link check, solution format check, build and test, and spike build |
| `.github/skills/code-review/SKILL.md` | GitHub Copilot code-review skill: checklists drawn from the BRD, NFRs, ADRs and spike rules. Update it when those rules change. It is also the checklist for the Claude Code and Codex skills below |
| `.claude/skills/fitcore-review/SKILL.md` | Claude Code review skill (`/fitcore-review`): runs the Copilot skill's checklist with local checks and verified findings |
| `.agents/skills/fitcore-review/SKILL.md` | Codex review skill (`$fitcore-review`): the same runner for Codex |
| `.markdownlint-cli2.jsonc` | Markdown lint configuration (MD013 line length is off) |

## Commands

CI lints every Markdown file and checks every link, including links to headings. Run the same checks before declaring a task done:

```sh
npx --yes markdownlint-cli2@0.18.1 "**/*.md"   # the version CI's action uses
lychee --include-fragments '**/*.md'           # https://lychee.cli.rs
```

Use the pinned linter version: newer markdownlint releases add rules (such as MD060) that CI doesn't run, and that flag the existing files.

The solution needs the .NET 10 SDK (`global.json`). CI runs these, and a PR must pass them:

```sh
dotnet format FitCore.slnx --verify-no-changes   # formatting and code style (.editorconfig)
dotnet build FitCore.slnx                        # warnings are errors
dotnet test --solution FitCore.slnx              # Microsoft.Testing.Platform runner, set in global.json
dotnet test --project tests/FitCore.Domain.Tests --coverage --coverage-output-format cobertura \
  --coverage-output domain.cobertura.xml --results-directory TestResults
python3 tests/coverage-gate.py TestResults/domain.cobertura.xml FitCore.Domain 90   # NFR-MNT-1
dotnet run --project src/FitCore.Web             # the web app and API, https://localhost:7180
```

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
- **Sign-in:** email/password, Google, Microsoft and Sign in with Apple, through Supabase Auth.
- **MFA:** TOTP, offered to everyone and required for clinicians and admins. "Required" is enforced in the API and in RLS by checking `aal2`, since Supabase has no per-role switch. Passkey sign-in counts as a single factor ([spike findings](docs/spikes/apple-sign-in-and-mfa.md#4-rules-for-the-build)).

### Solution rules

- **Modules** expose only their `<Name>Module` class (an `IModule`) and types in the `FitCore.Modules.<Name>.Contracts` namespace. Everything else is `internal`. `InternalsVisibleTo` is allowed only for the module's own test project. The architecture tests enforce this.
- **A new module** needs an ADR change first, then a project under `src/Modules/` and an entry in `ModuleCatalog.cs`. Its schema is its name in snake case.
- **Client projects** (`FitCore.Domain`, `FitCore.Contracts`, `FitCore.Client.Core`, `FitCore.Web.Client`) never reference modules, `FitCore.Platform`, the hosts or Npgsql.
- **Package versions** go in `Directory.Packages.props`, never in a project file.

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

- **RLS on every private table**, with pgTAP tests for each actor: owner, other member, grantee in scope, grantee out of scope, revoked grantee and admin (NFR-PRV-1, NFR-PRV-2).
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
