// Taps "Add banana" through a proxy that adds real round-trip time, for each render mode. Every tap on a
// circuit travels to the server and back before the screen changes; DevTools throttling can't show that,
// because it doesn't delay WebSocket frames. The last rows repeat /server with DevTools latency instead
// of the proxy, to show the difference.
// Usage: node latency.mjs [runs] [taps] > ../results/latency.md   (spike server on 127.0.0.1:5080)
import { startProxy } from './proxy.mjs';
import { launch, installTapProbe, waitReady, readTaps, sleep, median, max, ms } from './lib.mjs';

const RUNS = Number(process.argv[2] ?? 5);
const TAPS = Number(process.argv[3] ?? 4);
const CPU = 2; // a mid-range phone, as in the Fast 4G profile
const RTTS = [0, 50, 100, 200, 400];
const VARIANTS = [
  { path: '/wasm', name: 'WebAssembly, prerendered' },
  { path: '/server', name: 'Interactive Server' },
  { path: '/auto', name: 'Interactive Auto, first visit' },
  { path: '/auto', name: 'Interactive Auto, return visit', warm: true },
  { path: '/islands', name: 'Static SSR + JS island' },
];

const proxy = await startProxy({ listenPort: 5081, targetPort: 5080 });
const browser = await launch();
const rows = [];

/** Loads a variant through the proxy, taps several times and returns the taps and the renderer. */
async function run(variant, { devtoolsRtt = 0 } = {}) {
  const context = await browser.newContext({ viewport: { width: 412, height: 823 } });
  if (variant.warm) { // a return visit: the first one downloaded WebAssembly in the background
    const primer = await context.newPage();
    await primer.goto(proxy.url + variant.path);
    await waitReady(primer);
    await primer.waitForFunction(() => Object.keys(localStorage).some((k) => k.startsWith('blazor-resource-hash')), null, { timeout: 120_000 });
    await primer.close();
  }
  await installTapProbe(context);
  const page = await context.newPage();
  const cdp = await context.newCDPSession(page);
  await cdp.send('Emulation.setCPUThrottlingRate', { rate: CPU });
  if (devtoolsRtt) {
    await cdp.send('Network.enable');
    await cdp.send('Network.emulateNetworkConditions', { offline: false, latency: devtoolsRtt, downloadThroughput: -1, uploadThroughput: -1 });
  }
  await fetch('http://127.0.0.1:5080/api/_reset', { method: 'POST' });
  await page.goto(proxy.url + variant.path, { timeout: 120_000 });
  const renderer = await waitReady(page);
  await sleep(1000);
  for (let t = 0; t < TAPS; t++) {
    await page.click('.add');
    await page.waitForFunction((n) => window.__taps.taps.length === n && window.__taps.taps.at(-1).saved > 0, t + 1, { timeout: 30_000, polling: 20 });
    await sleep(300);
  }
  const taps = await readTaps(page);
  await context.close();
  return { renderer, taps };
}

/** Runs a variant several times and adds a summary row. */
async function measure(variant, rtt, label, opts) {
  const results = [];
  for (let i = 0; i < RUNS; i++) results.push(await run(variant, opts));
  const taps = results.flatMap((r) => r.taps);
  const firsts = results.map((r) => r.taps[0]);
  const renderers = [...new Set(results.map((r) => r.renderer))].join(', ');
  const row = {
    rtt: label ?? `${rtt}`, variant: variant.name, renderers,
    event: median(taps.map((t) => t.event)), eventMax: max(taps.map((t) => t.event)),
    firstRow: median(firsts.map((t) => t.toRow)),
    toRow: median(taps.map((t) => t.toRow)), toRowMax: max(taps.map((t) => t.toRow)),
    toSaved: median(taps.map((t) => t.toSaved)),
  };
  rows.push(row);
  console.error(`${row.rtt} ${variant.name} [${renderers}]: event ${ms(row.event)} row ${ms(row.toRow)} (${ms(row.toRowMax)}) saved ${ms(row.toSaved)}`);
}

for (const rtt of RTTS) {
  proxy.setRtt(rtt);
  for (const variant of VARIANTS) await measure(variant, rtt);
}
proxy.setRtt(0);
await measure(VARIANTS[1], 0, '200 (DevTools)', { devtoolsRtt: 200 });
await measure(VARIANTS[1], 0, '400 (DevTools)', { devtoolsRtt: 400 });
await browser.close();
await proxy.close();

console.log('# Taps by round-trip time\n');
console.log(`Output of \`node latency.mjs ${RUNS} ${TAPS}\` on ${new Date().toISOString().slice(0, 10)}: ${RUNS} page loads per row, ${TAPS} taps on "Add banana" each, ${CPU}× CPU slowdown. The round-trip time is added by \`proxy.mjs\` to every byte in both directions, WebSocket frames included, with no bandwidth limit. The server's write takes 80 ms. Times in ms, median (worst in brackets).\n`);
console.log('- **Tap event:** the Event Timing duration of the tap, what INP is built from (≤ 200 ms).');
console.log('- **Tap to row:** from the tap to the frame that shows the new row, NFR-PRF-7\'s visible feedback (≤ 100 ms). **First tap** is the first tap of each page load.');
console.log('- **Saved:** from the tap to the frame that shows the row as saved.');
console.log('- **Renderer:** what actually ran the page. The DevTools rows use Chromium\'s network emulation instead of the proxy.\n');
console.log('| Added RTT | Variant | Renderer | Tap event | Tap to row, first tap | Tap to row | Saved |');
console.log('|---|---|---|---|---|---|---|');
for (const r of rows) console.log(`| ${r.rtt} | ${r.variant} | ${r.renderers} | ${ms(r.event)} (${ms(r.eventMax)}) | ${ms(r.firstRow)} | ${ms(r.toRow)} (${ms(r.toRowMax)}) | ${ms(r.toSaved)} |`);
