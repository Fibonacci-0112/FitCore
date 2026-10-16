-- Identity: the 18+ gate and the profile created at sign-up (BRD §6.2 Safety; implementation plan PR 4).
--
-- The gate holds on every write path, not only in the app: Supabase Auth's sign-up endpoint is public, so a caller
-- can skip fitcore-app's form. A birth date is checked against the member's local date whenever it is written.
--
-- fitcore-app's email sign-up sends the display name, birth date and time zone as user metadata. The trigger on
-- auth.users turns them into the profile in the same transaction, so an under-18 birth date stops the account from
-- being created at all. Sign-ups without a birth date (later, Google, Microsoft and Apple) get no profile here; the
-- member creates it during onboarding, through the same check.

-- Completed years, as FitCore.Domain's LocalCalendar.AgeOn counts them: someone born on 29 February turns a year
-- older on 1 March in non-leap years.
create function identity.is_adult(p_birth_date date, p_on date) returns boolean
language sql immutable set search_path = ''
as $$
  select extract(year from age(p_on, p_birth_date)) >= 18
$$;

-- The profile trigger runs as the member, so authenticated keeps it.
revoke all on function identity.is_adult(date, date) from public, anon, service_role;
grant execute on function identity.is_adult(date, date) to authenticated;

create function identity.check_profile() returns trigger
language plpgsql set search_path = ''
as $$
declare
  v_today date;
begin
  if not exists (select 1 from pg_catalog.pg_timezone_names where name = new.time_zone) then
    raise exception 'Unknown time zone' using errcode = 'check_violation';
  end if;

  if new.birth_date is not null then
    v_today := (now() at time zone new.time_zone)::date;
    if new.birth_date > v_today then
      raise exception 'A birth date can''t be in the future' using errcode = 'check_violation';
    end if;
    if not identity.is_adult(new.birth_date, v_today) then
      raise exception 'FitCore is for adults 18 and over' using errcode = 'check_violation';
    end if;
  end if;

  return new;
end
$$;

revoke all on function identity.check_profile() from public, anon, authenticated, service_role;

-- Only when the birth date or time zone is written, so an existing row is never re-judged by an unrelated update.
create trigger profiles_check before insert or update of birth_date, time_zone on identity.profiles
  for each row execute function identity.check_profile();

-- Runs as the table owner when Supabase Auth inserts the user. The metadata is the caller's own claim; the profile
-- trigger above judges the birth date like any other write. An unknown time zone falls back to UTC, the default,
-- rather than failing a sign-up over a browser setting.
create function identity.create_profile_from_signup() returns trigger
language plpgsql security definer set search_path = ''
as $$
declare
  v_time_zone text := nullif(new.raw_user_meta_data ->> 'time_zone', '');
begin
  if new.raw_user_meta_data ->> 'birth_date' is null then
    return new;
  end if;

  if v_time_zone is null or not exists (select 1 from pg_catalog.pg_timezone_names where name = v_time_zone) then
    v_time_zone := 'UTC';
  end if;

  insert into identity.profiles (member_id, display_name, birth_date, time_zone)
  values (
    new.id,
    nullif(btrim(new.raw_user_meta_data ->> 'display_name'), ''),
    (new.raw_user_meta_data ->> 'birth_date')::date,
    v_time_zone);

  return new;
end
$$;

revoke all on function identity.create_profile_from_signup() from public, anon, authenticated, service_role;

create trigger profiles_from_signup after insert on auth.users
  for each row execute function identity.create_profile_from_signup();
