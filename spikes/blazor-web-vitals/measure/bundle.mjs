// Lists the published WebAssembly payload: brotli and raw size per file, largest first.
// Usage: node bundle.mjs ../publish > ../results/bundle.md
import { readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';

const dir = join(process.argv[2] ?? '../publish', 'wwwroot', '_framework');
const files = readdirSync(dir).filter((f) => f.endsWith('.br')).map((f) => ({
  name: f.slice(0, -3).replace(/\.[a-z0-9]{10}\./, '.'),
  br: statSync(join(dir, f)).size,
  raw: statSync(join(dir, f.slice(0, -3))).size,
})).sort((a, b) => b.br - a.br);
const kib = (n) => (n / 1024).toFixed(0);
console.log('# WebAssembly payload\n');
console.log(`Output of \`node bundle.mjs\` on ${new Date().toISOString().slice(0, 10)}: every file under \`_framework/\` that has a brotli copy, largest first. A browser downloads only the ICU shard for its culture (\`icudt_EFIGS.dat\` for English), and \`blazor.server.js\` is never loaded by the WebAssembly pages.\n`);
console.log('| File | Brotli KiB | Raw KiB |');
console.log('|---|---|---|');
for (const f of files) console.log(`| \`${f.name}\` | ${kib(f.br)} | ${kib(f.raw)} |`);
console.log(`| **All files** | **${kib(files.reduce((s, f) => s + f.br, 0))}** | **${kib(files.reduce((s, f) => s + f.raw, 0))}** |`);
