# Benchmark results: Supavisor 2.9.13

Output of `SPIKE_CONCURRENCY=8 SPIKE_REQUESTS=3000 dotnet run --project RlsSpike -- bench` on 2026-10-13. Postgres, Supavisor and the client ran on one 4-core container host, so absolute numbers understate network cost; compare round trips.

## Latency (8 concurrent callers, 3000 requests per row, ms)

| Endpoint | Strategy | Op | Round trips | p50 | p95 | p99 | Requests/s |
|---|---|---|---|---|---|---|---|
| direct | select 1 (RTT baseline) | - | 1 | 0.54 | 1.68 | 3.32 | 11328 |
| direct | Npgsql RoundTrips | read | 4 | 3.57 | 7.94 | 12.70 | 2014 |
| direct | Npgsql RoundTrips | write | 4 | 4.05 | 10.72 | 16.31 | 1627 |
| direct | Npgsql Batched | read | 2 | 2.12 | 4.76 | 8.42 | 3277 |
| direct | Npgsql Batched | write | 2 | 2.51 | 4.78 | 7.02 | 2906 |
| direct | Npgsql SingleTrip | read | 1 | 1.54 | 3.64 | 6.14 | 4480 |
| direct | Npgsql SingleTrip | write | 1 | 2.04 | 4.24 | 6.97 | 3492 |
| direct | EF Core interceptor | read | 3 | 4.04 | 7.27 | 9.70 | 1838 |
| direct | EF Core interceptor | write | 3 | 5.79 | 10.00 | 13.56 | 1271 |
| supavisor-session | select 1 (RTT baseline) | - | 1 | 0.74 | 1.77 | 4.39 | 8899 |
| supavisor-session | Npgsql RoundTrips | read | 4 | 4.41 | 8.47 | 11.30 | 1661 |
| supavisor-session | Npgsql RoundTrips | write | 4 | 4.47 | 7.52 | 10.08 | 1672 |
| supavisor-session | Npgsql Batched | read | 2 | 2.51 | 4.94 | 6.85 | 2942 |
| supavisor-session | Npgsql Batched | write | 2 | 3.04 | 5.80 | 8.35 | 2413 |
| supavisor-session | Npgsql SingleTrip | read | 1 | 2.39 | 5.96 | 8.66 | 2823 |
| supavisor-session | Npgsql SingleTrip | write | 1 | 3.39 | 6.37 | 8.30 | 2213 |
| supavisor-session | EF Core interceptor | read | 3 | 5.32 | 9.56 | 12.43 | 1393 |
| supavisor-session | EF Core interceptor | write | 3 | 6.03 | 9.81 | 13.09 | 1252 |
| supavisor-transaction | select 1 (RTT baseline) | - | 1 | 1.14 | 3.06 | 4.73 | 5886 |
| supavisor-transaction | Npgsql RoundTrips | read | 4 | 4.89 | 8.52 | 12.39 | 1523 |
| supavisor-transaction | Npgsql RoundTrips | write | 4 | 4.52 | 7.98 | 10.87 | 1640 |
| supavisor-transaction | Npgsql Batched | read | 2 | 2.96 | 6.27 | 9.59 | 2405 |
| supavisor-transaction | Npgsql Batched | write | 2 | 3.22 | 5.95 | 7.75 | 2306 |
| supavisor-transaction | Npgsql SingleTrip | read | 1 | 2.55 | 6.24 | 9.45 | 2649 |
| supavisor-transaction | Npgsql SingleTrip | write | 1 | 3.73 | 7.04 | 9.22 | 2010 |
| supavisor-transaction | EF Core interceptor | read | 3 | 4.67 | 8.06 | 10.30 | 1611 |
| supavisor-transaction | EF Core interceptor | write | 3 | 6.53 | 10.54 | 13.07 | 1162 |
