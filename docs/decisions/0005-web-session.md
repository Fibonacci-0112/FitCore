# 0005: Hold the web session in an encrypted server cookie, with a same-origin API

- **Status:** Accepted. Approved by the product owner on 2026-10-16. Amended the same day: decision 2 uses a secret key with IP forwarding, because Supabase rate-limits Auth per IP address.
- **Date:** 2026-10-16
- **Deciders:** Product owner
- **Related:** [ADR 0003](0003-modular-monolith.md), [implementation plan §3.3](../implementation-plan.md#33-authentication) and [PR 4](../implementation-plan.md#pr-4-sign-in-profile-and-app-shells), the [Blazor spike's open points](../spikes/blazor-prerendering-web-vitals.md#6-open-points) and the [auth spike's rules](../spikes/apple-sign-in-and-mfa.md#4-rules-for-the-build).

## Context

The MAUI apps keep their Supabase session in the platform secure store and send the access token as a bearer token (NFR-SEC-8). The web app needs its own design, because of how it renders:

- **Every page prerenders on the server and hydrates with WebAssembly** ([Blazor spike rules](../spikes/blazor-prerendering-web-vitals.md#5-rules-for-the-build)). To prerender a member's Today screen, the server must know who the member is on the page request itself, before any WebAssembly code runs. Only a cookie arrives with that request.
- **Tokens in the browser are exposed.** An access or refresh token that WebAssembly or local storage can read can be taken by any script injection, and with it the member's health data. OWASP ASVS Level 2 (NFR-SEC-1) expects session tokens out of script reach.
- **A cookie brings cross-site request forgery.** The browser attaches the cookie to requests that other sites trigger, so every state-changing request needs proof that it came from our own pages.
- **The API has two kinds of callers.** MAUI sends bearer tokens; the web sends cookies. Both must end up with the same identity, the same `aal` and the same RLS claims, so module code and policies don't care which client called (ADR 0003, decision 5).
- **Session rules** from NFR-SEC-4: access tokens last at most 1 hour, refresh tokens rotate, and signing out revokes them. Sign-in endpoints are rate-limited with progressive lockout.
- **Scale-out.** `fitcore-app` runs at least two instances and keeps no state of its own (NFR-SCL-2).

## Decision

The web app uses a **server-side session**: `fitcore-app` signs the member in with Supabase Auth, keeps the resulting Supabase session in an encrypted HttpOnly cookie, and the browser only ever calls the same-origin `/v1` API. No token reaches WebAssembly, JavaScript or browser storage.

1. **The cookie.** `__Host-fitcore-session` holds the Supabase access token and refresh token, encrypted and signed with ASP.NET Core Data Protection. It is `HttpOnly`, `Secure`, `SameSite=Lax` and `Path=/`, with no `Domain` (the `__Host-` prefix enforces the last three). It is persistent, so a member stays signed in across browser restarts until the Supabase session ends or they sign out. The cookie is split into chunks if it grows past the browser limit.
2. **Sign-in runs on the server.** The Platform auth code calls the Supabase Auth REST API with a typed `HttpClient`. Supabase limits sign-ins, token grants and MFA checks per IP address, and every web call comes from `fitcore-app`, so the project turns on IP address forwarding and each call sends the member's IP in the `Sb-Forwarded-For` header. Supabase accepts that header only with a secret API key, so these calls use one, never the legacy `service_role` key. The secret key lives in the server-side secret store (NFR-SEC-5), and Platform code uses it only for Auth endpoints, never for data.
   - **Email and password** (sign-up, sign-in, password reset) are server-rendered forms posted to `fitcore-app`, with antiforgery tokens. The 18+ check and the profile follow sign-up in the Identity module.
   - **Google, Microsoft and Apple** use Supabase's OAuth flow with PKCE. `fitcore-app` creates the code verifier, keeps it in a short-lived encrypted cookie (`__Host-fitcore-pkce`, 10 minutes, `SameSite=Lax` so it survives the redirect back), sends the browser to Supabase, and exchanges the code on its callback. Return URLs must be local paths.
   - **TOTP step-up** posts the code to `fitcore-app`, which verifies it with Supabase and replaces the cookie with the new `aal2` session ([auth spike rule 3](../spikes/apple-sign-in-and-mfa.md#4-rules-for-the-build)).
   - Sign-in, sign-up, step-up and sign-out each end with a full page load, so the prerendered page, the antiforgery token and the WebAssembly state all belong to the new user.
3. **One identity for both kinds of caller.** A policy authentication scheme in `FitCore.Platform` picks the handler per request: a bearer token if the request has an `Authorization` header, otherwise the session cookie. Both validate the Supabase access token the same way (signature against the project's JWKS, issuer, audience, expiry) and produce the same claims principal. `FitCoreDatabase` gets its RLS claims from that principal, whichever handler made it.
4. **Refresh on the server.** When the cookie's access token has less than 2 minutes left, or has expired, the cookie handler refreshes the session with Supabase before the request runs and writes the new cookie on the response. Parallel requests that refresh with the same refresh token both succeed within Supabase's refresh-token reuse interval, and the last cookie written wins. What happens when a refresh fails depends on why:
   - **Supabase rejects the refresh token** (revoked, already used outside the reuse interval, or the session ended). The session is over: the handler deletes the cookie, API requests get `401`, and page requests redirect to sign-in.
   - **Anything else** (a timeout, a network error, a `429` or a `5xx` from Supabase). The session may still be valid, so the handler keeps the cookie unchanged. If the access token hasn't expired yet, the request runs with it. If it has, the request is refused without being authorized: API requests get `503` with `Retry-After`, and page requests show an error page with a retry. The next request tries the refresh again.
5. **Cross-site request protection.** Every `/v1` request authenticated by the cookie with a method other than `GET`, `HEAD` or `OPTIONS` must pass two checks, or it is refused with `403` before any module code runs:
   - an ASP.NET Core antiforgery token in a request header, which the WebAssembly client gets from the prerendered page through Blazor's antiforgery state and the typed API client adds to every call; and
   - an `Origin` header (or `Sec-Fetch-Site: same-origin`) that matches the app's own origin.

   Bearer requests skip both checks, because browsers never attach bearer tokens on their own. `GET` endpoints never change state; `SameSite=Lax` still sends the cookie on top-level cross-site navigation, so this rule is what keeps links from other sites harmless. `/v1` sends no CORS headers.
6. **What WebAssembly knows.** The server passes the WebAssembly client only what the UI needs to render the signed-in shell: the user ID, the `aal` level and the roles, through Blazor's authentication state serialization. No token, email address or health data goes into that state. A `401` from the API sends the client to the sign-in page with a full page load. A `503` keeps the form and offers a retry (NFR-REL-1).
7. **Sign-out** is a `POST` with antiforgery. `fitcore-app` calls Supabase's logout for the session, which revokes its refresh tokens, deletes both cookies, and returns a page that clears every member record the web client cached (PR 4's acceptance).
8. **The Data Protection key ring** is shared by every `fitcore-app` instance and survives deploys, or every member would be signed out at each deploy. Every instance also sets the same application name (`SetApplicationName("fitcore-app")`), so the default discriminator, which comes from the content-root path, can't stop one instance from reading another's cookie. Locally it is the default per-machine key ring. When deployed, it lives in the server-side secret store chosen in PR 9 ([plan §8](../implementation-plan.md#8-decisions-still-needed)), encrypted at rest, and never in the repository or a client build (NFR-SEC-5).
9. **Rate limits** apply to the server's sign-in, sign-up, password-reset and step-up endpoints per IP address and per account, with progressive lockout (NFR-SEC-4), on top of Supabase's own per-IP limits, which see the member's IP through decision 2. The counters live in Valkey, which joins the AppHost in PR 4 for this purpose.

## Options considered

| Option | Why it wasn't chosen |
|---|---|
| **Tokens in the browser.** WebAssembly holds the Supabase session, in memory or local storage, and calls `/v1` with a bearer token, as MAUI does. | The server can't prerender member data, because the page request carries no identity. Any script injection can read the tokens. Rejected by the plan's rule that tokens never reach WebAssembly ([§3.3](../implementation-plan.md#33-authentication)). |
| **An opaque session ID cookie with the tokens in a server-side store** (Valkey or Postgres). | Smaller cookie and sign-out that takes effect instantly everywhere. But Valkey in R1 is a single small node that isn't durable, so a restart would sign everyone out, and Postgres would need a pre-identity read path that bypasses the unit of work's rule that every query runs as a caller. Every request would also pay a store lookup. Revisit if the cookie design can't meet a later sign-out or session-listing requirement. |
| **Cookie authentication with our own ASP.NET Core Identity accounts.** | Supabase Auth is the identity provider (ADR 0003, decision 6), and RLS relies on its JWT claims. A second account store would duplicate passwords, MFA and provider sign-in. |
| **`SameSite=Strict` instead of antiforgery tokens.** | Strict drops the session cookie on any arrival from another site, including the OAuth redirect back from Supabase and a link from an email, so members would land signed out. It also doesn't protect against a request from a compromised sibling subdomain. Lax plus antiforgery keeps both the protection and the flow. |

## Consequences

### Positive

- Prerendered pages can show a member's own data on the first response, which is what the Blazor spike's Web Vitals results assumed.
- No token is reachable from script, so a script injection can act as the member only while the page is open, not take the session away.
- Module endpoints, policies and RLS see one identity model for web and MAUI. Only the Platform auth code knows about cookies.
- No new service: the session lives in the cookie, and `fitcore-app` instances stay stateless.

### Negative

- The cookie is around 2 KB and goes with every same-origin request, including static assets. Static assets can move to a cookie-free path or CDN later if that matters.
- Every state-changing web call carries an antiforgery header, and an endpoint test must prove each cookie-authenticated write refuses a missing token and a foreign origin.
- Passwords pass through `fitcore-app` on the way to Supabase. Request logging must never record auth form bodies (NFR-PRV-6).
- `fitcore-app` handles refresh and its races. If Supabase's refresh-token reuse interval is turned off, parallel requests can sign the member out.

### Risks to track

- **A copied cookie outlives sign-out until its access token expires.** Logout revokes the refresh token, so a copied cookie can't be refreshed, but its access token keeps working for up to the token's lifetime. The same window covers a slow request that refreshed the session and whose response lands after sign-out or step-up: the browser stores the older cookie again, which works only until its access token expires, or holds `aal1` until the member steps up again. Keep the access-token lifetime short in the Supabase auth settings (PR 4). If that window isn't acceptable, the API can also check the token's `session_id` against Supabase's live sessions in the same round trip as the RLS claims.
- **Session length.** Supabase sessions don't end on their own unless the project sets a time box or an inactivity timeout, which may need a paid plan. The product owner should set both with the auth settings in PR 4.
- **Sign in with Apple on the web** needs Apple's web flow and a client secret that expires every 6 months. Whether the web offers it is still open ([plan §8](../implementation-plan.md#8-decisions-still-needed)). This design supports it either way.

## Revisit if

- A requirement needs immediate sign-out of every device, or a member-visible list of active sessions. Move the tokens to a durable server-side store then.
- The web client needs to call an API on another origin.
- Supabase ships a server-side session or cookie mode for non-JavaScript backends that covers these rules.

## Follow-up

1. PR 4 builds this in `FitCore.Platform` and `FitCore.Web`, with tests for: a forged or expired cookie refused, refresh on expiry, a rejected refresh token deleting the cookie, a transient refresh failure (timeout or Supabase `5xx`) keeping the cookie and returning `503`, a cross-site write refused without the antiforgery token or with a foreign origin, a bearer request accepted without antiforgery, sign-out revoking the refresh token, and no token in the prerendered HTML or the persisted state.
2. Add the cookie and cross-site rules to the [code-review skill](../../.github/skills/code-review/SKILL.md) and to `CLAUDE.md`. Done with the acceptance.
