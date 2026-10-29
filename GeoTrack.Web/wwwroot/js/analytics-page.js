(() => {
  const form = document.getElementById('analytics-filter');
  if (!form) return;
  const labels = window.GeoLabels;
  const message = document.getElementById('analytics-message');
  const results = document.getElementById('analytics-results');
  const named = (map, value) => map?.[value] || value;
  const toUtc = value => value ? new Date(value + 'T00:00:00').toISOString() : null;
  function showMessage(text, kind = 'info') { message.hidden = !text; message.textContent = text || ''; message.className = `alert ${kind}`; }
  function stat(label, value, danger = false) {
    const article = document.createElement('article'); article.className = 'stat' + (danger ? ' danger' : '');
    const number = document.createElement('span'); number.className = 'stat-value'; number.textContent = value;
    const caption = document.createElement('span'); caption.className = 'stat-label'; caption.textContent = label;
    article.append(number, caption); return article;
  }
  function renderBars(containerId, entries, map) {
    const container = document.getElementById(containerId); container.replaceChildren();
    const max = Math.max(1, ...entries.map(x => x.count));
    if (!entries.length) { const empty = document.createElement('p'); empty.className = 'empty-state'; empty.textContent = 'Bu filtreyle eşleşen veri yok.'; container.append(empty); return; }
    for (const entry of entries) {
      const row = document.createElement('div'); row.className = 'bar-item';
      const caption = document.createElement('div'); caption.className = 'bar-caption';
      const name = document.createElement('span'); name.textContent = named(map, entry.status ?? entry.priority ?? entry.category);
      const count = document.createElement('strong'); count.textContent = entry.count;
      const track = document.createElement('div'); track.className = 'bar-track';
      const fill = document.createElement('div'); fill.className = 'bar-fill'; fill.style.width = `${Math.max(3, entry.count / max * 100)}%`;
      track.append(fill); caption.append(name, count); row.append(caption, track); container.append(row);
    }
  }
  const NS = 'http://www.w3.org/2000/svg';
  const COLORS = { Critical: '#dc2626', High: '#f97316', Medium: '#1f6feb', Low: '#64748b', New: '#1f6feb', InProgress: '#f59e0b', Assigned: '#8b5cf6', Resolved: '#16a34a' };
  const PRIORITY_ORDER = ['Critical', 'High', 'Medium', 'Low'];
  const pct = (n, total) => total ? Math.round(n / total * 1000) / 10 : 0;
  const svg = (tag, attrs = {}) => { const el = document.createElementNS(NS, tag); for (const [k, v] of Object.entries(attrs)) el.setAttribute(k, v); return el; };
  function renderDonut(containerId, entries, map, keyName, summary) {
    const box = document.getElementById(containerId); box.replaceChildren();
    const total = entries.reduce((a, e) => a + e.count, 0);
    const R = 15.9155, wrap = svg('svg', { viewBox: '0 0 42 42', class: 'donut', role: 'img', 'aria-label': summary });
    wrap.append(svg('circle', { cx: 21, cy: 21, r: R, fill: 'none', stroke: '#e5e9f0', 'stroke-width': 6 }));
    let offset = 25;
    for (const e of entries) {
      const share = e.count / total * 100;
      const seg = svg('circle', { cx: 21, cy: 21, r: R, fill: 'none', stroke: COLORS[e[keyName]] || '#94a3b8', 'stroke-width': 6, 'stroke-dasharray': `${share} ${100 - share}`, 'stroke-dashoffset': offset });
      const title = svg('title'); title.textContent = `${named(map, e[keyName])}: ${e.count} (%${pct(e.count, total)})`; seg.append(title);
      wrap.append(seg); offset -= share;
    }
    const center = svg('text', { x: 21, y: 22.5, 'text-anchor': 'middle', class: 'donut-total' }); center.textContent = total;
    wrap.append(center);
    const legend = document.createElement('ul'); legend.className = 'legend';
    for (const e of entries) {
      const li = document.createElement('li');
      const dot = document.createElement('span'); dot.className = 'dot'; dot.style.background = COLORS[e[keyName]] || '#94a3b8';
      const text = document.createElement('span'); text.textContent = `${named(map, e[keyName])}: ${e.count} (%${pct(e.count, total)})`;
      li.append(dot, text); legend.append(li);
    }
    box.append(wrap, legend);
  }
  function renderTimeline(items) {
    const box = document.getElementById('chart-timeline'); box.replaceChildren();
    if (!items.length) { box.textContent = 'Bu aralıkta kayıt yok.'; return; }
    const W = 640, H = 200, padL = 30, padB = 28, padT = 12, max = Math.max(...items.map(i => i.count));
    const chart = svg('svg', { viewBox: `0 0 ${W} ${H}`, class: 'timeline-svg', role: 'img', 'aria-label': 'Günlük oluşturulan kayıt sayısı' });
    const step = (W - padL) / items.length, bw = Math.max(2, Math.min(34, step * 0.7));
    for (const t of [0, 0.5, 1]) {
      const y = padT + (H - padT - padB) * (1 - t);
      chart.append(svg('line', { x1: padL, x2: W, y1: y, y2: y, stroke: '#e5e9f0' }));
      const lab = svg('text', { x: padL - 4, y: y + 3, 'text-anchor': 'end', class: 'axis' }); lab.textContent = Math.round(max * t); chart.append(lab);
    }
    items.forEach((item, idx) => {
      const h = (H - padT - padB) * item.count / max, x = padL + step * idx + (step - bw) / 2, y = H - padB - h;
      const bar = svg('rect', { x, y, width: bw, height: h, rx: 2, fill: '#1f6feb' });
      const tip = svg('title'); tip.textContent = `${item.date}: ${item.count} kayıt`; bar.append(tip); chart.append(bar);
      if (items.length <= 12 || idx % Math.ceil(items.length / 8) === 0) {
        const lab = svg('text', { x: x + bw / 2, y: H - 10, 'text-anchor': 'middle', class: 'axis' }); lab.textContent = item.date.slice(5); chart.append(lab);
      }
    });
    box.append(chart);
  }
  function renderTable(matrix, total) {
    const body = document.getElementById('analytics-table'); body.replaceChildren();
    for (const level of PRIORITY_ORDER) {
      const rows = matrix.filter(m => m.priority === level);
      const sum = rows.reduce((a, m) => a + m.count, 0); if (!sum) continue;
      const resolved = rows.filter(m => m.status === 'Resolved').reduce((a, m) => a + m.count, 0);
      const tr = document.createElement('tr');
      for (const text of [named(labels.priority, level), sum, sum - resolved, resolved, `%${pct(sum, total)}`]) { const td = document.createElement('td'); td.textContent = text; tr.append(td); }
      body.append(tr);
    }
  }
  async function load() {
    showMessage('Filtreler uygulanıyor…');
    const params = new URLSearchParams();
    const from = toUtc(document.getElementById('analytics-from').value);
    const toInput = document.getElementById('analytics-to').value;
    if (from) params.set('fromUtc', from);
    if (toInput) { const end = new Date(toInput + 'T00:00:00'); end.setDate(end.getDate() + 1); params.set('toUtc', end.toISOString()); }
    for (const [control, name] of [['analytics-status','status'],['analytics-priority','priority'],['analytics-category','category']]) {
      const value = document.getElementById(control).value; if (value) params.set(name, value);
    }
    const lat = document.getElementById('analytics-lat').value;
    const lng = document.getElementById('analytics-lng').value;
    if (lat || lng) {
      if (!lat || !lng) { showMessage('Konum filtresi için enlem ve boylamı birlikte girin.', 'error'); return; }
      params.set('lat', lat); params.set('lng', lng); params.set('radiusMeters', document.getElementById('analytics-radius').value || '1000');
    }
    try {
      const response = await fetch('/api/analytics/summary?' + params, { headers: { Accept: 'application/json' } });
      const data = await response.json();
      if (!response.ok) throw new Error(Object.values(data.errors || {}).flat().join(' ') || data.detail || `Rapor alınamadı (HTTP ${response.status}).`);
      const empty = document.getElementById('analytics-empty');
      if (!data.total) { results.hidden = true; empty.hidden = false; showMessage(''); return; }
      empty.hidden = true;
      document.getElementById('analytics-stats').replaceChildren(
        stat('Toplam kayıt', data.total), stat('Açık kayıt', data.open), stat('Tamamlanan', data.resolved),
        stat('Yüksek öncelikli', data.highPriority, data.highPriority > 0), stat('Açık kritik', data.criticalOpen, data.criticalOpen > 0));
      const byPriority = PRIORITY_ORDER.map(p => data.priorities.find(x => x.priority === p)).filter(Boolean);
      renderDonut('chart-priority', byPriority, labels.priority, 'priority', 'Önem seviyelerine göre dağılım');
      renderDonut('chart-status', data.statuses, labels.status, 'status', 'Durum dağılımı');
      renderBars('analytics-categories', data.categories, labels.category);
      renderTimeline(data.timeline);
      renderTable(data.matrix, data.total);
      results.hidden = false; showMessage('');
    } catch (error) { showMessage(error.message, 'error'); }
  }
  form.addEventListener('submit', event => { event.preventDefault(); load(); });
  document.getElementById('analytics-clear').addEventListener('click', () => { form.reset(); load(); });
  load();
})();
