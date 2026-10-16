-- targets.targets for all six actors, with the guardrail rules the database holds and their audit entries
-- (BRD §3.2 "Targets", §6.2; NFR-PRV-2; NFR-AUD-1; R1 PR 3). A `nutrition` grantee reads and sets targets.
begin;
\ir helpers/actors.psql

select plan(37);

select tests.grant_scope('nutrition');
select tests.act_as_postgres();
insert into targets.targets (id, member_id, effective_from, kcal, protein_g, carbs_g, fat_g, source, set_by, set_by_id,
                             state, below_soft_limit, below_hard_floor)
values ('70000000-0000-4000-8000-000000000001', tests.actor('owner'), '2026-10-01', 2000, 120, 220, 60, 'estimated',
        'member', tests.actor('owner'), 'accepted', false, false);

-- Owner.
select tests.act_as('owner');
select is((select count(*) from targets.targets), 1::bigint, 'The owner reads their targets');
select lives_ok(
  $$insert into targets.targets (effective_from, kcal, protein_g, carbs_g, fat_g, source, set_by, state, below_soft_limit, below_hard_floor)
    values ('2026-10-10', 2100, 130, 230, 65, 'manual', 'member', 'accepted', false, false)$$,
  'The owner sets a manual target');
select lives_ok(
  $$insert into targets.targets (effective_from, kcal, protein_g, carbs_g, fat_g, source, set_by, state, below_soft_limit, below_hard_floor)
    values ('2026-10-11', 1100, 120, 100, 40, 'manual', 'member', 'accepted', true, false)$$,
  'The owner overrides a soft limit');
select throws_ok(
  $$insert into targets.targets (effective_from, kcal, protein_g, carbs_g, fat_g, source, set_by, state, below_soft_limit, below_hard_floor)
    values ('2026-10-12', 700, 100, 50, 20, 'manual', 'member', 'accepted', true, true)$$,
  '42501', null, 'The owner can''t go past the hard floor');
select throws_ok(
  $$insert into targets.targets (effective_from, kcal, protein_g, carbs_g, fat_g, source, set_by, state, below_soft_limit, below_hard_floor)
    values ('2026-10-12', 700, 100, 50, 20, 'manual', 'member', 'accepted', true, false)$$,
  '23514', null, 'A target under 800 kcal must be flagged past the hard floor');
select throws_ok(
  $$insert into targets.targets (effective_from, kcal, protein_g, carbs_g, fat_g, source, set_by, state, below_soft_limit, below_hard_floor)
    values ('2026-10-12', 2000, 120, 220, 60, 'manual', 'clinician', 'accepted', false, false)$$,
  '42501', null, 'The owner can''t write as a clinician');
select throws_ok(
  $$update targets.targets set kcal = 2500$$,
  '42501', null, 'A written target can''t be edited');
select throws_ok('delete from targets.targets', '42501', null, 'A target can''t be deleted');

-- Other member.
select tests.act_as('other_member');
select is((select count(*) from targets.targets), 0::bigint, 'Another member can''t read the owner''s targets');
select is(tests.rows($$update targets.targets set state = 'accepted', accepted_at = now()$$), 0::bigint,
  'Another member can''t change them');
select throws_ok(
  format($$insert into targets.targets (member_id, effective_from, kcal, protein_g, carbs_g, fat_g, source, set_by, state,
                                        below_soft_limit, below_hard_floor)
           values (%L, '2026-10-12', 1800, 100, 200, 50, 'manual', 'member', 'accepted', false, false)$$, tests.actor('owner')),
  '42501', null, 'Another member can''t set the owner''s target as the owner');
select throws_ok(
  format($$insert into targets.targets (member_id, effective_from, kcal, protein_g, carbs_g, fat_g, source, set_by, state,
                                        below_soft_limit, below_hard_floor)
           values (%L, '2026-10-12', 1800, 100, 200, 50, 'manual', 'coach', 'accepted', false, false)$$, tests.actor('owner')),
  '42501', null, 'Another member can''t set the owner''s target as a coach');

