// What a member sees when the connection or the server goes away mid-session, for each render mode.
// Each run loads the Today screen through proxy.mjs, saves one entry, starts a fault, taps "Add banana"
// one second into it, then tries to add another entry once the fault is over. The script starts its own
// spike server (port 5090) and, for the distributed-state runs, a Redis server (port 6390).
// Usage: node resilience.mjs [runs] > ../results/resilience.md   (nothing else may use ports 5090, 5091, 6390)
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { startProxy } from './proxy.mjs';
import { launch, startServer, installTapProbe, waitReady, readTaps, reconnectUi, errorBanner, sleep, ms } from './lib.mjs';

const RUNS = Number(process.argv[2] ?? 1);
const VARIANTS = [
  { path: '/wasm', name: 'WebAssembly' },
  { path: '/server', name: 'Interactive Server' },
  { path: '/auto', name: 'Interactive Auto, first visit' },
  { path: '/islands', name: 'Static SSR + JS island' },
];
const FAULTS = [
  { id: 'stall10', name: 'Dead zone, 10 s', kind: 'stall', ms: 10_000 },
  { id: 'stall45', name: 'Dead zone, 45 s', kind: 'stall', ms: 45_000 },
  { id: 'reset3', name: 'Network switch, offline 3 s', kind: 'reset', ms: 3_000 },
  // A short retention stands in for the default 3 minutes, which a phone in a pocket easily exceeds.
  { id: 'reset30', name: 'Offline 30 s, circuit kept 10 s', kind: 'reset', ms: 30_000, env: { SPIKE_CIRCUIT_RETENTION_SECONDS: '10' } },
  { id: 'deploy', name: 'Server restart (SIGTERM), state in memory', kind: 'restart', signal: 'SIGTERM', ms: 3_000 },
  { id: 'deploy-redis', name: 'Server restart (SIGTERM), state in Redis', kind: 'restart', signal: 'SIGTERM', ms: 3_000, redis: true },
  { id: 'crash-redis', name: 'Server crash (SIGKILL), state in Redis', kind: 'restart', signal: 'SIGKILL', ms: 3_000, redis: true },
];
const PORT = 5090;
const REDIS_PORT = 6390;

const redis = spawn('redis-server', ['--port', `${REDIS_PORT}`, '--save', '', '--appendonly', 'no'], { stdio: 'ignore' });
await sleep(500);
const browser = await launch();
const rows = [];

/** Runs one fault against one variant and describes what the member saw. */
async function run(variant, fault) {
  const dir = mkdtempSync(join(tmpdir(), 'fitcore-resilience-'));
  const env = { SPIKE_STORE_FILE: join(dir, 'diary.json'), ...(fault.env ?? {}), ...(fault.redis ? { SPIKE_REDIS: `127.0.0.1:${REDIS_PORT}` } : {}) };
  let server = await startServer(PORT, env);
  const proxy = await startProxy({ listenPort: PORT + 1, targetPort: PORT, rtt: 50 });
  const context = await browser.newContext({ viewport: { width: 412, height: 823 } });
  await installTapProbe(context);
  const page = await context.newPage();
  let reloads = -1; // the first load is not a reload
  page.on('load', () => reloads++);
  await page.goto(proxy.url + variant.path);
  const renderer = await waitReady(page);
  await page.click('.add');
  await page.waitForFunction(() => window.__taps.taps[0]?.saved > 0, null, { timeout: 30_000 });

  // The fault, with a tap one second in, where the "Add banana" button is on screen.
  const box = await page.locator('.add').boundingBox();
  const ui = new Set();
  let uiMs = 0;
  let banner = false;
  const t0 = Date.now();
  let done = false;
  const watch = (async () => {
    while (Date.now() - t0 < fault.ms + 60_000 && !done) {
      const text = await reconnectUi(page).catch(() => 'page navigating');
      if (text) { ui.add(text); uiMs += 200; }
      banner ||= await errorBanner(page).catch(() => false);
      await sleep(200);
    }
  })();
  if (fault.kind === 'stall') proxy.stall(fault.ms);
  if (fault.kind === 'reset') proxy.reset(fault.ms);
  if (fault.kind === 'restart') await server.stop(fault.signal);
  await sleep(1000);
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
  const tapsDuring = await page.evaluate(() => window.__taps.taps.length).catch(() => null);
  const duringRegistered = tapsDuring === 2;
  await sleep(Math.max(0, fault.ms - (Date.now() - t0)));
  if (fault.kind === 'restart') server = await startServer(PORT, env);
  const faultEnd = Date.now();

  // After the fault: when is the page usable again (no reconnect UI, screen ready)? Then let everything in flight
  // settle for 5 s and count what the tap during the fault left behind, before any other tap can be confused with it.
  const stats = async () => (await (await fetch(`http://127.0.0.1:${PORT}/api/_stats`)).json()).added;
  const bananas = () => page.evaluate(() => [...document.querySelectorAll('li[data-row]')].filter((li) => li.textContent.includes('Banana')).length).catch(() => null);
  let usable = null;
  for (const deadline = Date.now() + 90_000; Date.now() < deadline; await sleep(100)) {
    const blocked = await reconnectUi(page).catch(() => 'page navigating');
    const ready = await page.evaluate(() => performance.getEntriesByName('fitcore-ready').length > 0).catch(() => false);
    if (!blocked && ready) { usable = Date.now() - faultEnd; break; }
  }
  await sleep(5000);
  const settled = { shown: await bananas(), stored: await stats() };
  const duringTap = duringRegistered ? (await readTaps(page).catch(() => []))[1] ?? null : null;

  // Then one more tap: does the server store it? (Judged by the server's count: a lost tap leaves the probe waiting
  // for a row that never comes, so it can't tell which row belongs to the next tap.)
  let nextSaved = false;
  try {
    await page.click('.add', { timeout: 30_000 });
    for (const deadline = Date.now() + 30_000; Date.now() < deadline && !nextSaved; await sleep(100)) {
      nextSaved = (await stats()) > settled.stored;
    }
  } catch { /* stays false */ }
  await sleep(1500);
  done = true;
  await watch;

  const shown = await bananas();
  const stored = await stats();
  const attempts = 2 + (duringRegistered ? 1 : 0);
  const finalRenderer = await page.evaluate(() => document.querySelector('[data-renderer]')?.dataset.renderer).catch(() => null);
  await context.close();
  await proxy.close();
  await server.stop();
  rmSync(dir, { recursive: true, force: true });

  // The tap during the fault: what the member saw (from the probe, if the page wasn't reloaded) and whether the
  // server stored it (one entry was stored before the fault).
  const seen = !duringRegistered ? 'blocked by the reconnect UI'
    : duringTap == null ? 'page reloaded'
    : [duringTap.toRow != null ? `row at ${ms(duringTap.toRow)}` : 'no row', duringTap.failed != null ? `"Not saved" at ${ms(duringTap.failed)}` : duringTap.toSaved != null ? `shown saved at ${ms(duringTap.toSaved)}` : null].filter(Boolean).join(', ');
  const result = {
    variant: variant.name, fault: fault.name, renderer: finalRenderer && finalRenderer !== renderer ? `${renderer} → ${finalRenderer}` : renderer,
    during: duringRegistered ? `${seen}; ${settled.stored >= 2 ? 'stored' : '**not stored**'}` : seen,
    ui: ui.size ? `${[...ui].join(' / ')} (${(uiMs / 1000).toFixed(1)} s)` : 'none',
    usable: usable == null ? 'not within 90 s' : `${(usable / 1000).toFixed(1)} s`,
    banner: banner ? 'shown' : 'none',
    next: nextSaved ? 'saved' : '**not saved**',
    reloads, shown, stored, attempts,
  };
  console.error(`${variant.path} ${fault.id}:`, JSON.stringify(result));
  return result;
}

