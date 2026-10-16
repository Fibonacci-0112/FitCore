-- audit.events: written by triggers, readable by the member it's about, and append-only for every role
-- (NFR-AUD-1..3; R1 PR 3). Target events are checked in targets.test.sql.
begin;
\ir helpers/actors.psql

select plan(20);

select tests.grant_scope('nutrition');

select results_eq(
  format($$select actor_role, action, entity from audit.events where member_id = %L order by id$$, tests.actor('owner')),
  $$values ('system', 'grant.created', 'care.relationships'),
           ('system', 'grant.created', 'care.relationships'),
           ('system', 'grant.created', 'care.relationships')$$,
  'Creating a grant writes an audit entry'
);

update care.relationships set status = 'revoked', revoked_at = now()
where member_id = tests.actor('owner') and professional_id = tests.actor('grantee_in_scope');

select results_eq(
  format($$select action, before ->> 'status', after ->> 'status' from audit.events
           where member_id = %L order by id desc limit 1$$, tests.actor('owner')),
  $$values ('grant.changed', 'active', 'revoked')$$,
  'Revoking a grant writes an audit entry with the before and after values'
);

select ok(
  (select bool_and(occurred_at is not null and entity_id is not null) from audit.events where member_id = tests.actor('owner')),
  'Each entry records when and which row'
);

-- Reads: the member the entries are about, nobody else (NFR-AUD-4, NFR-PRV-5).

select tests.act_as('owner');
select is((select count(*) from audit.events), 4::bigint, 'The owner reads the entries about their data');

select tests.act_as('other_member');
select is((select count(*) from audit.events), 0::bigint, 'Another member reads none of them');

select tests.act_as('grantee_in_scope');
select is((select count(*) from audit.events), 0::bigint, 'A grantee in scope reads none of them');

select tests.act_as('grantee_out_of_scope');
select is((select count(*) from audit.events), 0::bigint, 'A grantee out of scope reads none of them');

select tests.act_as('revoked_grantee');
select is((select count(*) from audit.events), 0::bigint, 'A revoked grantee reads none of them');

select tests.act_as('admin');
select is((select count(*) from audit.events), 0::bigint, 'The admin reads none of them');

select tests.act_as_anon();
select throws_ok('select count(*) from audit.events', '42501', null, 'anon can''t read the audit log');

-- Writes: nobody adds, edits or deletes an entry through the app (NFR-AUD-3).

select tests.act_as('owner');
select throws_ok(
  format($$insert into audit.events (actor_role, member_id, action, entity) values ('member', %L, 'grant.created', 'x')$$,
         tests.actor('owner')),
  '42501', null, 'The owner can''t forge an entry');
select throws_ok($$update audit.events set action = 'grant.changed'$$, '42501', null, 'The owner can''t edit an entry');
select throws_ok('delete from audit.events', '42501', null, 'The owner can''t delete an entry');
select throws_ok(
  format($$select audit.append(%L, 'grant.created', 'x', null, null, null)$$, tests.actor('owner')),
  '42501', null, 'The owner can''t call audit.append');

select tests.act_as('admin');
select throws_ok('delete from audit.events', '42501', null, 'The admin can''t delete an entry');

-- Not even the table owner can change an entry.
select tests.act_as_postgres();
select throws_ok($$update audit.events set action = 'grant.changed'$$, '42501', 'audit.events is append-only',
  'The table owner can''t edit an entry');
select throws_ok('delete from audit.events', '42501', 'audit.events is append-only', 'The table owner can''t delete an entry');
select throws_ok('truncate audit.events', '42501', 'audit.events is append-only', 'The table owner can''t truncate the log');

select ok(not has_function_privilege('anon', 'audit.append(uuid, text, text, uuid, jsonb, jsonb)', 'execute'),
  'anon can''t call audit.append');
select is_empty(
  $$select grantee || ' ' || privilege_type from information_schema.role_table_grants
    where table_schema = 'audit' and table_name = 'events'
      and grantee in ('anon', 'authenticated', 'service_role', 'fitcore_api', 'PUBLIC')
      and privilege_type <> 'SELECT'$$,
  'No app role holds any privilege on audit.events but SELECT'
);

select * from finish();
rollback;
