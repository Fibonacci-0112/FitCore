# Spike: Blazor Prerendering and WebAssembly Against Core Web Vitals

- **Status:** In progress. The measurement runs are still going; this file will hold the findings.
- **Date:** 2026-10-13
- **Decision record:** [ADR 0003](../decisions/0003-modular-monolith.md), follow-up 3
- **Code and raw results:** [`spikes/blazor-web-vitals/`](../../spikes/blazor-web-vitals/README.md)

## 1. Question

Does a Blazor Web App that prerenders on the server and then runs on WebAssembly meet NFR-PRF-8 (LCP ≤ 2.5 s, INP ≤ 200 ms, CLS ≤ 0.1 at p75) and NFR-PRF-7 (visible feedback ≤ 100 ms after adding a food), and which render-mode rules does the scaffold need?
