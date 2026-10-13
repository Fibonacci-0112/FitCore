// Loads the Today screen in each Blazor variant under throttled network and CPU profiles, then
// taps "Add banana" once. Records LCP, CLS, FCP and TTFB with Google's web-vitals library, the
// tap's event duration (what INP measures), tap-to-row time (NFR-PRF-7), when the screen became
// interactive, bytes transferred, and how often the page read "today" from the API.
// Usage: node vitals.mjs [runs] > ../results/vitals.md   (server on BASE, default 127.0.0.1:5080)
import { readFileSync } from 'node:fs';
import { chromium } from 'playwright-core';

const BASE = process.env.BASE ?? 'http://127.0.0.1:5080';
const RUNS = Number(process.argv[2] ?? 5);
// Playwright wraps init scripts in a function, so the IIFE's global has to be put on window.
const WEB_VITALS = readFileSync(new URL('node_modules/web-vitals/dist/web-vitals.iife.js', import.meta.url), 'utf8') + ';window.webVitals = webVitals;';

const VARIANTS = [
  { path: '/static', name: 'Static SSR (baseline, not interactive)', interactive: false },
  { path: '/wasm', name: 'WebAssembly, prerendered, persisted state', interactive: true },
  { path: '/wasm-nopersist', name: 'WebAssembly, prerendered, refetch on start', interactive: true },
  { path: '/wasm-noprerender', name: 'WebAssembly only, no prerendering', interactive: true },
  { path: '/server', name: 'Interactive Server (reference)', interactive: true },
];

// Bytes per second. "Slow 4G" is Lighthouse's mobile profile as DevTools applies it
// (150 ms RTT x 3.75, 1.6 Mbps x 0.9), the harshest common lab profile.
const PROFILES = [
  { id: 'slow4g', name: 'Slow 4G, 4x CPU', latency: 562.5, down: 1474.56e3 / 8, up: 675e3 / 8, cpu: 4 },
  { id: 'fast4g', name: 'Fast 4G, 2x CPU', latency: 150, down: 9e6 / 8, up: 1.5e6 / 8, cpu: 2 },
  { id: 'desktop', name: 'Desktop cable, 1x CPU', latency: 40, down: 10240e3 / 8, up: 10240e3 / 8, cpu: 1 },
];

/** Installs page observers before application scripts to collect Web Vitals and tap timings in window.__m. */
const probe = () => {
  const m = (window.__m = { lcp: null, cls: 0, fcp: null, ttfb: null, events: [], tap: null, row: null, saved: null });
  const opts = { reportAllChanges: true };
  addEventListener('DOMContentLoaded', () => {
    webVitals.onLCP((x) => (m.lcp = x.value), opts);
    webVitals.onCLS((x) => (m.cls = x.value), opts);
    webVitals.onFCP((x) => (m.fcp = x.value), opts);
    webVitals.onTTFB((x) => (m.ttfb = x.value), opts);
  });
  // Event Timing with a 16 ms threshold: INP's definition, but it also sees short interactions.
  new PerformanceObserver((list) => {
    for (const e of list.getEntries()) if (e.interactionId) m.events.push({ name: e.name, duration: e.duration });
  }).observe({ type: 'event', durationThreshold: 16, buffered: true });
  addEventListener('pointerdown', (e) => { if (e.target.closest?.('.add')) m.tap = e.timeStamp; }, true);
  new MutationObserver(() => {
    const row = [...document.querySelectorAll('li[data-row]')].find((li) => li.textContent.includes('Banana'));
    if (!row) return;
    if (m.row === null) requestAnimationFrame(() => (m.row ??= performance.now()));
    if (m.saved === null && row.dataset.pending === 'false') requestAnimationFrame(() => (m.saved ??= performance.now()));
  }).observe(document, { subtree: true, childList: true, attributes: true, attributeFilter: ['data-pending'] });
};

/** Measures a cold and then a warm visit in one browser context under the supplied network and CPU profile. */
async function measure(browser, variant, profile) {
  const context = await browser.newContext({ viewport: { width: 412, height: 823 }, deviceScaleFactor: 1.75 });
  await context.addInitScript({ content: WEB_VITALS });
  await context.addInitScript(probe);
  const page = await context.newPage();
  const cdp = await context.newCDPSession(page);
  await cdp.send('Network.enable');
  await cdp.send('Network.emulateNetworkConditions', {
    offline: false, latency: profile.latency, downloadThroughput: profile.down, uploadThroughput: profile.up,
  });
  await cdp.send('Emulation.setCPUThrottlingRate', { rate: profile.cpu });
  const urls = new Map();
  let bytes = { total: 0, framework: 0 };
  cdp.on('Network.responseReceived', (e) => urls.set(e.requestId, e.response.url));
  cdp.on('Network.loadingFinished', (e) => {
    bytes.total += e.encodedDataLength;
    if ((urls.get(e.requestId) ?? '').includes('/_framework/')) bytes.framework += e.encodedDataLength;
  });

  /** Resets the diary, loads the variant and measures feedback for one add when interactive. */
  const visit = async () => {
    bytes = { total: 0, framework: 0 };
    const reset = await fetch(`${BASE}/api/_reset`, { method: 'POST' });
    if (!reset.ok) throw new Error(`_reset failed: HTTP ${reset.status}`);
    await page.goto(BASE + variant.path, { waitUntil: 'load', timeout: 120_000 });
    let ready = null;
    if (variant.interactive) {
      await page.waitForFunction(() => performance.getEntriesByName('fitcore-ready').length > 0, null, { timeout: 120_000, polling: 50 });
      ready = await page.evaluate(() => performance.getEntriesByName('fitcore-ready')[0].startTime);
    }
    await page.waitForTimeout(500); // let late layout shifts and LCP candidates land
    let tap = {};
    if (variant.interactive) {
      await page.click('.add');
      await page.waitForFunction(() => window.__m.saved !== null, null, { timeout: 60_000, polling: 50 });
      await page.waitForTimeout(300);
      tap = await page.evaluate(() => {
        const m = window.__m;
        return {
          event: Math.max(0, ...m.events.filter((e) => ['pointerdown', 'pointerup', 'click', 'mousedown', 'mouseup'].includes(e.name)).map((e) => e.duration)),
          toRow: m.row - m.tap,
          toSaved: m.saved - m.tap,
        };
      });
    }
    const m = await page.evaluate(() => window.__m);
    const reads = (await (await fetch(`${BASE}/api/_stats`)).json()).todayReads;
    return { lcp: m.lcp, cls: m.cls, fcp: m.fcp, ttfb: m.ttfb, ready, ...tap, bytes: { ...bytes }, reads };
  };

  const cold = await visit();
  const warm = await visit(); // same context: HTTP cache primed by the first visit
  await context.close();
  return { cold, warm };
}

