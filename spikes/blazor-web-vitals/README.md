# Spike: Blazor render modes against Core Web Vitals

Throwaway code for [ADR 0003](../../docs/decisions/0003-modular-monolith.md) follow-up 3. It builds the R1 Today screen as a Blazor Web App in several render modes and measures each one against NFR-PRF-8 (LCP ≤ 2.5 s, INP ≤ 200 ms, CLS ≤ 0.1) and NFR-PRF-7 (visible feedback ≤ 100 ms after adding a food). The findings are in [`docs/spikes/blazor-prerendering-web-vitals.md`](../../docs/spikes/blazor-prerendering-web-vitals.md).

A second round compares the candidate with three alternatives: Interactive Server, Interactive Auto, and static server rendering with a JavaScript island. It adds tests for network latency, connection loss, server restarts and server memory. Its findings are in [`docs/spikes/web-render-modes.md`](../../docs/spikes/web-render-modes.md).

Nothing here is production code. There's no sign-in: the server plays one member with an in-memory diary.

## Contents

| Path | Contents |
|---|---|
| `BlazorSpike.Core/` | Stand-in for `FitCore.Domain`, `FitCore.Contracts` and `FitCore.Client.Core`: diary DTOs, lower-bound totals, integer money, and a `TodayViewModel` built on CommunityToolkit.Mvvm, as MAUI would share it |
| `BlazorSpike.Client/` | Stand-in for `FitCore.Web.Client`: the `TodayView` component and its parts, the WebAssembly and Interactive Auto pages, and an HTTP `IDiaryClient` |
| `BlazorSpike/` | Stand-in for `FitCore.Web`: the host, the diary API, an in-process `IDiaryClient` for prerendering, the static SSR, Interactive Server and islands pages, and test hooks for circuits and memory |
| `BlazorSpike/wwwroot/islands/today.js` | The JavaScript island: the "Add banana" handler for `/islands` |
| `measure/` | Node scripts: `vitals.mjs` (Playwright and the web-vitals library under throttled profiles), `first-tap.mjs` (taps after start), `early-tap.mjs` (taps while the page starts), `lighthouse.mjs` and `bundle.mjs`. Second round: `proxy.mjs` (real latency and outages), `latency.mjs`, `resilience.mjs` and `memory.mjs` |
| `results/` | Raw output of the runs the findings quote, and [`experiments.md`](results/experiments.md), the manual runs behind the first-tap fix |

## Pages

| Route | Render mode |
|---|---|
| `/static` | Static server rendering, no interactivity. The baseline |
| `/wasm` | Prerendered, then WebAssembly; the data is carried to the client with `[PersistentState]`. The candidate |
| `/wasm-nopersist` | Prerendered, then WebAssembly, which fetches the data again on start |
| `/wasm-noprerender` | WebAssembly only |
| `/server` | Interactive Server: one circuit per tab. NFR-SCL-2 rules out per-tab circuits; the second round retests it |
| `/auto` | Interactive Auto: a circuit until the WebAssembly runtime is cached, then WebAssembly |
| `/islands` | Static server rendering with a JavaScript island: no circuit and no WebAssembly. The tap adds a pending row, posts the entry, and swaps in totals and a row that the server renders |

`TodayView` adds a `performance.mark("fitcore-ready")` when the screen first shows data and accepts taps, and a hidden `data-renderer` attribute that names what runs it. The islands script adds the same mark. The scripts wait for it.

## Run it

Needs the .NET 10 SDK, Node 22 and a Chromium build (set `CHROMIUM` to its path).

```sh
cd spikes/blazor-web-vitals
dotnet publish BlazorSpike -c Release -o publish
(cd publish && ASPNETCORE_URLS=http://127.0.0.1:5080 dotnet BlazorSpike.dll) &
cd measure && npm ci
node vitals.mjs 5 > ../results/vitals.md          # about 30 minutes
node lighthouse.mjs 3 > ../results/lighthouse.md
node first-tap.mjs 5 4 >> ../results/first-tap.md    # appends one section per build
node early-tap.mjs 5 > ../results/early-tap.md
node bundle.mjs ../publish > ../results/bundle.md
```

The second round's scripts. `resilience.mjs` and `memory.mjs` start their own servers from `publish/` (ports 5090 and 5095), and `resilience.mjs` needs `redis-server` on the path:

```sh
PATHS=/wasm,/server,/auto,/islands node vitals.mjs 5 > ../results/vitals-render-modes.md
PATHS=/wasm,/server,/auto,/islands node early-tap.mjs 5 > ../results/early-tap-render-modes.md
node latency.mjs 5 4 > ../results/latency.md      # through proxy.mjs on port 5081
node resilience.mjs 1 > ../results/resilience.md
node memory.mjs > ../results/memory.md
```

The server reads these environment variables, for the resilience runs:

| Variable | Effect |
|---|---|
| `SPIKE_STORE_FILE` | Keeps added diary entries in a JSON file, so they survive a restart as the database would make them |
| `SPIKE_CIRCUIT_RETENTION_SECONDS` | Sets `CircuitOptions.DisconnectedCircuitRetentionPeriod` (default 3 minutes) |
| `SPIKE_REDIS` | Adds a Redis-backed `HybridCache`, which .NET 10 uses to persist circuit state across servers. It covers an evicted or paused circuit only: when the server process stops, open pages still reload ([results](results/resilience.md)) |

`dotnet publish BlazorSpike -c Release -o publish -p:SpikeNoYield=true` builds the view model without the `Task.Yield()` that fixes the first tap, for comparison. Delete `obj/` and `bin/` before switching between AOT and non-AOT builds ([why](results/experiments.md#4-build-gotcha)).

The CPU slowdown in the scripts is relative to the machine that runs them. The published results ran on a 4-core 2.8 GHz Xeon container; Lighthouse's benchmark index for it is in [`lighthouse.md`](results/lighthouse.md).
