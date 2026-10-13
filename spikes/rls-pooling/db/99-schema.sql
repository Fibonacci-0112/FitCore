-- Minimal slice of the ADR 0003 schema: one grant table and one diary table,
-- with the grant-aware RLS pattern from architecture-options.md §B.3.
-- Spike data only; this is not the real migration.

create schema care;
create schema nutrition;

create table care.relationships (
  id uuid primary key default gen_random_uuid(),
  member_id uuid not null,
  professional_id uuid not null,
  role text not null check (role in ('coach', 'clinician')),
  scopes text[] not null,
  status text not null check (status in ('pending', 'active', 'revoked')),
  granted_at timestamptz,
  revoked_at timestamptz
);
create index on care.relationships (professional_id, member_id) where status = 'active';

-- Live grant check, no caching, so revocation applies on the next request
-- (NFR-PRV-4). SECURITY DEFINER so policies on other tables can call it
-- without granting SELECT on every relationship row.
create function care.has_grant(p_member_id uuid, p_scope text) returns boolean
language sql stable security definer set search_path = ''
as $$
  select exists (
    select 1 from care.relationships r
    where r.member_id = p_member_id
      and r.professional_id = auth.uid()
      and r.status = 'active'
      and p_scope = any (r.scopes)
  )
$$;

create table nutrition.diary_entries (
  id uuid primary key default gen_random_uuid(),
  member_id uuid not null,
  diary_date date not null,
  food_name text not null,
  energy_kcal numeric,          -- null means unknown, never zero
  cost_minor bigint,            -- integer minor units
  currency char(3),
  version int not null default 1,
  created_at timestamptz not null default now()
);
create index on nutrition.diary_entries (member_id, diary_date);

alter table care.relationships enable row level security;
alter table nutrition.diary_entries enable row level security;

create policy relationships_parties on care.relationships for select to authenticated
  using (member_id = (select auth.uid()) or professional_id = (select auth.uid()));

create policy diary_select on nutrition.diary_entries for select to authenticated
  using (member_id = (select auth.uid()) or care.has_grant(member_id, 'nutrition'));
create policy diary_insert on nutrition.diary_entries for insert to authenticated
  with check (member_id = (select auth.uid()));

grant usage on schema care, nutrition to authenticated;
grant select on care.relationships to authenticated;
grant select, insert on nutrition.diary_entries to authenticated;
grant execute on function care.has_grant(uuid, text) to authenticated;

-- Seed: 2,000 members with 60 entries each (120,000 rows). Member ids are
-- deterministic so the harness can address them: 00000000-0000-0000-0000-<n>.
insert into nutrition.diary_entries (member_id, diary_date, food_name, energy_kcal, cost_minor, currency)
select
  ('00000000-0000-0000-0000-' || lpad(m::text, 12, '0'))::uuid,
  current_date - (e % 30),
  'Food ' || e,
  case when e % 10 = 0 then null else 100 + e end,
  case when e % 7 = 0 then null else 50 * e end,
  'USD'
from generate_series(1, 2000) m, generate_series(1, 60) e;

-- Professionals are 00000000-0000-0000-0001-<n>. Coach 1 has an active
-- nutrition grant for member 1; coach 2's grant for member 1 is revoked;
-- coach 3 is active for member 1 but only for training.
insert into care.relationships (member_id, professional_id, role, scopes, status, granted_at, revoked_at) values
  ('00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0001-000000000001', 'coach', '{nutrition,training}', 'active', now(), null),
  ('00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0001-000000000002', 'coach', '{nutrition}', 'revoked', now() - interval '2 days', now() - interval '1 day'),
  ('00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0001-000000000003', 'coach', '{training}', 'active', now(), null);

analyze;