-- Grantee in scope, a coach: reads and sets targets (BRD §3.2).
select tests.act_as('grantee_in_scope');
select is((select count(*) from targets.targets), 3::bigint, 'A nutrition grantee reads the member''s targets');
select lives_ok(
  format($$insert into targets.targets (id, member_id, effective_from, kcal, protein_g, carbs_g, fat_g, source, set_by, state,
                                        below_soft_limit, below_hard_floor)
           values ('70000000-0000-4000-8000-000000000002', %L, '2026-10-13', 1900, 140, 190, 60, 'manual', 'coach',
                   'accepted', false, false)$$, tests.actor('owner')),
  'A coach sets a target within the limits');
select throws_ok(
  format($$insert into targets.targets (member_id, effective_from, kcal, protein_g, carbs_g, fat_g, source, set_by, state,
                                        below_soft_limit, below_hard_floor)
           values (%L, '2026-10-14', 1100, 140, 100, 40, 'manual', 'coach', 'accepted', true, false)$$, tests.actor('owner')),
  '42501', null, 'A coach''s target below a soft limit can''t take effect without the member');
select lives_ok(
  format($$insert into targets.targets (id, member_id, effective_from, kcal, protein_g, carbs_g, fat_g, source, set_by, state,
                                        below_soft_limit, below_hard_floor)
           values ('70000000-0000-4000-8000-000000000003', %L, '2026-10-14', 1100, 140, 100, 40, 'manual', 'coach',
                   'pending', true, false)$$, tests.actor('owner')),
  'A coach''s target below a soft limit is saved as pending');
select throws_ok(
  format($$insert into targets.targets (member_id, effective_from, kcal, protein_g, carbs_g, fat_g, source, set_by, state,
                                        below_soft_limit, below_hard_floor)
           values (%L, '2026-10-14', 700, 100, 50, 20, 'manual', 'coach', 'pending', true, true)$$, tests.actor('owner')),
  -- The grant lets the coach write; the table's own rule stops the value.
  '23514', null, 'A coach can''t go past the hard floor');
select throws_ok(
  format($$insert into targets.targets (member_id, effective_from, kcal, protein_g, carbs_g, fat_g, source, set_by, state,
                                        below_soft_limit, below_hard_floor)
           values (%L, '2026-10-14', 1900, 140, 190, 60, 'manual', 'clinician', 'accepted', false, false)$$, tests.actor('owner')),
  '42501', null, 'A coach can''t write as a clinician');
select throws_ok(
  format($$insert into targets.targets (member_id, effective_from, kcal, protein_g, carbs_g, fat_g, source, set_by, set_by_id,
                                        state, below_soft_limit, below_hard_floor)
           values (%L, '2026-10-14', 1900, 140, 190, 60, 'manual', 'coach', %L, 'accepted', false, false)$$,
         tests.actor('owner'), tests.actor('grantee_out_of_scope')),
  '42501', null, 'A coach can''t write in another professional''s name');
select is(tests.rows($$update targets.targets set state = 'accepted', accepted_at = now()$$), 0::bigint,
  'A coach can''t accept their own pending target');

-- Grantee out of scope, revoked grantee and admin: nothing.
select tests.act_as('grantee_out_of_scope');
select is((select count(*) from targets.targets), 0::bigint, 'A grantee without the nutrition scope reads no targets');
select throws_ok(
  format($$insert into targets.targets (member_id, effective_from, kcal, protein_g, carbs_g, fat_g, source, set_by, state,
                                        below_soft_limit, below_hard_floor)
           values (%L, '2026-10-14', 1900, 140, 190, 60, 'manual', 'coach', 'accepted', false, false)$$, tests.actor('owner')),
  '42501', null, 'A grantee without the nutrition scope can''t set a target');

select tests.act_as('revoked_grantee');
select is((select count(*) from targets.targets), 0::bigint, 'A revoked grantee reads no targets');
select throws_ok(
  format($$insert into targets.targets (member_id, effective_from, kcal, protein_g, carbs_g, fat_g, source, set_by, state,
                                        below_soft_limit, below_hard_floor)
           values (%L, '2026-10-14', 1900, 140, 190, 60, 'manual', 'coach', 'accepted', false, false)$$, tests.actor('owner')),
  '42501', null, 'A revoked grantee can''t set a target');

