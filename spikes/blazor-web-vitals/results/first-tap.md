# First tap after load

## With the yield (as committed)

Output of `node first-tap.mjs 5 4` on 2026-10-13: 5 page loads per row, 4 taps each, no network throttling. Event Timing duration in ms, median (worst in brackets).

| Variant | CPU slowdown | First tap | Later taps |
|---|---|---|---|
| `/wasm` | 1× | 32 (40) | 16 (24) |
| `/wasm` | 2× | 72 (88) | 32 (64) |
| `/wasm` | 4× | 160 (200) | 72 (88) |
| `/wasm-nopersist` | 1× | 32 (40) | 16 (24) |
| `/wasm-nopersist` | 2× | 72 (112) | 32 (48) |
| `/wasm-nopersist` | 4× | 176 (216) | 72 (112) |
| `/server` | 1× | 16 (16) | 16 (16) |
| `/server` | 2× | 16 (16) | 16 (16) |
| `/server` | 4× | 24 (32) | 16 (16) |

## Without the yield (dotnet publish -p:SpikeNoYield=true)

Output of `node first-tap.mjs 5 4` on 2026-10-13: 5 page loads per row, 4 taps each, no network throttling. Event Timing duration in ms, median (worst in brackets).

| Variant | CPU slowdown | First tap | Later taps |
|---|---|---|---|
| `/wasm` | 1× | 96 (112) | 16 (24) |
| `/wasm` | 2× | 224 (240) | 40 (56) |
| `/wasm` | 4× | 528 (584) | 80 (112) |
| `/wasm-nopersist` | 1× | 64 (72) | 16 (24) |
| `/wasm-nopersist` | 2× | 152 (168) | 40 (56) |
| `/wasm-nopersist` | 4× | 368 (472) | 80 (120) |
| `/server` | 1× | 16 (16) | 16 (16) |
| `/server` | 2× | 16 (16) | 16 (16) |
| `/server` | 4× | 16 (24) | 16 (24) |
