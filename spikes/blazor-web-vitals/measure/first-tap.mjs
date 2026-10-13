// Taps "Add banana" several times after the Today screen becomes interactive and records each
// tap's Event Timing duration (what INP is built from). Shows how much slower the first tap is.
// Usage: BUILD="label" node first-tap.mjs [runs] [taps] >> ../results/first-tap.md
import { chromium } from 'playwright-core';

const BASE = process.env.BASE ?? 'http://127.0.0.1:5080';
const RUNS = Number(process.argv[2] ?? 5);
const TAPS = Number(process.argv[3] ?? 4);
const PATHS = ['/wasm', '/wasm-nopersist', '/server'];
const CPUS = [1, 2, 4];

const browser = await chromium.launch({ executablePath: process.env.CHROMIUM ?? '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
/** Returns the median of a nonempty numeric sample without changing its order. */
const median = (xs) => { const s = [...xs].sort((a, b) => a - b); const m = Math.floor(s.length / 2); return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2; };
const rows = [];
for (const path of PATHS) {
  for (const cpu of CPUS) {
    const first = [], later = [];
    for (let i = 0; i < RUNS; i++) {
      const context = await browser.newContext({ viewport: { width: 412, height: 823 } });
      await context.addInitScript(() => {
        window.__taps = [];
        new PerformanceObserver((l) => { for (const e of l.getEntries()) if (e.interactionId && e.name === 'click') window.__taps.push(e.duration); })
          .observe({ type: 'event', durationThreshold: 16, buffered: true });
      });
      const page = await context.newPage();
      await (await context.newCDPSession(page)).send('Emulation.setCPUThrottlingRate', { rate: cpu });
      const reset = await fetch(`${BASE}/api/_reset`, { method: 'POST' });
      if (!reset.ok) throw new Error(`_reset failed: HTTP ${reset.status}`);
      await page.goto(BASE + path);
      await page.waitForFunction(() => performance.getEntriesByName('fitcore-ready').length > 0, null, { timeout: 60_000, polling: 50 });
      await page.waitForTimeout(1000);
      for (let t = 0; t < TAPS; t++) { await page.click('.add'); await page.waitForTimeout(600); }
      // Durations under 16 ms aren't reported; count them as 16.
      const taps = await page.evaluate((n) => Array.from({ length: n }, (_, i) => window.__taps[i] ?? 16), TAPS);
      first.push(taps[0]); later.push(...taps.slice(1));
      await context.close();
    }
    rows.push({ path, cpu, first: median(first), firstMax: Math.max(...first), later: median(later), laterMax: Math.max(...later) });
    console.error(`${path} ${cpu}x: first ${median(first)} later ${median(later)}`);
  }
}
await browser.close();
console.log(`\n## ${process.env.BUILD ?? 'Build'}\n`);
console.log(`Output of \`node first-tap.mjs ${RUNS} ${TAPS}\` on ${new Date().toISOString().slice(0, 10)}: ${RUNS} page loads per row, ${TAPS} taps each, no network throttling. Event Timing duration in ms, median (worst in brackets).\n`);
console.log('| Variant | CPU slowdown | First tap | Later taps |');
console.log('|---|---|---|---|');
for (const r of rows) console.log(`| \`${r.path}\` | ${r.cpu}× | ${Math.round(r.first)} (${Math.round(r.firstMax)}) | ${Math.round(r.later)} (${Math.round(r.laterMax)}) |`);
