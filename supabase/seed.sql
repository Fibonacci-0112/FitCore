-- Local stack only: `supabase start` and `supabase db reset` run this after the migrations. Hosted projects never do.
--
-- A fixed, local-only password for the API's login role, like the local stack's own postgres/postgres. Hosted
-- projects set a generated password out of band and keep it in the secret store (NFR-SEC-5); see supabase/README.md.
alter role fitcore_api with password 'fitcore-local-only';
alter role fitcore_catalog_import with password 'fitcore-catalog-local-only';