select tests.act_as('admin');
select is((select count(*) from targets.targets), 0::bigint, 'The admin reads no targets');
select throws_ok(
  format($$insert into targets.targets (member_id, effective_from, kcal, protein_g, carbs_g, fat_g, source, set_by, state,
                                        below_soft_limit, below_hard_floor)
           values (%L, '2026-10-14', 1900, 140, 190, 60, 'manual', 'coach', 'accepted', false, false)$$, tests.actor('owner')),
  '42501', null, 'The admin can''t set a target');

select tests.act_as_anon();
select throws_ok('select count(*) from targets.targets', '42501', null, 'anon can''t read targets');

-- The member accepts the coach's pending target; nothing else in it changes.
select tests.act_as('owner');
select throws_ok(
  $$update targets.targets set state = 'accepted' where id = '70000000-0000-4000-8000-000000000003'$$,
  '42501', null, 'Accepting records when');
select is(
  tests.rows($$update targets.targets set state = 'accepted', accepted_at = now() where id = '70000000-0000-4000-8000-000000000003'$$),
  1::bigint, 'The owner accepts a pending target');
select is(
  tests.rows($$update targets.targets set state = 'pending', accepted_at = null where id = '70000000-0000-4000-8000-000000000003'$$),
  0::bigint, 'An accepted target can''t go back to pending');

-- A clinician goes past the hard floor, at aal2 only (BRD §6.2, NFR-SEC-3).
select tests.act_as_postgres();
-- The coach's grant ends, and the same professional gets one as a clinician.
update care.relationships set status = 'revoked', revoked_at = now() where professional_id = tests.actor('grantee_in_scope');
insert into care.relationships (member_id, professional_id, role, scopes, status, granted_at)
values (tests.actor('owner'), tests.actor('grantee_in_scope'), 'clinician', '{nutrition}', 'active', now());

select tests.act_as('grantee_in_scope');
select is((select count(*) from targets.targets), 0::bigint, 'A clinician at aal1 reads no targets');
select throws_ok(
  format($$insert into targets.targets (member_id, effective_from, kcal, protein_g, carbs_g, fat_g, source, set_by, state,
                                        below_soft_limit, below_hard_floor)
           values (%L, '2026-10-15', 700, 100, 50, 20, 'manual', 'clinician', 'accepted', true, true)$$, tests.actor('owner')),
  '42501', null, 'A clinician at aal1 can''t set a target');

select tests.act_as('grantee_in_scope', 'aal2');
select lives_ok(
  format($$insert into targets.targets (id, member_id, effective_from, kcal, protein_g, carbs_g, fat_g, source, set_by, state,
                                        below_soft_limit, below_hard_floor)
           values ('70000000-0000-4000-8000-000000000004', %L, '2026-10-15', 700, 100, 50, 20, 'manual', 'clinician',
                   'accepted', true, true)$$, tests.actor('owner')),
  'A clinician at aal2 sets a target past the hard floor');

-- The audit log: professional writes, the member's override and the acceptance (NFR-AUD-1, NFR-AUD-2).
select tests.act_as_postgres();
select results_eq(
  format($$select actor_role, action, actor_id from audit.events
           where member_id = %L and entity = 'targets.targets' order by id$$, tests.actor('owner')),
  format($$values ('member', 'target.soft_limit_override', %L::uuid),
                  ('coach', 'target.set_by_professional', %L::uuid),
                  ('coach', 'target.set_by_professional', %L::uuid),
                  ('member', 'target.accepted', %L::uuid),
                  ('clinician', 'target.set_by_professional', %L::uuid)$$,
         tests.actor('owner'), tests.actor('grantee_in_scope'), tests.actor('grantee_in_scope'),
         tests.actor('owner'), tests.actor('grantee_in_scope')),
  'Overrides, professional writes and acceptances are audited with the actor and their role'
);
select is(
  (select after ->> 'kcal' from audit.events where entity_id = '70000000-0000-4000-8000-000000000004'),
  '700', 'An audit entry keeps the value written'
);
select results_eq(
  $$select before ->> 'state', after ->> 'state' from audit.events
    where entity_id = '70000000-0000-4000-8000-000000000003' and action = 'target.accepted'$$,
  $$values ('pending', 'accepted')$$,
  'An acceptance keeps the before and after state'
);
select is(
  (select count(*) from audit.events where entity = 'targets.targets' and after ->> 'kcal' = '2100'),
  0::bigint, 'A member''s target within the limits isn''t audited'
);

select * from finish();
rollback;
