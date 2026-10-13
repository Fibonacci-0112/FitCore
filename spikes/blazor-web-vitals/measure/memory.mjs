// Server memory per open tab, for each render mode. Opens more and more tabs on the Today screen and reads
// the server's memory after a full garbage collection. Then closes every tab without warning, as a phone
// does, and reads it again: a circuit whose tab vanished stays in memory for its retention period.
// The script starts its own spike server on port 5095.
// Usage: node memory.mjs > ../results/memory.md
import { launch, startServer, waitReady, sleep } from './lib.mjs';

const PORT = 5095;
const VARIANTS = [
  { path: '/server', name: 'Interactive Server', tabs: [0, 25, 50, 100, 200] },
  { path: '/wasm', name: 'WebAssembly', tabs: [0, 25, 50] },
  { path: '/islands', name: 'Static SSR + JS island', tabs: [0, 25, 50, 100, 200] },
];
const MiB = 1024 * 1024;
const rows = [];

for (const variant of VARIANTS) {
  const server = await startServer(PORT);
  const browser = await launch();
  const context = await browser.newContext({ viewport: { width: 412, height: 823 } });
  const memory = async () => (await fetch(`http://127.0.0.1:${PORT}/api/_memory`)).json();
  // Warm the server up first, so JIT and caches don't count as per-tab memory.
  const warm = await context.newPage();
  await warm.goto(`http://127.0.0.1:${PORT}${variant.path}`);
  await waitReady(warm);
  await warm.close();
  await sleep(1000);
  const pages = [];
  let base = null;
  for (const n of variant.tabs) {
    while (pages.length < n) {
      const page = await context.newPage();
      await page.goto(`http://127.0.0.1:${PORT}${variant.path}`);
      await waitReady(page);
      pages.push(page);
    }
    await sleep(2000);
    const m = await memory();
    base ??= m;
    rows.push({ variant: variant.name, tabs: n, state: 'open', ...m, perTab: n ? (m.managedBytes - base.managedBytes) / n : null });
    console.error(`${variant.path} ${n} tabs: ${m.circuits} circuits, managed ${(m.managedBytes / MiB).toFixed(1)} MiB, working set ${(m.workingSetBytes / MiB).toFixed(0)} MiB`);
  }
  const n = pages.length;
  await context.close(); // tabs gone without a goodbye
  await sleep(5000);
  const after = await memory();
  rows.push({ variant: variant.name, tabs: n, state: 'closed 5 s ago', ...after, perTab: n ? (after.managedBytes - base.managedBytes) / n : null });
  console.error(`${variant.path} closed: ${after.circuits} circuits, managed ${(after.managedBytes / MiB).toFixed(1)} MiB`);
  await browser.close();
  await server.stop();
}

console.log('# Server memory per open tab\n');
console.log(`Output of \`node memory.mjs\` on ${new Date().toISOString().slice(0, 10)}. One server process per variant, warmed up with one page view first. Memory after a full, compacting garbage collection. The Today screen has 18 diary rows; a bigger component tree holds more per circuit.\n`);
console.log('- **Managed per tab:** growth of the managed heap since 0 tabs, divided by the number of tabs.');
console.log('- **Closed 5 s ago:** every tab closed without warning, as when a phone drops a background tab. Disconnected circuits are kept for `DisconnectedCircuitRetentionPeriod` (3 minutes by default) in case the tab comes back.\n');
console.log('| Variant | Tabs | State | Circuits | Managed heap, MiB | Working set, MiB | Managed per tab, KiB |');
console.log('|---|---|---|---|---|---|---|');
for (const r of rows) console.log(`| ${r.variant} | ${r.tabs} | ${r.state} | ${r.circuits} | ${(r.managedBytes / MiB).toFixed(1)} | ${(r.workingSetBytes / MiB).toFixed(0)} | ${r.perTab == null ? 'n/a' : (r.perTab / 1024).toFixed(0)} |`);
