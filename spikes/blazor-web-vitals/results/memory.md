# Server memory per open tab

Output of `node memory.mjs` on 2026-10-13. One server process per variant, warmed up with one page view first. Memory after a full, compacting garbage collection. The Today screen has 18 diary rows; a bigger component tree holds more per circuit.

- **Managed per tab:** growth of the managed heap since 0 tabs, divided by the number of tabs.
- **Tabs closed 5 s ago:** the browser closed every tab, which closes their WebSockets cleanly, so the server ends those circuits at once. A tab that vanishes without closing its connection (a phone that loses signal or kills a background tab) leaves a disconnected circuit in memory for `DisconnectedCircuitRetentionPeriod`, 3 minutes by default; `resilience.mjs` covers that case.

| Variant | Tabs | State | Circuits | Managed heap, MiB | Working set, MiB | Managed per tab, KiB |
|---|---|---|---|---|---|---|
| Interactive Server | 0 | open | 0 | 7.3 | 123 | n/a |
| Interactive Server | 25 | open | 25 | 11.9 | 155 | 188 |
| Interactive Server | 50 | open | 50 | 16.1 | 174 | 180 |
| Interactive Server | 100 | open | 100 | 22.0 | 210 | 151 |
| Interactive Server | 200 | open | 200 | 38.4 | 281 | 159 |
| Interactive Server | 200 | tabs closed 5 s ago | 0 | 7.5 | 229 | 1 |
| WebAssembly | 0 | open | 0 | 7.1 | 118 | n/a |
| WebAssembly | 25 | open | 0 | 5.2 | 126 | -74 |
| WebAssembly | 50 | open | 0 | 5.3 | 131 | -37 |
| WebAssembly | 50 | tabs closed 5 s ago | 0 | 5.2 | 130 | -38 |
| Static SSR + JS island | 0 | open | 0 | 6.6 | 114 | n/a |
| Static SSR + JS island | 25 | open | 0 | 7.1 | 122 | 18 |
| Static SSR + JS island | 50 | open | 0 | 7.3 | 125 | 13 |
| Static SSR + JS island | 100 | open | 0 | 7.2 | 133 | 6 |
| Static SSR + JS island | 200 | open | 0 | 5.1 | 140 | -8 |
| Static SSR + JS island | 200 | tabs closed 5 s ago | 0 | 5.1 | 139 | -8 |
