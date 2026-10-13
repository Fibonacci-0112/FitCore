# Web Vitals by render mode

Output of `node vitals.mjs 5` on 2026-10-13: 5 runs per cell, median (worst in brackets). Chromium 141.0.7390.37, viewport 412×823. Times in ms, sizes in KiB as transferred.

- **LCP, CLS:** from the web-vitals library, measured up to the first tap.
- **Ready:** when the Today screen shows data and accepts taps (a `performance.mark` from the component).
- **Tap event:** duration of the "Add banana" tap from Event Timing, the number INP is built from.
- **Tap to row:** from the tap to the frame that shows the new row (NFR-PRF-7, ≤ 100 ms). **Saved:** to the frame that shows it confirmed.
- **Reads:** how many times the page read "today" from the diary (prerender plus client).

| Profile | Variant | Cache | LCP | CLS | FCP | Ready | Tap event | Tap to row | Saved | KiB | of which `_framework` | Reads |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Slow 4G, 4x CPU | Static SSR (baseline, not interactive) | cold | 1248 (1252) | 0.000 (0.000) | 1248 | n/a | n/a (n/a) | n/a (n/a) | n/a | 59 | 47 | 1 |
| Slow 4G, 4x CPU | Static SSR (baseline, not interactive) | warm | 672 (688) | 0.000 (0.000) | 672 | n/a | n/a (n/a) | n/a (n/a) | n/a | 11 | 0 | 1 |
| Slow 4G, 4x CPU | WebAssembly, prerendered, persisted state | cold | 1252 (1292) | 0.000 (0.000) | 1252 | 20296 | 152 (168) | 150 (163) | 1264 | 2351 | 2333 | 1 |
| Slow 4G, 4x CPU | WebAssembly, prerendered, persisted state | warm | 644 (696) | 0.000 (0.000) | 644 | 5033 | 136 (168) | 134 (153) | 1191 | 17 | 0 | 1 |
| Slow 4G, 4x CPU | WebAssembly, prerendered, refetch on start | cold | 1260 (1272) | 0.000 (0.000) | 1260 | 20909 | 160 (168) | 149 (158) | 982 | 2349 | 2333 | 2 |
| Slow 4G, 4x CPU | WebAssembly, prerendered, refetch on start | warm | 656 (688) | 0.000 (0.000) | 656 | 5626 | 152 (160) | 147 (152) | 975 | 15 | 0 | 2 |
| Slow 4G, 4x CPU | WebAssembly only, no prerendering | cold | 20944 (21252) | 0.000 (0.000) | 20104 | 20897 | 160 (208) | 155 (201) | 1037 | 2344 | 2333 | 1 |
| Slow 4G, 4x CPU | WebAssembly only, no prerendering | warm | 5536 (5988) | 0.000 (0.000) | 4696 | 5483 | 152 (200) | 148 (196) | 958 | 9 | 0 | 1 |
| Slow 4G, 4x CPU | Interactive Server (reference) | cold | 1244 (1276) | 0.000 (0.000) | 1244 | 3719 | 16 (24) | 35 (48) | 117 | 65 | 47 | 1 |
| Slow 4G, 4x CPU | Interactive Server (reference) | warm | 644 (672) | 0.000 (0.000) | 644 | 2697 | 16 (16) | 35 (37) | 111 | 17 | 0 | 1 |
| Fast 4G, 2x CPU | Static SSR (baseline, not interactive) | cold | 368 (372) | 0.000 (0.000) | 368 | n/a | n/a (n/a) | n/a (n/a) | n/a | 59 | 47 | 1 |
| Fast 4G, 2x CPU | Static SSR (baseline, not interactive) | warm | 208 (216) | 0.000 (0.000) | 208 | n/a | n/a (n/a) | n/a (n/a) | n/a | 11 | 0 | 1 |
| Fast 4G, 2x CPU | WebAssembly, prerendered, persisted state | cold | 360 (396) | 0.000 (0.000) | 360 | 5067 | 88 (104) | 81 (104) | 475 | 2351 | 2333 | 1 |
| Fast 4G, 2x CPU | WebAssembly, prerendered, persisted state | warm | 204 (212) | 0.000 (0.000) | 204 | 2091 | 72 (88) | 65 (86) | 440 | 17 | 0 | 1 |
| Fast 4G, 2x CPU | WebAssembly, prerendered, refetch on start | cold | 368 (376) | 0.000 (0.000) | 368 | 5157 | 64 (72) | 61 (64) | 350 | 2349 | 2333 | 2 |
| Fast 4G, 2x CPU | WebAssembly, prerendered, refetch on start | warm | 208 (216) | 0.000 (0.000) | 208 | 2227 | 72 (88) | 68 (85) | 335 | 15 | 0 | 2 |
| Fast 4G, 2x CPU | WebAssembly only, no prerendering | cold | 5212 (5532) | 0.000 (0.000) | 4836 | 5172 | 80 (88) | 76 (80) | 361 | 2344 | 2333 | 1 |
| Fast 4G, 2x CPU | WebAssembly only, no prerendering | warm | 2176 (2296) | 0.000 (0.000) | 1940 | 2152 | 64 (72) | 60 (67) | 332 | 9 | 0 | 1 |
| Fast 4G, 2x CPU | Interactive Server (reference) | cold | 364 (380) | 0.000 (0.000) | 364 | 1071 | 0 (16) | 23 (25) | 102 | 65 | 47 | 1 |
| Fast 4G, 2x CPU | Interactive Server (reference) | warm | 204 (236) | 0.000 (0.000) | 204 | 798 | 0 (16) | 24 (27) | 97 | 17 | 0 | 1 |
| Desktop cable, 1x CPU | Static SSR (baseline, not interactive) | cold | 120 (124) | 0.000 (0.000) | 120 | n/a | n/a (n/a) | n/a (n/a) | n/a | 59 | 47 | 1 |
| Desktop cable, 1x CPU | Static SSR (baseline, not interactive) | warm | 72 (80) | 0.000 (0.000) | 72 | n/a | n/a (n/a) | n/a (n/a) | n/a | 11 | 0 | 1 |
| Desktop cable, 1x CPU | WebAssembly, prerendered, persisted state | cold | 124 (132) | 0.000 (0.000) | 124 | 3044 | 32 (40) | 27 (38) | 206 | 2351 | 2333 | 1 |
| Desktop cable, 1x CPU | WebAssembly, prerendered, persisted state | warm | 76 (88) | 0.000 (0.000) | 76 | 807 | 24 (32) | 23 (25) | 192 | 17 | 0 | 1 |
| Desktop cable, 1x CPU | WebAssembly, prerendered, refetch on start | cold | 120 (136) | 0.000 (0.000) | 120 | 3097 | 32 (32) | 26 (30) | 160 | 2349 | 2333 | 2 |
| Desktop cable, 1x CPU | WebAssembly, prerendered, refetch on start | warm | 96 (104) | 0.000 (0.000) | 96 | 872 | 24 (32) | 24 (31) | 149 | 15 | 0 | 2 |
| Desktop cable, 1x CPU | WebAssembly only, no prerendering | cold | 3112 (3184) | 0.000 (0.000) | 3012 | 3089 | 32 (32) | 26 (28) | 163 | 2344 | 2333 | 1 |
| Desktop cable, 1x CPU | WebAssembly only, no prerendering | warm | 884 (984) | 0.000 (0.000) | 780 | 868 | 32 (40) | 28 (35) | 158 | 9 | 0 | 1 |
| Desktop cable, 1x CPU | Interactive Server (reference) | cold | 128 (144) | 0.000 (0.000) | 128 | 363 | 16 (16) | 29 (30) | 89 | 65 | 47 | 1 |
| Desktop cable, 1x CPU | Interactive Server (reference) | warm | 80 (88) | 0.000 (0.000) | 80 | 256 | 16 (16) | 29 (29) | 88 | 17 | 0 | 1 |
