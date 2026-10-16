-- nutrition.food_prices, diary_entries and diary_days for all six actors (BRD §3.2, §6.1; NFR-PRV-2; R1 PR 3).
-- A `nutrition` grantee reads the diary and its days; prices stay private to the member.
begin;
\ir helpers/actors.psql

select plan(63);

select tests.grant_scope('nutrition');
select tests.act_as_postgres();

-- A shared food with a serving, the owner's custom food, another member's custom food, a price for each member,
-- and the owner's diary entry and complete day.
insert into catalog.foods (id, owner_id, source, source_id, name, nutrient_basis, energy_kcal, protein_g, carbs_g, fat_g)
values ('c0000000-0000-4000-8000-000000000001', null, 'usda', '173944', 'Oats, rolled', 'per_100g', 379, 13.2, 67.7, 6.5),
       ('c0000000-0000-4000-8000-000000000011', tests.actor('owner'), 'custom', null, 'Bar', 'per_100g', 400, 30, 40, 12),
       ('c0000000-0000-4000-8000-000000000021', tests.actor('other_member'), 'custom', null, 'Stew', 'per_100g', 120, 8, 10, 5);
insert into catalog.food_servings (id, food_id, label, grams)
values ('c0000000-0000-4000-8000-000000000002', 'c0000000-0000-4000-8000-000000000001', '1 cup', 81),
       ('c0000000-0000-4000-8000-000000000012', 'c0000000-0000-4000-8000-000000000011', '1 bar', 60),
       ('c0000000-0000-4000-8000-000000000022', 'c0000000-0000-4000-8000-000000000021', '1 bowl', 350);

insert into nutrition.food_prices (id, member_id, food_id, package_amount, package_unit, price_minor, currency, effective_date)
values ('d0000000-0000-4000-8000-000000000001', tests.actor('owner'), 'c0000000-0000-4000-8000-000000000001',
        1, 'kg', 499, 'USD', '2026-10-01'),
       ('d0000000-0000-4000-8000-000000000021', tests.actor('other_member'), 'c0000000-0000-4000-8000-000000000021',
        500, 'g', 350, 'USD', '2026-10-01');

insert into nutrition.diary_entries (id, member_id, diary_date, meal, food_id, price_id, amount, unit, food_name, grams,
                                     energy_kcal, protein_g, carbs_g, fat_g, cost_status, cost_minor, currency)
values ('e0000000-0000-4000-8000-000000000001', tests.actor('owner'), '2026-10-15', 'breakfast',
        'c0000000-0000-4000-8000-000000000001', 'd0000000-0000-4000-8000-000000000001', 50, 'g', 'Oats, rolled', 50,
        189.5, 6.6, 33.85, 3.25, 'priced', 25, 'USD');

insert into nutrition.diary_days (member_id, diary_date, is_complete) values (tests.actor('owner'), '2026-10-15', true);

-- Owner.
select tests.act_as('owner');
select is((select count(*) from nutrition.food_prices), 1::bigint, 'The owner reads their own prices only');
select is((select count(*) from nutrition.diary_entries), 1::bigint, 'The owner reads their diary');
select is((select count(*) from nutrition.diary_days), 1::bigint, 'The owner reads their diary days');
select throws_ok(
  $$insert into nutrition.food_prices (food_id, package_amount, package_unit, price_minor, currency, effective_date)
    values ('c0000000-0000-4000-8000-000000000011', 12, 'serving', 1299, 'USD', '2026-10-01')$$,
  '23514', null, 'A per-serving price names its serving');
select lives_ok(
  $$insert into nutrition.food_prices (food_id, package_amount, package_unit, package_serving_id, price_minor, currency, effective_date)
    values ('c0000000-0000-4000-8000-000000000011', 12, 'serving', 'c0000000-0000-4000-8000-000000000012', 1299, 'USD', '2026-10-01')$$,
  'The owner prices their custom food per serving');
select lives_ok(
  $$insert into nutrition.food_prices (food_id, package_amount, package_unit, price_minor, currency, effective_date)
    values ('c0000000-0000-4000-8000-000000000001', 18, 'oz', 389, 'USD', '2026-10-10')$$,
  'The owner prices a shared food without changing it');
select throws_ok(
  $$insert into nutrition.food_prices (food_id, package_amount, package_unit, price_minor, currency, effective_date)
    values ('c0000000-0000-4000-8000-000000000021', 1, 'kg', 100, 'USD', '2026-10-01')$$,
  '42501', null, 'The owner can''t price another member''s custom food');
