# Spike: Sign in with Apple and MFA with Supabase Auth

- **Status:** Partly done. MFA, passkeys and the server side of Sign in with Apple were tested locally. The on-device Apple flow needs a Mac, an iPhone and an Apple developer account; see the [device test plan](#5-device-test-plan-for-sign-in-with-apple).
- **Date:** 2026-10-13
- **Decision record:** [ADR 0003](../decisions/0003-modular-monolith.md), follow-up 2
- **Code and raw results:** [`spikes/auth-mfa/`](../../spikes/auth-mfa/README.md)

## 1. Question

ADR 0003 puts sign-in on Supabase Auth with email/password, Google, Microsoft and Sign in with Apple ([BRD §5](../BRD.md#5-platform-and-technology-constraints)). The spike answers four questions:

1. Can the apps meet NFR-SEC-3 (MFA with TOTP or passkey for everyone, required for clinicians and admins) with Supabase Auth, and how do the API and RLS enforce "required"?
2. Does Supabase support passkeys, and do they count as MFA?
3. What does native Sign in with Apple from .NET MAUI to Supabase need, including App Store account-deletion rules?
4. Can the .NET clients use the `Supabase.Gotrue` package, or should they call the Auth REST API?

## 2. Setup

| Component | Version |
|---|---|
| Supabase CLI | 2.120.0, local stack with Auth, Postgres and the API gateway only |
| Supabase Auth | v2.197.0 (latest release, 2026-09-03) |
| Postgres | 17.11 (`supabase/postgres:17.11.0.004`) |
| .NET | .NET 10, `Microsoft.IdentityModel.JsonWebTokens` 8.23.0, Npgsql 10.0.3, `Supabase.Gotrue` 8.3.1 |
| WebAuthn | Chromium virtual authenticator (CTAP2, platform, user verification) through Playwright |

The local Auth server has 15-character passwords, TOTP, WebAuthn MFA and passkeys turned on, and the Apple provider with a bundle ID as its only client ID ([`config.toml`](../../spikes/auth-mfa/supabase/config.toml)). The checks also read the Auth source at the v2.197.0 tag. A research pass covered the Supabase, Apple and .NET MAUI documentation.

## 3. Results

Full output: [Auth checks](../../spikes/auth-mfa/results/auth-checks.md) and [passkey and WebAuthn checks](../../spikes/auth-mfa/results/webauthn-checks.md). All checks passed except the three gaps below.

### 3.1 Passwords and tokens

- A 14-character password is rejected with `weak_password`; 15 characters are accepted (NFR-SEC-2, single-factor minimum).
- Access tokens are ES256 and validate against the project's JWKS. A token whose `aal` claim was changed to `aal2` and re-signed with another key is rejected.
- A password sign-in gives `aal1` with `amr: [password]`.

### 3.2 TOTP and step-up

- A user enrolls TOTP from an `aal1` session; Auth returns the secret, an `otpauth://` URI and a QR code. Verifying a code upgrades the session to `aal2` with `amr: [totp, password]`. A wrong code is rejected.
- A later password sign-in is `aal1`, and the user record lists the verified factor, so the app must ask for a code (step-up). After challenge and verify, the session is `aal2`.
- Enrolling another factor and removing a factor both need `aal2`.
- Refreshing a session keeps its level: `aal1` stays `aal1`, `aal2` stays `aal2`.
- **Gap: a TOTP code can be used twice.** Auth accepts a code within ±1 time step (about 90 seconds) and keeps no record of used codes, so the same code verified a second challenge. NIST SP 800-63B requires a time-based OTP to be accepted only once. Rate limits on verification still apply. The MFA verification hook can't close this, because it receives only whether the code was valid, not the code.

### 3.3 Requiring MFA for clinicians

Supabase has no setting that requires MFA for a user or a role; enforcement belongs to the API and RLS. The spike's grant check accepts a clinician's grant only in an `aal2` session, and leaves coach grants working at `aal1` (coaches are prompted, not required, per NFR-SEC-3):

| Caller | Session | Rows of the member's diary |
|---|---|---|
| Member (owner) | `aal1` | 5 |
| Coach with an active grant | `aal1` | 5 |
| Clinician with an active grant | `aal1` | 0 |
| Clinician with an active grant | `aal2` | 5 |

The checks ran as the API would: the token validated against the JWKS, then its claims applied with `SET LOCAL` through a dedicated `fitcore_api` login role that has no table grants and can only switch to `authenticated`. That closes the login-role open point from the [RLS pooling spike](rls-connection-pooling.md#5-open-points) for the design, though not yet on a hosted project.

### 3.4 Passkeys and WebAuthn

- **Passkey sign-in works, but it's in public beta and gives `aal1`.** A user registered a passkey from a password session, then signed in with it without typing an email (discoverable credential). The session had `amr: [passkey]` and `aal1`. In the Auth source, only TOTP, phone, WebAuthn-as-MFA and recovery-code methods count towards `aal2`. Supabase documents passkeys as experimental ([docs](https://supabase.com/docs/guides/auth/passkeys)), and `Supabase.Gotrue` has no passkey support.
- Registration asks for user verification as "preferred", not "required", so Auth can't tell whether a passkey sign-in had biometrics or a PIN.
- With TOTP enrolled, registering a passkey from an `aal1` session is rejected with `insufficient_aal`.
- **WebAuthn as a second factor works in the server, but it's undocumented.** Enrolling a WebAuthn factor and stepping up a later sign-in both gave `aal2` with `amr: [password, mfa/webauthn]`. The [MFA reference](https://supabase.com/docs/reference/javascript/auth-mfa-enroll) lists only `totp` and `phone`.
- **Gap: a user whose only factor is WebAuthn can register a passkey from an `aal1` session.** The same request is rejected for a TOTP user. Someone with only the password could add a passkey and sign in with it later. This is another reason not to use the undocumented WebAuthn factor.
- **Recovery codes** exist in Auth v2.197.0 (`/factors/recovery-codes`, counted as `aal2`) but aren't in the docs or the CLI config yet. Not tested beyond the endpoint answering.

### 3.5 Sign in with Apple

What the local server showed:

- With the Apple provider's client ID set to the bundle ID, a forged Apple ID token is rejected with HTTP 400 after Auth checks it against Apple's published keys. No Services ID or client secret is needed for the native flow.

What the documentation and source say (not testable without Apple hardware and an account):

- **The call:** `POST /auth/v1/token?grant_type=id_token` with `provider: apple`, the `id_token` and the raw `nonce`. Apple receives the lowercase hex SHA-256 of the nonce, and Auth hashes the raw nonce to compare. A nonce is optional, but the token and the request must both have one or both lack one ([source](https://github.com/supabase/auth/blob/v2.197.0/internal/api/token_oidc.go), [docs](https://supabase.com/docs/guides/auth/social-login/auth-apple)).
- **MAUI can't send a nonce.** `AppleSignInAuthenticator` has only name and email options and never sets a nonce, so its tokens work but without replay protection ([API reference](https://learn.microsoft.com/dotnet/api/microsoft.maui.authentication.applesigninauthenticator?view=net-maui-10.0)). The iOS app needs a small platform file that calls `ASAuthorizationAppleIdProvider` directly, sets the hashed nonce, and reads the given and family names.
- **The name arrives once.** Apple sends the user's name only on the first authorization, and Supabase doesn't store it. The app must save it to the profile straight away.
- **Account deletion must revoke Apple's tokens.** App Store Guideline 5.1.1(v) requires apps with Sign in with Apple to revoke the user's Apple tokens when the account is deleted ([Apple](https://developer.apple.com/support/offering-account-deletion-in-your-app/)). Supabase doesn't do this and has declined to ([issue #1308](https://github.com/supabase/auth/issues/1308)). The backend must take the one-time `authorization_code` at first sign-in, exchange it at Apple for a refresh token, store that token encrypted, and call Apple's revoke endpoint during account deletion. It signs Apple's client secret JWT with the team's `.p8` key, which is a server-side secret (NFR-SEC-5).

### 3.6 .NET client

`Supabase.Gotrue` 8.3.1 (2026-10-09, actively maintained) ran the TOTP flow end to end: sign-up, enroll, challenge and verify, and reported `aal2`. It supports Apple ID tokens with a nonce, PKCE and the TOTP factor methods. It doesn't support passkeys, phone factors or Apple token revocation. The raw REST calls the spike used are short and stable.

## 4. Rules for the build

1. **TOTP is the MFA factor at launch.** It's documented, supported by the .NET client, and gives `aal2`. Offer it to everyone; require it for clinicians and admins (NFR-SEC-3).
2. **Enforce "required" twice.** API policies refuse clinician and admin endpoints without `aal2`, and RLS accepts a clinician's grant only at `aal2`, as in the spike's `care.has_grant`. Admin tooling checks `aal2` too (NFR-SEC-12).
3. **Step up in the apps.** After sign-in, when the user has a verified factor and the session is `aal1`, the apps ask for a code before showing clinician or admin features.
4. **Don't use the undocumented WebAuthn factor or the beta passkey sign-in for anything that needs MFA.** Passkey sign-in can be offered to members as a convenience once it leaves beta. It doesn't replace TOTP for clinicians or admins.
5. **Use `Supabase.Gotrue` behind a thin wrapper** in the shared service clients, with direct REST calls for anything it lacks (passkeys later).
6. **Native Apple sign-in on iOS uses a platform call with a nonce**, saves the name on first sign-in, and sends the authorization code to the API so it can store Apple's refresh token for revocation.
7. **The DataRights module revokes Apple tokens** as part of account deletion (NFR-DAT-1/2), and that step is tested.

## 5. Device test plan for Sign in with Apple

Run on a Mac with Xcode and an iPhone, with the team's Apple developer account, against a Supabase project whose Apple provider lists the app's bundle ID:

1. Sign in with Apple through the platform call with a nonce. Expect an `aal1` session and the email or private-relay address on the user.
2. Check the given and family names were saved to the profile on the first sign-in. Sign out and in again; expect no name from Apple and the saved name kept.
3. Send a token with the wrong nonce. Expect a 4xx from Auth.
4. Choose "Hide My Email". Expect a private-relay address and working sign-in.
5. Enroll TOTP for the Apple user and step up. Expect `aal2`.
6. Exchange the authorization code at Apple from the API, delete the account, and confirm that Apple's revoke call succeeds and that the app is gone from the Apple ID's list of apps using Sign in with Apple.
7. Repeat steps 1 and 5 on Mac Catalyst if the macOS app ships through it.

## 6. Open points

- **NFR-SEC-3 says "TOTP or passkey".** With Supabase today, only TOTP is a documented MFA factor, and passkey sign-in is single-factor (`aal1`). Proposed reading for the product owner: TOTP meets the requirement at launch, and passkeys count as MFA only when Supabase supports them as a factor. Raised as [NFR open item 4](../NFR.md#17-open-items).
- **TOTP code reuse** (§3.2) should be reported to Supabase. Until it's fixed, it is an accepted risk with rate limiting as the mitigation.
- **Plan level.** The CLI template says MFA needs the Pro plan; Supabase's billing docs list only "Advanced MFA" (phone) as a paid add-on. Confirm what the hosted project includes, and confirm leaked-password protection (NFR-SEC-2), which this spike didn't test.
- **Apple sign-in on Android, Windows and the web** needs Apple's web OAuth flow with a Services ID and a client secret that expires every 6 months. Decide whether those platforms offer Apple sign-in at all; App Store Guideline 4.8 applies only to the iOS app. Decided on 2026-10-16: every platform offers it ([implementation plan §8](../implementation-plan.md#8-decisions-still-needed)).
- **Passkeys in the MAUI apps** need platform code on each OS (no MAUI API exists) and associated-domain setup. Revisit when Supabase passkeys leave beta.
- **The device test plan** in §5 is still to run.
