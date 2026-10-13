# Web Vitals by render mode

Output of `node vitals.mjs 5` on 2026-10-13: 5 runs per cell, median (worst in brackets). Chromium 141.0.7390.37, viewport 412×823. Times in ms, sizes in KiB as transferred.

> The `/islands` rows here are from a build that still loaded `blazor.web.js` on that page. [`vitals-islands.md`](vitals-islands.md) has `/islands` rerun without it, as committed.

- **LCP, CLS:** from the web-vitals library, measured up to the first tap.
- **Ready:** when the Today screen shows data and accepts taps (a `performance.mark` from the component).
- **Tap event:** duration of the "Add banana" tap from Event Timing, the number INP is built from.
- **Tap to row:** from the tap to the frame that shows the new row (NFR-PRF-7, ≤ 100 ms). **Saved:** to the frame that shows it confirmed.
- **Reads:** how many times the page read "today" from the diary (prerender plus client).
- **Renderer:** what ran the page once it was interactive. DevTools throttling doesn't delay WebSocket frames, so taps on a circuit (Server) show no network time here; see `latency.md`.

| Profile | Variant | Cache | Renderer | LCP | CLS | FCP | Ready | Tap event | Tap to row | Saved | KiB | of which `_framework` | Reads |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Slow 4G, 4x CPU | WebAssembly, prerendered, persisted state | cold | WebAssembly | 1252 (1260) | 0.000 (0.000) | 1252 | 20464 | 184 (280) | 176 (275) | 1263 | 2353 | 2335 | 1 |
| Slow 4G, 4x CPU | WebAssembly, prerendered, persisted state | warm | WebAssembly | 644 (700) | 0.000 (0.000) | 644 | 4938 | 144 (232) | 143 (222) | 1168 | 16 | 0 | 1 |
| Slow 4G, 4x CPU | Interactive Server (reference) | cold | Server | 1240 (1256) | 0.000 (0.000) | 1240 | 3727 | 24 (40) | 64 (66) | 131 | 65 | 47 | 1 |
| Slow 4G, 4x CPU | Interactive Server (reference) | warm | Server | 692 (724) | 0.000 (0.000) | 692 | 2723 | 16 (24) | 46 (57) | 119 | 17 | 0 | 1 |
| Slow 4G, 4x CPU | Interactive Auto | cold | Server | 1236 (1252) | 0.000 (0.000) | 1236 | 4455 | 24 (32) | 68 (72) | 132 | 161 | 138 | 1 |
| Slow 4G, 4x CPU | Interactive Auto | warm | Server | 684 (764) | 0.000 (0.000) | 684 | 2961 | 16 (16) | 59 (67) | 116 | 22 | 0 | 1 |
| Slow 4G, 4x CPU | Static SSR + JS island | cold | Island | 1240 (1256) | 0.000 (0.000) | 1240 | 1548 | 32 (40) | 25 (34) | 607 | 61 | 47 | 1 |
| Slow 4G, 4x CPU | Static SSR + JS island | warm | Island | 640 (660) | 0.000 (0.000) | 640 | 671 | 16 (32) | 13 (28) | 597 | 12 | 0 | 1 |
| Fast 4G, 2x CPU | WebAssembly, prerendered, persisted state | cold | WebAssembly | 360 (364) | 0.000 (0.000) | 360 | 5069 | 88 (112) | 81 (110) | 476 | 2353 | 2335 | 1 |
| Fast 4G, 2x CPU | WebAssembly, prerendered, persisted state | warm | WebAssembly | 204 (220) | 0.000 (0.000) | 204 | 2081 | 64 (88) | 64 (84) | 424 | 16 | 0 | 1 |
| Fast 4G, 2x CPU | Interactive Server (reference) | cold | Server | 364 (368) | 0.000 (0.000) | 364 | 1023 | 0 (24) | 24 (32) | 103 | 65 | 47 | 1 |
| Fast 4G, 2x CPU | Interactive Server (reference) | warm | Server | 220 (228) | 0.000 (0.000) | 220 | 789 | 0 (16) | 26 (28) | 96 | 17 | 0 | 1 |
| Fast 4G, 2x CPU | Interactive Auto | cold | Server | 364 (376) | 0.000 (0.000) | 364 | 1242 | 0 (16) | 23 (27) | 99 | 1114 | 1091 | 1 |
| Fast 4G, 2x CPU | Interactive Auto | warm | Server | 220 (492) | 0.000 (0.000) | 220 | 940 | 0 (16) | 27 (29) | 98 | 693 | 671 | 1 |
| Fast 4G, 2x CPU | Static SSR + JS island | cold | Island | 360 (368) | 0.000 (0.000) | 360 | 413 | 16 (16) | 10 (11) | 174 | 61 | 47 | 1 |
| Fast 4G, 2x CPU | Static SSR + JS island | warm | Island | 212 (232) | 0.000 (0.000) | 212 | 203 | 16 (16) | 11 (12) | 170 | 12 | 0 | 1 |
| Desktop cable, 1x CPU | WebAssembly, prerendered, persisted state | cold | WebAssembly | 120 (128) | 0.000 (0.000) | 120 | 3097 | 40 (48) | 36 (43) | 224 | 2353 | 2335 | 1 |
| Desktop cable, 1x CPU | WebAssembly, prerendered, persisted state | warm | WebAssembly | 84 (100) | 0.000 (0.000) | 84 | 797 | 40 (48) | 35 (41) | 200 | 16 | 0 | 1 |
| Desktop cable, 1x CPU | Interactive Server (reference) | cold | Server | 120 (124) | 0.000 (0.000) | 120 | 365 | 16 (16) | 28 (29) | 89 | 65 | 47 | 1 |
| Desktop cable, 1x CPU | Interactive Server (reference) | warm | Server | 76 (88) | 0.000 (0.000) | 76 | 254 | 16 (16) | 28 (30) | 89 | 17 | 0 | 1 |
| Desktop cable, 1x CPU | Interactive Auto | cold | Server | 120 (128) | 0.000 (0.000) | 120 | 433 | 16 (16) | 27 (29) | 90 | 1114 | 1091 | 1 |
| Desktop cable, 1x CPU | Interactive Auto | warm | Server | 80 (84) | 0.000 (0.000) | 80 | 305 | 16 (32) | 29 (32) | 91 | 696 | 674 | 1 |
| Desktop cable, 1x CPU | Static SSR + JS island | cold | Island | 120 (200) | 0.000 (0.000) | 120 | 161 | 16 (16) | 10 (11) | 92 | 61 | 47 | 1 |
| Desktop cable, 1x CPU | Static SSR + JS island | warm | Island | 80 (80) | 0.000 (0.000) | 80 | 66 | 16 (16) | 12 (13) | 90 | 12 | 0 | 1 |
