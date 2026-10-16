-- fitcore_catalog_import, the import job's login role, reads and writes only the shared rows of the catalog
-- (implementation plan §3.2; R1 PR 3).
begin;
\ir helpers/actors.psql

select plan(28);

-- The role itself, like fitcore_api in api_role.test.sql.
select has_role('fitcore_catalog_import');
select ok((select rolcanlogin from pg_catalog.pg_roles where rolname = 'fitcore_catalog_import'), 'It can log in');
select ok(
  (select not (rolsuper or rolcreatedb or rolcreaterole or rolreplication or rolbypassrls or rolinherit)
   from pg_catalog.pg_roles where rolname = 'fitcore_catalog_import'),
  'It has no superuser, createdb, createrole, replication or bypassrls attribute, and is NOINHERIT'
);
select is_empty(
  $$select rolname from pg_catalog.pg_roles
    where pg_catalog.pg_has_role('fitcore_catalog_import', oid, 'SET') and rolname <> 'fitcore_catalog_import'$$,
  'It can switch to no other role'
);

-- Its privileges: SELECT, INSERT and UPDATE on the two catalog tables, nothing else anywhere. As in api_role.test.sql,
-- the extensions schema is left out for Supabase's PUBLIC read on the pg_stat_statements views.
select set_eq(
  $$select n.nspname || '.' || c.relname || ' ' || p.privilege
    from pg_catalog.pg_class c
    join pg_catalog.pg_namespace n on n.oid = c.relnamespace
    cross join (values ('SELECT'), ('INSERT'), ('UPDATE'), ('DELETE'), ('TRUNCATE'), ('REFERENCES'), ('TRIGGER')) p (privilege)
    where n.nspname not in ('pg_catalog', 'information_schema', 'extensions')
      and n.nspname not like 'pg\_temp\_%'
      and c.relkind in ('r', 'p', 'v', 'm', 'f')
      and pg_catalog.has_table_privilege('fitcore_catalog_import', c.oid, p.privilege)$$,
  $$values ('catalog.foods SELECT'), ('catalog.foods INSERT'), ('catalog.foods UPDATE'),
           ('catalog.food_servings SELECT'), ('catalog.food_servings INSERT'), ('catalog.food_servings UPDATE')$$,
  'It can read, insert and update the catalog tables, and nothing else'
);
select is_empty(
  $$select nspname from pg_catalog.pg_namespace
    where nspname in ('identity', 'nutrition', 'training', 'progress', 'targets', 'care', 'sync', 'billing',
                      'integrations', 'data_rights', 'audit', 'auth', 'storage')
      and pg_catalog.has_schema_privilege('fitcore_catalog_import', oid, 'USAGE')$$,
  'It can''t reach any member schema'
);
select is_empty(
  $$select nspname from pg_catalog.pg_namespace
    where pg_catalog.has_schema_privilege('fitcore_catalog_import', oid, 'CREATE') and nspname not like 'pg\_temp\_%'$$,
  'It can''t create objects in any schema'
);

-- What the job can do with rows. A member's custom food and serving are already there.
select tests.act_as_postgres();
insert into catalog.foods (id, owner_id, source, name, nutrient_basis, energy_kcal, protein_g, carbs_g, fat_g)
values ('c0000000-0000-4000-8000-000000000011', tests.actor('owner'), 'custom', 'Bar', 'per_100g', 400, 30, 40, 12);
insert into catalog.food_servings (id, food_id, label, grams)
values ('c0000000-0000-4000-8000-000000000012', 'c0000000-0000-4000-8000-000000000011', '1 bar', 60);

-- postgres created the role, so it holds the admin option needed to grant itself the role for this transaction. The
-- role also needs pgTAP's schema and the test helpers to run the checks; the rollback removes all three grants.
grant fitcore_catalog_import to postgres with inherit false, set true;
grant usage on schema extensions, tests to fitcore_catalog_import;
grant execute on all functions in schema tests to fitcore_catalog_import;
set local role fitcore_catalog_import;

