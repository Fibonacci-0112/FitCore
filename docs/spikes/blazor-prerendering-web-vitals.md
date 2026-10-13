# Spike: Blazor Prerendering and WebAssembly Against Core Web Vitals

- **Status:** Done. Findings feed the `FitCore.Web` and `FitCore.Web.Client` scaffold and the view-model rules in `FitCore.Client.Core`.
- **Date:** 2026-10-13
- **Decision record:** [ADR 0003](../decisions/0003-modular-monolith.md), follow-up 3
- **Code and raw results:** [`spikes/blazor-web-vitals/`](../../spikes/blazor-web-vitals/README.md)

## 1. Question

The architecture assumes the Blazor Web App prerenders on the server and then runs on WebAssembly, because a WebAssembly-only first load is too slow ([architecture-options §1.2](../architecture-options.md#12-nfrs-that-decide-the-architecture)). The spike answers three questions:

1. Does that design meet NFR-PRF-8 (LCP ≤ 2.5 s, INP ≤ 200 ms, CLS ≤ 0.1) and NFR-PRF-7 (visible feedback ≤ 100 ms after adding a food)?
2. What does sharing MVVM view models with the MAUI apps (NFR-MNT-4) cost in the browser?
3. Which rules must the scaffold follow?

## 2. Setup

| Component | Version or value |
|---|---|
| Framework | .NET 10 SDK 10.0.401, ASP.NET Core 10.0.12, published in Release with the default trimming and the prebuilt WebAssembly runtime (no `wasm-tools` workload) |
| Shared code | A `TodayViewModel` on CommunityToolkit.Mvvm 8.4.2, lower-bound totals and integer money, as `FitCore.Client.Core` and `FitCore.Domain` will share them |
| Screen | The R1 Today screen: three totals tiles, 18 diary entries (some with unknown nutrients or no price), and an "Add banana" quick-add with an optimistic row |
| Browser | Chromium 141 through Playwright, a 412 × 823 phone viewport, Google's web-vitals library 5.1 |
| Lab profiles | **Slow 4G, 4× CPU:** Lighthouse's mobile profile as DevTools applies it (562.5 ms latency, 1.47 Mbps). **Fast 4G, 2× CPU:** 150 ms, 9 Mbps. **Desktop:** 40 ms, 10 Mbps, 1× CPU |
| Host | 4-core 2.8 GHz Xeon container, Lighthouse benchmark index 2050. Lighthouse's 4× slowdown on a host like this stands in for a mid-range Android phone |

Five render modes of the same screen were measured:

| Route | Render mode |
|---|---|
| `/static` | Static server rendering, no interactivity. The best case for LCP and CLS |
| `/wasm` | Prerendered on the server, then WebAssembly. The data travels in the page with `[PersistentState]`. The candidate |
| `/wasm-nopersist` | Prerendered, then WebAssembly, which fetches the data again on start |
| `/wasm-noprerender` | WebAssembly only |
| `/server` | Interactive Server. Reference only: per-tab circuits are ruled out by NFR-SCL-2 |

**Cold** means an empty browser cache, a member's first visit. **Warm** means the same browser loads the page again, the normal case for a daily diary.

## 3. Results

Full output: [Web Vitals](../../spikes/blazor-web-vitals/results/vitals.md), [Lighthouse](../../spikes/blazor-web-vitals/results/lighthouse.md), [first tap](../../spikes/blazor-web-vitals/results/first-tap.md), [taps during start](../../spikes/blazor-web-vitals/results/early-tap.md), [payload](../../spikes/blazor-web-vitals/results/bundle.md) and the [manual experiments](../../spikes/blazor-web-vitals/results/experiments.md). Medians of 5 runs unless stated.

### 3.1 LCP and CLS: prerendering is required, and enough

LCP in ms, cold / warm:

| Variant | Slow 4G, 4× CPU | Fast 4G, 2× CPU | Desktop |
|---|---|---|---|
| Static SSR | 1,248 / 672 | 368 / 208 | 120 / 72 |
| WebAssembly, prerendered | 1,252 / 644 | 360 / 204 | 124 / 76 |
| WebAssembly only | **20,944** / **5,536** | **5,212** / 2,176 | **3,112** / 884 |
| Interactive Server | 1,244 / 644 | 364 / 204 | 128 / 80 |

Prerendered WebAssembly paints as fast as static SSR on every profile, well inside 2.5 s. WebAssembly only misses the target on every cold load, and on Slow 4G even when cached. Lighthouse agrees: on its mobile preset the prerendered page's LCP is 1.5 s and WebAssembly only is 18.2 s.

CLS was 0.000 in every run of every variant. That includes the refetch control, which does flicker: when WebAssembly starts, the diary disappears behind "Loading…" for the length of the API call and then comes back. CLS doesn't count content that is removed and added again, so the metric can't see it. [`[PersistentState]`](#35-persisted-state) avoids it.

### 3.2 The page looks ready long before it is

Time until the Today screen accepts taps, in ms, cold / warm:

| Variant | Slow 4G, 4× CPU | Fast 4G, 2× CPU | Desktop |
|---|---|---|---|
| WebAssembly, prerendered | 20,296 / 5,033 | 5,067 / 2,091 | 3,044 / 807 |
| Interactive Server | 3,719 / 2,697 | 1,071 / 798 | 363 / 256 |

A first visit downloads 2,351 KiB, of which 2,333 KiB is the .NET runtime and assemblies (brotli). The largest pieces are `dotnet.native.wasm` (953 KiB), `System.Private.CoreLib` (493 KiB), one ICU shard (141 KiB for English) and `System.Text.Json` (127 KiB). A warm visit transfers 17 KiB, but the runtime still has to start, which takes 2 to 5 s on the mobile profiles. Lighthouse's mobile preset reports 4.2 s of Total Blocking Time and an 18.6 s Time to Interactive for the prerendered page, against 0 ms and 1.2 s for static SSR.

Sharing view models costs little: `BlazorSpike.Core` and CommunityToolkit.Mvvm together add 31 KiB to the payload.

### 3.3 Taps after start: fast once view-model commands yield

The first tap on a WebAssembly page was slow. Without the fix in rule 5, the "Add banana" tap took 96 ms at 1× CPU, 224 ms at 2× and 528 ms at 4×, while later taps took 16 to 80 ms. A trace showed the time inside the click handler: the first run of the HTTP client and the JSON serializer happened synchronously before the command's first `await`, so the optimistic row could not paint until they finished. AOT compilation didn't help (264 ms at 2×) and added 2 MiB of payload; one `await Task.Yield()` after the optimistic update did ([experiments](../../spikes/blazor-web-vitals/results/experiments.md)).

With the yield, in the full run:

| Profile | First tap, cold / warm | Tap to new row, cold / warm | Later taps |
|---|---|---|---|
| Slow 4G, 4× CPU | 152 / 136 (worst 168) | **150 / 134** | 72 |
| Fast 4G, 2× CPU | 88 / 72 | 81 / 65 | 32 |
| Desktop | 32 / 24 | 27 / 23 | 16 |

"First tap" is the Event Timing duration INP is built from; "tap to new row" is NFR-PRF-7's visible feedback. Every tap meets INP's 200 ms. Visible feedback meets 100 ms except for the first tap on the slowest profile, where it takes about 150 ms; later taps there take about 72 ms.

### 3.4 Taps during start: the INP risk

A member who taps while WebAssembly is still starting waits behind the runtime's start-up work. The spike tapped the page heading every 250 ms from first paint until the screen was interactive:

| Profile | Variant | Worst tap per load, cold | Worst tap per load, warm |
|---|---|---|---|
| Slow 4G, 4× CPU | WebAssembly, prerendered | **1,072** | **1,184** |
| Slow 4G, 4× CPU | Interactive Server | 400 | 16 |
| Fast 4G, 2× CPU | WebAssembly, prerendered | **552** | **512** |
| Fast 4G, 2× CPU | Interactive Server | 104 | 16 |

The delay comes from a few long tasks of 360 to 500 ms each (at 2× CPU) in which the .NET runtime and Blazor initialize. Caching doesn't remove them, and they don't come from application code. INP is close to the worst interaction of a page view, so any page view where the member taps during those 2 to 20 seconds scores 0.5 to 1.2 s on these profiles. Whether that reaches NFR-PRF-8's p75 depends on how often real members tap during start, which a lab can't tell.

### 3.5 Persisted state

`[PersistentState]` saved one diary read per page view (1 read instead of 2) and removed the "Loading…" flicker described in [§3.1](#31-lcp-and-cls-prerendering-is-required-and-enough). It made the prerendered HTML about 5 KB larger for 18 entries, because the state is JSON encoded twice in base64. It made no difference to the measured LCP, CLS or tap times.

## 4. Verdict

NFR-PRF-8 is a p75 target for real users, so these are lab results. They show whether the design can meet the targets; acceptance needs the real-user data from the beta.

| Target | Lab result |
|---|---|
| LCP ≤ 2.5 s (NFR-PRF-8) | **Met in the lab** with prerendering on every profile, cold and warm. Not met with WebAssembly only |
| CLS ≤ 0.1 (NFR-PRF-8) | **Met in the lab**: 0.000 everywhere |
| INP ≤ 200 ms (NFR-PRF-8) | **Met in the lab for taps after start**, with rule 5. **At risk for taps during start**: 0.5 to 1.2 s on the mobile profiles |
| Feedback ≤ 100 ms (NFR-PRF-7) | Met on Fast 4G and desktop. The first tap on Slow 4G with 4× CPU takes about 150 ms |

The ADR's design stands: prerender every page, then hydrate with WebAssembly. Interactive Server measured better for taps during start, but per-tab circuits stay ruled out by NFR-SCL-2. A later spike found that DevTools throttling doesn't delay WebSocket frames, so this spike's Interactive Server tap times leave out the network; with real latency, every circuit tap waits a full round trip ([web render modes](web-render-modes.md#34-taps-with-real-network-latency)). The start-up INP risk is recorded as [NFR open item 5](../NFR.md#17-open-items).

## 5. Rules for the build

These rules go into the `FitCore.Web` and `FitCore.Web.Client` scaffold and its tests.

1. **Prerender every page and hydrate with WebAssembly.** Never ship a WebAssembly-only route, and don't use Interactive Server or Interactive Auto (NFR-SCL-2).
2. **Carry prerendered data with `[PersistentState]`**, so the client doesn't fetch it again and the screen doesn't flicker while it does.
3. **Pages that carry member data send `Cache-Control: no-store`.** The prerendered HTML and the persisted state both hold health data.
4. **The server resolves "today" from the member's profile time zone**, never from the server clock or the browser, so prerendering and the client agree on the diary date ([BRD §6.1](../BRD.md#61-data-contracts)).
5. **View-model commands yield to the UI after the optimistic update and before calling the API** (`await Task.Yield()`). On WebAssembly the first API call is slow, and it must not run inside the tap. The yield costs nothing on MAUI.
6. **Disable actions until the component is interactive** (`RendererInfo.IsInteractive`), so a tap before hydration is visibly refused instead of silently dropped.
7. **Use source-generated JSON for the contracts** (`JsonSerializerContext`), so trimming can't break serialization.
8. **Keep the default build:** no AOT, no `InvariantGlobalization`, and no `wasm-tools` workload unless a later measurement justifies it. Delete `obj/` when switching AOT on or off ([why](../../spikes/blazor-web-vitals/results/experiments.md#4-build-gotcha)).
9. **Use CommunityToolkit.Mvvm 8.4.2 or later.** 8.4.0's generator didn't build C# 14 partial properties on .NET 10.
10. **Measure real users with the web-vitals library**, sending only the metric name, value, rating and route template. Don't send the attribution build's element selectors or any page text (NFR-PRV-6).
11. **Test in CI with both Lighthouse and Playwright.** Lighthouse sees LCP, CLS and blocking time but never taps, so it can't see INP. A Playwright job like `first-tap.mjs` and `early-tap.mjs` covers taps.

## 6. Open points

- **Start-up INP** ([NFR open item 5](../NFR.md#17-open-items)). The product owner decided on 2026-10-13 to build as designed and settle this with real-user data from the beta. Options to test before the beta, none measured here: lazy-load the assemblies that the first screen doesn't need (`BlazorWebAssemblyLazyLoad`), start WebAssembly on idle instead of straight after the first paint, or keep the most-used actions working as enhanced static SSR forms until WebAssembly is ready. Real-user monitoring at the beta will show whether start-up taps reach p75.
- **Sign-in and prerendering.** The server can only prerender a member's diary if it knows who the member is on the page request, so the web app needs a server-side session (an HttpOnly cookie that holds the Supabase session, with the API called through the same origin). The spike had no sign-in. The scaffold's auth design must cover this, together with the [MFA rules](apple-sign-in-and-mfa.md#4-rules-for-the-build).
- **Real devices.** CPU slowdown on a container approximates phones. Before the beta, repeat the Slow 4G run on a real mid-range Android phone and an older iPhone in Safari, since NFR-CMP-2 includes Safari and WebKit compiles WebAssembly differently.
- **A bigger app.** The spike's payload is the floor. Every module that ships to the browser adds assemblies and start-up time; the CI job in rule 11 needs a payload budget too.
