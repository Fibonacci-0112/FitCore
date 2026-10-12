# 0002: Rebuild the clients and services on .NET

- **Status:** Proposed. Needs the product owner's approval and a decision on when to switch relative to finishing R1.
- **Date:** 2026-10-12
- **Scope:** every client platform (iOS, Android, Windows, macOS, web) and the server-side services. Supersedes the Electron approach in [0001](0001-windows-desktop-packaging.md) once accepted.
- **Related:** [`docs/BRD.md`](../BRD.md) §5

## Context

R1 is being built with Expo (React Native + web), an Electron shell for Windows, a TypeScript domain package (`packages/core`) and a Deno Edge Function on Supabase. The product owner prefers **.NET on every platform**, with desktop as a firm requirement and .NET elsewhere wherever it is practical.

Requirements the platform must meet (from the BRD):

- iOS, Android, Windows (Microsoft Store), macOS and a web app. The web app matters most for coaches reviewing clients.
- Native device features: Apple Health, Health Connect, camera and barcode, push notifications, secure storage, an offline workout logger with a rest timer.
- Email/password plus Google, Microsoft and Apple sign-in.
- The existing data contracts, RLS model and pgTAP tests must keep holding.

## Decision 1: UI with .NET MAUI Blazor Hybrid + Blazor Web, plus native pages where they pay off

A shared Razor class library holds the screens. A .NET MAUI app hosts them in a `BlazorWebView` on iOS, Android, Windows and macOS, and a Blazor Web App serves the same components on the web. This is the layout of the .NET 9+ "MAUI Blazor Hybrid and Web App" template.

Native MAUI XAML pages are used only where a native feel or performance clearly matters:
- the active-workout screen with its rest timer, and
- the camera / barcode scanner.

MAUI lets native pages and `BlazorWebView` pages live in the same app and share C# services through dependency injection.

### Options considered

| Option | Platforms | Pros | Cons | Verdict |
|---|---|---|---|---|
| **MAUI Blazor Hybrid + Blazor Web** | iOS, Android, Windows, macOS, Web | Microsoft-supported; one UI for apps and web; HTML/CSS skills and the web component ecosystem (charts, grids); full native API access from C#; MSIX for the Microsoft Store | UI runs in a WebView, so it is slightly less native; slower cold start; MAUI regressions between releases; no Linux | **Chosen**, with native pages for the workout and scanner screens |
| Uno Platform | iOS, Android, Windows, macOS, Linux, Web (WASM) | One XAML codebase everywhere, including web and Linux; consistent rendering; WinUI-compatible | Third-party, smaller community; WASM web is heavier to load and weaker for SEO; WinUI XAML learning curve | Good alternative if Linux or one XAML codebase becomes important |
| Avalonia | Windows, macOS, Linux (strong); iOS, Android, Web (newer) | Best desktop experience; mature; WPF-like | Mobile and web less mature; health and wearable bindings would be hand-written | Desktop-first fit, but mobile is this product's core |
| MAUI XAML + separate Blazor Web | iOS, Android, Windows, macOS + Web | Most native feel and performance on devices | Two UIs to build, test and keep in step for every feature | Rejected: the extra cost doesn't pay off for a small team |

### Deeper comparison of the two finalists

| Factor | Blazor Hybrid + Blazor Web | MAUI XAML + Blazor Web |
|---|---|---|
| Screens to build | One shared set | Two sets (XAML + Razor); view models can be shared |
| UI effort over time | About 1× | About 1.6–2×, with web and app drifting apart |
| Look and feel | Same web-styled UI everywhere; needs deliberate mobile styling (for example MudBlazor or Fluent UI Blazor) | Native controls and behaviour on each platform |
| Startup / long lists | Slightly slower cold start (WebView); long lists need `<Virtualize>` | Fastest start and smoothest lists |
| Charts | Web chart libraries, the same in app and web | Native chart libraries, rebuilt for web |
| Device features | Through C# services, or a native page when needed | Native from the start |
| Offline | SQLite in C#, the same in both | Same |
| Accessibility | HTML semantics inside the WebView; needs VoiceOver/TalkBack testing | Native accessibility APIs, most predictable |
| Testing | bUnit + Playwright for the shared Razor UI; Appium and the [native smoke checklist](../native-smoke-checklist.md) on devices for the native workout and scanner pages and the health, camera and notification integrations | bUnit + Playwright (web) + Appium (apps) + the native smoke checklist |
| Coach web dashboard | Mostly the same components the apps already use | Built separately |

