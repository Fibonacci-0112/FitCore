# Benchmark results: Supavisor 2.9.13

Output of `SPIKE_CONCURRENCY=8 SPIKE_REQUESTS=3000 dotnet run --project RlsSpike -- bench` on 2026-10-13, with each endpoint's recommended settings (Npgsql's default reset on direct and session, `No Reset On Close=true` on transaction mode). Postgres, Supavisor and the client ran on one 4-core container host, so absolute numbers understate network cost; compare round trips.

## Latency (8 concurrent callers, 3000 requests per row, ms)

| Endpoint | Strategy | Op | Round trips | p50 | p95 | p99 | Requests/s |
|---|---|---|---|---|---|---|---|
| direct | select 1 (RTT baseline) | - | 1 | 0.48 | 1.27 | 2.40 | 13325 |
| direct | Npgsql RoundTrips | read | 4 | 3.13 | 5.76 | 8.46 | 2382 |
| direct | Npgsql RoundTrips | write | 4 | 3.18 | 7.34 | 10.40 | 2183 |
| direct | Npgsql Batched | read | 2 | 1.71 | 3.82 | 5.49 | 4108 |
| direct | Npgsql Batched | write | 2 | 2.14 | 4.47 | 7.76 | 3261 |
| direct | Npgsql SingleTrip | read | 1 | 1.56 | 3.40 | 4.70 | 4569 |
| direct | Npgsql SingleTrip | write | 1 | 2.47 | 5.00 | 7.45 | 2941 |
| direct | EF Core interceptor | read | 3 | 3.99 | 7.50 | 11.14 | 1813 |
| direct | EF Core interceptor | write | 3 | 6.21 | 11.37 | 15.80 | 1175 |
| supavisor-session | select 1 (RTT baseline) | - | 1 | 0.97 | 3.21 | 4.71 | 6601 |
| supavisor-session | Npgsql RoundTrips | read | 4 | 5.08 | 10.61 | 14.75 | 1416 |
| supavisor-session | Npgsql RoundTrips | write | 4 | 4.49 | 7.77 | 10.00 | 1657 |
| supavisor-session | Npgsql Batched | read | 2 | 3.34 | 7.12 | 9.62 | 2151 |
| supavisor-session | Npgsql Batched | write | 2 | 3.68 | 7.19 | 10.68 | 1974 |
| supavisor-session | Npgsql SingleTrip | read | 1 | 3.12 | 7.65 | 11.09 | 2188 |
| supavisor-session | Npgsql SingleTrip | write | 1 | 3.92 | 7.38 | 10.10 | 1910 |
| supavisor-session | EF Core interceptor | read | 3 | 4.38 | 8.90 | 12.80 | 1630 |
| supavisor-session | EF Core interceptor | write | 3 | 4.96 | 8.98 | 11.59 | 1486 |
| supavisor-transaction | select 1 (RTT baseline) | - | 1 | 0.94 | 2.24 | 4.18 | 7235 |
| supavisor-transaction | Npgsql RoundTrips | read | 4 | 4.67 | 8.40 | 10.85 | 1593 |
| supavisor-transaction | Npgsql RoundTrips | write | 4 | 4.31 | 7.67 | 9.47 | 1720 |
| supavisor-transaction | Npgsql Batched | read | 2 | 3.28 | 6.94 | 10.60 | 2149 |
| supavisor-transaction | Npgsql Batched | write | 2 | 3.26 | 5.96 | 8.70 | 2257 |
| supavisor-transaction | Npgsql SingleTrip | read | 1 | 2.33 | 6.22 | 10.57 | 2810 |
| supavisor-transaction | Npgsql SingleTrip | write | 1 | 3.73 | 7.48 | 10.06 | 1962 |
| supavisor-transaction | EF Core interceptor | read | 3 | 4.51 | 7.74 | 10.46 | 1665 |
| supavisor-transaction | EF Core interceptor | write | 3 | 5.55 | 8.97 | 11.08 | 1367 |
