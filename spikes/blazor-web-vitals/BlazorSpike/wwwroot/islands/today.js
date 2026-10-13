// The island: one click handler on server-rendered HTML. No framework, no circuit, no WebAssembly.
// The tap adds a pending row at once (NFR-PRF-7), then the server's fragment replaces the totals and the row.
const button = document.querySelector('[data-island-add]');
const list = document.querySelector('.diary');
const pending = document.getElementById('pending-row');
// Saves run in parallel, and their responses can arrive in any order. Each response's totals cover the entries the
// server had stored by then (data-entries), so only totals covering more entries replace what's on screen.

button.addEventListener('click', async () => {
  // The client generates the ID, so a retry is idempotent, as in TodayViewModel.
  const entry = { ...JSON.parse(button.dataset.entry), id: crypto.randomUUID() };
  const row = pending.content.firstElementChild.cloneNode(true);
  list.append(row);
  try {
    const res = await fetch(`islands/diary/${button.dataset.date}`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(entry),
    });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const html = document.createElement('template');
    html.innerHTML = await res.text();
    const current = document.getElementById('totals');
    const next = html.content.getElementById('totals');
    if (Number(next.dataset.entries) > Number(current.dataset.entries)) current.replaceWith(next);
    row.replaceWith(html.content.querySelector('li[data-row]'));
  } catch {
    row.append(Object.assign(document.createElement('span'), { className: 'failed', textContent: 'Not saved' }));
  }
});

button.disabled = false;
performance.mark('fitcore-ready');
