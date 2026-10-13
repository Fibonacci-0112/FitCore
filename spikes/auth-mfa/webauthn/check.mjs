// Passkey sign-in and WebAuthn MFA against local Supabase Auth (spikes/auth-mfa).
// A Chromium virtual authenticator (CTAP2, platform, user verification) stands in for
// Face ID / Windows Hello. The page runs on http://localhost:3000, the configured RP origin.
import http from 'node:http';
import { chromium } from 'playwright-core';

const AUTH = 'http://127.0.0.1:54321/auth/v1';
const KEY = 'sb_publishable_ACJWlzQHlZjBrEguHvfOxg_3BJgxAaH'; // local CLI default, public
const results = [];
const add = (area, check, pass, detail) => results.push({ area, check, pass, detail });

async function auth(method, path, token, body) {
  const res = await fetch(AUTH + path, {
    method,
    headers: { apikey: KEY, 'content-type': 'application/json', ...(token ? { authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  let json; try { json = JSON.parse(text); } catch { json = text; }
  return { status: res.status, ok: res.ok, json, toString: () => res.ok ? `HTTP ${res.status}` : `HTTP ${res.status} ${json?.error_code ?? ''}: ${json?.msg ?? json?.message ?? text}` };
}
const claims = (jwt) => JSON.parse(Buffer.from(jwt.split('.')[1], 'base64url').toString());
const describe = (c) => `aal ${c.aal ?? 'aal1'}, amr [${(c.amr ?? []).map((e) => e.method).join(', ')}]`;

// The page only hosts navigator.credentials; options and responses are JSON (WebAuthn Level 3).
const server = http.createServer((_, res) => { res.setHeader('content-type', 'text/html'); res.end('<!doctype html><title>FitCore WebAuthn spike</title>'); });
await new Promise((r) => server.listen(3000, 'localhost', r));
const browser = await chromium.launch({ executablePath: process.env.CHROMIUM ?? '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
const page = await browser.newPage();
await page.goto('http://localhost:3000/');
const cdp = await page.context().newCDPSession(page);
await cdp.send('WebAuthn.enable');
await cdp.send('WebAuthn.addVirtualAuthenticator', {
  options: { protocol: 'ctap2', transport: 'internal', hasResidentKey: true, hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true },
});

const create = (options) => page.evaluate(async (o) => {
  const publicKey = PublicKeyCredential.parseCreationOptionsFromJSON(o.publicKey ?? o);
  return (await navigator.credentials.create({ publicKey })).toJSON();
}, options);
const get = (options) => page.evaluate(async (o) => {
  const publicKey = PublicKeyCredential.parseRequestOptionsFromJSON(o.publicKey ?? o);
  return (await navigator.credentials.get({ publicKey })).toJSON();
}, options);

try {
  const run = Date.now();
  const password = 'correct-horse-battery-staple';

  // Passkey as a first factor (public beta).
  const member = await auth('POST', '/signup', null, { email: `passkey-${run}@spike.fitcore.test`, password });
  const regOptions = await auth('POST', '/passkeys/registration/options', member.json.access_token, {});
  const regCredential = await create(regOptions.json.options);
  const reg = await auth('POST', '/passkeys/registration/verify', member.json.access_token, { challenge_id: regOptions.json.challenge_id, credential: regCredential });
  add('Passkey', 'Register a passkey from a password session', reg.ok, `${reg}; user verification requested: ${regOptions.json.options?.authenticatorSelection?.userVerification ?? regOptions.json.options?.publicKey?.authenticatorSelection?.userVerification}`);

  const authOptions = await auth('POST', '/passkeys/authentication/options', null, {});
  const assertion = await get(authOptions.json.options);
  const signIn = await auth('POST', '/passkeys/authentication/verify', null, { challenge_id: authOptions.json.challenge_id, credential: assertion });
  const passkeyClaims = signIn.ok ? claims(signIn.json.access_token) : {};
  add('Passkey', 'Sign in with the passkey, no email typed (discoverable credential)', signIn.ok && passkeyClaims.sub === member.json.user.id, signIn.toString());
  add('Passkey', 'Passkey sign-in gives aal1, not aal2', signIn.ok && (passkeyClaims.aal ?? 'aal1') === 'aal1' && passkeyClaims.amr?.[0]?.method === 'passkey', describe(passkeyClaims));

  // WebAuthn as a second factor (in the server, not in the docs).
  const clinician = await auth('POST', '/signup', null, { email: `webauthn-${run}@spike.fitcore.test`, password });
  const enroll = await auth('POST', '/factors', clinician.json.access_token, { factor_type: 'webauthn', friendly_name: 'Security key' });
  const enrollChallenge = await auth('POST', `/factors/${enroll.json.id}/challenge`, clinician.json.access_token, {});
  const mfaCredential = await create(enrollChallenge.json.webauthn.credential_options);
  const enrollVerify = await auth('POST', `/factors/${enroll.json.id}/verify`, clinician.json.access_token,
    { challenge_id: enrollChallenge.json.id, webauthn: { type: 'create', credential_response: mfaCredential } });
  const enrolledClaims = enrollVerify.ok ? claims(enrollVerify.json.access_token) : {};
  add('WebAuthn MFA', 'Enroll a WebAuthn factor; session becomes aal2', enrollVerify.ok && enrolledClaims.aal === 'aal2', enrollVerify.ok ? describe(enrolledClaims) : enrollVerify.toString());

  const later = await auth('POST', '/token?grant_type=password', null, { email: `webauthn-${run}@spike.fitcore.test`, password });
  const stepChallenge = await auth('POST', `/factors/${enroll.json.id}/challenge`, later.json.access_token, {});
  const stepAssertion = await get(stepChallenge.json.webauthn.credential_options);
  const stepVerify = await auth('POST', `/factors/${enroll.json.id}/verify`, later.json.access_token,
    { challenge_id: stepChallenge.json.id, webauthn: { type: 'request', credential_response: stepAssertion } });
  const steppedClaims = stepVerify.ok ? claims(stepVerify.json.access_token) : {};
  add('WebAuthn MFA', 'Step up a later password sign-in with the WebAuthn factor', stepVerify.ok && steppedClaims.aal === 'aal2', stepVerify.ok ? describe(steppedClaims) : stepVerify.toString());

  // With MFA enrolled, managing passkeys should need aal2 (requirePasskeyManagementAAL, v2.190.0+).
  // Try the whole registration from an aal1 session, for a WebAuthn-MFA user and a TOTP user.
  const registerAtAal1 = async (token) => {
    const options = await auth('POST', '/passkeys/registration/options', token, {});
    if (!options.ok) return options;
    const credential = await create(options.json.options);
    return auth('POST', '/passkeys/registration/verify', token, { challenge_id: options.json.challenge_id, credential });
  };
  const webauthnUserAal1 = await registerAtAal1(later.json.access_token);
  results.push({ area: 'WebAuthn MFA', check: 'GAP: user with WebAuthn MFA registers a passkey from an aal1 session', pass: !webauthnUserAal1.ok, gap: true, detail: webauthnUserAal1.toString() });

  const totpUser = await auth('POST', '/signup', null, { email: `totp-passkey-${run}@spike.fitcore.test`, password });
  const totp = await auth('POST', '/factors', totpUser.json.access_token, { factor_type: 'totp', friendly_name: 'App' });
  const { createHmac } = await import('node:crypto');
  const code = (secret) => {
    const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'; let bits = ''; for (const c of secret.replace(/=+$/, '')) bits += alphabet.indexOf(c).toString(2).padStart(5, '0');
    const key = Buffer.from(bits.match(/.{8}/g).map((b) => parseInt(b, 2)));
    const msg = Buffer.alloc(8); msg.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30000)));
    const h = createHmac('sha1', key).update(msg).digest(); const o = h[h.length - 1] & 15;
    return String(((h.readUInt32BE(o) & 0x7fffffff) % 1000000)).padStart(6, '0');
  };
  const totpChallenge = await auth('POST', `/factors/${totp.json.id}/challenge`, totpUser.json.access_token, {});
  await auth('POST', `/factors/${totp.json.id}/verify`, totpUser.json.access_token, { challenge_id: totpChallenge.json.id, code: code(totp.json.totp.secret) });
  const totpLater = await auth('POST', '/token?grant_type=password', null, { email: `totp-passkey-${run}@spike.fitcore.test`, password });
  const totpUserAal1 = await registerAtAal1(totpLater.json.access_token);
  results.push({ area: 'Passkey', check: 'User with TOTP MFA can\'t register a passkey from an aal1 session', pass: !totpUserAal1.ok, detail: `${totpUserAal1}; session ${describe(claims(totpLater.json.access_token))}` });
} catch (e) {
  add('Harness', 'Unexpected error', false, String(e?.message ?? e).split('\n')[0]);
} finally {
  await browser.close();
  server.close();
}

console.log('| Area | Check | Result | Detail |\n|---|---|---|---|');
// Gap rows record a known gap: they show whether it is still present and don't fail the run.
const verdict = (r) => (r.gap ? (r.pass ? 'fixed' : '**gap present**') : r.pass ? 'pass' : 'FAIL');
for (const r of results) console.log(`| ${r.area} | ${r.check} | ${verdict(r)} | ${String(r.detail).replaceAll('|', '/')} |`);
process.exitCode = results.every((r) => r.pass || r.gap) ? 0 : 1;
