# Taps while the page starts

Output of `node early-tap.mjs 5` on 2026-10-13: 5 loads per row, cold (empty cache) and warm (cache primed by one earlier load), a tap on the heading every 250 ms from first paint until the screen is interactive. Event Timing duration of each tap in ms.

> The `/islands` rows here are from a build that still loaded `blazor.web.js` on that page. [`early-tap-islands.md`](early-tap-islands.md) has `/islands` rerun without it, as committed.

INP takes roughly the worst interaction of a page view, so the last column is the one that compares with the 200 ms target.

| Profile | Variant | Cache | Taps | Median | p75 | Worst | Median of each load's worst |
|---|---|---|---|---|---|---|---|
| Slow 4G, 4x CPU | `/wasm` | cold | 298 | 16 | 16 | 1248 | 1088 |
| Slow 4G, 4x CPU | `/wasm` | warm | 19 | 16 | 1000 | 1288 | 1208 |
| Slow 4G, 4x CPU | `/server` | cold | 58 | 16 | 16 | 432 | 408 |
| Slow 4G, 4x CPU | `/server` | warm | 40 | 16 | 16 | 16 | 16 |
| Slow 4G, 4x CPU | `/auto` | cold | 71 | 16 | 16 | 400 | 384 |
| Slow 4G, 4x CPU | `/auto` | warm | 44 | 16 | 80 | 1208 | 1160 |
| Slow 4G, 4x CPU | `/islands` | cold | 16 | 136 | 400 | 440 | 400 |
| Slow 4G, 4x CPU | `/islands` | warm | 0 | interactive before the first tap | | | |
| Fast 4G, 2x CPU | `/wasm` | cold | 65 | 16 | 96 | 536 | 520 |
| Fast 4G, 2x CPU | `/wasm` | warm | 12 | 488 | 544 | 592 | 520 |
| Fast 4G, 2x CPU | `/server` | cold | 17 | 16 | 88 | 104 | 96 |
| Fast 4G, 2x CPU | `/server` | warm | 15 | 16 | 16 | 16 | 16 |
| Fast 4G, 2x CPU | `/auto` | cold | 20 | 16 | 88 | 104 | 96 |
| Fast 4G, 2x CPU | `/auto` | warm | 25 | 40 | 496 | 680 | 640 |
| Fast 4G, 2x CPU | `/islands` | cold | 5 | 104 | 104 | 104 | 104 |
| Fast 4G, 2x CPU | `/islands` | warm | 0 | interactive before the first tap | | | |