select lives_ok(
  $$insert into catalog.foods (id, owner_id, source, source_id, name, nutrient_basis, energy_kcal)
    values ('c0000000-0000-4000-8000-000000000001', null, 'usda', '173944', 'Oats, rolled', 'per_100g', 379)$$,
  'It inserts a shared food'
);
select lives_ok(
  $$insert into catalog.food_servings (food_id, label, grams) values ('c0000000-0000-4000-8000-000000000001', '1 cup', 81)$$,
  'It inserts a serving of a shared food'
);
select is(
  tests.rows($$insert into catalog.foods (owner_id, source, source_id, name, nutrient_basis, energy_kcal)
               values (null, 'usda', '173944', 'Oats, rolled, old-fashioned', 'per_100g', 379)
               on conflict (source, source_id) where source_id is not null
               do update set name = excluded.name$$),
  1::bigint,
  'A re-import updates the shared food in place'
);
select is((select count(*) from catalog.foods where source = 'usda'), 1::bigint, 'A re-import adds no duplicate');
select is(tests.rows($$update catalog.food_servings set grams = 80$$), 1::bigint, 'It updates shared servings');

-- Members' custom foods and servings are out of its reach.
select is((select count(*) from catalog.foods where owner_id is not null), 0::bigint, 'It can''t read custom foods');
select is((select count(*) from catalog.food_servings where food_id = 'c0000000-0000-4000-8000-000000000011'), 0::bigint,
  'It can''t read custom servings');
select is(tests.rows($$update catalog.foods set name = 'x' where id = 'c0000000-0000-4000-8000-000000000011'$$), 0::bigint,
  'It can''t update a custom food');
select is(tests.rows($$update catalog.food_servings set grams = 1 where id = 'c0000000-0000-4000-8000-000000000012'$$), 0::bigint,
  'It can''t update a custom serving');
select throws_ok(
  format($$insert into catalog.foods (owner_id, source, name, nutrient_basis, energy_kcal, protein_g, carbs_g, fat_g)
           values (%L, 'custom', 'Planted', 'per_100g', 1, 1, 1, 1)$$, tests.actor('owner')),
  '42501', null, 'It can''t insert a food as a member'
);
select throws_ok(
  $$insert into catalog.foods (owner_id, source, name, nutrient_basis, energy_kcal, protein_g, carbs_g, fat_g)
    values (null, 'custom', 'Orphan', 'per_100g', 1, 1, 1, 1)$$,
  '42501', null, 'It can''t insert a custom food'
);
select throws_ok(
  $$insert into catalog.food_servings (food_id, label, grams) values ('c0000000-0000-4000-8000-000000000011', 'x', 1)$$,
  '42501', null, 'It can''t add a serving to a custom food'
);
select throws_ok(
  format($$update catalog.foods set owner_id = %L where id = 'c0000000-0000-4000-8000-000000000001'$$, tests.actor('owner')),
  '42501', null, 'It can''t give a shared food to a member'
);
select throws_ok(
  $$update catalog.food_servings set food_id = 'c0000000-0000-4000-8000-000000000011'$$,
  '42501', null, 'It can''t move a shared serving onto a custom food'
);
select throws_ok('delete from catalog.foods', '42501', null, 'It can''t delete foods');
select throws_ok('delete from catalog.food_servings', '42501', null, 'It can''t delete servings');

-- Or any member schema.
select throws_ok('select count(*) from nutrition.food_prices', '42501', null, 'It can''t read prices');
select throws_ok('select count(*) from nutrition.diary_entries', '42501', null, 'It can''t read the diary');
select throws_ok('select count(*) from identity.profiles', '42501', null, 'It can''t read profiles');
select throws_ok('select count(*) from auth.users', '42501', null, 'It can''t read auth.users');

select tests.act_as_postgres();
select is(
  (select name from catalog.foods where id = 'c0000000-0000-4000-8000-000000000011'),
  'Bar',
  'The member''s custom food is unchanged'
);

select * from finish();
rollback;
