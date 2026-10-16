-- progress.body_weights for all six actors (BRD §3.2 "Weight & measurements"; NFR-PRV-2; R1 PR 3).
-- A `body_metrics` grantee reads; only the member writes.
begin;
\ir helpers/actors.psql

select plan(27);

select tests.grant_scope('body_metrics');
select tests.act_as_postgres();
insert into progress.body_weights (id, member_id, measured_on, weight_kg)
values ('f0000000-0000-4000-8000-000000000001', tests.actor('owner'), '2026-10-15', 72.4);

-- Owner.
select tests.act_as('owner');
select is((select count(*) from progress.body_weights), 1::bigint, 'The owner reads their weigh-ins');
select lives_ok(
  $$insert into progress.body_weights (id, measured_on, weight_kg) values ('f0000000-0000-4000-8000-000000000002', '2026-10-15', 72.1)$$,
  'The owner weighs in twice on one day');
select is(
  tests.rows($$insert into progress.body_weights (id, measured_on, weight_kg)
               values ('f0000000-0000-4000-8000-000000000002', '2026-10-15', 72.1) on conflict (id) do nothing$$),
  0::bigint, 'A retried save with the same client id inserts nothing');
select is(tests.rows($$update progress.body_weights set weight_kg = 72.0 where id = 'f0000000-0000-4000-8000-000000000002'$$),
  1::bigint, 'The owner corrects a weigh-in');
select throws_ok(
  $$insert into progress.body_weights (id, measured_on, weight_kg) values (gen_random_uuid(), '2026-10-15', 5)$$,
  '23514', null, 'An impossible weight is rejected');

-- Other member.
select tests.act_as('other_member');
select is((select count(*) from progress.body_weights), 0::bigint, 'Another member can''t read the owner''s weigh-ins');
select is(tests.rows($$update progress.body_weights set weight_kg = 50$$), 0::bigint, 'Another member can''t edit them');
select is(tests.rows('delete from progress.body_weights'), 0::bigint, 'Another member can''t delete them');
select throws_ok(
  format($$insert into progress.body_weights (id, member_id, measured_on, weight_kg) values (gen_random_uuid(), %L, '2026-10-16', 99)$$,
         tests.actor('owner')),
  '42501', null, 'Another member can''t log a weight for the owner');

-- Grantee in scope: reads, never writes.
select tests.act_as('grantee_in_scope');
select is((select count(*) from progress.body_weights), 2::bigint, 'A body-metrics grantee reads the member''s weigh-ins');
select is(tests.rows($$update progress.body_weights set weight_kg = 50$$), 0::bigint, 'A grantee can''t edit them');
select is(tests.rows('delete from progress.body_weights'), 0::bigint, 'A grantee can''t delete them');
select throws_ok(
  format($$insert into progress.body_weights (id, member_id, measured_on, weight_kg) values (gen_random_uuid(), %L, '2026-10-16', 99)$$,
         tests.actor('owner')),
  '42501', null, 'A grantee can''t log a weight for the member');

-- Grantee out of scope: holds nutrition and the other scopes, but not body_metrics.
select tests.act_as('grantee_out_of_scope');
select is((select count(*) from progress.body_weights), 0::bigint, 'A grantee without the body_metrics scope reads nothing');
select is(tests.rows($$update progress.body_weights set weight_kg = 50$$), 0::bigint, 'A grantee out of scope can''t edit');
select is(tests.rows('delete from progress.body_weights'), 0::bigint, 'A grantee out of scope can''t delete');

select tests.act_as('revoked_grantee');
select is((select count(*) from progress.body_weights), 0::bigint, 'A revoked grantee reads nothing');
select is(tests.rows($$update progress.body_weights set weight_kg = 50$$), 0::bigint, 'A revoked grantee can''t edit');
select is(tests.rows('delete from progress.body_weights'), 0::bigint, 'A revoked grantee can''t delete');

select tests.act_as('admin');
select is((select count(*) from progress.body_weights), 0::bigint, 'The admin reads no weigh-ins (NFR-PRV-5)');
select is(tests.rows($$update progress.body_weights set weight_kg = 50$$), 0::bigint, 'The admin can''t edit them');
select is(tests.rows('delete from progress.body_weights'), 0::bigint, 'The admin can''t delete them');

select tests.act_as_anon();
select throws_ok('select count(*) from progress.body_weights', '42501', null, 'anon can''t read weigh-ins');
select throws_ok(
  $$insert into progress.body_weights (id, measured_on, weight_kg) values (gen_random_uuid(), '2026-10-16', 70)$$,
  '42501', null, 'anon can''t write weigh-ins');

-- A clinician's body_metrics grant counts only at aal2.
select tests.act_as_postgres();
-- The coach's grant ends, and the same professional gets one as a clinician.
update care.relationships set status = 'revoked', revoked_at = now() where professional_id = tests.actor('grantee_in_scope');
insert into care.relationships (member_id, professional_id, role, scopes, status, granted_at)
values (tests.actor('owner'), tests.actor('grantee_in_scope'), 'clinician', '{body_metrics}', 'active', now());
select tests.act_as('grantee_in_scope');
select is((select count(*) from progress.body_weights), 0::bigint, 'A clinician at aal1 reads nothing');
select tests.act_as('grantee_in_scope', 'aal2');
select is((select count(*) from progress.body_weights), 2::bigint, 'A clinician at aal2 reads the weigh-ins');

select tests.act_as('owner');
select is(tests.rows('delete from progress.body_weights'), 2::bigint, 'The owner deletes their weigh-ins');

select * from finish();
rollback;