/** Returns the median excluding null, undefined and NaN samples, or null if none remain. */
const median = (xs) => {
  const s = xs.filter((x) => x !== null && x !== undefined && !Number.isNaN(x)).sort((a, b) => a - b);
  if (!s.length) return null;
  const mid = Math.floor(s.length / 2);
  return s.length % 2 ? s[mid] : (s[mid - 1] + s[mid]) / 2;
};
/** Returns the maximum excluding null and undefined samples, or null if none remain. */
const max = (xs) => (xs.some((x) => x != null) ? Math.max(...xs.filter((x) => x != null)) : null);
/** Formats milliseconds as a whole number, or n/a for a missing metric. */
const ms = (x) => (x == null ? 'n/a' : `${Math.round(x)}`);
/** Formats bytes as whole KiB, or n/a for a missing metric. */
const kb = (x) => (x == null ? 'n/a' : (x / 1024).toFixed(0));
/** Formats layout shift to three decimal places, or n/a for a missing metric. */
const cls = (x) => (x == null ? 'n/a' : x.toFixed(3));

const browser = await chromium.launch({ executablePath: process.env.CHROMIUM ?? '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
const rows = [];
for (const profile of PROFILES) {
  for (const variant of VARIANTS) {
    const runs = [];
    for (let i = 0; i < RUNS; i++) runs.push(await measure(browser, variant, profile));
    for (const cache of ['cold', 'warm']) {
      const r = runs.map((x) => x[cache]);
      /** Extracts a metric from every run for this cache state. */
      const pick = (f) => r.map(f);
      rows.push({
        profile: profile.name, variant: variant.name, cache,
        lcp: median(pick((x) => x.lcp)), lcpMax: max(pick((x) => x.lcp)),
        cls: median(pick((x) => x.cls)), clsMax: max(pick((x) => x.cls)),
        fcp: median(pick((x) => x.fcp)), ready: median(pick((x) => x.ready)),
        event: median(pick((x) => x.event)), eventMax: max(pick((x) => x.event)),
        toRow: median(pick((x) => x.toRow)), toRowMax: max(pick((x) => x.toRow)),
        toSaved: median(pick((x) => x.toSaved)),
        kb: median(pick((x) => x.bytes.total)), fwKb: median(pick((x) => x.bytes.framework)),
        reads: median(pick((x) => x.reads)),
      });
      console.error(`${profile.id} ${variant.path} ${cache}: LCP ${ms(rows.at(-1).lcp)} ready ${ms(rows.at(-1).ready)} tap ${ms(rows.at(-1).event)}/${ms(rows.at(-1).toRow)}`);
    }
  }
}
const version = browser.version();
await browser.close();

console.log(`# Web Vitals by render mode\n`);
console.log(`Output of \`node vitals.mjs ${RUNS}\` on ${new Date().toISOString().slice(0, 10)}: ${RUNS} runs per cell, median (worst in brackets). Chromium ${version}, viewport 412×823. Times in ms, sizes in KiB as transferred.\n`);
console.log('- **LCP, CLS:** from the web-vitals library, measured up to the first tap.');
console.log('- **Ready:** when the Today screen shows data and accepts taps (a `performance.mark` from the component).');
console.log('- **Tap event:** duration of the "Add banana" tap from Event Timing, the number INP is built from.');
console.log('- **Tap to row:** from the tap to the frame that shows the new row (NFR-PRF-7, ≤ 100 ms). **Saved:** to the frame that shows it confirmed.');
console.log('- **Reads:** how many times the page read "today" from the diary (prerender plus client).\n');
console.log('| Profile | Variant | Cache | LCP | CLS | FCP | Ready | Tap event | Tap to row | Saved | KiB | of which `_framework` | Reads |');
console.log('|---|---|---|---|---|---|---|---|---|---|---|---|---|');
for (const r of rows) {
  console.log(`| ${r.profile} | ${r.variant} | ${r.cache} | ${ms(r.lcp)} (${ms(r.lcpMax)}) | ${cls(r.cls)} (${cls(r.clsMax)}) | ${ms(r.fcp)} | ${ms(r.ready)} | ${ms(r.event)} (${ms(r.eventMax)}) | ${ms(r.toRow)} (${ms(r.toRowMax)}) | ${ms(r.toSaved)} | ${kb(r.kb)} | ${kb(r.fwKb)} | ${r.reads} |`);
}
