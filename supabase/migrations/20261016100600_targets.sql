-- Targets: daily calorie and macro targets, as a history (BRD §6.2; implementation plan §4; R1 PR 3).
--
-- The newest accepted target wins; rows are never deleted. Who may set what (BRD §3.2 "Targets", §6.2):
--   * The member sets their own targets, estimated or manual, accepted at once. Past a soft limit only after the
--     warning round trip in the API, and the override is audited. Never past the hard floor.
--   * A coach or clinician with an active `nutrition` grant sets targets for the member. A coach's target below a
--     soft limit stays pending until the member accepts it. Only a clinician may go past the hard floor.
--   * The member accepts a pending target. PR 5 adds the rest of the precedence rules in the Targets module.
--
-- The guardrails themselves are computed by FitCore.Domain (TargetGuardrails) and stored in the two flags. The
-- database checks the flags against who set the row, and holds one hard limit itself, as a second layer.

create table targets.targets (
  id uuid primary key default gen_random_uuid(),
  member_id uuid not null default auth.uid() references auth.users (id) on delete cascade,
  effective_from date not null,
  kcal integer not null check (kcal between 1 and 10000),
  protein_g numeric(6, 1) not null check (protein_g >= 0),
  carbs_g numeric(6, 1) not null check (carbs_g >= 0),
  fat_g numeric(6, 1) not null check (fat_g >= 0),
  source text not null check (source in ('estimated', 'manual')),
  set_by text not null check (set_by in ('member', 'coach', 'clinician')),
  -- Who set it. Cleared if a professional's account is deleted; the audit entry keeps the id (NFR-DAT-4).
  set_by_id uuid default auth.uid() references auth.users (id) on delete set null,
  state text not null check (state in ('pending', 'accepted', 'superseded')),
  below_soft_limit boolean not null,
  below_hard_floor boolean not null,
  accepted_at timestamptz,
  created_at timestamptz not null default now(),
  -- Only a clinician goes past the hard floor (BRD §6.2).
  constraint targets_hard_floor_is_clinical check (not below_hard_floor or set_by = 'clinician'),
  -- The hard calorie floor, GuardrailSettings.HardCalorieFloorKcal. Change both together.
  constraint targets_below_800_kcal_is_flagged check (kcal >= 800 or below_hard_floor),
  -- A coach's target below a soft limit takes effect only once the member accepts it.
  constraint targets_coach_soft_limit_needs_acceptance check (
    not (set_by = 'coach' and below_soft_limit and state = 'accepted' and accepted_at is null)),
  constraint targets_member_sets_own check ((set_by = 'member') = (set_by_id = member_id))
);

create index targets_member on targets.targets (member_id, effective_from desc, created_at desc);

alter table targets.targets enable row level security;

create policy targets_select on targets.targets for select to authenticated
  using (member_id = (select auth.uid()) or care.has_grant(member_id, 'nutrition'));

create policy targets_insert_member on targets.targets for insert to authenticated
  with check (
    member_id = (select auth.uid())
    and set_by = 'member'
    and set_by_id = (select auth.uid())
    and state = 'accepted'
    and not below_hard_floor);

-- set_by must be the role of the caller's live grant, so a coach can't write as a clinician, and a clinician's
-- grant counts only at aal2 (care.grant_role).
create policy targets_insert_professional on targets.targets for insert to authenticated
  with check (
    member_id <> (select auth.uid())
    and set_by = care.grant_role(member_id, 'nutrition')
    and set_by_id = (select auth.uid())
    and accepted_at is null
    and state = case when set_by = 'coach' and below_soft_limit then 'pending' else 'accepted' end);

-- The member accepts a pending target. Nothing else in a row changes once written.
create policy targets_accept_pending on targets.targets for update to authenticated
  using (member_id = (select auth.uid()) and state = 'pending')
  with check (member_id = (select auth.uid()) and state = 'accepted' and accepted_at is not null);

grant usage on schema targets to authenticated;
grant select, insert on targets.targets to authenticated;
grant update (state, accepted_at) on targets.targets to authenticated;

-- Audits professional writes, the member's soft-limit overrides and acceptances (NFR-AUD-1).
create function targets.audit_target() returns trigger
language plpgsql security definer set search_path = ''
as $$
begin
  if tg_op = 'INSERT' then
    if new.set_by <> 'member' then
      perform audit.append(new.member_id, 'target.set_by_professional', 'targets.targets', new.id, null, to_jsonb(new));
    elsif new.below_soft_limit then
      perform audit.append(new.member_id, 'target.soft_limit_override', 'targets.targets', new.id, null, to_jsonb(new));
    end if;
  elsif new.state is distinct from old.state then
    perform audit.append(new.member_id, 'target.' || new.state, 'targets.targets', new.id, to_jsonb(old), to_jsonb(new));
  end if;

  return new;
end
$$;

revoke all on function targets.audit_target() from public, anon, authenticated, service_role;

create trigger targets_audit after insert or update on targets.targets
  for each row execute function targets.audit_target();
