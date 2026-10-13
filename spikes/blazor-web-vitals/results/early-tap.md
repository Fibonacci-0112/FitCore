# Taps while the page starts

Output of `node early-tap.mjs 5` on 2026-10-13: 5 loads per row, cold (empty cache) and warm (cache primed by one earlier load), a tap on the heading every 250 ms from first paint until the screen is interactive. Event Timing duration of each tap in ms.

INP takes roughly the worst interaction of a page view, so the last column is the one that compares with the 200 ms target.

| Profile | Variant | Cache | Taps | Median | p75 | Worst | Median of each load's worst |
|---|---|---|---|---|---|---|---|
| Slow 4G, 4x CPU | `/wasm` | cold | 298 | 16 | 16 | 1144 | 1072 |
| Slow 4G, 4x CPU | `/wasm` | warm | 17 | 16 | 1088 | 1256 | 1184 |
| Slow 4G, 4x CPU | `/server` | cold | 58 | 16 | 16 | 424 | 400 |
| Slow 4G, 4x CPU | `/server` | warm | 40 | 16 | 16 | 16 | 16 |
| Fast 4G, 2x CPU | `/wasm` | cold | 65 | 16 | 96 | 688 | 552 |
| Fast 4G, 2x CPU | `/wasm` | warm | 13 | 352 | 464 | 568 | 512 |
| Fast 4G, 2x CPU | `/server` | cold | 19 | 16 | 88 | 112 | 104 |
| Fast 4G, 2x CPU | `/server` | warm | 14 | 16 | 16 | 16 | 16 |
