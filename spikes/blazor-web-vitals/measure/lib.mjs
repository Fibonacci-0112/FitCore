// Shared helpers for the render-mode comparison scripts: the browser, the spike server and page probes.
import { spawn } from 'node:child_process';
import { chromium } from 'playwright-core';

export const PUBLISH = new URL('../publish/', import.meta.url).pathname;
export const CHROMIUM = process.env.CHROMIUM ?? '/opt/pw-browsers/chromium-1194/chrome-linux/chrome';

export const launch = () => chromium.launch({ executablePath: CHROMIUM });
export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/** Starts the published spike server with extra environment variables and waits until it answers. */
export async function startServer(port, env = {}) {
  const proc = spawn('dotnet', ['BlazorSpike.dll'], {
    cwd: PUBLISH,
    env: { ...process.env, ASPNETCORE_URLS: `http://127.0.0.1:${port}`, ...env },
    stdio: ['ignore', 'ignore', 'inherit'],
  });
  const base = `http://127.0.0.1:${port}`;
  for (let i = 0; i < 200; i++) {
    try { if ((await fetch(`${base}/api/_stats`)).ok) return { proc, base, stop: (signal = 'SIGTERM') => stop(proc, signal) }; } catch { /* not up yet */ }
    await sleep(100);
  }
  proc.kill('SIGKILL');
  throw new Error('spike server did not start');
}

/** Stops a server process and waits for it to exit. */
function stop(proc, signal) {
  return new Promise((resolve) => {
    if (proc.exitCode !== null) return resolve();
    proc.once('exit', resolve);
    proc.kill(signal);
  });
}

/** Waits for the Today screen's ready mark and returns the renderer that runs it (Server, WebAssembly or Island). */
export async function waitReady(page, timeout = 120_000) {
  await page.waitForFunction(() => performance.getEntriesByName('fitcore-ready').length > 0, null, { timeout, polling: 50 });
  return page.evaluate(() => {
    const marker = document.querySelector('[data-renderer]');
    return marker.dataset.renderer === 'Island' ? 'Island' : marker.dataset.renderer;
  });
}

/**
 * Installs observers that record each tap on "Add banana": its Event Timing duration (what INP uses),
 * when a new row first shows, and when that row shows as saved. Call before the page loads.
 */
export async function installTapProbe(context) {
  await context.addInitScript(() => {
    const m = (window.__taps = { events: [], taps: [] });
    new PerformanceObserver((list) => {
      for (const e of list.getEntries()) if (e.interactionId && e.name === 'pointerdown') m.events.push({ t: e.startTime, d: e.duration });
    }).observe({ type: 'event', durationThreshold: 16, buffered: true });
    addEventListener('pointerdown', (e) => {
      if (!e.target.closest?.('.add')) return;
      // A tap's row is the next one after the rows on screen, plus one for each earlier tap still waiting for its
      // row: two taps before either adds a row (as at the end of a dead zone) get different rows, in order.
      const rows = document.querySelectorAll('li[data-row]').length;
      const waiting = m.taps.filter((t) => t.slot >= rows).length;
      m.taps.push({ at: e.timeStamp, slot: rows + waiting, row: null, saved: null, failed: null });
    }, true);
    new MutationObserver(() => {
      const rows = [...document.querySelectorAll('li[data-row]')];
      for (const tap of m.taps) {
        const row = rows[tap.slot];
        if (!row) continue;
        if (tap.row === null) { tap.row = -1; requestAnimationFrame(() => (tap.row = performance.now())); }
        if (tap.saved === null && row.dataset.pending === 'false') { tap.saved = -1; requestAnimationFrame(() => (tap.saved = performance.now())); }
        if (tap.failed === null && row.querySelector('.failed')) tap.failed = performance.now();
      }
    }).observe(document, { subtree: true, childList: true, attributes: true, attributeFilter: ['data-pending'] });
  });
}

/** Returns the recorded taps with their event duration matched by start time. */
export const readTaps = (page) => page.evaluate(() => {
  const m = window.__taps;
  return m.taps.map((tap) => {
    const ev = m.events.find((e) => Math.abs(e.t - tap.at) < 2);
    const rel = (x) => (x === null || x < 0 ? null : x - tap.at);
    return { event: ev ? ev.d : 16, toRow: rel(tap.row), toSaved: rel(tap.saved), failed: rel(tap.failed) };
  });
});

/** Visible reconnect or error UI that Blazor shows over the page, or null when there is none. */
export const reconnectUi = (page) => page.evaluate(() => {
  const el = document.getElementById('components-reconnect-modal');
  const visible = (x) => !!x && x.checkVisibility?.() && getComputedStyle(x).display !== 'none';
  if (visible(el)) {
    // The default UI lives in a shadow root with its own <style>; read only the visible text.
    const root = el.shadowRoot ?? el;
    const text = [...root.querySelectorAll('*')]
      .filter((n) => n.tagName !== 'STYLE' && n.children.length === 0 && n.checkVisibility?.())
      .map((n) => n.textContent.trim()).filter(Boolean).join(' ');
    return `"${text.replace(/\s+/g, ' ').slice(0, 60)}"` || 'reconnect UI';
  }
  const err = document.getElementById('blazor-error-ui');
  if (visible(err)) return 'error UI';
  return null;
});

export const median = (xs) => {
  const s = xs.filter((x) => x != null && !Number.isNaN(x)).sort((a, b) => a - b);
  if (!s.length) return null;
  const mid = Math.floor(s.length / 2);
  return s.length % 2 ? s[mid] : (s[mid - 1] + s[mid]) / 2;
};
export const max = (xs) => (xs.some((x) => x != null) ? Math.max(...xs.filter((x) => x != null)) : null);
export const ms = (x) => (x == null ? 'n/a' : `${Math.round(x)}`);
