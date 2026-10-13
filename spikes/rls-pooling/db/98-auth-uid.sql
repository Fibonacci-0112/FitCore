-- The image ships the legacy auth.uid(), which reads only
-- request.jwt.claim.sub. Hosted projects get this definition from Supabase
-- Auth's migrations: it also reads the sub claim from request.jwt.claims.
create or replace function auth.uid() returns uuid
language sql stable
as $$
  select coalesce(
    nullif(current_setting('request.jwt.claim.sub', true), ''),
    (nullif(current_setting('request.jwt.claims', true), '')::jsonb ->> 'sub')
  )::uuid
$$;

-- The API logs in as authenticator, as PostgREST does on Supabase.
alter role authenticator with password 'spike-password';
