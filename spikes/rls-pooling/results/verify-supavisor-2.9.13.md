# Verify results: Supavisor 2.9.13

Output of `dotnet run --project RlsSpike -- verify` on 2026-10-13 (exit code 0), Supabase Postgres 17.6.1.016, Npgsql 10.0.3, EF Core 10 (Npgsql provider 10.0.3). Rows marked CONTROL show what breaks without the rules and are expected to break; their counts vary between runs.

## Correctness

| Endpoint | Check | Result | Detail |
|---|---|---|---|
| direct | RoundTrips: owner reads own diary | pass | 52 rows |
| direct | RoundTrips: other member sees nothing | pass | 0 rows |
| direct | RoundTrips: grantee in scope reads | pass | 52 rows |
| direct | RoundTrips: grantee out of scope sees nothing | pass | 0 rows |
| direct | RoundTrips: revoked grantee sees nothing | pass | 0 rows |
| direct | RoundTrips: member writes own diary | pass | 1 rows |
| direct | RoundTrips: write to another member's diary rejected | pass | SQLSTATE 42501 |
| direct | Batched: owner reads own diary | pass | 52 rows |
| direct | Batched: other member sees nothing | pass | 0 rows |
| direct | Batched: grantee in scope reads | pass | 52 rows |
| direct | Batched: grantee out of scope sees nothing | pass | 0 rows |
| direct | Batched: revoked grantee sees nothing | pass | 0 rows |
| direct | Batched: member writes own diary | pass | 1 rows |
| direct | Batched: write to another member's diary rejected | pass | SQLSTATE 42501 |
| direct | SingleTrip: owner reads own diary | pass | 52 rows |
| direct | SingleTrip: other member sees nothing | pass | 0 rows |
| direct | SingleTrip: grantee in scope reads | pass | 52 rows |
| direct | SingleTrip: grantee out of scope sees nothing | pass | 0 rows |
| direct | SingleTrip: revoked grantee sees nothing | pass | 0 rows |
| direct | SingleTrip: member writes own diary | pass | 1 rows |
| direct | SingleTrip: write to another member's diary rejected | pass | SQLSTATE 42501 |
| direct | EfCore: owner reads own diary | pass | 52 rows |
| direct | EfCore: other member sees nothing | pass | 0 rows |
| direct | EfCore: grantee in scope reads | pass | 52 rows |
| direct | EfCore: member writes own diary | pass | 1 row |
| direct | EfCore: write to another member's diary rejected | pass | SQLSTATE 42501 |
| direct | EfCore: query outside a transaction fails closed | pass | SQLSTATE 42501 |
| direct | Revocation applies on the next request | pass | 52 rows, then 0 |
| direct | Identity is cleared after each request | pass | role authenticator, claims none |
| direct | Concurrent soak: no cross-request leak | pass | 9600 requests, 1920 probes, 0 leaks, 0 stale probes, 0 errors |
| supavisor-session | RoundTrips: owner reads own diary | pass | 52 rows |
| supavisor-session | RoundTrips: other member sees nothing | pass | 0 rows |
| supavisor-session | RoundTrips: grantee in scope reads | pass | 52 rows |
| supavisor-session | RoundTrips: grantee out of scope sees nothing | pass | 0 rows |
| supavisor-session | RoundTrips: revoked grantee sees nothing | pass | 0 rows |
| supavisor-session | RoundTrips: member writes own diary | pass | 1 rows |
| supavisor-session | RoundTrips: write to another member's diary rejected | pass | SQLSTATE 42501 |
| supavisor-session | Batched: owner reads own diary | pass | 52 rows |
| supavisor-session | Batched: other member sees nothing | pass | 0 rows |
| supavisor-session | Batched: grantee in scope reads | pass | 52 rows |
| supavisor-session | Batched: grantee out of scope sees nothing | pass | 0 rows |
| supavisor-session | Batched: revoked grantee sees nothing | pass | 0 rows |
| supavisor-session | Batched: member writes own diary | pass | 1 rows |
| supavisor-session | Batched: write to another member's diary rejected | pass | SQLSTATE 42501 |
| supavisor-session | SingleTrip: owner reads own diary | pass | 52 rows |
| supavisor-session | SingleTrip: other member sees nothing | pass | 0 rows |
| supavisor-session | SingleTrip: grantee in scope reads | pass | 52 rows |
| supavisor-session | SingleTrip: grantee out of scope sees nothing | pass | 0 rows |
| supavisor-session | SingleTrip: revoked grantee sees nothing | pass | 0 rows |
| supavisor-session | SingleTrip: member writes own diary | pass | 1 rows |
| supavisor-session | SingleTrip: write to another member's diary rejected | pass | SQLSTATE 42501 |
| supavisor-session | EfCore: owner reads own diary | pass | 52 rows |
| supavisor-session | EfCore: other member sees nothing | pass | 0 rows |
| supavisor-session | EfCore: grantee in scope reads | pass | 52 rows |
| supavisor-session | EfCore: member writes own diary | pass | 1 row |
| supavisor-session | EfCore: write to another member's diary rejected | pass | SQLSTATE 42501 |
| supavisor-session | EfCore: query outside a transaction fails closed | pass | SQLSTATE 42501 |
| supavisor-session | Revocation applies on the next request | pass | 52 rows, then 0 |
| supavisor-session | Identity is cleared after each request | pass | role authenticator, claims none |
| supavisor-session | Concurrent soak: no cross-request leak | pass | 9600 requests, 1920 probes, 0 leaks, 0 stale probes, 0 errors |
| supavisor-transaction | RoundTrips: owner reads own diary | pass | 52 rows |
| supavisor-transaction | RoundTrips: other member sees nothing | pass | 0 rows |
| supavisor-transaction | RoundTrips: grantee in scope reads | pass | 52 rows |
| supavisor-transaction | RoundTrips: grantee out of scope sees nothing | pass | 0 rows |
| supavisor-transaction | RoundTrips: revoked grantee sees nothing | pass | 0 rows |
| supavisor-transaction | RoundTrips: member writes own diary | pass | 1 rows |
| supavisor-transaction | RoundTrips: write to another member's diary rejected | pass | SQLSTATE 42501 |
| supavisor-transaction | Batched: owner reads own diary | pass | 52 rows |
| supavisor-transaction | Batched: other member sees nothing | pass | 0 rows |
| supavisor-transaction | Batched: grantee in scope reads | pass | 52 rows |
| supavisor-transaction | Batched: grantee out of scope sees nothing | pass | 0 rows |
| supavisor-transaction | Batched: revoked grantee sees nothing | pass | 0 rows |
| supavisor-transaction | Batched: member writes own diary | pass | 1 rows |
| supavisor-transaction | Batched: write to another member's diary rejected | pass | SQLSTATE 42501 |
| supavisor-transaction | SingleTrip: owner reads own diary | pass | 52 rows |
| supavisor-transaction | SingleTrip: other member sees nothing | pass | 0 rows |
| supavisor-transaction | SingleTrip: grantee in scope reads | pass | 52 rows |
| supavisor-transaction | SingleTrip: grantee out of scope sees nothing | pass | 0 rows |
| supavisor-transaction | SingleTrip: revoked grantee sees nothing | pass | 0 rows |
| supavisor-transaction | SingleTrip: member writes own diary | pass | 1 rows |
| supavisor-transaction | SingleTrip: write to another member's diary rejected | pass | SQLSTATE 42501 |
| supavisor-transaction | EfCore: owner reads own diary | pass | 52 rows |
| supavisor-transaction | EfCore: other member sees nothing | pass | 0 rows |
| supavisor-transaction | EfCore: grantee in scope reads | pass | 52 rows |
| supavisor-transaction | EfCore: member writes own diary | pass | 1 row |
| supavisor-transaction | EfCore: write to another member's diary rejected | pass | SQLSTATE 42501 |
| supavisor-transaction | EfCore: query outside a transaction fails closed | pass | SQLSTATE 42501 |
| supavisor-transaction | Revocation applies on the next request | pass | 52 rows, then 0 |
| supavisor-transaction | Identity is cleared after each request | pass | role authenticator, claims none |
| supavisor-transaction | Concurrent soak: no cross-request leak | pass | 9600 requests, 1920 probes, 0 leaks, 0 stale probes, 0 errors |
| direct | CONTROL unsafe session-level SET (No Reset On Close=False) | clean | 0 of 400 no-identity probes saw another caller's role or claims; 0 errors |
| direct | CONTROL unsafe session-level SET (No Reset On Close=True) | **breaks** | 400 of 400 no-identity probes saw another caller's role or claims; 0 errors |
| supavisor-session | CONTROL unsafe session-level SET (No Reset On Close=False) | clean | 0 of 400 no-identity probes saw another caller's role or claims; 0 errors |
| supavisor-session | CONTROL unsafe session-level SET (No Reset On Close=True) | **breaks** | 400 of 400 no-identity probes saw another caller's role or claims; 0 errors |
| supavisor-transaction | CONTROL unsafe session-level SET (No Reset On Close=False) | **breaks** | 5 of 7 no-identity probes saw another caller's role or claims; 16 errors |
| supavisor-transaction | CONTROL unsafe session-level SET (No Reset On Close=True) | **breaks** | 400 of 400 no-identity probes saw another caller's role or claims; 0 errors |
| direct | CONTROL Npgsql default reset-on-close | clean | 640 ok, 0 hung (timed out), 0 landed in another client's open transaction, 0 other errors, 0 of 640 probes saw an identity |
| supavisor-session | CONTROL Npgsql default reset-on-close | clean | 640 ok, 0 hung (timed out), 0 landed in another client's open transaction, 0 other errors, 0 of 640 probes saw an identity |
| supavisor-transaction | CONTROL Npgsql default reset-on-close | **breaks** | 22 ok, 13 hung (timed out), 0 landed in another client's open transaction, 3 other errors, 0 of 9 probes saw an identity |
| supavisor-transaction | CONTROL auto-prepare (Max Auto Prepare=20) under concurrency | **breaks** | 1814 requests, 1419 errors: 42P05: prepared statement "_auto0" already exists |
