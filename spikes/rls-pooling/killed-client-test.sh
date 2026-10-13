#!/bin/bash
# Killed-client check (findings §3.2, item 2): a victim opens a transaction through
# Supavisor transaction mode, sets claims, and is killed without COMMIT or ROLLBACK.
# The next clients print their backend, role, claims and the rows they can see.
# With the leak, a next client shows the victim's backend, role authenticated and
# member 42's claims. Without it, they fail with "permission denied" (no identity).
set -u
export PGPASSWORD=spike-password
out=$(mktemp)
POOL="host=localhost port=54330 user=authenticator.fitcore dbname=postgres"
for trial in 1 2 3; do
  fifo=$(mktemp -u); mkfifo "$fifo"
  psql -qtA "$POOL" < "$fifo" > "$out" 2>&1 &
  vpid=$!
  exec 3>"$fifo"
  printf "begin;\nset local role authenticated;\nselect set_config('request.jwt.claims', '{\"sub\":\"00000000-0000-0000-0000-000000000042\"}', true);\nselect 'victim backend ' || pg_backend_pid();\n" >&3
  sleep 2
  kill -9 "$vpid"; exec 3>&-; rm -f "$fifo"; sleep 1
  echo "trial $trial: $(grep victim "$out")"
  for i in 1 2 3 4 5 6; do
    psql -qtA "$POOL" -c "select 'next client backend ' || pg_backend_pid(), current_user, coalesce(nullif(current_setting('request.jwt.claims', true),''),'<none>'), (select count(*) from nutrition.diary_entries where member_id = auth.uid()) as rows_visible" 2>&1 | head -1
  done
done
psql -tA "host=localhost port=54322 user=supabase_admin dbname=postgres" -c "select pid, state from pg_stat_activity where usename='authenticator' and backend_type='client backend'"
