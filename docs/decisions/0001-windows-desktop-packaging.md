# 0001: Windows desktop packaging for the Microsoft Store

- **Status:** Accepted for the spike. The `desktop-windows` CI job is green; Store submission still needs the real Partner Center identity values (see below).
- **Date:** 2026-10-10
- **Scope:** the "Windows spike" in [`docs/PLAN.md`](../PLAN.md) (Platform validation)

## Context

The plan ships Windows as an Electron shell over the Expo web export and lists it in the Microsoft Store. The spike had to answer three questions:

1. Which package format to submit, given that electron-builder's `appx` target builds `.appx` and not `.msix`.
2. How the shell serves the web export so Expo Router, `localStorage` and Supabase all behave as they do on the web.
3. How Supabase auth redirects reach the app: a custom protocol or a loopback server.

## Decision 1: submit `.appx` built by electron-builder

Partner Center accepts `.msix`, `.appx`, their bundles and their upload files for packaged apps ([App package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements)). MSIX is the newer name for the same package family. Both use the same manifest schema, `makeappx` tool, full-trust desktop entry point (`Windows.FullTrustApplication`), Store re-signing and Store-delivered updates. electron-builder 26 always writes `.appx`. Its AppX target runs `makeappx pack` with a generated `AppxManifest.xml`.

For an Electron app that needs a window, a protocol handler and network access, `.appx` loses nothing, so we keep one toolchain and submit the `.appx`.

| Option | Verdict |
|---|---|
| **`.appx` from electron-builder** | **Chosen.** One command (`npm run dist:win -w @hfn/desktop`), the manifest is generated from `electron-builder.yml` (identity, `hfn:` protocol), and it runs on a hosted Windows runner. The Store re-signs it, so no code-signing certificate is needed. |
| `.msix` via the MSIX Packaging Tool or `makeappx` on the unpacked app | Same result for this app, plus a second toolchain and a hand-maintained manifest. Worth it only if we need an MSIX-only feature (for example modification packages or package-level app attach). None is planned. |
| EXE/NSIS or MSI installer submitted to the Store | Rejected. The Store doesn't re-sign these, so we would have to buy an Authenticode certificate and host the installer ourselves, and we'd lose Store-managed updates. |

**Revisit if:** Partner Center rejects the `.appx`; we need an MSIX-only capability; or electron-builder adds a native MSIX target and the Store starts preferring it.

## Decision 2: serve the export from a privileged `app://hfn` scheme

The shell registers `app` as a standard, secure scheme with Fetch API and CORS support. It serves `resources/web` (a copy of `apps/mobile/dist`) through `protocol.handle`:

- A path with a file extension is served as that file, or returns 404 when it's missing, so a missing script never comes back as HTML.
- Any other path returns `index.html`, because the export is a single-page app (`web.output: "single"`) and Expo Router renders the route.
- Requests that would escape the web root, or that target any other host, are refused.

The routing rules live in `apps/desktop/src/routing.ts` and have unit tests.

Rejected alternatives:
- **`file://`.** Expo's absolute asset paths (`/_expo/...`) and client-side routes break, and `file://` origins are opaque for storage and CORS.
- **A loopback HTTP server.** It works, but needs a free port, exposes the app to other local processes, and the origin changes whenever the port does, which would orphan `localStorage` sessions.

Hardening in `main.ts`: context isolation, the Chromium sandbox, no Node integration, no preload, and no menu. Navigation can't leave `app://hfn`, and only `https:` links are handed to the system browser.

## Decision 3: auth redirects use the `hfn:` custom protocol

R1 sign-in is email and password over `fetch`, so it needs no redirect. Email confirmation, password reset and OAuth (later releases) do. We use the same `hfn` scheme the native apps already declare in `app.json`:

- The AppX manifest declares `hfn` as a `windows.protocol` extension (`protocols` in `electron-builder.yml`). Outside the Store package, the app registers the scheme itself with `app.setAsDefaultProtocolClient`.
- A single-instance lock keeps one window. A link opened while the app runs arrives in the second instance's command line. The running instance maps `hfn://<path>?<query>#<hash>` to `app://hfn/<path>?<query>#<hash>` and loads it. Supabase URL session detection is disabled: unsolicited links must not sign in or replace the current account. R1 uses password sign-in, including after email confirmation.
- When redirect auth flows land: configure the Supabase Auth redirect allow list and pass `emailRedirectTo`/`redirectTo: "hfn://auth/callback"` when running in the shell (`location.protocol === "app:"`). Use PKCE with a locally initiated transaction and correlated callback handling; reject unsolicited implicit token sessions. The code verifier lives in the shell's own `localStorage`, which is the same profile that receives the link.

We rejected the loopback redirect (`http://127.0.0.1:<port>/callback`) for the same port and exposure reasons as Decision 2. It would also mean a different flow from the native apps, which already use `hfn://`.

## Evidence

| Check | Where | Result |
|---|---|---|
| Routing rules (asset vs. route fallback, traversal, other hosts, deep-link mapping) | Vitest, `apps/desktop/src/routing.test.ts` | Passing |
| Shell serves the export at `app://hfn`, a client route survives navigation, a missing asset is a 404 | Playwright `_electron`, dev build and packaged (unpacked) Linux build | Passing locally and in CI |
| A second instance launched with `hfn://sign-in?…#…` routes the running window, query and hash intact, still one window | Same | Passing locally and in CI |
| Sign-up from the `app://hfn` origin against Supabase (CORS + auth), then the session survives an app restart | Same, against a local Supabase stack. Also runs in the CI `e2e` job | Passing locally and in CI |
| `.appx` builds, and its manifest has the `hfn` protocol and full-trust entry point | CI `desktop-windows` (`windows-latest`) | Passing |
| Packaged `win-unpacked/HFN.exe` passes the same smoke tests | CI `desktop-windows` | Passing |
| A test-signed copy installs with `Add-AppxPackage`, and opening `hfn://sign-in` starts the packaged app | CI `desktop-windows` | Passing |

Hosted Supabase answers CORS the same way as the local stack (its API gateway allows any origin), but check it once against the dev project before submission.

## Before the first Store submission

1. Reserve the app name in Partner Center and copy **Product identity** into `appx` in `apps/desktop/electron-builder.yml`: `identityName`, `publisher` (`CN=…`) and `publisherDisplayName`. The values are case-sensitive. Update the publisher in the CI install step to match.
2. Replace electron-builder's default tile and logo assets with real ones in `apps/desktop/build-resources/appx/` (`StoreLogo.png`, `Square150x150Logo.png`, `Square44x44Logo.png`, `Wide310x150Logo.png`).
3. Run the Windows App Certification Kit on the `.appx` on a Windows machine.
4. Point the export at the production Supabase project, and provide a privacy policy URL (Store requirement for apps that handle personal data).
5. Optional: add `--arm64` and submit an `.appxbundle` for Windows on Arm.

## Consequences

- Windows reuses the web build unchanged. Anything that works on web works in the shell, and native-only features (camera, barcode in R4) need a web implementation or a desktop fallback.
- Every CI run now downloads Electron during `npm ci`, and a Windows job adds a few minutes.
- The Store delivers updates, so the shell has no auto-updater (`publish: null`).
