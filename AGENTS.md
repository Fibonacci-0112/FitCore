# AGENTS.md

Guidance for AI coding agents working in this repository. Read [`docs/BRD.md`](docs/BRD.md) before any non-trivial change. It is the source of truth for scope, roles, permissions, business rules and the release phases.

## Project

FitCore is a health, fitness and nutrition app. Members plan targets and track nutrition (including estimated food cost), training, body metrics and wellness, with optional support from a coach or clinician. It is multi-user from day one, and every member's data is private by default.

The repository is at the requirements stage. It holds documentation only. No application code exists yet.

| Path | Contents |
|---|---|
| `README.md` | Product overview, summarized from the BRD |
| `docs/BRD.md` | Business requirements document |
| `.github/workflows/ci.yml` | CI: Markdown lint and link check |
| `.markdownlint-cli2.jsonc` | Markdown lint configuration |

## Commands

CI lints every Markdown file and checks its links, including links to headings. Run the same checks before declaring a task done:

```sh
npx markdownlint-cli2 "**/*.md"
lychee --include-fragments '**/*.md'   # https://lychee.cli.rs
```

Link to removed files at a commit permalink rather than leaving a dead relative link.

## Planned platform

The BRD (§5) proposes .NET on every platform. The platform decision is still awaiting approval (BRD §10), so confirm with the product owner before scaffolding code.

- **Clients:** .NET MAUI Blazor Hybrid apps (iOS, Android, Windows, macOS) and a Blazor Web App, sharing one Razor component library. Use native XAML pages only where they clearly pay off: the active workout / rest timer and the camera / barcode scanner.
- **Domain logic:** one shared C# library for units, nutrition totals, estimated cost, targets and guardrails, covered by table tests.
- **Backend:** Supabase (Postgres, Auth, Storage, row-level security) plus ASP.NET Core services for food search/import, AI calls, payment webhooks and notifications.
- **Sign-in:** email/password, Google, Microsoft and Sign in with Apple.

When code is added, update this file with the layout and the commands to build, test and lint it, and extend the CI workflow to run them.

## Business rules (do not break)

These are summarized from BRD §6. Follow them in domain logic, SQL and UI.

- **Unknown is not zero.** Missing nutrient values stay unknown (`null`), never `0`. Totals carry an incomplete flag and display as a lower bound with a count of incomplete items.
- **Days without a diary record are unknown**, never zero intake. Only days marked complete feed adaptive targets.
- **Money** is integer minor units plus an ISO 4217 currency code. Never use floats. Early releases do no currency conversion.
- **"Estimated food cost"** is consumption cost and is always labelled as an estimate, never "spend". Actual spend comes only from purchase records.
- **Units:** `g` and `ml` are canonical, and household measures are servings. Mass↔volume conversion only with a known density. Reject an impossible conversion with a clear error. Never guess.
- **Snapshots:** a log entry keeps the nutrients and cost from log time. Editing a food or price never rewrites history.
- **Dates:** diary dates are the member's local calendar date, and the profile stores an IANA timezone.
- **Targets:** Mifflin-St Jeor × activity factor. Soft limits (loss ≤ 1% of body weight per week, ≥ 1,200 kcal/day, protein 1.6 g/kg by default) can be overridden by the member after a warning, and the override is logged. The hard floor (initially < 800 kcal/day or a goal BMI < 18.5) blocks members and coaches; only a clinician can go past it, and the member is told. A coach target below a soft limit stays pending until the member accepts it.
- **Safety:** 18+ only. AI food estimates are saved only after the member confirms them. The app never generates condition-specific or medical advice. Adaptive targets are never applied without the member accepting them.

## Privacy and security rules

- **Private by default.** Every private table has row-level security, owner policies and tests for cross-user SELECT/INSERT/UPDATE/DELETE.
- **Professional access goes only through active grants** with scopes. Professionals read granted data and write only targets, assigned plans, comments, messages and check-in responses. A member's own logs stay read-only to them. Follow the permission matrix in BRD §3.2 exactly.
- **Audit** every grant change and every edit a professional makes to a member's data.
- **Admins never read member diaries.** They see account metadata and aggregate, anonymized analytics only, and curate the shared food and exercise catalog.
- **Shared catalog:** only the service writes shared food and exercise rows. Users write only their own custom items.
- **Secrets** live only server-side. Never commit them. Clients use only public keys from git-ignored config.
- **No health data in analytics events.**
- Show the attribution "Data: USDA FoodData Central" wherever USDA foods appear.

## Conventions

- Write documentation in plain, direct US English. Keep one paragraph or table row per line.
- Keep PRs small and scoped to one roadmap item, with its own acceptance criteria. Don't build features the BRD places in a later phase than the one being worked on.
- When a change contradicts the BRD, update the BRD in the same PR or raise it as an open question. Don't diverge silently.
