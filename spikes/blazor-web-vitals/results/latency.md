# Taps by round-trip time

Output of `node latency.mjs 5 4` on 2026-10-13: 5 page loads per row, 4 taps on "Add banana" each, 2× CPU slowdown. The round-trip time is added by `proxy.mjs` to every byte in both directions, WebSocket frames included, with no bandwidth limit. The server's write takes 80 ms. Times in ms, median (worst in brackets).

- **Tap event:** the Event Timing duration of the tap, what INP is built from (≤ 200 ms).
- **Tap to row:** from the tap to the frame that shows the new row, NFR-PRF-7's visible feedback (≤ 100 ms). **First tap** is the first tap of each page load.
- **Saved:** from the tap to the frame that shows the row as saved.
- **Renderer:** what actually ran the page. The DevTools rows use Chromium's network emulation instead of the proxy.

| Added RTT | Variant | Renderer | Tap event | Tap to row, first tap | Tap to row | Saved |
|---|---|---|---|---|---|---|
| 0 | WebAssembly, prerendered | WebAssembly | 40 (128) | 91 | 38 (122) | 154 |
| 0 | Interactive Server | Server | 16 (16) | 25 | 26 (28) | 90 |
| 0 | Interactive Auto, first visit | Server | 16 (16) | 25 | 26 (38) | 90 |
| 0 | Interactive Auto, return visit | WebAssembly | 40 (88) | 76 | 37 (83) | 147 |
| 0 | Static SSR + JS island | Island | 16 (16) | 10 | 10 (12) | 93 |
| 50 | WebAssembly, prerendered | WebAssembly | 40 (104) | 75 | 34 (101) | 197 |
| 50 | Interactive Server | Server | 16 (16) | 65 | 61 (67) | 140 |
| 50 | Interactive Auto, first visit | Server | 16 (16) | 64 | 62 (69) | 140 |
| 50 | Interactive Auto, return visit | WebAssembly | 32 (104) | 92 | 30 (98) | 194 |
| 50 | Static SSR + JS island | Island | 16 (16) | 9 | 10 (13) | 142 |
| 100 | WebAssembly, prerendered | WebAssembly | 40 (104) | 71 | 37 (103) | 248 |
| 100 | Interactive Server | Server | 16 (80) | 114 | 109 (130) | 190 |
| 100 | Interactive Auto, first visit | Server | 16 (32) | 114 | 109 (134) | 190 |
| 100 | Interactive Auto, return visit | WebAssembly | 40 (104) | 93 | 36 (98) | 248 |
| 100 | Static SSR + JS island | Island | 16 (16) | 9 | 11 (14) | 193 |
| 200 | WebAssembly, prerendered | WebAssembly | 40 (112) | 71 | 38 (106) | 349 |
| 200 | Interactive Server | Server | 16 (16) | 213 | 209 (214) | 290 |
| 200 | Interactive Auto, first visit | Server | 16 (16) | 215 | 209 (216) | 290 |
| 200 | Interactive Auto, return visit | WebAssembly | 40 (104) | 75 | 34 (101) | 346 |
| 200 | Static SSR + JS island | Island | 16 (16) | 10 | 10 (13) | 293 |
| 400 | WebAssembly, prerendered | WebAssembly | 40 (96) | 69 | 34 (92) | 546 |
| 400 | Interactive Server | Server | 16 (16) | 414 | 409 (417) | 491 |
| 400 | Interactive Auto, first visit | Server | 16 (16) | 415 | 409 (416) | 490 |
| 400 | Interactive Auto, return visit | WebAssembly | 40 (104) | 76 | 34 (102) | 547 |
| 400 | Static SSR + JS island | Island | 16 (16) | 10 | 10 (12) | 494 |
| 200 (DevTools) | Interactive Server | Server | 16 (16) | 24 | 27 (29) | 91 |
| 400 (DevTools) | Interactive Server | Server | 16 (64) | 25 | 27 (79) | 90 |
