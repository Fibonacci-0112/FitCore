# Experiments on the slow first tap

Manual runs on 2026-10-13 that found why the first "Add banana" tap was slow on WebAssembly, and what fixes it. They used throwaway variants of `measure/first-tap.mjs`, without network throttling, on the `/wasm` page. Numbers are the click's Event Timing duration in ms; each cell is one or two page loads, so read them as directions, not statistics. [`first-tap.md`](first-tap.md) has the repeatable before-and-after run.

## 1. The first tap is slow, later taps are not

Build without the yield (the view model awaited the API call straight after adding the row). Taps 1 to 6 on one page load:

| CPU slowdown | Taps (ms) |
|---|---|
| 1× | 120, 16, 16, 16, 16, 16 |
| 2× | 216, 56, 32, 40, 24, 32 |
| 4× | 496, 88, 56, 80, 64, 64 |

The `/wasm-nopersist` page was faster on its first tap (160 ms at 2×) because it had already made an HTTP call and parsed JSON while starting up.

## 2. Where the time goes

A Chrome trace of the first tap at 4× CPU spent 562 ms in the mouse-release event handler, which is .NET code running inside the click. V8's lazy WebAssembly compilation was only 47 ms of it, and style, layout and paint about 15 ms.

A trivial Blazor button (a counter) tapped first, followed by "Add banana":

| Build | CPU slowdown | Counter, first tap | "Add", first tap | "Add", second tap |
|---|---|---|---|---|
| No yield | 2× | 80 | 240, 232 | 24, 32 |
| No yield | 4× | 128, 144 | 456, 504 | 64, 72 |
| `await Task.Yield()` before the API call | 2× | 64, 96 | 48, 56 | 32, 40 |
| `await Task.Yield()` before the API call | 4× | 136, 168 | 88, 112 | 56, 72 |
| `await Task.Delay(1)` before the API call | 2× | 56, 72 | 40, 56 | 32, 40 |
| `await Task.Delay(1)` before the API call | 4× | 136, 152 | 128, 152 | 48, 56 |

Blazor's own event path costs 60 to 90 ms extra the first time. The rest was the first run of the HTTP client and JSON serializer. Those ran synchronously inside the click, before the handler's first real `await`, so the optimistic row couldn't paint until they finished. Yielding once before the API call moves that work after the paint. `Task.Delay(1)` does no better than `Task.Yield()`.

## 3. AOT compilation and eager wasm compilation don't fix it

Built with `dotnet workload install wasm-tools` and `-p:RunAOTCompilation=true`, without the yield:

| CPU slowdown | First tap | Later taps |
|---|---|---|
| 1× | 104 | 16 |
| 2× | 264 | 24 |
| 4× | 512 | 40 to 64 |

AOT makes later taps a little faster but leaves the first tap as slow, and the payload grows from 2.8 MiB to 4.8 MiB brotli on disk (`dotnet.native.wasm` grows from 953 to 3,113 KiB). Starting Chromium with `--js-flags=--no-wasm-lazy-compilation` cut the AOT first tap to 192 ms at 2× and 376 ms at 4×, and did nothing for the interpreter build (240 ms at 2×, 568 ms at 4×). Neither is worth its cost next to the yield.

## 4. Build gotcha

Switching between AOT and interpreter builds without deleting `obj/` produced an app that crashed on start (`MONO interpreter: NIY encountered in method System.OutOfMemoryException:.ctor`). AOT strips IL from the assemblies it compiles (`WasmStripILAfterAOT`), and the interpreter build reused the stripped copies from `obj/`. A clean build works. Installing `wasm-tools` also makes every Release publish relink `dotnet.native.wasm`, which saved only 45 KiB here. The published results use the SDK without the workload, as CI does.

## 5. The refetch control flickers

A MutationObserver on `/wasm-nopersist` (Fast 4G) recorded the screen's states from first paint until interactive: prerendered content with 18 rows, then "Loading…" with no rows, then 18 rows again. `/wasm` went straight from the prerendered content to the interactive content. CLS stayed at 0 for both, because content that is removed and added again isn't a layout shift.
