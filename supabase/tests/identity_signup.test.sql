-- The 18+ gate and the profile created at sign-up (BRD §6.2 Safety; implementation plan PR 4).
begin;
\ir helpers/actors.psql

select plan(23);

-- The rule matches FitCore.Domain's LocalCalendar.IsOldEnough, including 29 February.
select ok(identity.is_adult('2008-10-14', '2026-10-14'), 'Someone turns 18 on their birthday');
select ok(not identity.is_adult('2008-10-15', '2026-10-14'), 'A day short of 18 is too young');
select ok(not identity.is_adult('2008-02-29', '2026-02-28'), 'Born on 29 February: still 17 on 28 February');
select ok(identity.is_adult('2008-02-29', '2026-03-01'), 'Born on 29 February: 18 on 1 March');

-- Sign-up with metadata, as fitcore-app's form sends it.
insert into auth.users (id, aud, role, email, raw_user_meta_data)
values ('b0000000-0000-4000-8000-000000000001', 'authenticated', 'authenticated', 'adult@signup.test',
  '{"display_name": " Ada ", "birth_date": "1990-04-01", "time_zone": "America/Chicago"}');
select results_eq(
  $$select display_name, birth_date, time_zone from identity.profiles
    where member_id = 'b0000000-0000-4000-8000-000000000001'$$,
  $$values ('Ada'::text, '1990-04-01'::date, 'America/Chicago'::text)$$,
  'An adult sign-up creates the profile from the metadata'
);

select throws_ok(
  format($$insert into auth.users (id, aud, role, email, raw_user_meta_data)
    values ('b0000000-0000-4000-8000-000000000002', 'authenticated', 'authenticated', 'minor@signup.test',
      '{"birth_date": "%s"}')$$, (current_date - interval '17 years')::date),
  '23514', 'FitCore is for adults 18 and over', 'An under-18 sign-up is refused');
select is(
  (select count(*) from auth.users where id = 'b0000000-0000-4000-8000-000000000002'),
  0::bigint,
  'The refused sign-up leaves no account'
);

insert into auth.users (id, aud, role, email, raw_user_meta_data)
values ('b0000000-0000-4000-8000-000000000003', 'authenticated', 'authenticated', 'no-tz@signup.test',
  '{"birth_date": "1990-04-01", "time_zone": "Mars/Olympus_Mons"}');
select is(
  (select time_zone from identity.profiles where member_id = 'b0000000-0000-4000-8000-000000000003'),
  'UTC',
  'An unknown time zone at sign-up falls back to UTC'
);

-- Supabase Auth's sign-up endpoint is public, so a caller can leave the birth date out of the metadata.
select throws_ok(
  $$insert into auth.users (id, aud, role, email, raw_app_meta_data, raw_user_meta_data)
    values ('b0000000-0000-4000-8000-000000000005', 'authenticated', 'authenticated', 'no-date@signup.test',
      '{"provider": "email", "providers": ["email"]}', '{"display_name": "No date"}')$$,
  '23514', 'FitCore is for adults 18 and over: a birth date is required',
  'An email sign-up without a birth date is refused');
select throws_ok(
  $$insert into auth.users (id, aud, role, email, raw_app_meta_data, raw_user_meta_data)
    values ('b0000000-0000-4000-8000-000000000006', 'authenticated', 'authenticated', 'empty-date@signup.test',
      '{"provider": "email"}', '{"birth_date": ""}')$$,
  '23514', 'FitCore is for adults 18 and over: a birth date is required',
  'An email sign-up with an empty birth date is refused');
select throws_ok(
  $$insert into auth.users (id, aud, role, email, raw_user_meta_data)
    values ('b0000000-0000-4000-8000-000000000007', 'authenticated', 'authenticated', 'no-provider@signup.test', '{}')$$,
  '23514', 'FitCore is for adults 18 and over: a birth date is required',
  'A sign-up with no provider and no birth date is refused');
select is(
  (select count(*) from auth.users where id in ('b0000000-0000-4000-8000-000000000005',
    'b0000000-0000-4000-8000-000000000006', 'b0000000-0000-4000-8000-000000000007')),
  0::bigint,
  'The refused sign-ups leave no account'
);

-- OAuth providers supply no birth date; onboarding collects it before the member gets in (implementation plan PR 4).
insert into auth.users (id, aud, role, email, raw_app_meta_data, raw_user_meta_data)
values ('b0000000-0000-4000-8000-000000000004', 'authenticated', 'authenticated', 'oauth@signup.test',
  '{"provider": "google", "providers": ["google"]}', '{}');
select is(
  (select count(*) from identity.profiles where member_id = 'b0000000-0000-4000-8000-000000000004'),
  0::bigint,
  'An OAuth sign-up without a birth date gets no profile yet'
);

-- The member's own writes go through the same gate.
select tests.act_as('owner');
select throws_ok(
  format($$insert into identity.profiles (birth_date) values ('%s')$$, (current_date - interval '10 years')::date),
  '23514', 'FitCore is for adults 18 and over', 'A member can''t create a profile with an under-18 birth date');
select lives_ok($$insert into identity.profiles (birth_date) values ('1990-04-01')$$, 'An adult creates their profile');
select throws_ok(
  format($$update identity.profiles set birth_date = '%s'$$, (current_date - interval '10 years')::date),
  '23514', 'FitCore is for adults 18 and over', 'A member can''t change their birth date to an under-18 one');
select throws_ok(
  format($$update identity.profiles set birth_date = '%s'$$, current_date + 2),
  '23514', 'A birth date can''t be in the future', 'A birth date in the future is refused');
select throws_ok(
  $$update identity.profiles set time_zone = 'Mars/Olympus_Mons'$$,
  '23514', 'Unknown time zone', 'An unknown time zone is refused');
select lives_ok($$update identity.profiles set time_zone = 'Pacific/Auckland'$$, 'A real time zone is accepted');
select lives_ok($$update identity.profiles set display_name = 'Owner'$$, 'An update that doesn''t touch the birth date isn''t re-judged');
select is(
  (select birth_date from identity.profiles),
  '1990-04-01'::date,
  'The refused changes left the birth date alone'
);

-- Nobody but the database calls the trigger functions.
select ok(
  not has_function_privilege('authenticated', 'identity.create_profile_from_signup()', 'execute'),
  'authenticated can''t call the sign-up function'
);
select ok(
  not has_function_privilege('anon', 'identity.is_adult(date, date)', 'execute'),
  'anon can''t call the age rule'
);

select * from finish();
rollback;
