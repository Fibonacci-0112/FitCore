-- care.relationships and the grant check every grant-aware policy uses (BRD §3.1; NFR-PRV-2..4; R1 PR 3).
begin;
\ir helpers/actors.psql

select plan(34);

select tests.grant_scope('nutrition');
-- A second member's grant to the same coach, which the owner must not see.
insert into care.relationships (member_id, professional_id, role, scopes, status, granted_at)
values (tests.actor('other_member'), tests.actor('grantee_in_scope'), 'coach', '{nutrition}', 'active', now());

-- Reads: each party sees its own relationships, nobody else's.

select tests.act_as('owner');
select is((select count(*) from care.relationships), 3::bigint, 'The owner sees the three grants they gave');

select tests.act_as('other_member');
select is((select count(*) from care.relationships), 1::bigint, 'Another member sees only their own grant');

select tests.act_as('grantee_in_scope');
select is((select count(*) from care.relationships), 2::bigint, 'A grantee sees the grants made to them, from each member');

select tests.act_as('grantee_out_of_scope');
select is((select count(*) from care.relationships), 1::bigint, 'A grantee out of scope sees only their own grant');

select tests.act_as('revoked_grantee');
select is((select count(*) from care.relationships), 1::bigint, 'A revoked grantee sees only their own, revoked, grant');

select tests.act_as('admin');
select is((select count(*) from care.relationships), 0::bigint, 'The admin sees no grants');

select tests.act_as_anon();
select throws_ok('select count(*) from care.relationships', '42501', null, 'anon can''t read grants');

-- Writes: no grant UI until the Coach/Pro tier, so no app role writes grants.

select tests.act_as('owner');
select throws_ok(
  format($$insert into care.relationships (member_id, professional_id, role, scopes, status, granted_at)
           values (%L, %L, 'coach', '{nutrition}', 'active', now())$$, tests.actor('owner'), tests.actor('admin')),
  '42501', null, 'The owner can''t create a grant directly');
select throws_ok(
  $$update care.relationships set status = 'revoked', revoked_at = now()$$,
  '42501', null, 'The owner can''t change a grant directly');
select throws_ok('delete from care.relationships', '42501', null, 'The owner can''t delete a grant');

select tests.act_as('grantee_out_of_scope');
select throws_ok(
  $$update care.relationships set scopes = '{nutrition,training,body_metrics,photos,wellness}'$$,
  '42501', null, 'A grantee can''t widen their own grant');

-- The grant check.

select tests.act_as('grantee_in_scope');
select ok(care.has_grant(tests.actor('owner'), 'nutrition'), 'An active grant covers its scope');
select ok(not care.has_grant(tests.actor('owner'), 'training'), 'An active grant covers no other scope');
select is(care.grant_role(tests.actor('owner'), 'nutrition'), 'coach', 'grant_role returns the grantee''s role');

select tests.act_as('grantee_out_of_scope');
select ok(not care.has_grant(tests.actor('owner'), 'nutrition'), 'A grant without the scope doesn''t cover it');
select ok(care.has_grant(tests.actor('owner'), 'training'), 'The out-of-scope grant does cover its own scopes');

select tests.act_as('revoked_grantee');
select ok(not care.has_grant(tests.actor('owner'), 'nutrition'), 'A revoked grant covers nothing');

select tests.act_as('other_member');
select ok(not care.has_grant(tests.actor('owner'), 'nutrition'), 'Another member has no grant');

select tests.act_as('admin');
select ok(not care.has_grant(tests.actor('owner'), 'nutrition'), 'The admin has no grant');

select tests.act_as('owner');
select ok(not care.has_grant(tests.actor('owner'), 'nutrition'), 'A member''s own data isn''t reached through a grant');

-- A pending grant isn't active yet.
select tests.act_as_postgres();
insert into care.relationships (member_id, professional_id, role, scopes, status)
values (tests.actor('other_member'), tests.actor('grantee_out_of_scope'), 'coach', '{nutrition}', 'pending');
select tests.act_as('grantee_out_of_scope');
select ok(not care.has_grant(tests.actor('other_member'), 'nutrition'), 'A pending grant covers nothing');

-- A clinician's grant counts only at aal2 (NFR-SEC-3).
select tests.act_as_postgres();
insert into care.relationships (member_id, professional_id, role, scopes, status, granted_at)
values (tests.actor('other_member'), tests.actor('revoked_grantee'), 'clinician', '{nutrition}', 'active', now());

select tests.act_as('revoked_grantee');
select ok(not care.has_grant(tests.actor('other_member'), 'nutrition'), 'A clinician''s grant doesn''t count at aal1');
select tests.act_as('revoked_grantee', 'aal2');
select ok(care.has_grant(tests.actor('other_member'), 'nutrition'), 'A clinician''s grant counts at aal2');
select is(care.grant_role(tests.actor('other_member'), 'nutrition'), 'clinician', 'grant_role returns clinician at aal2');

-- Revocation applies to the very next statement: nothing caches the grant (NFR-PRV-4).
select tests.act_as_postgres();
update care.relationships set status = 'revoked', revoked_at = now()
where member_id = tests.actor('owner') and professional_id = tests.actor('grantee_in_scope');
select tests.act_as('grantee_in_scope');
select ok(not care.has_grant(tests.actor('owner'), 'nutrition'), 'Revoking a grant stops it at once');

-- The table's own rules.
select tests.act_as_postgres();
select throws_ok(
  format($$insert into care.relationships (member_id, professional_id, role, scopes, status, granted_at)
           values (%L, %L, 'coach', '{diary}', 'active', now())$$, tests.actor('other_member'), tests.actor('admin')),
  '23514', null, 'An unknown scope is rejected');
select throws_ok(
  format($$insert into care.relationships (member_id, professional_id, role, scopes, status, granted_at)
           values (%L, %L, 'coach', '{}', 'active', now())$$, tests.actor('other_member'), tests.actor('admin')),
  '23514', null, 'A grant needs at least one scope');
select throws_ok(
  format($$insert into care.relationships (member_id, professional_id, role, scopes, status, granted_at)
           values (%L, %L, 'coach', '{nutrition}', 'active', now())$$, tests.actor('owner'), tests.actor('owner')),
  '23514', null, 'A member can''t grant access to themselves');
select throws_ok(
  format($$insert into care.relationships (member_id, professional_id, role, scopes, status, granted_at)
           values (%L, %L, 'clinician', '{training}', 'active', now())$$,
         tests.actor('owner'), tests.actor('grantee_out_of_scope')),
  '23505', null, 'A pair has at most one open relationship');
select throws_ok(
  format($$insert into care.relationships (member_id, professional_id, role, scopes, status)
           values (%L, %L, 'coach', '{nutrition}', 'active')$$, tests.actor('other_member'), tests.actor('admin')),
  '23514', null, 'An active grant records when it was granted');

-- The definer functions are reachable only where they need to be.
select ok(not has_function_privilege('anon', 'care.has_grant(uuid, text)', 'execute'), 'anon can''t call has_grant');
select ok(not has_function_privilege('anon', 'care.grant_role(uuid, text)', 'execute'), 'anon can''t call grant_role');
select ok(
  not has_function_privilege('authenticated', 'care.audit_relationship()', 'execute'),
  'Users can''t call the audit trigger function');
select ok(
  (select prosecdef from pg_catalog.pg_proc where oid = 'care.has_grant(uuid, text)'::regprocedure),
  'has_grant is security definer, so policies can call it without exposing every grant');

select * from finish();
rollback;
