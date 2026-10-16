-- Every sign-up that isn't an OAuth provider must carry a birth date (BRD §6.2 Safety). Supabase Auth's sign-up
-- endpoint is public, so a caller can skip fitcore-app's form and leave the date out; before this, that account got
-- no profile, and nothing stopped it signing in once its email was confirmed. Now the sign-up is refused and no
-- account is created. The provider is in app metadata, which only Supabase Auth writes, so a caller can't claim one.
--
-- Google, Microsoft and Apple don't supply a birth date. Those accounts still start without a profile, and the
-- OAuth part of PR 4 must keep them out of the app until onboarding records an adult birth date (implementation
-- plan PR 4). None of them is enabled yet (supabase/config.toml).
create or replace function identity.create_profile_from_signup() returns trigger
language plpgsql security definer set search_path = ''
as $$
declare
  v_time_zone text := nullif(new.raw_user_meta_data ->> 'time_zone', '');
begin
  if nullif(new.raw_user_meta_data ->> 'birth_date', '') is null then
    if new.raw_app_meta_data ->> 'provider' in ('google', 'azure', 'apple') then
      return new;
    end if;

    raise exception 'FitCore is for adults 18 and over: a birth date is required'
      using errcode = 'check_violation';
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
