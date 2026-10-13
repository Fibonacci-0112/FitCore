# Taps while the page starts

Output of `node early-tap.mjs 5` on 2026-10-13: 5 loads per row, cold (empty cache) and warm (cache primed by one earlier load), a tap on the heading every 250 ms from first paint until the screen is interactive. Event Timing duration of each tap in ms.

INP takes roughly the worst interaction of a page view, so the last column is the one that compares with the 200 ms target.

| Profile | Variant | Cache | Taps | Median | p75 | Worst | Median of each load's worst |
|---|---|---|---|---|---|---|---|
| Slow 4G, 4x CPU | `/islands` | cold | 10 | 336 | 392 | 456 | 392 |
| Slow 4G, 4x CPU | `/islands` | warm | 0 | interactive before the first tap | | | |
| Fast 4G, 2x CPU | `/islands` | cold | 5 | 96 | 112 | 120 | 96 |
| Fast 4G, 2x CPU | `/islands` | warm | 0 | interactive before the first tap | | | |
