# Spike: Sign in with Apple and MFA with Supabase Auth

Throwaway code for [ADR 0003](../../docs/decisions/0003-modular-monolith.md) follow-up 2. It runs Supabase Auth locally and checks passwords, TOTP MFA, step-up, how RLS sees `aal2`, passkeys, WebAuthn as a second factor and the native Apple ID-token path. The findings are in [`docs/spikes/apple-sign-in-and-mfa.md`](../../docs/spikes/apple-sign-in-and-mfa.md).

Nothing here is production code. Sign in with Apple on a real device needs a Mac, an iPhone and an Apple developer account; the device test plan is in the findings.

## Contents

| Path | Contents |
|---|---|
| `supabase/config.toml` | Supabase CLI project: 15-character passwords, TOTP and WebAuthn MFA, passkeys, and the Apple provider with a bundle ID as its client ID |
| `supabase/migrations/` | `care.relationships`, `nutrition.diary_entries` and a grant check where a clinician's grant needs `aal2`; the `fitcore_api` login role |
| `AuthSpike/` | .NET 10 console app: Auth REST calls, JWKS validation, TOTP, RLS through `fitcore_api`, and the `Supabase.Gotrue` package |
| `webauthn/` | Node script that drives a Chromium virtual authenticator for passkeys and WebAuthn MFA |
| `results/` | Raw output of the runs the findings quote |

All keys and passwords are the Supabase CLI's public local defaults or local-only placeholders.

## Run it

Needs Docker, the .NET 10 SDK, Node 22 and a Chromium build (set `CHROMIUM` to its path).

```sh
cd spikes/auth-mfa
SUPABASE_AUTH_EXTERNAL_APPLE_SECRET= npx supabase@2.120.0 start \
  -x realtime,storage-api,imgproxy,postgrest,postgres-meta,studio,edge-runtime,logflare,vector,supavisor,mailpit
dotnet run --project AuthSpike           # exit code 1 on any failure; GAP rows don't fail
(cd webauthn && npm ci && npm run check)
npx supabase@2.120.0 stop --no-backup
```

The Apple check fetches Apple's public keys from `appleid.apple.com`. Behind a TLS-intercepting proxy, the Auth container must trust the proxy's CA, or the check gets HTTP 500 instead of 400.
