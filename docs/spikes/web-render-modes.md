# Spike findings: web render modes compared

- **Status:** In progress. The measurements are running; this file gets the results and a verdict when they finish.
- **Date:** 2026-10-13
- **Related:** [Blazor prerendering spike](blazor-prerendering-web-vitals.md), [NFR open item 5](../NFR.md#17-open-items), [ADR 0003](../decisions/0003-modular-monolith.md). Code and run instructions: [`spikes/blazor-web-vitals`](../../spikes/blazor-web-vitals/README.md).

## 1. Question

The first spike chose prerendering plus WebAssembly, and left one risk open: a tap while WebAssembly starts waits 0.5–1.2 s on mobile profiles. This spike compares three alternatives on the same Today screen, so a later decision has evidence for each:

| Variant | Route | How it works |
|---|---|---|
| WebAssembly, prerendered | `/wasm` | The current design. The baseline |
| Interactive Server | `/server` | A SignalR circuit per tab; the UI runs on the server |
| Interactive Auto | `/auto` | A circuit until the WebAssembly runtime is cached, then WebAssembly |
| Static SSR + JavaScript island | `/islands` | Plain server-rendered HTML; a small script handles "Add banana" and swaps in HTML that the server renders |

## 2. What it measures

- Core Web Vitals and taps during start, with the first spike's scripts.
- Taps by round-trip time, through a proxy that delays every byte. Chromium's network emulation doesn't delay WebSocket frames, so it can't show what a round trip costs a circuit.
- Connection loss and server restarts mid-session, including .NET 10 circuit state persistence in memory and in Redis.
- Server memory per open tab.
