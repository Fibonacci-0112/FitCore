// Taps the page heading every 250 ms from the first paint until the Today screen is interactive,
// the way an impatient member taps while WebAssembly starts. Records each tap's Event Timing
// duration: the input delay behind startup work counts towards INP even with no handler.
// Warm loads first prime the HTTP cache with one load in the same browser context.
// Usage: node early-tap.mjs [runs] > ../results/early-tap.md
import { chromium } from 'playwright-core';

const BASE = process.env.BASE ?? 'http://127.0.0.1:5080';
const RUNS = Number(process.argv[2] ?? 5);
const PROFILES = [
  { name: 'Slow 4G, 4x CPU', latency: 562.5, down: 1474.56e3 / 8, up: 675e3 / 8, cpu: 4 },
  { name: 'Fast 4G, 2x CPU', latency: 150, down: 9e6 / 8, up: 1.5e6 / 8, cpu: 2 },
];
// PATHS=/wasm,/server,/auto,/islands node early-tap.mjs ... picks the variants (default: the original two).
const PATHS = (process.env.PATHS ?? '/wasm,/server').split(',');

const browser = await chromium.launch({ executablePath: process.env.CHROMIUM ?? '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
/** Returns the sorted sample at percentile fraction p (0–1), or null for an empty sample. */
const pct = (xs, p) => { const s = [...xs].sort((a, b) => a - b); return s.length ? s[Math.min(s.length - 1, Math.floor(p * s.length))] : null; };
const rows = [];
for (const profile of PROFILES) {
  for (const path of PATHS) {
    for (const cache of ['cold', 'warm']) {
      const all = [], worstPerLoad = [];
      for (let i = 0; i < RUNS; i++) {
        const context = await browser.newContext({ viewport: { width: 412, height: 823 } });
        if (cache === 'warm') { // prime the HTTP cache with one full load first
          const primer = await context.newPage();
          await primer.goto(BASE + path);
          await primer.waitForFunction(() => performance.getEntriesByName('fitcore-ready').length > 0, null, { timeout: 120_000 });
          // /auto switches to WebAssembly on a return visit only once the first visit has downloaded it.
          if (path === '/auto') await primer.waitForFunction(() => Object.keys(localStorage).some((k) => k.startsWith('blazor-resource-hash')), null, { timeout: 120_000 });
          await primer.close();
        }
        await context.addInitScript(() => {
          window.__taps = [];
          new PerformanceObserver((l) => { for (const e of l.getEntries()) if (e.interactionId && e.name === 'pointerdown') window.__taps.push(e.duration); })
            .observe({ type: 'event', durationThreshold: 16, buffered: true });
        });
        const page = await context.newPage();
        const cdp = await context.newCDPSession(page);
        await cdp.send('Network.enable');
        await cdp.send('Network.emulateNetworkConditions', { offline: false, latency: profile.latency, downloadThroughput: profile.down, uploadThroughput: profile.up });
        await cdp.send('Emulation.setCPUThrottlingRate', { rate: profile.cpu });
        const reset = await fetch(`${BASE}/api/_reset`, { method: 'POST' });
        if (!reset.ok) throw new Error(`_reset failed: HTTP ${reset.status}`);
        await page.goto(BASE + path, { waitUntil: 'commit' });
        await page.waitForSelector('h1', { timeout: 60_000 });
        let taps = 0;
        const deadline = Date.now() + 120_000;
        while (!(await page.evaluate(() => performance.getEntriesByName('fitcore-ready').length > 0))) {
          if (Date.now() > deadline) throw new Error(`${path} on ${profile.name} (${cache}) wasn't interactive within 120 s`);
          await page.mouse.click(60, 30); // the "Today" heading: no handler, so only input delay and paint count
          taps++;
          await page.waitForTimeout(250);
        }
        await page.waitForTimeout(500);
        // Taps faster than 16 ms aren't reported; count them as 16.
        const durations = await page.evaluate((n) => Array.from({ length: n }, (_, k) => window.__taps[k] ?? 16), taps);
        all.push(...durations);
        worstPerLoad.push(Math.max(16, ...durations));
        await context.close();
      }
      rows.push({ profile: profile.name, path, cache, taps: all.length, p50: pct(all, 0.5), p75: pct(all, 0.75), max: Math.max(...all), worst: pct(worstPerLoad, 0.5) });
      console.error(`${profile.name} ${path} ${cache}: taps ${all.length} p75 ${pct(all, 0.75)} max ${Math.max(...all)}`);
    }
  }
}
await browser.close();
console.log('# Taps while the page starts\n');
console.log(`Output of \`node early-tap.mjs ${RUNS}\` on ${new Date().toISOString().slice(0, 10)}: ${RUNS} loads per row, cold (empty cache) and warm (cache primed by one earlier load), a tap on the heading every 250 ms from first paint until the screen is interactive. Event Timing duration of each tap in ms.\n`);
console.log('INP takes roughly the worst interaction of a page view, so the last column is the one that compares with the 200 ms target.\n');
console.log('| Profile | Variant | Cache | Taps | Median | p75 | Worst | Median of each load\'s worst |');
console.log('|---|---|---|---|---|---|---|---|');
// A row with no taps: the screen was interactive before the first tap could land.
for (const r of rows) console.log(r.taps ? `| ${r.profile} | \`${r.path}\` | ${r.cache} | ${r.taps} | ${Math.round(r.p50)} | ${Math.round(r.p75)} | ${Math.round(r.max)} | ${Math.round(r.worst)} |` : `| ${r.profile} | \`${r.path}\` | ${r.cache} | 0 | interactive before the first tap | | | |`);
