-- Database foundation (implementation plan §3.1, §3.2; R1 PR 2).
--
-- 1. One schema per module, named after the module in snake case (ADR 0003 decision 2, architecture-options §B.3).
--    The schemas start empty. Each module's tables arrive with their RLS policies and pgTAP tests, from PR 3 on.
-- 2. The fitcore_api login role for the API and the worker (RLS spike §4 rule 4, §5). It has no table grants and
--    can only switch to authenticated, inside a transaction, with SET LOCAL ROLE. A query without an identity
--    therefore fails with 42501 instead of reading data.
--
-- Forward-only (NFR-OPS-6). The role's password is never set here: hosted projects set it out of band and keep it
-- in the secret store, and the local stack sets a local-only one in supabase/seed.sql.

-- 1. Module schemas.

create schema identity;
create schema catalog;
create schema nutrition;
create schema training;
create schema progress;
create schema targets;
create schema care;
create schema sync;
create schema billing;
create schema integrations;
create schema data_rights;
create schema audit;

-- Nothing in a module schema is reachable until a migration grants it, table by table, to authenticated.
revoke all on schema identity, catalog, nutrition, training, progress, targets, care, sync, billing, integrations, data_rights, audit
  from public;

-- 2. The API's login role.

-- NOINHERIT: fitcore_api gets none of authenticated's privileges by default. The grant below lets it SET ROLE to
-- authenticated (SET true) without inheriting its privileges (INHERIT false), so it holds nothing until a unit of
-- work switches role.
create role fitcore_api with login noinherit nosuperuser nocreatedb nocreaterole noreplication nobypassrls;
grant authenticated to fitcore_api with inherit false, set true;

comment on role fitcore_api is
  'Login role for fitcore-app and fitcore-worker. No table grants: each unit of work runs SET LOCAL ROLE authenticated with the caller''s claims (RLS spike §4).';
