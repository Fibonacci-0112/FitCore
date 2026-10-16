-- The six-actor helper (NFR-PRV-2) does what the API's unit of work does: SET LOCAL ROLE plus transaction-local
-- claims. Table tests from PR 3 on rely on it.
begin;
\ir helpers/actors.psql

select plan(13);

select set_eq(
  $$select name from tests.actors$$,
  $$values ('owner'), ('other_member'), ('grantee_in_scope'), ('grantee_out_of_scope'), ('revoked_grantee'), ('admin')$$,
  'The six actors of NFR-PRV-2 exist'
);
select is(
  (select count(*)::int from auth.users u join tests.actors a on a.id = u.id),
  6,
  'Each actor is a user in auth.users'
);

select tests.act_as('owner');
select is(current_user::text, 'authenticated', 'act_as switches to authenticated');
select is(auth.uid(), tests.actor('owner'), 'act_as sets the actor''s claims');
select is(auth.jwt() ->> 'aal', 'aal1', 'A member signs in at aal1');

select tests.act_as('other_member');
select is(auth.uid(), tests.actor('other_member'), 'A test can switch from one actor to another');

select tests.act_as('admin');
select is(auth.jwt() ->> 'aal', 'aal2', 'The admin signs in at aal2');
select is(auth.jwt() -> 'app_metadata' -> 'roles', '["admin"]'::jsonb, 'The admin is marked admin in app_metadata');

select tests.act_as_anon();
select is(current_user::text, 'anon', 'act_as_anon switches to anon');
select is(auth.uid(), null, 'anon has no user id');

select tests.act_as_postgres();
select is(current_user::text, 'postgres', 'act_as_postgres switches back');
select is(nullif(current_setting('request.jwt.claims', true), ''), null, 'act_as_postgres clears the claims');

select throws_ok($$select tests.act_as('nobody')$$, 'P0001', 'Unknown actor nobody', 'An unknown actor is rejected');

select * from finish();
rollback;