// FAULTS=stall10,deploy picks faults (default: all).
for (const fault of FAULTS.filter((f) => !process.env.FAULTS || process.env.FAULTS.split(',').includes(f.id))) {
  for (const variant of VARIANTS) {
    for (let i = 0; i < RUNS; i++) rows.push(await run(variant, fault));
  }
}
await browser.close();
redis.kill();

console.log('# Faults mid-session\n');
console.log(`Output of \`node resilience.mjs ${RUNS}\` on ${new Date().toISOString().slice(0, 10)}, ${RUNS} run(s) per row. Each run loads the Today screen through \`proxy.mjs\` (50 ms RTT), saves one entry, starts the fault, taps "Add banana" 1 s into it, and after the fault taps "Add banana" again.\n`);
console.log('- **Dead zone:** nothing gets through, then everything queued is delivered, as when a phone loses signal and TCP retransmits. **Network switch / offline:** every connection is reset and new ones are refused, as when a phone changes network.');
console.log('- **Server restart:** the server process stops and a new one starts 3 s later on the same port, as in a deploy. The diary data survives (a file stands in for the database); circuits don\'t. "State in Redis" uses .NET 10 circuit state persistence with a Redis `HybridCache`.');
console.log('- **Tap during the fault:** what the member saw after the tap 1 s in (times from the tap, in ms), and whether the server had stored it once the page was usable again and 5 s had passed. A tap that landed on the reconnect UI never reached the button.');
console.log('- **Reconnect UI:** what Blazor showed over the page, and for how long. **Usable again:** from the end of the fault until no reconnect UI covered a ready screen.');
console.log('- **Error banner:** whether Blazor\'s error banner appeared at any point. It sits along the bottom of the page and doesn\'t block taps, so it doesn\'t count against "Usable again".');
console.log('- **Next tap:** one more "Add banana" after that, and whether it saved. **Reloads:** full page reloads.');
console.log('- **Bananas shown / stored / tapped:** rows on screen at the end, entries the server stored, and taps that reached the button.\n');
console.log('| Fault | Variant | Renderer | Tap during the fault | Reconnect UI | Usable again | Error banner | Next tap | Reloads | Bananas shown / stored / tapped |');
console.log('|---|---|---|---|---|---|---|---|---|---|');
for (const r of rows) console.log(`| ${r.fault} | ${r.variant} | ${r.renderer} | ${r.during} | ${r.ui} | ${r.usable} | ${r.banner} | ${r.next} | ${r.reloads} | ${r.shown} / ${r.stored} / ${r.attempts} |`);
