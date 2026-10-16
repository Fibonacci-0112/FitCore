-- catalog.foods and catalog.food_servings: shared rows readable by every user and written by none of them; custom
-- foods private to their owner, for all six actors (BRD §6.1; NFR-PRV-2; R1 PR 3). The import role is checked in
-- catalog_import_role.test.sql.
begin;
\ir helpers/actors.psql

select plan(44);

select tests.grant_scope('nutrition');
select tests.act_as_postgres();

-- A shared USDA food with a serving, the owner's custom per-serving food, and another member's custom food.
insert into catalog.foods (id, owner_id, source, source_id, name, nutrient_basis, energy_kcal, protein_g, carbs_g, fat_g)
values ('c0000000-0000-4000-8000-000000000001', null, 'usda', '173944', 'Oats, rolled', 'per_100g', 379, 13.2, 67.7, 6.5);
insert into catalog.food_servings (id, food_id, label, grams)
values ('c0000000-0000-4000-8000-000000000002', 'c0000000-0000-4000-8000-000000000001', '1 cup', 81);

insert into catalog.foods (id, owner_id, source, name, nutrient_basis, basis_serving_id, energy_kcal, protein_g, carbs_g, fat_g)
values ('c0000000-0000-4000-8000-000000000011', tests.actor('owner'), 'custom', 'Protein bar', 'per_serving',
        'c0000000-0000-4000-8000-000000000012', 210, 20, 22, 7);
insert into catalog.food_servings (id, food_id, label, grams)
values ('c0000000-0000-4000-8000-000000000012', 'c0000000-0000-4000-8000-000000000011', '1 bar', 60);

insert into catalog.foods (id, owner_id, source, name, nutrient_basis, energy_kcal, protein_g, carbs_g, fat_g)
values ('c0000000-0000-4000-8000-000000000021', tests.actor('other_member'), 'custom', 'Stew', 'per_100g', 120, 8, 10, 5);

set constraints all immediate;

-- Owner.
select tests.act_as('owner');
select set_eq('select id::text from catalog.foods',
  $$values ('c0000000-0000-4000-8000-000000000001'), ('c0000000-0000-4000-8000-000000000011')$$,
  'The owner reads the shared foods and their own');
select is((select count(*) from catalog.food_servings), 2::bigint, 'The owner reads the servings of those foods');
select is(tests.rows($$update catalog.foods set name = 'Bar' where id = 'c0000000-0000-4000-8000-000000000011'$$),
  1::bigint, 'The owner edits their custom food');
select lives_ok(
  $$insert into catalog.food_servings (food_id, label, grams) values ('c0000000-0000-4000-8000-000000000011', 'half', 30)$$,
  'The owner adds a serving to their food');
select lives_ok(
  format($$insert into catalog.foods (owner_id, source, name, nutrient_basis, energy_kcal, protein_g, carbs_g, fat_g)
           values (%L, 'custom', 'Toast', 'per_100g', 265, 9, 49, 3.2)$$, tests.actor('owner')),
  'The owner creates a custom food');

-- Shared rows are read-only to every user (BRD §6.1). fitcore_api writes only as authenticated, so this covers it.
select is(tests.rows($$update catalog.foods set name = 'x' where id = 'c0000000-0000-4000-8000-000000000001'$$),
  0::bigint, 'The owner can''t edit a shared food');
select is(tests.rows($$delete from catalog.foods where id = 'c0000000-0000-4000-8000-000000000001'$$),
  0::bigint, 'The owner can''t delete a shared food');
select throws_ok(
  $$insert into catalog.foods (owner_id, source, source_id, name, nutrient_basis) values (null, 'usda', '1', 'Fake', 'per_100g')$$,
  '42501', null, 'The owner can''t add a shared food');
select throws_ok(
  format($$insert into catalog.foods (owner_id, source, source_id, name, nutrient_basis) values (%L, 'usda', '1', 'x', 'per_100g')$$,
         tests.actor('owner')),
  '42501', null, 'The owner can''t add a food as an import');
select throws_ok(
  $$insert into catalog.food_servings (food_id, label, grams) values ('c0000000-0000-4000-8000-000000000001', 'huge', 1000)$$,
  '42501', null, 'The owner can''t add a serving to a shared food');
select is(tests.rows($$update catalog.food_servings set grams = 1 where id = 'c0000000-0000-4000-8000-000000000002'$$),
  0::bigint, 'The owner can''t edit a shared serving');
select throws_ok(
  $$update catalog.foods set owner_id = null, source = 'usda', source_id = '9' where id = 'c0000000-0000-4000-8000-000000000011'$$,
  '42501', null, 'The owner can''t turn their food into a shared one');

-- Other member.
select tests.act_as('other_member');
select is((select count(*) from catalog.foods where id = 'c0000000-0000-4000-8000-000000000011'), 0::bigint,
  'Another member can''t read the owner''s custom food');
select is((select count(*) from catalog.food_servings where food_id = 'c0000000-0000-4000-8000-000000000011'), 0::bigint,
  'Another member can''t read its servings');
select is(tests.rows($$update catalog.foods set name = 'x' where id = 'c0000000-0000-4000-8000-000000000011'$$),
  0::bigint, 'Another member can''t edit it');
select is(tests.rows($$delete from catalog.foods where id = 'c0000000-0000-4000-8000-000000000011'$$),
  0::bigint, 'Another member can''t delete it');
select throws_ok(
  $$insert into catalog.food_servings (food_id, label, grams) values ('c0000000-0000-4000-8000-000000000011', 'x', 1)$$,
  '42501', null, 'Another member can''t add a serving to it');
