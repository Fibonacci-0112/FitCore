-- Identity: the member's profile (BRD §4.2 Account; implementation plan §4; R1 PR 3).
--
-- The values target estimates need (birth date, sex, height, activity level) and the member's display settings
-- (time zone, units, currency). They stay nullable until onboarding fills them in; PR 4 adds sign-up and the 18+ gate.
-- Codes match FitCore.Domain: BiologicalSex and ActivityLevel in snake case.
--
-- Private to the member. The permission matrix lets a granted coach or clinician read the member's name and goals
-- (BRD §3.2), but not the rest of this row, and RLS can't hide columns. That column-limited read arrives with the
-- Coach/Pro roster; until then grantees, like every other user, can't read profiles at all.

create table identity.profiles (
  member_id uuid primary key default auth.uid() references auth.users (id) on delete cascade,
  display_name text check (char_length(display_name) between 1 and 80),
  birth_date date,
  sex text check (sex in ('male', 'female')),
  height_cm numeric(4, 1) check (height_cm between 50 and 272),
  activity_level text check (activity_level in ('sedentary', 'light', 'moderate', 'active', 'very_active')),
  -- An IANA time zone name, set from the device at sign-up (PR 4). "Today" and diary dates come from it, never from
  -- the server clock.
  time_zone text not null default 'UTC' check (char_length(time_zone) between 1 and 64),
  unit_system text not null default 'us_customary' check (unit_system in ('us_customary', 'metric')),
  -- ISO 4217. Prices in another currency leave entries unpriced; R1 does no conversion (BRD §6.1).
  currency char(3) not null default 'USD' check (currency ~ '^[A-Z]{3}$'),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

alter table identity.profiles enable row level security;

create policy profiles_select_own on identity.profiles for select to authenticated
  using (member_id = (select auth.uid()));
create policy profiles_insert_own on identity.profiles for insert to authenticated
  with check (member_id = (select auth.uid()));
create policy profiles_update_own on identity.profiles for update to authenticated
  using (member_id = (select auth.uid())) with check (member_id = (select auth.uid()));

-- No DELETE: the profile goes with the account, through DataRights.
grant usage on schema identity to authenticated;
grant select, insert, update on identity.profiles to authenticated;

create function identity.touch() returns trigger
language plpgsql set search_path = ''
as $$
begin
  new.updated_at := now();
  return new;
end
$$;

revoke all on function identity.touch() from public, anon, authenticated, service_role;

create trigger profiles_touch before update on identity.profiles
  for each row execute function identity.touch();
