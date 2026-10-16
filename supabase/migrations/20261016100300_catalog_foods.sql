-- Catalog: foods and their servings (BRD §6.1; implementation plan §4; R1 PR 3).
--
-- Two kinds of row share these tables:
--   * shared rows (owner_id is null), from an import such as USDA FoodData Central. Every signed-in user reads them.
--     Only the import job writes them, as fitcore_catalog_import; members, coaches, clinicians and the API can't.
--   * custom foods, private to their owner. Nobody else reads them, grantees included: the diary keeps its own
--     snapshot of each entry, so a grantee never needs the food row.
--
-- Nutrients only, never prices: prices are private per member, in nutrition.food_prices. Every nutrient column is
-- nullable, and null means unknown, never zero (BRD §6.1). Codes match FitCore.Domain's NutrientBasis.

create table catalog.foods (
  id uuid primary key default gen_random_uuid(),
  -- No default: the API sets the caller's id, and the import job sets null, so neither depends on the other's role.
  owner_id uuid references auth.users (id) on delete cascade,
  source text not null check (source in ('custom', 'usda')),
  source_id text check (char_length(source_id) between 1 and 64),
  name text not null check (char_length(name) between 1 and 200),
  brand text check (char_length(brand) between 1 and 200),
  nutrient_basis text not null check (nutrient_basis in ('per_100g', 'per_100ml', 'per_serving')),
  basis_serving_id uuid,
  -- Needed for any mass↔volume conversion, and never guessed (BRD §6.1).
  density_g_per_ml numeric(8, 4) check (density_g_per_ml > 0),
  -- Per basis unit: 100 g, 100 ml or the basis serving.
  energy_kcal numeric(8, 2) check (energy_kcal >= 0),
  protein_g numeric(8, 2) check (protein_g >= 0),
  carbs_g numeric(8, 2) check (carbs_g >= 0),
  fat_g numeric(8, 2) check (fat_g >= 0),
  fiber_g numeric(8, 2) check (fiber_g >= 0),
  sugar_g numeric(8, 2) check (sugar_g >= 0),
  saturated_fat_g numeric(8, 2) check (saturated_fat_g >= 0),
  sodium_mg numeric(9, 2) check (sodium_mg >= 0),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  -- A custom food is owned and declares energy and the three macros (FitCore.Domain Nutrients.RequiredForCustomFoods).
  constraint foods_custom_is_owned_and_complete check (
    source <> 'custom'
    or (owner_id is not null and energy_kcal is not null and protein_g is not null
        and carbs_g is not null and fat_g is not null)),
  -- An imported food is shared and keeps its source's id, so a re-import updates it instead of adding a duplicate.
  constraint foods_imported_is_shared check (source = 'custom' or (owner_id is null and source_id is not null)),
  constraint foods_per_serving_has_basis check ((nutrient_basis = 'per_serving') = (basis_serving_id is not null))
);

create unique index foods_source_unique on catalog.foods (source, source_id) where source_id is not null;
create index foods_owner on catalog.foods (owner_id) where owner_id is not null;

-- Food-specific measures, such as "1 slice = 28 g" or "1 cup, chopped = 128 g" (BRD §6.1).
create table catalog.food_servings (
  id uuid primary key default gen_random_uuid(),
  food_id uuid not null references catalog.foods (id) on delete cascade,
  label text not null check (char_length(label) between 1 and 80),
  grams numeric(10, 3) check (grams > 0),
  ml numeric(10, 3) check (ml > 0),
  check (grams is not null or ml is not null),
  -- Lets other tables require that a serving belongs to the food they name.
  unique (id, food_id)
);

create index food_servings_food on catalog.food_servings (food_id);

-- The basis serving belongs to the same food. Deferred, so a food and its basis serving go in one transaction.
alter table catalog.foods
  add constraint foods_basis_serving_fk foreign key (basis_serving_id, id)
  references catalog.food_servings (id, food_id) deferrable initially deferred;

