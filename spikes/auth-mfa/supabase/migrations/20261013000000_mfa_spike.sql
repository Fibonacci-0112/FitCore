-- MFA spike schema: a grant table and a diary table, where a clinician's
-- grant only applies in an aal2 session (NFR-SEC-3). Spike data only.

-- The API's own login role (open point in docs/spikes/rls-connection-pooling.md):
-- no table grants of its own, it can only switch to authenticated per request.
create role fitcore_api login noinherit password 'local-spike-password';
grant authenticated to fitcore_api;

create schema care;
create schema nutrition;

create table care.relationships (
  id uuid primary key default gen_random_uuid(),
  member_id uuid not null,
  professional_id uuid not null,
  role text not null check (role in ('coach', 'clinician')),
  scopes text[] not null,
  status text not null check (status in ('pending', 'active', 'revoked'))
);

-- A clinician's grant counts only when the session is aal2, so a clinician
-- without MFA (or not stepped up) sees nothing. Coaches are prompted to
-- enable MFA but not required to, so their grants work at aal1.
create function care.has_grant(p_member_id uuid, p_scope text) returns boolean
language sql stable security definer set search_path = ''
as $$
  select exists (
    select 1 from care.relationships r
    where r.member_id = p_member_id
      and r.professional_id = auth.uid()
      and r.status = 'active'
      and p_scope = any (r.scopes)
      and (r.role <> 'clinician' or (auth.jwt() ->> 'aal') = 'aal2')
  )
$$;

create table nutrition.diary_entries (
  id uuid primary key default gen_random_uuid(),
  member_id uuid not null,
  diary_date date not null default current_date,
  food_name text not null
);

alter table care.relationships enable row level security;
alter table nutrition.diary_entries enable row level security;

create policy diary_select on nutrition.diary_entries for select to authenticated
  using (member_id = (select auth.uid()) or care.has_grant(member_id, 'nutrition'));

grant usage on schema care, nutrition to authenticated;
grant select on nutrition.diary_entries to authenticated;
grant execute on function care.has_grant(uuid, text) to authenticated;
