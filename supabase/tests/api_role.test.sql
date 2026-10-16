-- The API's login role, fitcore_api, holds no privileges of its own: it can only switch to authenticated
-- (RLS spike §4 rule 4 and §5). A query without an identity then fails with 42501 instead of reading data.
begin;
create extension if not exists pgtap with schema extensions;

select plan(12);

select has_role('fitcore_api');
select ok((select rolcanlogin from pg_catalog.pg_roles where rolname = 'fitcore_api'), 'fitcore_api can log in');
select ok(
  (select not (rolsuper or rolcreatedb or rolcreaterole or rolreplication or rolbypassrls)
   from pg_catalog.pg_roles where rolname = 'fitcore_api'),
  'fitcore_api has no superuser, createdb, createrole, replication or bypassrls attribute'
);
select ok(
  not (select rolinherit from pg_catalog.pg_roles where rolname = 'fitcore_api'),
  'fitcore_api is NOINHERIT'
);

select results_eq(
  $$select r.rolname::text collate "default", m.inherit_option, m.set_option, m.admin_option
    from pg_catalog.pg_auth_members m
    join pg_catalog.pg_roles r on r.oid = m.roleid
    where m.member = 'fitcore_api'::regrole$$,
  $$values ('authenticated', false, true, false)$$,
  'fitcore_api is a member of authenticated only, and can switch to it without inheriting its privileges'
);

select is_empty(
  $$select table_schema || '.' || table_name from information_schema.role_table_grants where grantee = 'fitcore_api'
    union all
    select table_schema || '.' || table_name || '.' || column_name from information_schema.column_privileges
    where grantee = 'fitcore_api'$$,
  'fitcore_api has no table or column grants'
);

select is_empty(
  $$select routine_schema || '.' || routine_name from information_schema.role_routine_grants where grantee = 'fitcore_api'
    union all
    select object_schema || '.' || object_name from information_schema.role_usage_grants where grantee = 'fitcore_api'$$,
  'fitcore_api has no function, sequence or type grants'
);

-- Effective privileges include PUBLIC grants. Supabase grants PUBLIC read on the pg_stat_statements views in
-- extensions, which hide other roles' query text, so that schema is left out.
select is_empty(
  $$select n.nspname || '.' || c.relname
    from pg_catalog.pg_class c
    join pg_catalog.pg_namespace n on n.oid = c.relnamespace
    where n.nspname not in ('pg_catalog', 'information_schema', 'extensions')
      and n.nspname not like 'pg\_temp\_%'
      and c.relkind in ('r', 'p', 'v', 'm', 'f')
      and (pg_catalog.has_table_privilege('fitcore_api', c.oid, 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER')
        -- has_table_privilege ignores column grants, which can also reach fitcore_api through PUBLIC.
        or pg_catalog.has_any_column_privilege('fitcore_api', c.oid, 'SELECT, INSERT, UPDATE, REFERENCES'))$$,
  'fitcore_api can''t read or write any table, view or column, through PUBLIC or otherwise'
);

select is_empty(
  $$select nspname from pg_catalog.pg_namespace
    where pg_catalog.has_schema_privilege('fitcore_api', oid, 'CREATE')
      and nspname not like 'pg\_temp\_%'$$,
  'fitcore_api can''t create objects in any schema'
);
select ok(
  not pg_catalog.has_database_privilege('fitcore_api', current_database(), 'CREATE'),
  'fitcore_api can''t create schemas'
);

-- Through any chain of memberships, not just direct ones: SET ROLE to service_role or postgres would skip RLS.
select is_empty(
  $$select rolname from pg_catalog.pg_roles
    where pg_catalog.pg_has_role('fitcore_api', oid, 'SET') and rolname not in ('fitcore_api', 'authenticated')$$,
  'fitcore_api can switch to no role but authenticated'
);

-- What the API sees on a fresh connection, before a unit of work switches role. postgres created fitcore_api, so
-- it holds the admin option needed to grant itself the role for this transaction. fitcore_api also needs pgTAP's
-- schema to run the checks; the rollback removes both grants.
grant fitcore_api to postgres with inherit false, set true;
grant usage on schema extensions to fitcore_api;
set local role fitcore_api;
select throws_ok(
  'select count(*) from auth.users',
  '42501',
  null,
  'fitcore_api can''t read auth.users without switching role'
);

select * from finish();
rollback;