alter table catalog.foods enable row level security;
alter table catalog.food_servings enable row level security;

-- Members, coaches and clinicians: shared rows plus their own custom foods (BRD §3.2 "Shared food & exercise catalog").
create policy foods_select on catalog.foods for select to authenticated
  using (owner_id is null or owner_id = (select auth.uid()));
create policy foods_insert_own on catalog.foods for insert to authenticated
  with check (owner_id = (select auth.uid()) and source = 'custom');
create policy foods_update_own on catalog.foods for update to authenticated
  using (owner_id = (select auth.uid()))
  with check (owner_id = (select auth.uid()) and source = 'custom');
create policy foods_delete_own on catalog.foods for delete to authenticated
  using (owner_id = (select auth.uid()));

-- A serving follows its food. The subqueries run under the caller's own policies on foods, so they see exactly the
-- foods the caller may see.
create policy food_servings_select on catalog.food_servings for select to authenticated
  using (exists (select 1 from catalog.foods f where f.id = food_id));
create policy food_servings_insert_own on catalog.food_servings for insert to authenticated
  with check (exists (select 1 from catalog.foods f where f.id = food_id and f.owner_id = (select auth.uid())));
create policy food_servings_update_own on catalog.food_servings for update to authenticated
  using (exists (select 1 from catalog.foods f where f.id = food_id and f.owner_id = (select auth.uid())))
  with check (exists (select 1 from catalog.foods f where f.id = food_id and f.owner_id = (select auth.uid())));
create policy food_servings_delete_own on catalog.food_servings for delete to authenticated
  using (exists (select 1 from catalog.foods f where f.id = food_id and f.owner_id = (select auth.uid())));

grant usage on schema catalog to authenticated;
grant select, insert, update, delete on catalog.foods, catalog.food_servings to authenticated;

-- The import job's login role (implementation plan §3.2). It writes outside any caller's identity, so it gets its own
-- least-privilege role and data source, never fitcore_api. It reads, inserts and updates shared rows only: its
-- policies hide members' custom foods and servings, it can't delete, and it has no access to any other schema.
-- Its password is set the way fitcore_api's is: out of band on hosted projects, in supabase/seed.sql locally.
create role fitcore_catalog_import with login noinherit nosuperuser nocreatedb nocreaterole noreplication nobypassrls;

comment on role fitcore_catalog_import is
  'Login role for the catalog import job (implementation plan §3.2). Reads and writes only the shared rows of catalog.foods and catalog.food_servings.';

grant usage on schema catalog to fitcore_catalog_import;
grant select, insert, update on catalog.foods, catalog.food_servings to fitcore_catalog_import;

create policy foods_import_select on catalog.foods for select to fitcore_catalog_import
  using (owner_id is null);
create policy foods_import_insert on catalog.foods for insert to fitcore_catalog_import
  with check (owner_id is null and source <> 'custom');
create policy foods_import_update on catalog.foods for update to fitcore_catalog_import
  using (owner_id is null)
  with check (owner_id is null and source <> 'custom');

create policy food_servings_import_select on catalog.food_servings for select to fitcore_catalog_import
  using (exists (select 1 from catalog.foods f where f.id = food_id and f.owner_id is null));
create policy food_servings_import_insert on catalog.food_servings for insert to fitcore_catalog_import
  with check (exists (select 1 from catalog.foods f where f.id = food_id and f.owner_id is null));
create policy food_servings_import_update on catalog.food_servings for update to fitcore_catalog_import
  using (exists (select 1 from catalog.foods f where f.id = food_id and f.owner_id is null))
  with check (exists (select 1 from catalog.foods f where f.id = food_id and f.owner_id is null));

create function catalog.touch() returns trigger
language plpgsql set search_path = ''
as $$
begin
  new.updated_at := now();
  return new;
end
$$;

revoke all on function catalog.touch() from public, anon, authenticated, service_role;

create trigger foods_touch before update on catalog.foods
  for each row execute function catalog.touch();
