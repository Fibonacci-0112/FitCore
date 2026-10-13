# Web Vitals by render mode

Output of `node vitals.mjs 5` on 2026-10-13: 5 runs per cell, median (worst in brackets). Chromium 141.0.7390.37, viewport 412×823. Times in ms, sizes in KiB as transferred.

- **LCP, CLS:** from the web-vitals library, measured up to the first tap.
- **Ready:** when the Today screen shows data and accepts taps (a `performance.mark` from the component).
- **Tap event:** duration of the "Add banana" tap from Event Timing, the number INP is built from.
- **Tap to row:** from the tap to the frame that shows the new row (NFR-PRF-7, ≤ 100 ms). **Saved:** to the frame that shows it confirmed.
- **Reads:** how many times the page read "today" from the diary (prerender plus client).
- **Renderer:** what ran the page once it was interactive. DevTools throttling doesn't delay WebSocket frames, so taps on a circuit (Server) show no network time here; see `latency.md`.

| Profile | Variant | Cache | Renderer | LCP | CLS | FCP | Ready | Tap event | Tap to row | Saved | KiB | of which `_framework` | Reads |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Slow 4G, 4x CPU | Static SSR + JS island | cold | Island | 1252 (1264) | 0.000 (0.000) | 1252 | 1246 | 24 (32) | 23 (26) | 613 | 14 | 0 | 1 |
| Slow 4G, 4x CPU | Static SSR + JS island | warm | Island | 652 (712) | 0.000 (0.000) | 652 | 652 | 16 (24) | 16 (17) | 604 | 12 | 0 | 1 |
| Fast 4G, 2x CPU | Static SSR + JS island | cold | Island | 372 (388) | 0.000 (0.000) | 372 | 357 | 16 (16) | 10 (10) | 173 | 14 | 0 | 1 |
| Fast 4G, 2x CPU | Static SSR + JS island | warm | Island | 200 (204) | 0.000 (0.000) | 200 | 193 | 16 (16) | 10 (11) | 171 | 12 | 0 | 1 |
| Desktop cable, 1x CPU | Static SSR + JS island | cold | Island | 124 (124) | 0.000 (0.000) | 124 | 111 | 16 (16) | 11 (11) | 92 | 14 | 0 | 1 |
| Desktop cable, 1x CPU | Static SSR + JS island | warm | Island | 76 (80) | 0.000 (0.000) | 76 | 58 | 16 (16) | 12 (13) | 91 | 12 | 0 | 1 |
