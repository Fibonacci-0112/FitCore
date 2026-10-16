-- NFR-PRV-1: every table FitCore creates has row-level security enabled, and anon can't read or write any of it.
--
-- FitCore's own schemas are every schema except the ones Postgres and Supabase manage, so a table in public or in
-- a schema created later is covered too. FitCore doesn't use Supabase's "Enable automatic RLS" option, so each
-- migration has to enable RLS itself (implementation plan §3.1).
begin;
create extension if not exists pgtap with schema extensions;

create function pg_temp.fitcore_schemas() returns setof name
language sql stable set search_path = ''
as $$
  select n.nspname
  from pg_catalog.pg_namespace n
  where n.nspname not in (
      'pg_catalog', 'information_schema', 'pg_toast', 'auth', 'storage', 'realtime', '_realtime', 'graphql',
      'graphql_public', 'extensions', 'vault', 'pgbouncer', 'pgsodium', 'pgsodium_masks', 'net', 'cron',
      'supabase_functions', 'supabase_migrations', '_analytics')
    and n.nspname not like 'pg\_temp\_%'
    and n.nspname not like 'pg\_toast\_temp\_%'
$$;

select plan(5);

select set_has(
  'select * from pg_temp.fitcore_schemas()',
  $$values ('identity'), ('catalog'), ('nutrition'), ('training'), ('progress'), ('targets'), ('care'), ('sync'),
           ('billing'), ('integrations'), ('data_rights'), ('audit'), ('public')$$,
  'Every module schema in ADR 0003 exists and is covered'
);

select is_empty(
  $$select n.nspname || '.' || c.relname
    from pg_catalog.pg_class c
    join pg_catalog.pg_namespace n on n.oid = c.relnamespace
    where n.nspname in (select pg_temp.fitcore_schemas())
      and c.relkind in ('r', 'p')
      and not c.relrowsecurity$$,
  'Every table in a FitCore schema has row-level security enabled'
);

-- A view runs with its owner's rights unless it is security_invoker, which would bypass the tables' policies.
select is_empty(
  $$select n.nspname || '.' || c.relname
    from pg_catalog.pg_class c
    join pg_catalog.pg_namespace n on n.oid = c.relnamespace
    where n.nspname in (select pg_temp.fitcore_schemas())
      and c.relkind = 'v'
      and not coalesce('security_invoker=true' = any (c.reloptions), false)
      and not coalesce('security_invoker=on' = any (c.reloptions), false)$$,
  'Every view in a FitCore schema is security_invoker, so the tables'' policies apply'
);

-- A materialized view or foreign table can't have row-level security at all.
select is_empty(
  $$select n.nspname || '.' || c.relname
    from pg_catalog.pg_class c
    join pg_catalog.pg_namespace n on n.oid = c.relnamespace
    where n.nspname in (select pg_temp.fitcore_schemas())
      and c.relkind in ('m', 'f')$$,
  'No FitCore schema has a materialized view or foreign table, which would bypass row-level security'
);

select is_empty(
  $$select n.nspname || '.' || c.relname
    from pg_catalog.pg_class c
    join pg_catalog.pg_namespace n on n.oid = c.relnamespace
    where n.nspname in (select pg_temp.fitcore_schemas())
      and c.relkind in ('r', 'p', 'v')
      and (pg_catalog.has_table_privilege('anon', c.oid, 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE')
        -- has_table_privilege ignores column grants, such as GRANT SELECT (body) ... TO anon.
        or pg_catalog.has_any_column_privilege('anon', c.oid, 'SELECT, INSERT, UPDATE'))$$,
  'anon, the public key''s role, has no privileges on any table, view or column in a FitCore schema'
);

select * from finish();
rollback;
