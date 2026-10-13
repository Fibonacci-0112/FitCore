# Lighthouse by render mode

Output of `node lighthouse.mjs 3` on 2026-10-13: Lighthouse 12.8.2, performance category, simulated throttling (mobile: 150 ms RTT, 1.6 Mbps, 4× CPU; desktop: 40 ms RTT, 10 Mbps, 1× CPU). Median of 3 runs. Host benchmark index 2050.

Lighthouse loads the page without tapping, so it reports Total Blocking Time (TBT) as its lab proxy for INP.

| Preset | Variant | Score | LCP ms | CLS | TBT ms | TTI ms | KiB |
|---|---|---|---|---|---|---|---|
| mobile | `/static` | 100 | 1202 | 0.000 | 0 | 1202 | 59 |
| mobile | `/wasm` | 70 | 1503 | 0.000 | 4239 | 18633 | 2352 |
| mobile | `/wasm-nopersist` | 70 | 1351 | 0.000 | 3908 | 18530 | 2349 |
| mobile | `/wasm-noprerender` | 32 | 18196 | 0.000 | 3832 | 18497 | 2343 |
| mobile | `/server` | 100 | 1402 | 0.000 | 4 | 1432 | 66 |
| desktop | `/static` | 100 | 333 | 0.000 | 0 | 333 | 59 |
| desktop | `/wasm` | 73 | 373 | 0.000 | 824 | 3619 | 2352 |
| desktop | `/wasm-nopersist` | 74 | 421 | 0.000 | 753 | 3586 | 2349 |
| desktop | `/wasm-noprerender` | 51 | 3508 | 0.000 | 755 | 3580 | 2343 |
| desktop | `/server` | 100 | 413 | 0.000 | 0 | 413 | 66 |