**Revisit if:** WebView performance or accessibility fails testing on target devices (move more screens to native XAML), or Linux desktop becomes a requirement (consider Uno Platform).

## Decision 2: keep Supabase, add ASP.NET Core services

- **Supabase** stays the system of record: Postgres, Auth, Storage and row-level security. The migrations, RLS policies, `private` helper functions and pgTAP tests carry over unchanged.
- **Supabase Auth** provides email/password and the Google, Microsoft (Azure) and Apple providers. Apple App Review Guideline 4.8 requires an iOS app that offers third-party or social login to also offer an equivalent login option with defined privacy protections. We meet it with Sign in with Apple.
- **ASP.NET Core** replaces the Deno Edge Function for server-side work: USDA and other food-database search and import into the shared catalog, AI calls, payment webhooks (RevenueCat, Stripe), notifications. It holds the service-role key and third-party API keys as server secrets only.
- Clients call Supabase with the member's session so RLS still applies. Services act for the member by forwarding the member's JWT. They use the service role only for shared-catalog writes and system jobs.

### Options considered

| Option | Verdict |
|---|---|
| **Supabase + ASP.NET Core** | **Chosen.** Keeps the tested schema and RLS. Social login is built in. The least to rebuild. |
| ASP.NET Core Identity + EF Core + self-managed Postgres | Rejected for now. We would own password security, MFA, email flows and data-access rules, and lose database-enforced RLS unless we rebuild it. |
| Azure + Entra External ID | Viable and the most Microsoft-native. Azure offers BAAs for the later HIPAA phase. Reconsider at the Clinic phase. |
| Auth0 / Firebase Auth | Rejected. Per-user pricing at scale, and a second identity system next to the database. |

## Consequences

- **Rewrite cost.** The Expo app, the Electron shell and `packages/core` are rebuilt in C#. The TypeScript code stays in the repo as the reference implementation until the .NET version reaches R1 parity, then gets removed.
- **What carries over:** the database schema, RLS policies, `private` helpers, RPC functions, pgTAP tests, data contracts, and the core rules with their table tests (ported to C# test projects).
- **Supabase C# client** (`supabase-csharp`) is community-maintained, not first-party. Mitigation: keep data access behind our own interfaces. Where it is enough, call PostgREST and GoTrue over plain HTTP with typed models.
- **MAUI risk.** MAUI has had regressions between releases. Mitigation: pin SDK versions, run device smoke tests per release (extending `docs/native-smoke-checklist.md`), upgrade deliberately.
- **Windows packaging** moves from an Electron `.appx` to a MAUI MSIX, which Partner Center accepts natively. The OAuth redirect design in 0001 (custom `hfn:` protocol, PKCE, no unsolicited sessions) still applies; MAUI's `WebAuthenticator` handles it on mobile.
- **Tooling and CI.** The .NET SDK with the MAUI workloads replaces Node for the apps. iOS and macOS builds need macOS runners, and the Windows MSIX build needs a Windows runner.
- **Health integrations** use .NET bindings for HealthKit and Health Connect. These are community or hand-written; check each one's maturity before committing to it.

## Follow-ups

1. Product owner approves this record and decides the timing: finish R1 on TypeScript first, or switch now.
2. Update `docs/PLAN.md` (architecture, R1 PR list), `AGENTS.md` and `CLAUDE.md` (commands, layout, conventions) for .NET.
3. Spike: a MAUI Blazor Hybrid + Web solution with a shared Razor library, Supabase sign-in (email + Google + Apple) on iOS, Android, Windows and web, and an MSIX build on a Windows runner.
4. Port `packages/core` to a C# library, with its table tests.
5. Replace the `usda-search` Edge Function with an ASP.NET Core endpoint, keeping its tests and fixtures.
