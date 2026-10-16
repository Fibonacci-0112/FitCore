# Supabase: schema, roles and pgTAP tests

`supabase/` is the only source of FitCore's database schema ([implementation plan §3.1](../docs/implementation-plan.md#31-database)). The [Supabase CLI](https://supabase.com/docs/guides/local-development/cli/getting-started) runs it locally and in CI, and applies it to the hosted projects.

## Contents

| Path | Contents |
|---|---|
| `config.toml` | The local stack. The Data API exposes only `public` and `graphql_public`; module schemas are never added (architecture-options §B.3) |
| `migrations/` | Forward-only SQL migrations, expand-then-contract (NFR-OPS-6). Each one enables RLS on every table it creates and ships with pgTAP tests (NFR-MNT-2) |
| `seed.sql` | Local stack only: the local-only passwords for `fitcore_api` and `fitcore_catalog_import` |
| `tests/*.test.sql` | The pgTAP suite, run by `supabase test db` |
| `tests/helpers/actors.psql` | The six actors of NFR-PRV-2 and their grants, included by table tests with `\ir helpers/actors.psql` |

## Roles and schemas

The first migration, `20261016000000_database_foundation.sql`, creates:

- one empty schema per module in [ADR 0003](../docs/decisions/0003-modular-monolith.md): `identity`, `catalog`, `nutrition`, `training`, `progress`, `targets`, `care`, `sync`, `billing`, `integrations`, `data_rights` and `audit`;
- `fitcore_api`, the login role for `fitcore-app` and `fitcore-worker`. It is `NOINHERIT`, has no table grants, and can switch only to `authenticated`. `FitCore.Platform`'s unit of work does that per transaction with `SET LOCAL ROLE authenticated` and the caller's claims ([RLS spike §4](../docs/spikes/rls-connection-pooling.md#4-rules-for-the-api)), so a query without an identity fails with 42501.

The R1 migrations add the tables of [implementation plan §4](../docs/implementation-plan.md#4-r1-data-model), one migration per module, each with RLS on, its policies, its grants to `authenticated` and its pgTAP test:

| Migration | Tables | Who reads and writes |
|---|---|---|
| `…_care_relationships.sql` | `care.relationships`, `care.has_grant()`, `care.grant_role()` | Both parties read a relationship; no app role writes one until the Coach/Pro tier. `has_grant(member, scope)` reads the live table, so a revocation applies to the next request (NFR-PRV-4), and counts a clinician's grant only at `aal2` |
| `…_audit_events.sql` | `audit.events`, `audit.append()` | Module triggers write through `audit.append`; no app role can insert, update or delete an entry, and a trigger stops even the table owner (NFR-AUD-3). The member reads the entries about their data |
| `…_identity_profiles.sql` | `identity.profiles` | The member only. A grantee's column-limited read of name and goals arrives with the Coach/Pro roster |
| `…_catalog_foods.sql` | `catalog.foods`, `catalog.food_servings`, the `fitcore_catalog_import` role | Everyone reads shared rows; only `fitcore_catalog_import` writes them. Custom foods are their owner's alone |
| `…_nutrition_diary.sql` | `nutrition.food_prices`, `nutrition.diary_entries`, `nutrition.diary_days` | The member writes; a `nutrition` grantee reads entries and days. Prices stay private to the member |
| `…_progress_body_weights.sql` | `progress.body_weights` | The member writes; a `body_metrics` grantee reads |
| `…_targets.sql` | `targets.targets` | The member writes their own; a `nutrition` grantee sets targets as their grant's role, and a coach's target below a soft limit stays pending until the member accepts it. Only a clinician goes past the hard floor |

Grant scopes follow [BRD §3.1](../docs/BRD.md#31-sharing-model): `nutrition`, `training`, `body_metrics`, `photos` and `wellness`. In R1, `nutrition` covers the diary, diary days and targets, and `body_metrics` covers weigh-ins. Admins have no access to any of these tables (NFR-PRV-5); their tooling arrives with the public launch.

`fitcore_catalog_import` is the login role for the USDA import job ([implementation plan §3.2](../docs/implementation-plan.md#32-database-identity)). It can read, insert and update only the shared rows of `catalog.foods` and `catalog.food_servings`, can't delete, and has no access to any other schema.

## Tests

| Test | Checks |
|---|---|
| `rls_coverage.test.sql` | Every table in a FitCore schema (`public`, the module schemas and any schema added later) has RLS enabled; views are `security_invoker`; there are no materialized views or foreign tables; `anon` has no table or column privileges (NFR-PRV-1) |
| `api_role.test.sql` | `fitcore_api` has no attributes, grants or memberships beyond switching to `authenticated` ([RLS spike §5](../docs/spikes/rls-connection-pooling.md#5-open-points)) |
| `actors.test.sql` | The six-actor helper switches role and claims the way the unit of work does, and gives the grantees their grants |
| `care.test.sql`, `identity.test.sql`, `catalog.test.sql`, `nutrition.test.sql`, `progress.test.sql`, `targets.test.sql` | Each private table for all six actors and `anon`, plus the table's own rules (NFR-PRV-2) |
| `audit.test.sql` | Grant changes are audited, the member reads their entries, and no role edits or deletes one (NFR-AUD-1..4) |
| `catalog_import_role.test.sql` | `fitcore_catalog_import` reads and writes only shared catalog rows, and reaches no other schema |

Every private table has a test file that includes the helper and checks each of the six actors: owner, other member, grantee in scope, grantee out of scope, revoked grantee and admin (NFR-PRV-2). Grantees are plain members until the test calls `tests.grant_scope(scope)`, which gives `grantee_in_scope` an active grant with that scope, `grantee_out_of_scope` an active grant with every other scope, and `revoked_grantee` a revoked one. `tests.act_as(name, 'aal2')` checks a clinician's grant at `aal2`.

## Run it locally

Needs Docker and Node 22. Run from the repository root:

```sh
npx --yes supabase@2.121.0 db start    # Postgres only, with migrations and seed applied; `start` runs the whole stack
npx --yes supabase@2.121.0 test db     # the pgTAP suite
npx --yes supabase@2.121.0 db reset    # reapply migrations and seed after changing them
npx --yes supabase@2.121.0 stop
```

The local database listens on `127.0.0.1:54322`. The .NET database tests (`tests/FitCore.Platform.Tests` and the module test projects, through the shared fixture in `tests/FitCore.Testing`) and the Aspire AppHost use it.

## Hosted projects

The development project is `FitCore-Dev-DB` (implementation plan §2). For each hosted project:

1. Apply the migrations: `npx supabase link --project-ref <ref>`, then `npx supabase db push`.
2. Set the `fitcore_api` and `fitcore_catalog_import` passwords from the SQL editor, each with its own generated value: `alter role fitcore_api with password '…';` and `alter role fitcore_catalog_import with password '…';`. Store them in the secret store (NFR-SEC-5), never in the repository.
3. Connect the API through Supavisor session mode (port 5432 on the pooler host, user `fitcore_api.<ref>`) or directly. `FitCore.Platform` refuses transaction mode (port 6543) and `No Reset On Close` ([RLS spike §4](../docs/spikes/rls-connection-pooling.md#4-rules-for-the-api) rule 5).
4. Keep the Data API's exposed schemas to `public` and `graphql_public`, and keep "Enable automatic RLS" off ([implementation plan §3.1](../docs/implementation-plan.md#31-database)).

## Auth settings

Decided on 2026-10-16 (implementation plan §8). Set them in each hosted project's Auth settings. PR 4 sets the same values in `config.toml` for the local stack, except where the table says otherwise.

| Setting | Value | Why |
|---|---|---|
| Plan | Pro for production | Leaked-password protection, the session time-box and the inactivity timeout need it. The development project can stay on Free until the beta and go without those three |
| Access-token (JWT) expiry | 900 seconds | Bounds how long a copied cookie works after sign-out ([ADR 0005](../docs/decisions/0005-web-session.md) risks); within NFR-SEC-4's 1 hour |
| Refresh-token rotation | On, reuse interval 10 seconds | ADR 0005 decision 4 relies on it |
| Session time-box | 30 days | Forces a fresh sign-in at least monthly (Pro) |
| Inactivity timeout | 14 days | An abandoned device signs itself out (Pro) |
| Single session per user | Off | Members use several devices |
| Minimum password length | 15 | NFR-SEC-2's single-factor minimum. Supabase can't lower it for MFA users |
| Required characters | None | NFR-SEC-2 forbids composition rules |
| Leaked-password protection | On | NFR-SEC-2's breached-password check (Pro; the local stack has no equivalent) |
| MFA | TOTP on, phone off | NFR-SEC-3 and the [auth spike rules](../docs/spikes/apple-sign-in-and-mfa.md#4-rules-for-the-build) |
| Confirm email | On | Proves the member owns the address |
| Secure email change | On | Confirms both the old and the new address |
| Custom SMTP | Amazon SES in `us-west-2` | The built-in mailer sends only to team members, 2 emails per hour. Locally the stack's mail catcher receives all email |
| Email rate limit | 300 per hour | The default with custom SMTP is 30 per hour |
| IP address forwarding | On | Supabase rate-limits Auth per IP, and every web call comes from `fitcore-app` ([ADR 0005](../docs/decisions/0005-web-session.md) decision 2) |
| Apple provider | Native client IDs plus a Services ID for the web flow | Every platform offers Sign in with Apple. The web flow's client secret expires every 6 months: renew it from the signing key before it lapses, or Apple sign-in stops on Android, Windows and the web |
