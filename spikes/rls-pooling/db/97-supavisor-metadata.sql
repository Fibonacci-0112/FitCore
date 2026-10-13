-- Metadata database for Supavisor, as in Supabase's self-hosted setup.
create database _supabase with owner supabase_admin;
\c _supabase
create schema if not exists _supavisor;
alter schema _supavisor owner to supabase_admin;
\c postgres
