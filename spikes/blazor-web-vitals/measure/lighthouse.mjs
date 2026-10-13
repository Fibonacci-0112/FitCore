// Runs Lighthouse (performance category) against each variant with the mobile and desktop
// presets, the way a CI job would, and prints the median of the runs.
// Usage: node lighthouse.mjs [runs] > ../results/lighthouse.md
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const BASE = process.env.BASE ?? 'http://127.0.0.1:5080';
const RUNS = Number(process.argv[2] ?? 3);
const CHROME = process.env.CHROMIUM ?? '/opt/pw-browsers/chromium-1194/chrome-linux/chrome';
const PATHS = ['/static', '/wasm', '/wasm-nopersist', '/wasm-noprerender', '/server'];
const out = mkdtempSync(join(tmpdir(), 'lh-'));
const median = (xs) => { const s = [...xs].sort((a, b) => a - b); const m = Math.floor(s.length / 2); return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2; };

const rows = [];
let version = '', benchmark = [];
for (const preset of ['mobile', 'desktop']) {
  for (const path of PATHS) {
    const runs = [];
    for (let i = 0; i < RUNS; i++) {
      const file = join(out, `${preset}${path.replace('/', '-')}-${i}.json`);
      const args = ['lighthouse', BASE + path, '--quiet', '--only-categories=performance', '--output=json', `--output-path=${file}`,
        '--chrome-flags=--headless=new --no-sandbox', ...(preset === 'desktop' ? ['--preset=desktop'] : [])];
      const res = spawnSync('npx', args, { env: { ...process.env, CHROME_PATH: CHROME }, stdio: ['ignore', 'ignore', 'inherit'] });
      if (res.status !== 0) throw new Error(`lighthouse failed for ${preset} ${path}`);
      const lhr = JSON.parse(readFileSync(file, 'utf8'));
      version = lhr.lighthouseVersion; benchmark.push(lhr.environment.benchmarkIndex);
      const a = lhr.audits;
      runs.push({ score: lhr.categories.performance.score * 100, lcp: a['largest-contentful-paint'].numericValue,
        cls: a['cumulative-layout-shift'].numericValue, tbt: a['total-blocking-time'].numericValue,
        tti: a['interactive'].numericValue, kib: a['total-byte-weight'].numericValue / 1024 });
    }
    const pick = (k) => median(runs.map((r) => r[k]));
    rows.push({ preset, path, score: pick('score'), lcp: pick('lcp'), cls: pick('cls'), tbt: pick('tbt'), tti: pick('tti'), kib: pick('kib') });
    console.error(`${preset} ${path}: score ${pick('score')} LCP ${Math.round(pick('lcp'))}`);
  }
}
console.log('# Lighthouse by render mode\n');
console.log(`Output of \`node lighthouse.mjs ${RUNS}\` on ${new Date().toISOString().slice(0, 10)}: Lighthouse ${version}, performance category, simulated throttling (mobile: 150 ms RTT, 1.6 Mbps, 4× CPU; desktop: 40 ms RTT, 10 Mbps, 1× CPU). Median of ${RUNS} runs. Host benchmark index ${Math.round(median(benchmark))}.\n`);
console.log('Lighthouse loads the page without tapping, so it reports Total Blocking Time (TBT) as its lab proxy for INP.\n');
console.log('| Preset | Variant | Score | LCP ms | CLS | TBT ms | TTI ms | KiB |');
console.log('|---|---|---|---|---|---|---|---|');
for (const r of rows) console.log(`| ${r.preset} | \`${r.path}\` | ${Math.round(r.score)} | ${Math.round(r.lcp)} | ${r.cls.toFixed(3)} | ${Math.round(r.tbt)} | ${Math.round(r.tti)} | ${Math.round(r.kib)} |`);
