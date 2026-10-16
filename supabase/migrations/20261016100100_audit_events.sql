-- Audit: the append-only log (NFR-AUD-1..3; implementation plan §3.5; R1 PR 3).
--
-- Triggers in each module write entries in the same transaction as the change, through audit.append. No app role can
-- insert, update or delete an entry directly, and a trigger rejects updates, deletes and truncation from any role,
-- including the table owner. R1 audits grant changes, professional writes to targets, guardrail overrides and target
-- acceptances; admin actions join with the admin tooling.
--
-- Entries hold before and after values (NFR-AUD-2), so they can hold health data. They stay in the database: nothing
-- here sends them to logs or analytics (NFR-PRV-6). The member they're about can read them (NFR-AUD-4). Account
-- deletion pseudonymizes them rather than deleting them (NFR-DAT-4); that job arrives with DataRights.

create table audit.events (
  id bigint generated always as identity primary key,
  occurred_at timestamptz not null default now(),
  -- auth.uid() of the caller; null for work outside any caller's identity, such as a cascade from auth.users.
  actor_id uuid,
  actor_role text not null check (actor_role in ('member', 'coach', 'clinician', 'admin', 'system', 'unknown')),
  -- The member whose data changed. No foreign key: entries outlive the account (NFR-DAT-4).
  member_id uuid not null,
  action text not null check (action ~ '^[a-z_]+\.[a-z_]+$'),
  entity text not null,
  entity_id uuid,
  before jsonb,
  after jsonb
);

create index events_member on audit.events (member_id, occurred_at desc);

alter table audit.events enable row level security;

create policy events_select_member on audit.events for select to authenticated
  using (member_id = (select auth.uid()));

grant usage on schema audit to authenticated;
grant select on audit.events to authenticated;

-- Appends one entry. Only trigger functions, which run as the table owner, can call it.
create function audit.append(
  p_member_id uuid, p_action text, p_entity text, p_entity_id uuid, p_before jsonb, p_after jsonb) returns void
language plpgsql security definer set search_path = ''
as $$
declare
  v_actor uuid := auth.uid();
  v_role text;
begin
  if v_actor is null then
    v_role := 'system';
  elsif v_actor = p_member_id then
    v_role := 'member';
  else
    -- The professional's role in their active relationship with the member. Failing that, in their latest one, so
    -- the change that revokes a grant still shows who made it.
    select r.role into v_role
    from care.relationships r
    where r.member_id = p_member_id and r.professional_id = v_actor
    order by r.status = 'active' desc, r.created_at desc
    limit 1;

    if v_role is null then
      v_role := case
        when coalesce(auth.jwt() -> 'app_metadata' -> 'roles', '[]'::jsonb) ? 'admin' then 'admin'
        else 'unknown'
      end;
    end if;
  end if;

  insert into audit.events (actor_id, actor_role, member_id, action, entity, entity_id, before, after)
  values (v_actor, v_role, p_member_id, p_action, p_entity, p_entity_id, p_before, p_after);
end
$$;

revoke all on function audit.append(uuid, text, text, uuid, jsonb, jsonb) from public, anon, authenticated, service_role;

-- NFR-AUD-3: no one edits or deletes an entry. Revoking privileges stops the app roles; this stops the owner too.
create function audit.reject_change() returns trigger
language plpgsql set search_path = ''
as $$
begin
  raise exception 'audit.events is append-only' using errcode = '42501';
end
$$;

revoke all on function audit.reject_change() from public, anon, authenticated, service_role;

create trigger events_append_only before update or delete on audit.events
  for each row execute function audit.reject_change();
create trigger events_no_truncate before truncate on audit.events
  for each statement execute function audit.reject_change();