select throws_ok(
  format($$insert into catalog.foods (owner_id, source, name, nutrient_basis, energy_kcal, protein_g, carbs_g, fat_g)
           values (%L, 'custom', 'Planted', 'per_100g', 1, 1, 1, 1)$$, tests.actor('owner')),
  '42501', null, 'Another member can''t create a food owned by the owner');
select is((select count(*) from catalog.foods where id = 'c0000000-0000-4000-8000-000000000001'), 1::bigint,
  'Another member reads the shared food');

-- Grantees: custom foods are their owner's alone, whatever the grant (BRD §3.2).
select tests.act_as('grantee_in_scope');
select set_eq('select id::text from catalog.food_servings', $$values ('c0000000-0000-4000-8000-000000000002')$$,
  'A grantee in scope reads the shared serving, not the member''s custom servings');
select is((select count(*) from catalog.foods where owner_id is not null), 0::bigint,
  'A grantee in scope can''t read the member''s custom foods');
select is(tests.rows($$update catalog.foods set name = 'x' where id = 'c0000000-0000-4000-8000-000000000011'$$),
  0::bigint, 'A grantee in scope can''t edit them');
select is((select count(*) from catalog.foods where owner_id is null), 1::bigint, 'A grantee reads the shared food');

select tests.act_as('grantee_out_of_scope');
select set_eq('select id::text from catalog.food_servings', $$values ('c0000000-0000-4000-8000-000000000002')$$,
  'A grantee out of scope reads the shared serving, not the member''s custom servings');
select is((select count(*) from catalog.foods where owner_id is not null), 0::bigint,
  'A grantee out of scope can''t read custom foods');
select is(tests.rows($$delete from catalog.foods where id = 'c0000000-0000-4000-8000-000000000011'$$),
  0::bigint, 'A grantee out of scope can''t delete one');

select tests.act_as('revoked_grantee');
select set_eq('select id::text from catalog.food_servings', $$values ('c0000000-0000-4000-8000-000000000002')$$,
  'A revoked grantee reads the shared serving, not the member''s custom servings');
select is((select count(*) from catalog.foods where owner_id is not null), 0::bigint,
  'A revoked grantee can''t read custom foods');
select is(tests.rows($$update catalog.food_servings set grams = 1$$), 0::bigint, 'A revoked grantee can''t edit servings');

-- Admin: curation of shared rows arrives with the admin tooling; in R1 the admin is read-only like everyone else.
select tests.act_as('admin');
select set_eq('select id::text from catalog.food_servings', $$values ('c0000000-0000-4000-8000-000000000002')$$,
  'The admin reads the shared serving, not the member''s custom servings');
select is((select count(*) from catalog.foods where owner_id is not null), 0::bigint, 'The admin can''t read custom foods');
select is(tests.rows($$update catalog.foods set name = 'x'$$), 0::bigint, 'The admin can''t edit any food');
select throws_ok(
  $$insert into catalog.foods (owner_id, source, source_id, name, nutrient_basis) values (null, 'usda', '2', 'x', 'per_100g')$$,
  '42501', null, 'The admin can''t add a shared food');

-- anon: nothing, not even the shared rows.
select tests.act_as_anon();
select throws_ok('select count(*) from catalog.foods', '42501', null, 'anon can''t read foods');
select throws_ok('select count(*) from catalog.food_servings', '42501', null, 'anon can''t read servings');

-- The rows' own rules (BRD §6.1).
select tests.act_as('owner');
select throws_ok(
  format($$insert into catalog.foods (owner_id, source, name, nutrient_basis, protein_g, carbs_g, fat_g)
           values (%L, 'custom', 'No energy', 'per_100g', 1, 1, 1)$$, tests.actor('owner')),
  '23514', null, 'A custom food without energy is rejected');
select throws_ok(
  format($$insert into catalog.foods (owner_id, source, name, nutrient_basis, energy_kcal, carbs_g, fat_g)
           values (%L, 'custom', 'No protein', 'per_100g', 1, 1, 1)$$, tests.actor('owner')),
  '23514', null, 'A custom food without a macro is rejected');
select throws_ok(
  format($$insert into catalog.foods (owner_id, source, name, nutrient_basis, energy_kcal, protein_g, carbs_g, fat_g)
           values (%L, 'custom', 'x', 'per_serving', 1, 1, 1, 1)$$, tests.actor('owner')),
  '23514', null, 'A per-serving food names its basis serving');
select throws_ok(
  $$insert into catalog.food_servings (food_id, label) values ('c0000000-0000-4000-8000-000000000011', 'nothing')$$,
  '23514', null, 'A serving has grams or millilitres');
select throws_ok(
  format($$insert into catalog.foods (owner_id, source, name, nutrient_basis, density_g_per_ml, energy_kcal, protein_g, carbs_g, fat_g)
           values (%L, 'custom', 'x', 'per_100ml', 0, 1, 1, 1, 1)$$, tests.actor('owner')),
  '23514', null, 'A density is positive');

select tests.act_as_postgres();
select is(
  (select fiber_g from catalog.foods where id = 'c0000000-0000-4000-8000-000000000011'),
  null,
  'An unknown nutrient stays null, never zero'
);
select throws_ok(
  $$insert into catalog.foods (owner_id, source, source_id, name, nutrient_basis) values (null, 'usda', '173944', 'Dup', 'per_100g')$$,
  '23505', null, 'An import id is unique per source'
);

-- Deleting their food removes its servings.
select tests.act_as('owner');
select is(tests.rows($$delete from catalog.foods where id = 'c0000000-0000-4000-8000-000000000011'$$),
  1::bigint, 'The owner deletes their custom food');
select tests.act_as_postgres();
select is((select count(*) from catalog.food_servings where food_id = 'c0000000-0000-4000-8000-000000000011'), 0::bigint,
  'Its servings go with it');

select * from finish();
rollback;
