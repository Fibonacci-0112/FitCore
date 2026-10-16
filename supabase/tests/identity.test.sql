-- identity.profiles: private to the member, for all six actors (NFR-PRV-1, NFR-PRV-2; R1 PR 3).
begin;
\ir helpers/actors.psql

select plan(24);

-- Grantees hold every scope: profiles stay private to them all the same (see the migration).
select tests.grant_scope('nutrition');
select tests.act_as_postgres();
update care.relationships set scopes = '{nutrition,training,body_metrics,photos,wellness}'
where professional_id = tests.actor('grantee_in_scope');

insert into identity.profiles (member_id, display_name, birth_date, sex, height_cm, activity_level, time_zone)
values (tests.actor('owner'), 'Owner', '1990-04-01', 'female', 168.0, 'moderate', 'America/Chicago');

-- Owner.
select tests.act_as('owner');
select is((select count(*) from identity.profiles), 1::bigint, 'The owner reads their profile');
select is(tests.rows($$update identity.profiles set display_name = 'Me'$$), 1::bigint, 'The owner updates their profile');
select is((select display_name from identity.profiles), 'Me', 'The update is kept');
select throws_ok(
  format($$insert into identity.profiles (member_id) values (%L)$$, tests.actor('other_member')),
  '42501', null, 'The owner can''t create a profile for someone else');
select throws_ok('delete from identity.profiles', '42501', null, 'Nobody deletes a profile through the app');

-- Other member.
select tests.act_as('other_member');
select is((select count(*) from identity.profiles), 0::bigint, 'Another member can''t read the profile');
select is(tests.rows($$update identity.profiles set display_name = 'x'$$), 0::bigint, 'Another member can''t update it');
select throws_ok(
  format($$insert into identity.profiles (member_id) values (%L)$$, tests.actor('owner')),
  '42501', null, 'Another member can''t write a profile as the owner');
select lives_ok('insert into identity.profiles default values', 'A member creates their own profile');
select is((select currency::text from identity.profiles), 'USD', 'A new profile defaults to USD');

-- Grantees: none of the profile row, whatever their scope.
select tests.act_as('grantee_in_scope');
select is((select count(*) from identity.profiles), 0::bigint, 'A grantee with every scope can''t read the profile row');
select is(tests.rows($$update identity.profiles set display_name = 'x'$$), 0::bigint, 'A grantee can''t update it');

select tests.act_as('grantee_out_of_scope');
select is((select count(*) from identity.profiles), 0::bigint, 'A grantee out of scope can''t read it');
select is(tests.rows($$update identity.profiles set display_name = 'x'$$), 0::bigint, 'A grantee out of scope can''t update it');

select tests.act_as('revoked_grantee');
select is((select count(*) from identity.profiles), 0::bigint, 'A revoked grantee can''t read it');
select is(tests.rows($$update identity.profiles set display_name = 'x'$$), 0::bigint, 'A revoked grantee can''t update it');

-- Admin: account metadata only, never the profile (BRD §3.2).
select tests.act_as('admin');
select is((select count(*) from identity.profiles), 0::bigint, 'The admin can''t read it');
select is(tests.rows($$update identity.profiles set display_name = 'x'$$), 0::bigint, 'The admin can''t update it');

-- anon.
select tests.act_as_anon();
select throws_ok('select count(*) from identity.profiles', '42501', null, 'anon can''t read profiles');
select throws_ok('insert into identity.profiles default values', '42501', null, 'anon can''t write profiles');

-- The row's own rules.
select tests.act_as('owner');
select throws_ok($$update identity.profiles set currency = 'usd'$$, '23514', null, 'A currency is an ISO 4217 code');
select throws_ok($$update identity.profiles set sex = 'x'$$, '23514', null, 'Sex is one the estimate supports');
select throws_ok($$update identity.profiles set unit_system = 'imperial'$$, '23514', null, 'An unknown unit system is rejected');

select tests.act_as_postgres();
select is(
  (select display_name from identity.profiles where member_id = tests.actor('owner')),
  'Me',
  'No one else''s write reached the owner''s profile'
);

select * from finish();
rollback;
