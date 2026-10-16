-- Care: grants from a member to a coach or clinician (BRD §3.1; implementation plan §4; R1 PR 3).
--
-- The table exists from R1 so every policy on member data is grant-aware from the start (architecture-options §7.4).
-- R1 has no grant UI: nothing but the tests writes it until the Coach/Pro tier adds invites and revocation.
--
-- Scopes follow BRD §3.1. In R1, `nutrition` covers the food diary, diary days and targets, and `body_metrics` covers
-- weigh-ins. `training`, `photos` and `wellness` have no R1 tables yet.

create table care.relationships (
  id uuid primary key default gen_random_uuid(),
  member_id uuid not null references auth.users (id) on delete cascade,
  professional_id uuid not null references auth.users (id) on delete cascade,
  role text not null check (role in ('coach', 'clinician')),
  scopes text[] not null check (
    cardinality(scopes) > 0
    and scopes <@ array['nutrition', 'training', 'body_metrics', 'photos', 'wellness']::text[]),
  status text not null check (status in ('pending', 'active', 'revoked')),
  created_at timestamptz not null default now(),
  granted_at timestamptz,
  revoked_at timestamptz,
  check (member_id <> professional_id),
  check (status <> 'active' or granted_at is not null),
  check (status <> 'revoked' or revoked_at is not null)
);

-- One open relationship per pair; a revoked one stays as history.
create unique index relationships_open_pair on care.relationships (member_id, professional_id)
  where status in ('pending', 'active');
-- The grant check: the professional's active grants for a member.
create index relationships_active_grant on care.relationships (professional_id, member_id) where status = 'active';
create index relationships_member on care.relationships (member_id);

alter table care.relationships enable row level security;

-- Both parties see the relationship. Writes arrive with the Coach/Pro tier.
create policy relationships_select_parties on care.relationships for select to authenticated
  using (member_id = (select auth.uid()) or professional_id = (select auth.uid()));

grant usage on schema care to authenticated;
grant select on care.relationships to authenticated;

-- The caller's role in an active grant from p_member_id that covers p_scope, or null. It reads the live table with no
-- caching, so a revocation applies to the next request (NFR-PRV-4). A clinician's grant counts only in an aal2
-- session (NFR-SEC-3; auth spike §4). SECURITY DEFINER, so policies on other tables can call it without the caller
-- seeing every relationship row. It answers only for the caller's own grants.
create function care.grant_role(p_member_id uuid, p_scope text) returns text
language sql stable security definer set search_path = ''
as $$
  select r.role
  from care.relationships r
  where r.member_id = p_member_id
    and r.professional_id = auth.uid()
    and r.status = 'active'
    and p_scope = any (r.scopes)
    and (r.role <> 'clinician' or (auth.jwt() ->> 'aal') = 'aal2')
$$;

-- Whether the caller holds an active grant from p_member_id that covers p_scope. Every grant-aware policy uses it.
create function care.has_grant(p_member_id uuid, p_scope text) returns boolean
language sql stable security definer set search_path = ''
as $$
  select care.grant_role(p_member_id, p_scope) is not null
$$;

revoke all on function care.grant_role(uuid, text), care.has_grant(uuid, text) from public, anon, service_role;
grant execute on function care.grant_role(uuid, text), care.has_grant(uuid, text) to authenticated;

-- Grant changes are audited (NFR-AUD-1). audit.append is created by the next migration; PL/pgSQL resolves it when the
-- trigger first runs.
create function care.audit_relationship() returns trigger
language plpgsql security definer set search_path = ''
as $$
begin
  if tg_op = 'INSERT' then
    perform audit.append(new.member_id, 'grant.created', 'care.relationships', new.id, null, to_jsonb(new));
    return new;
  elsif tg_op = 'UPDATE' then
    perform audit.append(new.member_id, 'grant.changed', 'care.relationships', new.id, to_jsonb(old), to_jsonb(new));
    return new;
  else
    perform audit.append(old.member_id, 'grant.deleted', 'care.relationships', old.id, to_jsonb(old), null);
    return old;
  end if;
end
$$;

revoke all on function care.audit_relationship() from public, anon, authenticated, service_role;

create trigger relationships_audit after insert or update or delete on care.relationships
  for each row execute function care.audit_relationship();
