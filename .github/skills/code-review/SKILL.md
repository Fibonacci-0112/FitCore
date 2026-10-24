---
name: code-review
description: Review a FitCore pull request or diff against the BRD, the NFRs, the accepted ADRs and the spike rules. Use when asked to review code, documentation, SQL migrations, RLS policies, Blazor or MAUI UI, spike code or CI changes in this repository.
---

# FitCore code review

FitCore is a multi-user health, fitness and nutrition app where every member's data is private by default. A review here checks a change against the project's written requirements, not only general code quality. Most of the serious defects in this domain are silent: a `0` where a value is unknown, a policy that lets a revoked coach read a diary, a weight in a log line.

This file is also the checklist for the Claude Code skill in [`.claude/skills/fitcore-review/`](../../../.claude/skills/fitcore-review/SKILL.md) and the Codex skill in [`.agents/skills/fitcore-review/`](../../../.agents/skills/fitcore-review/SKILL.md), which cite its section numbers. Keep the numbering stable, or update those skills in the same PR.

## 1. Sources of truth

Read the parts of these that the diff touches before you comment. When the diff and a document disagree, the document wins unless the same PR updates it.

| Source | Use it for |
|---|---|
| [`CLAUDE.md`](../../../CLAUDE.md) | Repository conventions, commands and the condensed rules below |
| [`docs/BRD.md`](../../../docs/BRD.md) | Roles, the [permission matrix](../../../docs/BRD.md#32-permission-matrix), [release phases](../../../docs/BRD.md#41-release-phases), [business rules](../../../docs/BRD.md#6-business-rules) |
| [`docs/NFR.md`](../../../docs/NFR.md) | Measurable requirements with IDs such as `NFR-PRV-4` |
| [`docs/decisions/`](../../../docs/decisions/) | Accepted ADRs, currently [ADR 0003](../../../docs/decisions/0003-modular-monolith.md) (modular monolith on ASP.NET Core with Supabase) and [ADR 0005](../../../docs/decisions/0005-web-session.md) (web session) |
| [RLS pooling rules](../../../docs/spikes/rls-connection-pooling.md#4-rules-for-the-api) | Database identity and connection rules for the API |
| [Blazor build rules](../../../docs/spikes/blazor-prerendering-web-vitals.md#5-rules-for-the-build) | Web rendering, hydration and view-model rules |
| [Auth and MFA rules](../../../docs/spikes/apple-sign-in-and-mfa.md#4-rules-for-the-build) | Sign-in, MFA, `aal2` and Apple token rules |

## 2. How to review

1. **Identify the roadmap item.** Find the phase in [BRD §4.1](../../../docs/BRD.md#41-release-phases) and [§4.2](../../../docs/BRD.md#42-feature-list). Flag work that belongs to a later phase than the one being built, and PRs that mix several roadmap items.
2. **Classify the files.** Documentation, domain library, SQL and RLS, API, Blazor, MAUI, shared view models, spike code, CI. Apply every matching checklist in section 4.
3. **Trace the data.** For any member data the change reads, writes, logs or sends, follow it from input to storage to every output: UI, API response, log, analytics event, crash report, push payload, export.
4. **Check the actors.** For anything access-related, walk all six actors from NFR-PRV-2: owner, other member, grantee in scope, grantee out of scope, revoked grantee, admin.
5. **Check for silent divergence.** If the change contradicts the BRD, the NFRs or an accepted ADR, the same PR must update that document or record an open question.

## 3. How to report

Give each finding a severity, a location (`path:line`) and the rule it breaks, cited by section or ID (`BRD §6.1`, `NFR-PRV-6`, `ADR 0003`). Say what goes wrong and for whom, then suggest the smallest fix.

| Severity | Meaning |
|---|---|
| **Blocking** | Breaks a business rule, a privacy or security rule, an accepted ADR or a spike rule, or will fail CI |
| **Should fix** | A real defect or a missing test with a plausible path to harm, but no rule violation |
| **Nit** | Style or wording; never blocks a merge |

Don't report a finding you can't tie to a concrete failure or a written rule. Don't restate what the code does. If nothing is wrong, say so in one line.

## 4. Checklists

### 4.1 Business rules (BRD §6)

- **Unknown is not zero.** Missing nutrient values stay `null` in C#, SQL and JSON. Look for `?? 0`, `GetValueOrDefault()`, `COALESCE(x, 0)`, `SUM` that drops nulls without counting them, and non-nullable nutrient fields. Totals must show as a lower bound with a count of incomplete items.
- **Days without a diary record are unknown**, never zero intake. Only days marked complete feed adaptive targets.
- **Money** is an integer count of minor units plus an ISO 4217 code. Flag `double`, `float` or `decimal` money, money without a currency, and any currency conversion.
- **Wording.** "Estimated food cost" is always labelled as an estimate and never called "spend". Actual spend comes only from purchase records.
- **Units.** `g` and `ml` are canonical. Standard mass and volume units convert exactly to them through `QuantityUnits`, with US customary definitions (a cup is 236.6 ml). Food-specific measures are servings. Mass↔volume conversion needs a known density. An impossible conversion must be rejected, never guessed or defaulted to a density of 1.
- **Snapshots.** A log entry stores its nutrients and cost at log time. Flag joins that recompute history from the current food or price, and updates that cascade into past entries.
- **Dates.** Diary dates are the member's local calendar date, resolved from the profile time zone. Flag `DateTime.Now`, `DateTime.UtcNow.Date`, `DateOnly.FromDateTime(DateTime.Now)`, `CURRENT_DATE` or browser time used as a diary date.
- **Targets and guardrails** ([BRD §6.2](../../../docs/BRD.md#62-targets-and-safety)). Mifflin-St Jeor × activity factor. Soft limits (loss ≤ 1% of body weight per week, ≥ 1,200 kcal/day, protein 1.6 g/kg) allow a member override after a warning, and the override is audited. The hard floor (< 800 kcal/day or goal BMI < 18.5) blocks members and coaches; only a clinician may pass it, and the member is told. A coach target below a soft limit stays pending until the member accepts it. A member flagged by ED screening can't override soft limits.
- **Safety.** 18+ only. AI food estimates are saved only after the member confirms or edits them. No generated medical advice or condition-specific recommendations. Adaptive targets need the data-quality gate (≥ 21 days, ≥ 80% complete days, ≥ 8 weigh-ins) and are never applied without the member accepting.
- **Shared catalog.** Only the service writes shared food and exercise rows, and admins curate them. Other actors write only their own custom items.
- **Attribution.** "Data: USDA FoodData Central" appears wherever USDA foods appear.

### 4.2 Privacy, access control and audit

- **Permission matrix.** Professionals reach member data only through an active grant and its scopes, with exactly the access in [BRD §3.2](../../../docs/BRD.md#32-permission-matrix) (NFR-PRV-3). They write only targets, assigned plans, comments, messages and check-in responses. Clinicians read workouts but don't assign them. Progress photos need an explicit photo scope.
- **Revocation** takes effect on the professional's next request; no cached grant decision may outlive it (NFR-PRV-4). Offline copies on a professional's device expire 24 hours after the last grant check.
- **Admins and support staff** have no path to diaries, measurements, photos, wellness data or messages, including through support tools, logs and backups in normal operation (NFR-PRV-5). Support runs export and deletion on a verified request without seeing diary content (NFR-DAT-6). Admin analytics are aggregate and anonymized only.
- **No health data** (foods, nutrients, weights, measurements, wellness values, photos, messages, conditions) in analytics events, logs, crash reports, exception messages, validation messages or push payloads (NFR-PRV-6). Check string interpolation in `throw`, `ILogger` calls, telemetry properties and web-vitals payloads.
- **Audit.** Grant changes, professional writes to member data, guardrail overrides and acceptances, admin catalog changes, support-run exports and deletions, and admin sign-ins write to the append-only audit log, with actor, actor role, member, action, UTC time and before/after values (NFR-AUD-1, NFR-AUD-2). No user, including admins, can edit or delete audit entries through the app or API (NFR-AUD-3). The only permitted change is the retention job's pseudonymization after account deletion (NFR-DAT-4). Members can view the entries about their own data (NFR-AUD-4).
- **Hide-numbers mode** removes calorie and weight figures on every surface, including notifications and coach messages (NFR-ACC-5).
- **Consent** for health data, sharing and AI photo processing is recorded separately and can be withdrawn (NFR-PRV-7). No location data (NFR-PRV-8).

### 4.3 Database, SQL and RLS

- **RLS on every private table**, and nothing private readable with the anon key (NFR-PRV-1).
- **pgTAP tests for each actor** on every private table: owner, other member, grantee in scope, grantee out of scope, revoked grantee and admin (NFR-PRV-2), using the helper in `supabase/tests/helpers/actors.psql`. Every migration ships with pgTAP tests (NFR-MNT-2). A new table or policy without them is blocking.
- **Views** in a FitCore schema are `security_invoker`, and there are no materialized views or foreign tables there; `supabase/tests/rls_coverage.test.sql` checks both.
- **Grant-aware policies** call `care.has_grant(member_id, scope)`, which reads `care.relationships` live and accepts a clinician's grant only at `aal2`. In R1 the `nutrition` scope covers the diary, diary days and targets, and `body_metrics` covers weigh-ins. A policy that caches a grant, or checks one some other way, is blocking.
- **Shared catalog rows** (`owner_id` null) are written only by the `fitcore_catalog_import` login role, whose policies hide members' custom foods. A grant or policy that lets `authenticated` or `fitcore_api` write a shared row, or lets the import role reach a member's row or any other schema, is blocking.
- **Migrations** live under `supabase/`, are forward-only and follow expand-then-contract (NFR-OPS-6, ADR 0003). Flag edits to an already-merged migration and destructive changes without an expand step.
- **Module schemas.** Each module owns one schema (`identity`, `catalog`, `nutrition`, `training`, `progress`, `targets`, `care`, `sync`, `billing`, `integrations`, `data_rights`, `audit`). Flag cross-schema writes other than audit triggers calling `audit.append()`; cross-module side effects go through the transactional outbox. Policies read another schema only to decide visibility ([implementation plan §3.1](../../../docs/implementation-plan.md#31-database)).
- **The API's login role has no table grants.** `fitcore_api` can only switch to `authenticated`; a migration that grants it anything is blocking. Grants go to `authenticated`, table by table, never to `anon` or `public`.
- **Writes are idempotent by client UUID**, so a retried request doesn't create duplicates.

### 4.4 API and database identity (ADR 0003, RLS spike)

- **Transaction-scoped identity only.** Each unit of work runs `SET LOCAL ROLE authenticated` and `set_config('request.jwt.claims', …, true)`. Session-level `SET`, `SET ROLE` or `set_config(…, false)` on an API connection is blocking.
- **Identity first, same round trip.** Role and claims go in the same batch as the first statement. Server code runs SQL only through `FitCoreDatabase` in `FitCore.Platform` (`UnitOfWork` or `ReadAsync`); a module that opens its own Npgsql connection or data source is blocking. EF Core work always runs in a transaction with the claims interceptor; work outside one should fail closed.
- **Connections** go through Supavisor session mode or directly, never transaction mode, unless the PR follows rule 6 of the [spike](../../../docs/spikes/rls-connection-pooling.md#4-rules-for-the-api) and reruns its checks.
- **Two authorization layers.** API policies check roles, entitlements and live grants; RLS checks again. Don't accept "RLS will catch it" in place of an API policy, or the reverse.
- **The API is the only way in.** Clients call versioned REST (`/v1`) through the typed C# client. The Supabase Data API stays disabled for module schemas. Breaking contract changes need a new version (NFR-CMP-5).
- **Module boundaries.** A module uses another only through its contracts surface; architecture tests enforce this.
- **Secrets** (service-role key, vendor keys) never appear in client builds, config files or the repository (NFR-SEC-5).

### 4.5 Auth and MFA (auth spike, ADR 0005)

- TOTP is offered to everyone and required for clinicians and admins. "Required" is enforced in API policies (`aal2`) and in RLS; admin tooling checks `aal2` too (NFR-SEC-3, NFR-SEC-12).
- Passkey sign-in counts as a single factor and never satisfies an MFA requirement. The undocumented WebAuthn factor is not used.
- Native Apple sign-in on iOS uses a nonce, saves the name on first sign-in and sends the authorization code to the API. Account deletion in the DataRights module revokes Apple tokens, with a test.
- Passwords follow NIST SP 800-63B-4: no composition rules, breached-password check, length limits from NFR-SEC-2. Access tokens last ≤ 1 hour and refresh tokens rotate (NFR-SEC-4).
- **Web session** ([ADR 0005](../../../docs/decisions/0005-web-session.md)). The Supabase session lives only in the encrypted `__Host-fitcore-session` cookie (HttpOnly, Secure, `SameSite=Lax`). A token in WebAssembly state, `[PersistentState]`, prerendered HTML, JavaScript or browser storage is blocking. Sign-in, sign-up, step-up and sign-out run on the server and end with a full page load; OAuth uses PKCE and accepts only local return URLs.
- **Auth calls from the server** send the member's IP in `Sb-Forwarded-For` with a secret API key, used only for Auth endpoints ([ADR 0005](../../../docs/decisions/0005-web-session.md) decision 2). A secret key on a data path, or the legacy `service_role` key anywhere, is blocking.
- **One identity.** Bearer and cookie requests go through the Platform authentication scheme and produce the same claims principal. Module code that reads the cookie or the `Authorization` header itself is blocking.
- **Cross-site writes.** A cookie-authenticated `/v1` endpoint with a method other than `GET`, `HEAD` or `OPTIONS` must check the antiforgery header and a same-origin `Origin` (or `Sec-Fetch-Site`), with a test for each refusal. A `GET` that changes state, or CORS headers on `/v1`, is blocking.
- **Auth logging.** Auth form bodies, tokens and cookie values never reach logs (NFR-PRV-6). Sign-in, sign-up, password-reset and step-up endpoints are rate-limited with progressive lockout (NFR-SEC-4).

### 4.6 Clients: shared code, Blazor and MAUI

- **No business logic in UI code** (NFR-MNT-4). Calculations of totals, costs, units, targets or guardrails in XAML code-behind, Razor components or view models belong in `FitCore.Domain` or the server.
- **Parity.** A feature lands on both the MAUI XAML UI and the Blazor UI, or the gap is recorded in the release notes with a date (NFR-CMP-3). View models and service clients are shared, not duplicated per UI.
- **Blazor rendering.** Every page prerenders on the server and hydrates with WebAssembly. Flag WebAssembly-only routes, `InteractiveServer` and `InteractiveAuto`. Prerendered data is carried by `[PersistentState]`.
- **Member-data pages** send `Cache-Control: no-store`.
- **"Today"** on the server comes from the member's profile time zone, never the server clock or the browser.
- **View-model commands** `await Task.Yield()` after the optimistic update and before calling the API. Actions stay disabled until `RendererInfo.IsInteractive`.
- **Serialization** uses source-generated JSON (`JsonSerializerContext`). No AOT, `InvariantGlobalization` or `wasm-tools` without a measurement that justifies it. CommunityToolkit.Mvvm is 8.4.2 or later.
- **On-device health data** is stored in Keychain, Keystore, DPAPI or an encrypted database (NFR-SEC-8).
- **Device sign-in.** The MAUI app reaches Supabase Auth only through `FitCore.Client.Auth` (`Supabase.Gotrue`), with the project's publishable key. `Supabase.Gotrue` or `FitCore.Client.Auth` in anything the Blazor client is built from, or a secret key in a client build, is blocking ([ADR 0005](../../../docs/decisions/0005-web-session.md)). Auth failures reach the view model as a status, never as an exception with the response body.
- **Device session.** The MAUI app keeps the Supabase session only in the platform secure store, through `ISessionStore` (NFR-SEC-8). A token in `Preferences`, a file, a log or a `ToString()` is blocking. `DeviceSession` is the only owner: it refreshes under one lock, because refresh tokens are single-use, and deletes the device's copy only once Supabase Auth has ended the session. Anything that caches member data on the device clears it when `DeviceSession.SignedInChanged` reports a sign-out.
- **XAML** bindings declare `x:DataType`. Pages bind to `FitCore.Client.Core` view models; code-behind only wires up the view (for example, screen-reader announcements).
- **Accessibility.** Every control has an accessible name; touch targets are ≥ 44×44 pt; color is never the only signal, for example for incomplete or unpriced items; motion respects "reduce motion" (NFR-ACC-1..6).
- **Strings** are in resource files, not hard-coded (NFR-L10N-2): text both UIs show in `FitCore.Client.Core/Resources`, server-only text in its project's `Resources/`. Validation attributes use `ErrorMessageResourceType`; a sentence with a link is one resource with `{0}`, shown with `LinkedText`, never split into fragments. XAML takes text with `x:Static` from the same classes. `UserVisibleTextTests` catches Razor text, literal XAML text attributes and literal `ErrorMessage`s, but not strings a C# method returns. Numbers, dates and currency use the member's locale and units.

### 4.7 Domain library and tests

- Every rule in BRD §6 has table tests, and `FitCore.Domain` keeps at least 90% line coverage (NFR-MNT-1).
- Tests cover the edges that matter here: `null` nutrients, zero versus unknown, a missing density, a currency mismatch, time-zone boundaries around midnight and DST, each guardrail threshold exactly at and just past its value, and a revoked grant.
- A change to a rule without a matching test change is at least "should fix".

### 4.8 Documentation

- CI runs `markdownlint-cli2` (pinned 0.18.1 locally; MD013 is off) and `lychee --include-fragments`. Flag broken relative links, links to renamed headings, and repeated heading text in one file (MD024; prefix repeats such as `A.1`, `B.1`).
- Plain, direct US English, one paragraph or table row per line.
- Requirements are cited by section (`BRD §6.2`) or ID (`NFR-SCL-5`).
- Links to removed files use a commit permalink, never a dead relative link.
- A new architecture decision is a new ADR in `docs/decisions/` continuing the numbering (0001 and 0002 exist in git history), not an edit that rewrites an accepted one.
- When code is added, `CLAUDE.md` gains the layout and the build, test and lint commands, and CI runs them.

### 4.9 Spikes and CI

- Code under `spikes/` is throwaway. Review it for whether it answers its question and whether its findings doc states the rules honestly; don't hold it to production standards. Production code must not reference spike projects.
- Each spike has a README with run instructions, and CI builds it with `dotnet build`.
- CI changes keep `permissions: contents: read` unless more is needed, don't print secrets, and don't remove a required check (NFR-MNT-3). Security scanning (dependencies, SAST, secrets) belongs in CI once code exists (NFR-SEC-9).

## 5. What not to flag

- Formatting that markdownlint or the compiler already enforces, beyond noting that CI will fail.
- Preferences that contradict the documented decisions, such as proposing Interactive Server, MAUI Blazor Hybrid or a microservice split. Raise those as a question for a new ADR, not as a defect.
- Missing features that the BRD places in a later phase.