select throws_ok(
  $$insert into nutrition.food_prices (food_id, package_amount, package_unit, package_serving_id, price_minor, currency, effective_date)
    values ('c0000000-0000-4000-8000-000000000011', 1, 'serving', 'c0000000-0000-4000-8000-000000000002', 100, 'USD', '2026-10-01')$$,
  '23503', null, 'A price''s serving belongs to the priced food');

select lives_ok(
  $$insert into nutrition.diary_entries (id, diary_date, meal, food_id, serving_id, amount, unit, food_name, serving_label,
                                         grams, serving_count, energy_kcal, protein_g, carbs_g, fat_g, cost_status)
    values ('e0000000-0000-4000-8000-000000000002', '2026-10-15', 'snack', 'c0000000-0000-4000-8000-000000000011',
            'c0000000-0000-4000-8000-000000000012', 1, 'serving', 'Bar', '1 bar', 60, 1, 240, 18, 24, 7.2, 'no_price')$$,
  'The owner logs their custom food by the serving');
select is(
  tests.rows($$insert into nutrition.diary_entries (id, diary_date, meal, amount, unit, food_name, cost_status)
               values ('e0000000-0000-4000-8000-000000000002', '2026-10-15', 'snack', 1, 'g', 'Retry', 'no_price')
               on conflict (id) do nothing$$),
  0::bigint, 'A retried save with the same client id inserts nothing');
select is(
  tests.rows($$update nutrition.diary_entries set meal = 'lunch' where id = 'e0000000-0000-4000-8000-000000000001'$$),
  1::bigint, 'The owner edits their entry');
select is(
  (select version from nutrition.diary_entries where id = 'e0000000-0000-4000-8000-000000000001'),
  2, 'An edit bumps the entry''s version');
select lives_ok(
  $$insert into nutrition.diary_days (diary_date, is_complete) values ('2026-10-16', false)$$,
  'The owner marks a day');
select throws_ok(
  $$insert into nutrition.diary_entries (id, diary_date, meal, food_id, amount, unit, food_name, cost_status)
    values (gen_random_uuid(), '2026-10-15', 'dinner', 'c0000000-0000-4000-8000-000000000021', 100, 'g', 'Stew', 'no_price')$$,
  '42501', null, 'The owner can''t log another member''s custom food');
select throws_ok(
  $$insert into nutrition.diary_entries (id, diary_date, meal, food_id, price_id, amount, unit, food_name, cost_status, cost_minor, currency)
    values (gen_random_uuid(), '2026-10-15', 'dinner', 'c0000000-0000-4000-8000-000000000001',
            'd0000000-0000-4000-8000-000000000021', 100, 'g', 'Oats', 'priced', 70, 'USD')$$,
  '42501', null, 'The owner can''t cost an entry with another member''s price');
select throws_ok(
  $$insert into nutrition.diary_entries (id, diary_date, meal, food_id, price_id, amount, unit, food_name, cost_status, cost_minor, currency)
    values (gen_random_uuid(), '2026-10-15', 'dinner', 'c0000000-0000-4000-8000-000000000011',
            'd0000000-0000-4000-8000-000000000001', 100, 'g', 'Bar', 'priced', 50, 'USD')$$,
  '23503', null, 'An entry''s price is for its food');
select throws_ok(
  $$insert into nutrition.diary_entries (id, diary_date, meal, price_id, amount, unit, food_name, cost_status, cost_minor, currency)
    values (gen_random_uuid(), '2026-10-15', 'dinner', 'd0000000-0000-4000-8000-000000000001', 100, 'g', 'Oats', 'priced', 50, 'USD')$$,
  '42501', null, 'A priced entry names its food');
select throws_ok(
  $$insert into nutrition.diary_entries (id, diary_date, meal, food_id, serving_id, amount, unit, food_name, serving_count, cost_status)
    values (gen_random_uuid(), '2026-10-15', 'dinner', 'c0000000-0000-4000-8000-000000000011',
            'c0000000-0000-4000-8000-000000000002', 1, 'serving', 'Bar', 1, 'no_price')$$,
  '23503', null, 'An entry''s serving belongs to its food');
