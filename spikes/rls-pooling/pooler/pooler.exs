# Creates the Supavisor tenant "fitcore" on first start. Clients connect as
# "authenticator.fitcore". Port 5432 in the container is session mode and
# port 6543 is transaction mode.
{:ok, _} = Application.ensure_all_started(:supavisor)

%{rows: [[server_version]]} = Supavisor.Repo.query!("show server_version")

params = %{
  "external_id" => "fitcore",
  "db_host" => "db",
  "db_port" => 5432,
  "db_database" => "postgres",
  "require_user" => true,
  "default_max_clients" => 500,
  "default_parameter_status" => %{"server_version" => server_version},
  "default_pool_size" => String.to_integer(System.get_env("POOL_SIZE", "10")),
  "users" => [
    %{
      "db_user" => "authenticator",
      "db_password" => System.get_env("DB_PASSWORD"),
      "mode_type" => "transaction",
      "pool_size" => String.to_integer(System.get_env("POOL_SIZE", "10")),
      "max_clients" => 500,
      "is_manager" => true
    }
  ]
}

if !Supavisor.Tenants.get_tenant_by_external_id(params["external_id"]) do
  {:ok, _} = Supavisor.Tenants.create_tenant(params)
end
