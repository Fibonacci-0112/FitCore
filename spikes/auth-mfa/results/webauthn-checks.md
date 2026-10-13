# Passkey and WebAuthn checks: Supabase Auth v2.197.0

Output of `npm run check` in `webauthn/` on 2026-10-13 (exit code 0), using a Chromium virtual authenticator (CTAP2, platform, user verification). Rows marked GAP record a known gap and do not fail the run.

| Area | Check | Result | Detail |
|---|---|---|---|
| Passkey | Register a passkey from a password session | pass | HTTP 200; user verification requested: preferred |
| Passkey | Sign in with the passkey, no email typed (discoverable credential) | pass | HTTP 200 |
| Passkey | Passkey sign-in gives aal1, not aal2 | pass | aal aal1, amr [passkey] |
| WebAuthn MFA | Enroll a WebAuthn factor; session becomes aal2 | pass | aal aal2, amr [mfa/webauthn, password] |
| WebAuthn MFA | Step up a later password sign-in with the WebAuthn factor | pass | aal aal2, amr [password, mfa/webauthn] |
| WebAuthn MFA | GAP: user with WebAuthn MFA registers a passkey from an aal1 session | **gap present** | HTTP 200 |
| Passkey | User with TOTP MFA can't register a passkey from an aal1 session | pass | HTTP 403 insufficient_aal: AAL2 session is required to manage passkeys when MFA is enabled; session aal aal1, amr [password] |
