# Auth checks: Supabase Auth v2.197.0

Output of `dotnet run --project AuthSpike` on 2026-10-13 (exit code 0) against local Supabase (CLI 2.120.0, Auth v2.197.0, Postgres 17.11). Rows marked GAP record a known gap and do not fail the run.

| Area | Check | Result | Detail |
|---|---|---|---|
| Password | 14-character password rejected | pass | HTTP 422 weak_password: Password should be at least 15 characters. |
| Password | 15+ character password accepted | pass | HTTP 200 |
| Token | Access token validates against JWKS (ES256) | pass | iss `http://127.0.0.1:54321/auth/v1` |
| Token | Password sign-in gives aal1, amr password | pass | aal aal1, amr [password] |
| Token | Token with aal forged to aal2 is rejected | pass | SecurityTokenInvalidSignatureException |
| TOTP | Enroll from an aal1 session (no factors yet) | pass | HTTP 200, otpauth URI and QR code returned: True |
| TOTP | Wrong code rejected | pass | HTTP 422 mfa_verification_failed: Invalid TOTP code entered |
| TOTP | Verify upgrades the session to aal2 | pass | aal aal2, amr [totp, password] |
| TOTP | GAP: a used code is accepted again within its window | **gap present** | HTTP 200 |
| Step-up | Password sign-in is aal1 and lists a verified factor, so the app must step up | pass | aal aal1, verified factor listed: True |
| Step-up | Enrolling another factor needs aal2 | pass | HTTP 403 insufficient_aal: AAL2 required to enroll a new factor |
| Step-up | Removing a factor needs aal2 | pass | HTTP 422 insufficient_aal: AAL2 required to unenroll verified factor |
| Step-up | Refreshing an aal1 session stays aal1 | pass | HTTP 200 |
| Step-up | Challenge and verify steps the session up to aal2 | pass | HTTP 200 |
| Step-up | Refreshing an aal2 session stays aal2 | pass | HTTP 200 |
| RLS | Member reads own diary at aal1 | pass | 5 rows |
| RLS | Coach grant works at aal1 | pass | 5 rows |
| RLS | Clinician grant sees nothing at aal1 | pass | 0 rows |
| RLS | Clinician grant works at aal2 | pass | 5 rows |
| Step-up | Removing a factor works at aal2 | pass | HTTP 200 |
| Recovery | Recovery codes endpoint (informational) | pass | HTTP 404 mfa_factor_not_found: The user has not enrolled recovery codes |
| Apple | Forged Apple ID token rejected | pass | HTTP 400 : Bad ID token |
| Library | Supabase.Gotrue 8.3.1: sign up, enroll TOTP, challenge and verify | pass | current aal2, next aal2 |
