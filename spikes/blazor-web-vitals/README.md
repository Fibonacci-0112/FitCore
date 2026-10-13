# Spike: Blazor render modes against Core Web Vitals

Throwaway code for [ADR 0003](../../docs/decisions/0003-modular-monolith.md) follow-up 3. It builds the R1 Today screen as a Blazor Web App in five render modes and measures each one against NFR-PRF-8 (LCP ≤ 2.5 s, INP ≤ 200 ms, CLS ≤ 0.1) and NFR-PRF-7 (visible feedback ≤ 100 ms after adding a food). The findings are in [`docs/spikes/blazor-prerendering-web-vitals.md`](../../docs/spikes/blazor-prerendering-web-vitals.md).

Nothing here is production code. There's no sign-in: the server plays one member with an in-memory diary.

## Contents

| Path | Contents |
|---|---|
| `BlazorSpike.Core/` | Stand-in for `FitCore.Domain`, `FitCore.Contracts` and `FitCore.Client.Core`: diary DTOs, lower-bound totals, integer money, and a `TodayViewModel` built on CommunityToolkit.Mvvm, as MAUI would share it |
| `BlazorSpike.Client/` | Stand-in for `FitCore.Web.Client`: the `TodayView` component, the three WebAssembly pages and an HTTP `IDiaryClient` |
| `BlazorSpike/` | Stand-in for `FitCore.Web`: the host, the diary API, an in-process `IDiaryClient` for prerendering, and the static SSR and Interactive Server pages |
| `measure/` | Node scripts: `vitals.mjs` (Playwright and the web-vitals library under throttled profiles), `first-tap.mjs`, `lighthouse.mjs` and `bundle.mjs` |
| `results/` | Raw output of the runs the findings quote, and [`experiments.md`](results/experiments.md), the manual runs behind the first-tap fix |

## Pages

| Route | Render mode |
|---|---|
| `/static` | Static server rendering, no interactivity. The baseline |
| `/wasm` | Prerendered, then WebAssembly; the data is carried to the client with `[PersistentState]`. The candidate |
| `/wasm-nopersist` | Prerendered, then WebAssembly, which fetches the data again on start |
| `/wasm-noprerender` | WebAssembly only |
| `/server` | Interactive Server. Reference only: NFR-SCL-2 rules out per-tab circuits |

`TodayView` adds a `performance.mark("fitcore-ready")` when the screen first shows data and accepts taps. The scripts wait for it.

## Run it

Needs the .NET 10 SDK, Node 22 and a Chromium build (set `CHROMIUM` to its path).

```sh
cd spikes/blazor-web-vitals
dotnet publish BlazorSpike -c Release -o publish
(cd publish && ASPNETCORE_URLS=http://127.0.0.1:5080 dotnet BlazorSpike.dll) &
cd measure && npm ci
node vitals.mjs 5 > ../results/vitals.md          # about 30 minutes
node lighthouse.mjs 3 > ../results/lighthouse.md
node first-tap.mjs 5 4 >> ../results/first-tap.md
node bundle.mjs ../publish > ../results/bundle.md
```

`dotnet publish BlazorSpike -c Release -o publish -p:SpikeNoYield=true` builds the view model without the `Task.Yield()` that fixes the first tap, for comparison. Delete `obj/` and `bin/` before switching between AOT and non-AOT builds ([why](results/experiments.md#4-build-gotcha)).

The CPU slowdown in the scripts is relative to the machine that runs them. The published results ran on a 4-core 2.8 GHz Xeon container; Lighthouse's benchmark index for it is in `results/lighthouse.md`.