select throws_ok(
  $$insert into nutrition.diary_entries (id, diary_date, meal, food_id, amount, unit, food_name, serving_count, cost_status)
    values (gen_random_uuid(), '2026-10-15', 'dinner', 'c0000000-0000-4000-8000-000000000011', 1, 'serving', 'Bar', 1, 'no_price')$$,
  '42501', null, 'An entry by the serving names its serving');
select throws_ok(
  $$insert into nutrition.diary_entries (id, diary_date, meal, serving_id, amount, unit, food_name, serving_count, cost_status)
    values (gen_random_uuid(), '2026-10-15', 'dinner', 'c0000000-0000-4000-8000-000000000022', 1, 'serving', 'Stew', 1, 'no_price')$$,
  '42501', null, 'The owner can''t name another member''s serving without a food');
select throws_ok(
  $$update nutrition.diary_entries set food_id = null, serving_id = 'c0000000-0000-4000-8000-000000000022'
    where id = 'e0000000-0000-4000-8000-000000000002'$$,
  '42501', null, 'The owner can''t edit an entry to name another member''s serving without a food');
select throws_ok(
  $$insert into nutrition.diary_entries (id, diary_date, meal, amount, unit, food_name, cost_status, cost_minor, currency)
    values (gen_random_uuid(), '2026-10-15', 'dinner', 1, 'g', 'x', 'no_price', 10, 'USD')$$,
  '23514', null, 'Only a priced entry has a cost');
select throws_ok(
  $$insert into nutrition.diary_entries (id, diary_date, meal, amount, unit, food_name, cost_status, cost_minor)
    values (gen_random_uuid(), '2026-10-15', 'dinner', 1, 'g', 'x', 'priced', 10)$$,
  '23514', null, 'A cost has a currency');
select throws_ok(
  $$insert into nutrition.diary_entries (id, diary_date, meal, amount, unit, food_name, cost_status)
    values (gen_random_uuid(), '2026-10-15', 'dinner', 1, 'stone', 'x', 'no_price')$$,
  '23514', null, 'An unknown unit is rejected');

-- Other member.
select tests.act_as('other_member');
select is((select count(*) from nutrition.food_prices where member_id = tests.actor('owner')), 0::bigint,
  'Another member can''t read the owner''s prices');
select is((select count(*) from nutrition.diary_entries), 0::bigint, 'Another member can''t read the owner''s diary');
select is((select count(*) from nutrition.diary_days), 0::bigint, 'Another member can''t read the owner''s days');
select is(tests.rows($$update nutrition.food_prices set price_minor = 1 where id = 'd0000000-0000-4000-8000-000000000001'$$),
  0::bigint, 'Another member can''t edit the owner''s price');
select is(tests.rows($$update nutrition.diary_entries set amount = 1$$), 0::bigint, 'Another member can''t edit the diary');
select is(tests.rows('delete from nutrition.diary_entries'), 0::bigint, 'Another member can''t delete diary entries');
select is(tests.rows($$update nutrition.diary_days set is_complete = false$$), 0::bigint, 'Another member can''t edit days');
select throws_ok(
  format($$insert into nutrition.diary_entries (id, member_id, diary_date, meal, amount, unit, food_name, cost_status)
           values (gen_random_uuid(), %L, '2026-10-15', 'dinner', 1, 'g', 'x', 'no_price')$$, tests.actor('owner')),
  '42501', null, 'Another member can''t log food for the owner');
select lives_ok(
  $$insert into nutrition.diary_days (diary_date, is_complete) values ('2026-10-15', true)$$,
  'Another member marks the same date in their own diary');

-- Grantee in scope: reads the diary and days, never prices, and writes nothing (BRD §3.2).
select tests.act_as('grantee_in_scope');
select is((select count(*) from nutrition.diary_entries), 2::bigint, 'A nutrition grantee reads the member''s diary');
select is((select count(*) from nutrition.diary_days), 2::bigint, 'A nutrition grantee reads the member''s days');
select is(
  (select cost_minor from nutrition.diary_entries where id = 'e0000000-0000-4000-8000-000000000001'),
  25::bigint, 'A nutrition grantee sees each entry''s estimated cost');
select is((select count(*) from nutrition.food_prices), 0::bigint, 'A nutrition grantee can''t read the member''s prices');
select is(tests.rows($$update nutrition.diary_entries set amount = 1$$), 0::bigint, 'A grantee can''t edit the diary');
select is(tests.rows('delete from nutrition.diary_entries'), 0::bigint, 'A grantee can''t delete diary entries');
select is(tests.rows($$update nutrition.diary_days set is_complete = false$$), 0::bigint, 'A grantee can''t edit days');
select throws_ok(
  format($$insert into nutrition.diary_entries (id, member_id, diary_date, meal, amount, unit, food_name, cost_status)
           values (gen_random_uuid(), %L, '2026-10-15', 'dinner', 1, 'g', 'x', 'no_price')$$, tests.actor('owner')),
  '42501', null, 'A grantee can''t log food for the member');
