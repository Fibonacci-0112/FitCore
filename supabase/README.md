# Supabase: schema, roles and pgTAP tests

`supabase/` is the only source of FitCore's database schema ([implementation plan §3.1](../docs/implementation-plan.md#31-database)). The [Supabase CLI](https://supabase.com/docs/guides/local-development/cli/getting-started) runs it locally and in CI, and applies it to the hosted projects.

## Contents

| Path | Contents |
|---|---|
| `config.toml` | The local stack. The Data API exposes only `public` and `graphql_public`; module schemas are never added (architecture-options §B.3) |
| `migrations/` | Forward-only SQL migrations, expand-then-contract (NFR-OPS-6). Each one enables RLS on every table it creates and ships with pgTAP tests (NFR-MNT-2) |
| `seed.sql` | Local stack only: the local-only password for `fitcore_api` |
| `tests/*.test.sql` | The pgTAP suite, run by `supabase test db` |
| `tests/helpers/actors.psql` | The six actors of NFR-PRV-2, included by table tests with `\ir helpers/actors.psql` |

## Roles and schemas

The first migration, `20261016000000_database_foundation.sql`, creates:

- one empty schema per module in [ADR 0003](../docs/decisions/0003-modular-monolith.md): `identity`, `catalog`, `nutrition`, `training`, `progress`, `targets`, `care`, `sync`, `billing`, `integrations`, `data_rights` and `audit`;
- `fitcore_api`, the login role for `fitcore-app` and `fitcore-worker`. It is `NOINHERIT`, has no table grants, and can switch only to `authenticated`. `FitCore.Platform`'s unit of work does that per transaction with `SET LOCAL ROLE authenticated` and the caller's claims ([RLS spike §4](../docs/spikes/rls-connection-pooling.md#4-rules-for-the-api)), so a query without an identity fails with 42501.

## Tests

| Test | Checks |
|---|---|
| `rls_coverage.test.sql` | Every table in a FitCore schema (`public`, the module schemas and any schema added later) has RLS enabled; views are `security_invoker`; there are no materialized views or foreign tables; `anon` has no table or column privileges (NFR-PRV-1) |
| `api_role.test.sql` | `fitcore_api` has no attributes, grants or memberships beyond switching to `authenticated` ([RLS spike §5](../docs/spikes/rls-connection-pooling.md#5-open-points)) |
| `actors.test.sql` | The six-actor helper switches role and claims the way the unit of work does |

From PR 3 on, every private table gets a test file that includes the helper and checks each of the six actors: owner, other member, grantee in scope, grantee out of scope, revoked grantee and admin (NFR-PRV-2). Grantees are plain members until the test inserts their `care.relationships` row.

## Run it locally

Needs Docker and Node 22. Run from the repository root:

```sh
npx --yes supabase@2.121.0 db start    # Postgres only, with migrations and seed applied; `start` runs the whole stack
npx --yes supabase@2.121.0 test db     # the pgTAP suite
npx --yes supabase@2.121.0 db reset    # reapply migrations and seed after changing them
npx --yes supabase@2.121.0 stop
```

The local database listens on `127.0.0.1:54322`. The .NET database tests (`tests/FitCore.Platform.Tests`) and the Aspire AppHost use it.

## Hosted projects

The development project is `FitCore-Dev-DB` (implementation plan §2). For each hosted project:

1. Apply the migrations: `npx supabase link --project-ref <ref>`, then `npx supabase db push`.
2. Set the `fitcore_api` password from the SQL editor, with a generated value: `alter role fitcore_api with password '…';`. Store it in the secret store (NFR-SEC-5), never in the repository.
3. Connect the API through Supavisor session mode (port 5432 on the pooler host, user `fitcore_api.<ref>`) or directly. `FitCore.Platform` refuses transaction mode (port 6543) and `No Reset On Close` ([RLS spike §4](../docs/spikes/rls-connection-pooling.md#4-rules-for-the-api) rule 5).
4. Keep the Data API's exposed schemas to `public` and `graphql_public`, and keep "Enable automatic RLS" off ([implementation plan §3.1](../docs/implementation-plan.md#31-database)).
