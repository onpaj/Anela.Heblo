const LS_KEY = `replay-cursor:${window.location.origin}`;

let rows = [];
let cursor = parseInt(localStorage.getItem(LS_KEY) || '0', 10);
const results = new Map(); // id -> last 5 ForwardResult[]

fetch('/api/config')
  .then(r => r.json())
  .then(cfg => { document.getElementById('target-url').textContent = `Target: ${cfg.targetUrl}`; });

async function fetchRows() {
  const event = document.getElementById('f-event').value.trim();
  const proc = document.getElementById('f-proc-status').value;
  const sig = document.getElementById('f-sig-status').value;
  const from = document.getElementById('f-from').value;
  const to = document.getElementById('f-to').value;

  const params = new URLSearchParams({ take: '500' });
  if (event) params.set('event', event);
  if (proc) params.set('processingStatus', proc);
  if (sig) params.set('signatureStatus', sig);
  if (from) params.set('from', new Date(from).toISOString());
  if (to) params.set('to', new Date(to).toISOString());

  const data = await fetch(`/api/audit?${params}`).then(r => r.json());
  rows = data;
  cursor = Math.min(cursor, rows.length > 0 ? rows.length - 1 : 0);
  render();
}

function render() {
  const tbody = document.getElementById('tbody');
  tbody.innerHTML = '';
  rows.forEach((row, i) => {
    const tr = document.createElement('tr');
    if (i === cursor) tr.classList.add('cursor');
    tr.dataset.idx = i;
    tr.innerHTML = `
      <td>${new Date(row.receivedAt).toLocaleTimeString()}</td>
      <td>${row.eventName}</td>
      <td>${row.accountId ?? '-'}</td>
      <td class="status-${row.signatureStatus.toLowerCase()}">${row.signatureStatus}</td>
      <td class="status-${row.processingStatus.toLowerCase()}">${row.processingStatus}</td>
      <td>${row.bodySizeBytes}</td>
      <td>${row.processingDurationMs ?? '-'}</td>
      <td>
        <button class="send-btn" data-id="${row.id}" data-idx="${i}">Send</button>
        <button class="json-btn" data-id="${row.id}">{ }</button>
        <span class="pills" id="pills-${row.id}"></span>
      </td>`;
    tbody.appendChild(tr);
  });
}

async function showJson(id) {
  const modal = document.getElementById('json-modal');
  const pre = document.getElementById('json-content');
  pre.textContent = 'Loading…';
  modal.showModal();
  try {
    const detail = await fetch(`/api/audit/${id}`).then(r => r.json());
    try {
      pre.textContent = JSON.stringify(JSON.parse(detail.rawBody), null, 2);
    } catch {
      pre.textContent = detail.rawBody;
    }
  } catch {
    pre.textContent = 'Failed to load payload.';
  }
}

async function sendRow(id) {
  const btn = document.querySelector(`button[data-id="${id}"]`);
  if (btn) btn.disabled = true;

  try {
    const result = await fetch(`/api/audit/${id}/forward`, { method: 'POST' }).then(r => r.json());
    const list = results.get(id) ?? [];
    list.unshift(result);
    if (list.length > 5) list.pop();
    results.set(id, list);
    renderPills(id, list);
  } finally {
    if (btn) btn.disabled = false;
  }
}

function renderPills(id, list) {
  const el = document.getElementById(`pills-${id}`);
  if (!el) return;
  el.innerHTML = list.slice(0, 5).map(r => {
    const ok = r.httpStatus >= 200 && r.httpStatus < 300;
    return `<span class="pill ${ok ? 'pill-ok' : 'pill-err'}">${r.httpStatus} · ${r.durationMs}ms</span>`;
  }).join('');
}

function saveCursor() {
  localStorage.setItem(LS_KEY, String(cursor));
}

document.getElementById('btn-send-next').addEventListener('click', async () => {
  if (rows.length === 0) return;
  const row = rows[cursor];
  await sendRow(row.id);
  cursor = Math.min(cursor + 1, rows.length - 1);
  saveCursor();
  render();
});

document.getElementById('btn-refresh').addEventListener('click', fetchRows);

document.getElementById('btn-modal-close').addEventListener('click', () => {
  document.getElementById('json-modal').close();
});

document.getElementById('json-modal').addEventListener('click', e => {
  if (e.target === e.currentTarget) e.currentTarget.close();
});

document.getElementById('tbody').addEventListener('click', e => {
  const jsonBtn = e.target.closest('.json-btn');
  if (jsonBtn) {
    showJson(jsonBtn.dataset.id);
    return;
  }
  const btn = e.target.closest('.send-btn');
  if (btn) {
    sendRow(btn.dataset.id);
    return;
  }
  const tr = e.target.closest('tr');
  if (!tr || tr.dataset.idx === undefined) return;
  cursor = parseInt(tr.dataset.idx, 10);
  saveCursor();
  render();
});

document.addEventListener('keydown', e => {
  if (e.target.tagName === 'INPUT' || e.target.tagName === 'SELECT') return;
  if (e.key === 'n' || e.key === 'Enter') {
    document.getElementById('btn-send-next').click();
  } else if (e.key === 's') {
    cursor = Math.min(cursor + 1, rows.length - 1);
    saveCursor();
    render();
  } else if (e.key === 'r') {
    fetchRows();
  }
});

fetchRows();
