# Spike findings: web render modes compared

- **Status:** Done. Lab results; the decision on [NFR open item 5](../NFR.md#17-open-items) still waits for real-user data from the beta.
- **Date:** 2026-10-13
- **Related:** [Blazor prerendering spike](blazor-prerendering-web-vitals.md), [ADR 0003](../decisions/0003-modular-monolith.md), [BRD §5](../BRD.md#5-platform-and-technology-constraints). Code, scripts and raw results: [`spikes/blazor-web-vitals`](../../spikes/blazor-web-vitals/README.md).

## 1. Question

The first spike chose prerendering plus WebAssembly, and left one risk open: a tap while WebAssembly starts waits 0.5–1.2 s on mobile profiles (NFR open item 5). This spike asks whether another render mode would serve the web app better, so that a later decision has evidence for each option. It builds the same Today screen four ways:

| Variant | Route | How it works |
|---|---|---|
| WebAssembly, prerendered | `/wasm` | The current design. The server prerenders the page; the .NET runtime then starts in the browser and takes over |
| Interactive Server | `/server` | The server prerenders the page, then runs the UI over a SignalR circuit: every tap goes to the server, which sends back the change to the screen |
| Interactive Auto | `/auto` | A circuit until the browser has downloaded and cached the WebAssembly runtime, then WebAssembly on later visits |
| Static SSR with a JavaScript island | `/islands` | Plain server-rendered HTML with no Blazor script. A 30-line script handles "Add banana": it adds a pending row, posts the entry, and swaps in the totals and the saved row, which the server renders with the same Razor components |

All four use the same diary data, totals code (`NutritionTotals`) and markup components.

## 2. Method

- **Load and taps:** the first spike's scripts (`vitals.mjs`, `early-tap.mjs`) with the new variants. Three profiles: Slow 4G with 4× CPU slowdown, Fast 4G with 2×, and desktop.
- **Network latency:** `latency.mjs` taps "Add banana" through `proxy.mjs`, a TCP proxy that delays every byte by a set round-trip time (RTT), from 0 to 400 ms. **Chromium's DevTools throttling doesn't delay WebSocket frames**, so it can't show what a round trip costs a circuit. The first spike used it, so its Interactive Server tap times have no network time in them; this spike's latency rows replace them.
- **Faults:** `resilience.mjs` loads the screen through the proxy at 50 ms RTT, saves one entry, then starts a fault and taps 1 s into it. The faults are a dead zone (nothing gets through for 10 or 45 s, then TCP delivers the backlog), a network switch (connections reset, offline for 3 or 30 s), and a server restart or crash. For the restarts, the diary survives in a file, as it would in the database, and circuit state is kept either in memory or in Redis through .NET 10 circuit state persistence.
- **Server memory:** `memory.mjs` opens up to 200 tabs and reads the server's managed heap after a full garbage collection.

The raw results are in [`spikes/blazor-web-vitals/results`](../../spikes/blazor-web-vitals/results/): `vitals-render-modes.md`, `early-tap-render-modes.md`, `latency.md`, `resilience.md`, `memory.md`, and `vitals-islands.md` and `early-tap-islands.md` for `/islands` without the Blazor script.

## 3. Results

### 3.1 First paint is the same for every variant

LCP in ms, cold / warm, median of 5. CLS was 0.000 everywhere.

| Variant | Slow 4G, 4× CPU | Fast 4G, 2× CPU | Desktop |
|---|---|---|---|
| WebAssembly | 1,252 / 644 | 360 / 204 | 120 / 84 |
| Interactive Server | 1,240 / 692 | 364 / 220 | 120 / 76 |
| Interactive Auto | 1,236 / 684 | 364 / 220 | 120 / 80 |
| JavaScript island | 1,252 / 652 | 372 / 200 | 124 / 76 |

Prerendering does the work in every variant, so LCP and CLS don't separate them.

### 3.2 When the screen accepts taps

Time until the screen accepts taps, in ms, cold / warm:

| Variant | Slow 4G, 4× CPU | Fast 4G, 2× CPU | Desktop | Transferred, cold |
|---|---|---|---|---|
| WebAssembly | 20,464 / 4,938 | 5,069 / 2,081 | 3,097 / 797 | 2,353 KiB |
| Interactive Server | 3,727 / 2,723 | 1,023 / 789 | 365 / 254 | 65 KiB |
| Interactive Auto, first visit | 4,455 | 1,242 | 433 | 161 KiB on Slow 4G; 1,114 KiB on Fast 4G and desktop, where more of the WebAssembly runtime arrived in the background before the visit ended |
| JavaScript island | 1,246 / 652 | 357 / 193 | 111 / 58 | 14 KiB |

The island is interactive as soon as it paints. Interactive Auto behaves like Interactive Server on a first visit. On a return visit it runs WebAssembly, but only if the first visit stayed long enough to download the runtime in the background: about 3 s on a fast connection, and longer than these short visits on Slow 4G.

### 3.3 Taps while the page starts (the INP risk)

Median of each load's worst tap, in ms, tapping every 250 ms from first paint until the screen is interactive:

| Variant | Slow 4G, cold / warm | Fast 4G, cold / warm |
|---|---|---|
| WebAssembly | **1,088 / 1,208** | **520 / 520** |
| Interactive Server | 408 / 16 | 96 / 16 |
| Interactive Auto | 384 (first visit) / **1,160** (return visit, WebAssembly) | 96 / **640** |
| JavaScript island | 392 / interactive before the first tap | 96 / interactive before the first tap |

The 0.4 s on cold Slow 4G loads is the browser parsing and laying out the first page on a 4× slower CPU; Interactive Server and the island pay it equally, and neither adds to it. Only WebAssembly adds the runtime's start-up work, and Interactive Auto brings it back on every return visit, which for a daily tracker is most visits.

### 3.4 Taps with real network latency

Tap to visible row, in ms (NFR-PRF-7 requires ≤ 100 ms "whatever the network latency"), median of 5 loads × 4 taps at 2× CPU:

| Added RTT | WebAssembly | Interactive Server | Interactive Auto, first visit | JavaScript island |
|---|---|---|---|---|
| 0 ms | 38 | 26 | 26 | 10 |
| 50 ms | 34 | 61 | 62 | 10 |
| 100 ms | 37 | **109** | **109** | 11 |
| 200 ms | 38 | **209** | **209** | 10 |
| 400 ms | 34 | **409** | **409** | 10 |

On a circuit the row appears only after a full round trip, so Interactive Server and Interactive Auto miss NFR-PRF-7 once the RTT passes about 90 ms. Mobile networks regularly exceed that, and so does a phone far from the AWS region. **INP doesn't see this:** the tap's Event Timing duration stays at 16 ms, because the browser paints at once and the change arrives later. Real-user INP would report these taps as fast. WebAssembly and the island update the screen locally, so their feedback doesn't depend on the network. With DevTools latency instead of the proxy, Interactive Server showed 27 ms at a nominal 400 ms, which confirms that DevTools throttling skips WebSockets.

Saving takes about RTT + 90 ms for the circuit and the island, and about RTT + 150 ms for WebAssembly, whose HTTP and JSON code is slower in the browser.

### 3.5 Faults mid-session

Each run taps once during the fault, waits until the page is usable again plus 5 s, and only then asks the server whether that tap was stored, so the next tap can't be mistaken for it.

| Fault | WebAssembly and JavaScript island | Interactive Server and Interactive Auto (first visit) |
|---|---|---|
| Dead zone, 10 s | Row at once; stored when the signal returns | No row for 9 s, then the row; stored |
| Dead zone, 45 s | Row at once; stored when the signal returns | "Rejoining the server..." for 15 s. **The tap during the dead zone never showed a row and was never stored.** The next tap after the dead zone saved |
| Network switch, offline 3 s | Row at once, then "Not saved" | The page is covered by "Rejoining the server..." and "Rejoin failed... trying again in 5 seconds", so the tap can't reach the button. Both are usable 2.3 s after the network returns |
| Offline 30 s, circuit kept 10 s | Row at once, then "Not saved" | Covered for 30 s. Both are usable 0.4 s after the network returns, with the screen intact: .NET 10 restored the evicted circuit from its persisted state |
| Server restart or crash | Row at once, then "Not saved"; usable at once | Covered for about 5 s, then **a full page reload**, usable 2.3 to 2.5 s after the server is back. The same with circuit state in Redis: persistence covers an evicted or paused circuit, not one whose server process is gone |

Nothing that was saved was ever lost, in any variant, and in every run the first tap after the fault saved. The "Not saved" rows show that the spike's WebAssembly and island code doesn't retry; production code would queue the entry and retry with its client-generated ID, which the API already treats as idempotent. A circuit can't queue anything while it's disconnected: it covers the page instead, and a tap sent into a connection that later times out is dropped without a trace. In every fault that cut the connection (the network switch, the 30 s outage and the server restarts), Interactive Auto also showed Blazor's error banner along the bottom of the page. The banner doesn't block taps, so it doesn't count against "usable", but it tells the member something broke. Interactive Server, WebAssembly and the island never showed it.

### 3.6 Server memory

Interactive Server holds about **150–190 KiB of managed memory per open tab** for this screen (38 MiB at 200 tabs, from 7 MiB with none). WebAssembly and the island hold nothing per tab. Even 1,000 concurrent tabs would cost about 160 MB, which is affordable at the launch sizing, so memory doesn't decide this. A tab that disappears without closing its connection keeps its circuit for 3 minutes by default.

## 4. Verdict

| Variant | Verdict |
|---|---|
| **WebAssembly, prerendered** | **Keep as the design.** It meets LCP and CLS, its tap feedback doesn't depend on network latency (median about 35 ms at 2× CPU), it shares view models with MAUI (NFR-MNT-4) and it degrades well offline. Two weaknesses remain: taps during start (open item 5), and slower single taps after start, over NFR-PRF-7's 100 ms: up to 122 ms here at 2× CPU, and about 150 ms for the first tap on Slow 4G with 4× CPU in the first spike |
| **Interactive Server** | **Ruled out**, now for a stronger reason than NFR-SCL-2: it can't meet NFR-PRF-7 at ordinary mobile latency, it blocks the screen and can lose a tap during a dead zone, and a deploy reloads every open page. Its good start-up numbers come from not running anything in the browser, which is also why every tap waits for the network |
| **Interactive Auto** | **Ruled out.** First visits inherit every circuit problem above; return visits bring back WebAssembly's start-up taps. It doesn't remove open item 5 for returning members, who are most of a daily tracker's visits |
| **Static SSR with JavaScript islands** | **The fallback, if beta data says open item 5 is real.** It leads on readiness and tap feedback: interactive at first paint, 10 ms feedback at any latency, 14 KiB, no circuit and no runtime. It doesn't lead on first paint, which is the same for every variant, or on taps during a cold first load on Slow 4G, where it waits about 0.4 s like Interactive Server and Auto. The cost is code, not speed: every interactive behavior on the web is hand-written JavaScript plus a server endpoint that returns HTML, instead of a Razor component bound to the shared view model. That breaks NFR-MNT-4's shared view models for the web, and the gap grows with the richer Public-phase screens (meal planner, recipe builder, charts, the coach workspace). Even the spike's one button needed hand-written state handling: review found that responses to two quick taps could arrive out of order and leave stale totals |

The ADR 0003 design stands. The web keeps prerendering plus WebAssembly, and the build rules in the [first spike's findings](blazor-prerendering-web-vitals.md#5-rules-for-the-build) still apply.

## 5. What this changes

- **NFR open item 5** gets a concrete fallback. If beta data shows start-up taps pushing INP's p75 past 200 ms, the options are, from least to most change:
  1. A JavaScript island for the Today screen's quick-add only, to accept taps before WebAssembly is ready. Not measured here.
  2. Islands for the screens most used on phones.
  3. Islands for the whole web app, which needs a change to NFR-MNT-4 and BRD §5.
- **Measure Interactive Server only through a real-latency proxy.** The first spike's Interactive Server tap times came from DevTools throttling and leave out the network; the findings there point here.
- **INP can't police circuit feedback.** Real-user monitoring would report Interactive Server taps as fast while members waited a full round trip. Nothing here uses circuits, but any future server-driven UI needs its own feedback timing in RUM, as `latency.mjs` measures here.

## 6. Open points

- **Real devices and real networks.** These are lab profiles on a 4-core container. The beta's real-user data decides open item 5.
- **The quick-add island (option 1 in section 5)** wasn't built. It would need care so that a tap taken by the island and the later WebAssembly component don't save the entry twice.
- **Pausing circuits on hidden tabs** (`Blazor.pauseCircuit()`) and a load balancer in front of several instances weren't tested. They would matter only if Interactive Server came back into consideration.