select throws_ok(
  format($$insert into nutrition.diary_days (member_id, diary_date, is_complete) values (%L, '2026-10-17', true)$$,
         tests.actor('owner')),
  '42501', null, 'A grantee can''t mark the member''s days');

-- Grantee out of scope, revoked grantee and admin: nothing.
select tests.act_as('grantee_out_of_scope');
select is((select count(*) from nutrition.diary_entries), 0::bigint, 'A grantee without the nutrition scope reads no diary');
select is((select count(*) from nutrition.food_prices), 0::bigint, 'A grantee out of scope reads no prices');
select is((select count(*) from nutrition.diary_days), 0::bigint, 'A grantee without the nutrition scope reads no days');
select is(tests.rows($$update nutrition.diary_entries set amount = 1$$), 0::bigint, 'A grantee out of scope can''t edit');

select tests.act_as('revoked_grantee');
select is((select count(*) from nutrition.diary_entries), 0::bigint, 'A revoked grantee reads no diary');
select is((select count(*) from nutrition.food_prices), 0::bigint, 'A revoked grantee reads no prices');
select is(tests.rows($$update nutrition.food_prices set price_minor = 1$$), 0::bigint, 'A revoked grantee can''t edit prices');
select is((select count(*) from nutrition.diary_days), 0::bigint, 'A revoked grantee reads no days');
select is(tests.rows($$update nutrition.diary_entries set amount = 1$$), 0::bigint, 'A revoked grantee can''t edit');

select tests.act_as('admin');
select is((select count(*) from nutrition.diary_entries), 0::bigint, 'The admin reads no diary (NFR-PRV-5)');
select is((select count(*) from nutrition.diary_days), 0::bigint, 'The admin reads no days');
select is((select count(*) from nutrition.food_prices), 0::bigint, 'The admin reads no prices');
select is(tests.rows('delete from nutrition.diary_entries'), 0::bigint, 'The admin can''t delete diary entries');

-- anon.
select tests.act_as_anon();
select throws_ok('select count(*) from nutrition.diary_entries', '42501', null, 'anon can''t read the diary');
select throws_ok('select count(*) from nutrition.diary_days', '42501', null, 'anon can''t read days');
select throws_ok('select count(*) from nutrition.food_prices', '42501', null, 'anon can''t read prices');

-- Snapshots: editing the food or deleting the price never rewrites a logged entry (BRD §6.1, NFR-REL-4).
select tests.act_as_postgres();
update catalog.foods set energy_kcal = 999, name = 'Oats, renamed' where id = 'c0000000-0000-4000-8000-000000000001';
delete from nutrition.food_prices where id = 'd0000000-0000-4000-8000-000000000001';
select results_eq(
  $$select food_name, energy_kcal, cost_status, cost_minor, price_id from nutrition.diary_entries
    where id = 'e0000000-0000-4000-8000-000000000001'$$,
  $$values ('Oats, rolled'::text, 189.5::numeric(9, 2), 'priced'::text, 25::bigint, null::uuid)$$,
  'The entry keeps its snapshot when the food changes and its price is deleted'
);

-- Unknown is not zero: an entry with unknown fiber keeps it null.
select is(
  (select fiber_g from nutrition.diary_entries where id = 'e0000000-0000-4000-8000-000000000001'),
  null, 'An unknown nutrient in an entry stays null'
);

-- Deleting the custom food leaves its entry, without the reference.
select tests.act_as('owner');
select is(tests.rows($$delete from catalog.foods where id = 'c0000000-0000-4000-8000-000000000011'$$), 1::bigint,
  'The owner deletes a food they logged');
select results_eq(
  $$select food_id, serving_id, food_name from nutrition.diary_entries where id = 'e0000000-0000-4000-8000-000000000002'$$,
  $$values (null::uuid, null::uuid, 'Bar'::text)$$,
  'The entry stays, with its snapshot'
);
select is(tests.rows('delete from nutrition.diary_entries'), 2::bigint, 'The owner deletes their entries');

select * from finish();
rollback;
