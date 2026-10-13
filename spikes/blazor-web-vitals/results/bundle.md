# WebAssembly payload

Output of `node bundle.mjs` on 2026-10-13: every file under `_framework/` that has a brotli copy, largest first. A browser downloads only the ICU shard for its culture (`icudt_EFIGS.dat` for English), and `blazor.server.js` is never loaded by the WebAssembly pages.

| File | Brotli KiB | Raw KiB |
|---|---|---|
| `dotnet.native.wasm` | 953 | 2931 |
| `System.Private.CoreLib.wasm` | 493 | 1575 |
| `icudt_CJK.dat` | 243 | 934 |
| `icudt_no_CJK.dat` | 217 | 1081 |
| `icudt_EFIGS.dat` | 141 | 538 |
| `System.Text.Json.wasm` | 127 | 378 |
| `Microsoft.AspNetCore.Components.wasm` | 61 | 171 |
| `System.Net.Http.wasm` | 50 | 133 |
| `dotnet.runtime.js` | 47 | 194 |
| `blazor.web.js` | 46 | 196 |
| `Microsoft.AspNetCore.Components.WebAssembly.wasm` | 39 | 115 |
| `blazor.server.js` | 38 | 161 |
| `dotnet.native.js` | 30 | 142 |
| `System.Private.Uri.wasm` | 26 | 65 |
| `Microsoft.Extensions.DependencyInjection.wasm` | 19 | 46 |
| `Microsoft.AspNetCore.Components.Web.wasm` | 19 | 55 |
| `System.Collections.Immutable.wasm` | 19 | 50 |
| `Microsoft.JSInterop.wasm` | 18 | 45 |
| `System.Runtime.InteropServices.JavaScript.wasm` | 17 | 43 |
| `blazor.webassembly.js` | 16 | 59 |
| `CommunityToolkit.Mvvm.wasm` | 16 | 24 |
| `System.Diagnostics.DiagnosticSource.wasm` | 16 | 39 |
| `BlazorSpike.Core.wasm` | 14 | 40 |
| `dotnet.js` | 13 | 50 |
| `System.Linq.wasm` | 11 | 25 |
| `Microsoft.Extensions.Logging.Abstractions.wasm` | 11 | 27 |
| `Microsoft.Extensions.DependencyInjection.Abstractions.wasm` | 10 | 26 |
| `System.Text.Encodings.Web.wasm` | 9 | 29 |
| `System.Collections.wasm` | 9 | 22 |
| `System.Net.Http.Json.wasm` | 9 | 21 |
| `System.Collections.Concurrent.wasm` | 8 | 20 |
| `BlazorSpike.Client.wasm` | 8 | 21 |
| `System.Security.Cryptography.wasm` | 8 | 20 |
| `Microsoft.Extensions.Logging.wasm` | 7 | 17 |
| `Microsoft.Extensions.Options.wasm` | 7 | 16 |
| `System.Text.RegularExpressions.wasm` | 7 | 15 |
| `System.Console.wasm` | 6 | 14 |
| `System.Memory.wasm` | 6 | 14 |
| `Microsoft.JSInterop.WebAssembly.wasm` | 5 | 12 |
| `Microsoft.Extensions.Configuration.Json.wasm` | 4 | 10 |
| `System.ObjectModel.wasm` | 4 | 10 |
| `Microsoft.Extensions.Configuration.wasm` | 4 | 9 |
| `Microsoft.Extensions.Primitives.wasm` | 3 | 9 |
| `System.Net.Primitives.wasm` | 3 | 7 |
| `System.IO.Pipelines.wasm` | 3 | 6 |
| `System.Runtime.wasm` | 2 | 7 |
| `Microsoft.Extensions.Configuration.Abstractions.wasm` | 2 | 7 |
| `System.ComponentModel.wasm` | 2 | 5 |
| **All files** | **2824** | **9433** |
