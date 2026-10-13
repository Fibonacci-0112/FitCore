# Faults mid-session

Output of `node resilience.mjs 1` on 2026-10-13, 1 run(s) per row. Each run loads the Today screen through `proxy.mjs` (50 ms RTT), saves one entry, starts the fault, taps "Add banana" 1 s into it, and after the fault taps "Add banana" again.

- **Dead zone:** nothing gets through, then everything queued is delivered, as when a phone loses signal and TCP retransmits. **Network switch / offline:** every connection is reset and new ones are refused, as when a phone changes network.
- **Server restart:** the server process stops and a new one starts 3 s later on the same port, as in a deploy. The diary data survives (a file stands in for the database); circuits don't. "State in Redis" uses .NET 10 circuit state persistence with a Redis `HybridCache`.
- **Tap during the fault:** what the tap 1 s in did, times from the tap in ms. **Reconnect UI:** what Blazor showed over the page, and for how long.
- **Usable again:** from the end of the fault until the next tap on "Add banana" went through, and whether it saved. **Reloads:** full page reloads. **Bananas shown / stored:** rows on screen at the end against entries the server stored, out of 3 attempts.

| Fault | Variant | Renderer | Tap during the fault | Reconnect UI | Usable again | Reloads | Bananas shown / stored |
|---|---|---|---|---|---|---|---|
| Dead zone, 10 s | WebAssembly | WebAssembly | row at 18, saved at 9142 | none | 0.0 s | 0 | 3 / 3 |
| Dead zone, 10 s | Interactive Server | Server | row at 9063, saved at 9147 | none | 0.0 s | 0 | 3 / 3 |
| Dead zone, 10 s | Interactive Auto, first visit | Server | row at 9069, saved at 9152 | none | 0.0 s | 0 | 3 / 3 |
| Dead zone, 10 s | Static SSR + JS island | Island | row at 4, saved at 9151 | none | 0.0 s | 0 | 3 / 3 |
| Dead zone, 45 s | WebAssembly | WebAssembly | row at 17, saved at 44153 | none | 0.1 s | 0 | 3 / 3 |
| Dead zone, 45 s | Interactive Server | Server | row at 44332, saved at 44415 | "Rejoining the server..." (15.0 s) | 0.3 s | 0 | 2 / 2 |
| Dead zone, 45 s | Interactive Auto, first visit | Server | row at 44319, saved at 44402 | "Rejoining the server..." (15.0 s) | 0.3 s | 0 | 2 / 2 |
| Dead zone, 45 s | Static SSR + JS island | Island | row at 3, saved at 44152 | none | 0.0 s | 0 | 3 / 3 |
| Network switch, offline 3 s | WebAssembly | WebAssembly | row at 13, "Not saved" at 29 | none | 0.0 s | 0 | 3 / 2 |
| Network switch, offline 3 s | Interactive Server | Server | blocked by the reconnect UI | "Rejoining the server..." / "Rejoin failed... trying again in 5 seconds" / "Rejoin failed... trying again in 4 seconds" / "Rejoin failed... trying again in 3 seconds" / "Rejoin failed... trying again in 2 seconds" / "Rejoin failed... trying again in 1 second" (5.2 s) | 2.5 s | 0 | 2 / 2 |
| Network switch, offline 3 s | Interactive Auto, first visit | Server | blocked by the reconnect UI | "Rejoining the server..." / "Rejoin failed... trying again in 5 seconds" / "Rejoin failed... trying again in 4 seconds" / "Rejoin failed... trying again in 3 seconds" / "Rejoin failed... trying again in 2 seconds" / "Rejoin failed... trying again in 1 second" / error UI (7.2 s) | 2.5 s | 0 | 2 / 2 |
| Network switch, offline 3 s | Static SSR + JS island | Island | row at 3, "Not saved" at 4 | none | 0.0 s | 0 | 3 / 2 |
| Offline 30 s, circuit kept 10 s | WebAssembly | WebAssembly | row at 17, "Not saved" at 31 | none | 0.0 s | 0 | 3 / 2 |
| Offline 30 s, circuit kept 10 s | Interactive Server | Server | blocked by the reconnect UI | "Rejoining the server..." / "Rejoin failed... trying again in 5 seconds" / "Rejoin failed... trying again in 4 seconds" / "Rejoin failed... trying again in 3 seconds" / "Rejoin failed... trying again in 2 seconds" / "Rejoin failed... trying again in 1 second" (30.0 s) | 0.4 s | 0 | 2 / 2 |
| Offline 30 s, circuit kept 10 s | Interactive Auto, first visit | Server | blocked by the reconnect UI | "Rejoining the server..." / "Rejoin failed... trying again in 5 seconds" / "Rejoin failed... trying again in 4 seconds" / "Rejoin failed... trying again in 3 seconds" / "Rejoin failed... trying again in 2 seconds" / "Rejoin failed... trying again in 1 second" / error UI (31.6 s) | 0.4 s | 0 | 2 / 2 |
| Offline 30 s, circuit kept 10 s | Static SSR + JS island | Island | row at 3, "Not saved" at 4 | none | 0.0 s | 0 | 3 / 2 |
| Server restart (SIGTERM), state in memory | WebAssembly | WebAssembly | row at 17, "Not saved" at 35 | none | 0.0 s | 0 | 3 / 2 |
| Server restart (SIGTERM), state in memory | Interactive Server | Server | blocked by the reconnect UI | "Rejoin failed... trying again in 5 seconds" / "Rejoin failed... trying again in 4 seconds" / "Rejoin failed... trying again in 3 seconds" / "Rejoin failed... trying again in 2 seconds" / "Rejoin failed... trying again in 1 second" / "Rejoining the server..." / page navigating (5.4 s) | 2.8 s | 1 | 2 / 2 |
| Server restart (SIGTERM), state in memory | Interactive Auto, first visit | Server | blocked by the reconnect UI | "Rejoin failed... trying again in 5 seconds" / "Rejoin failed... trying again in 4 seconds" / "Rejoin failed... trying again in 3 seconds" / "Rejoin failed... trying again in 2 seconds" / "Rejoin failed... trying again in 1 second" / "Rejoining the server..." / page navigating (5.4 s) | 3.0 s | 1 | 2 / 2 |
| Server restart (SIGTERM), state in memory | Static SSR + JS island | Island | row at 2, "Not saved" at 4 | none | 0.0 s | 0 | 3 / 2 |
| Server restart (SIGTERM), state in Redis | WebAssembly | WebAssembly | row at 12, "Not saved" at 26 | none | 0.0 s | 0 | 3 / 2 |
| Server restart (SIGTERM), state in Redis | Interactive Server | Server | blocked by the reconnect UI | "Rejoin failed... trying again in 5 seconds" / "Rejoin failed... trying again in 4 seconds" / "Rejoin failed... trying again in 3 seconds" / "Rejoin failed... trying again in 2 seconds" / "Rejoin failed... trying again in 1 second" / "Rejoining the server..." / page navigating (5.4 s) | 2.6 s | 1 | 2 / 2 |
| Server restart (SIGTERM), state in Redis | Interactive Auto, first visit | Server | blocked by the reconnect UI | "Rejoin failed... trying again in 5 seconds" / "Rejoin failed... trying again in 4 seconds" / "Rejoin failed... trying again in 3 seconds" / "Rejoin failed... trying again in 2 seconds" / "Rejoin failed... trying again in 1 second" / "Rejoining the server..." / page navigating (5.4 s) | 2.8 s | 1 | 2 / 2 |
| Server restart (SIGTERM), state in Redis | Static SSR + JS island | Island | row at 2, "Not saved" at 4 | none | 0.0 s | 0 | 3 / 2 |
| Server crash (SIGKILL), state in Redis | WebAssembly | WebAssembly | row at 13, "Not saved" at 27 | none | 0.0 s | 0 | 3 / 2 |
| Server crash (SIGKILL), state in Redis | Interactive Server | Server | blocked by the reconnect UI | "Rejoin failed... trying again in 5 seconds" / "Rejoin failed... trying again in 4 seconds" / "Rejoin failed... trying again in 3 seconds" / "Rejoin failed... trying again in 2 seconds" / "Rejoin failed... trying again in 1 second" / "Rejoining the server..." / page navigating (5.4 s) | 2.5 s | 1 | 2 / 2 |
| Server crash (SIGKILL), state in Redis | Interactive Auto, first visit | Server | blocked by the reconnect UI | "Rejoin failed... trying again in 5 seconds" / "Rejoin failed... trying again in 4 seconds" / "Rejoin failed... trying again in 3 seconds" / "Rejoin failed... trying again in 2 seconds" / "Rejoin failed... trying again in 1 second" / "Rejoining the server..." / page navigating (5.4 s) | 2.8 s | 1 | 2 / 2 |
| Server crash (SIGKILL), state in Redis | Static SSR + JS island | Island | row at 3, "Not saved" at 4 | none | 0.0 s | 0 | 3 / 2 |
