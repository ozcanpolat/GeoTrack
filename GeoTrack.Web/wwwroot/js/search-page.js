(() => {
  const form = document.getElementById('search-form');
  if (!form) return;
  const input = document.getElementById('search-query');
  const results = document.getElementById('search-results');
  const history = document.getElementById('search-history');
  const message = document.getElementById('search-message');
  const labels = window.GeoLabels;

  function show(text, kind = 'info') {
    message.hidden = !text;
    message.textContent = text || '';
    message.className = `alert ${kind}`;
  }
  function empty(list, text) {
    const li = document.createElement('li');
    li.className = 'empty-state';
    li.textContent = text;
    list.replaceChildren(li);
  }
  async function loadHistory() {
    const response = await fetch('/api/search-history', { headers: { Accept: 'application/json' } });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const items = await response.json();
    history.replaceChildren();
    if (!items.length) return empty(history, 'Henüz arama geçmişi yok.');
    for (const item of items) {
      const li = document.createElement('li');
      const button = document.createElement('button');
      button.type = 'button'; button.className = 'btn'; button.textContent = item.query;
      button.addEventListener('click', () => { input.value = item.query; form.requestSubmit(); });
      const date = document.createElement('span'); date.className = 'muted';
      date.textContent = new Date(item.createdAt).toLocaleString('tr-TR');
      li.append(button, document.createTextNode(' '), date);
      history.append(li);
    }
  }
  async function search(query) {
    show('Aranıyor…');
    const params = new URLSearchParams({ q: query });
    const response = await fetch('/api/search?' + params, { headers: { Accept: 'application/json' } });
    if (!response.ok) throw new Error(response.status === 401 ? 'Oturum süresi doldu. Yeniden giriş yapın.' : `Arama başarısız (HTTP ${response.status}).`);
    const items = await response.json();
    results.replaceChildren();
    if (!items.length) empty(results, 'Bu aramayla eşleşen olay bulunamadı.');
    for (const item of items) {
      const li = document.createElement('li');
      const title = document.createElement('a'); title.href = `/Incidents/Edit/${encodeURIComponent(item.id)}`; title.textContent = item.title;
      const detail = document.createElement('p'); detail.className = 'muted'; detail.textContent = `${item.description} · ${labels?.status?.[item.status] || item.status} · ${labels?.priority?.[item.priority] || item.priority}`;
      li.append(title, detail); results.append(li);
    }
    show(`${items.length} sonuç gösteriliyor.`, 'success');
    await loadHistory();
  }
  form.addEventListener('submit', async event => {
    event.preventDefault();
    const query = input.value.trim();
    if (query.length < 2 || query.length > 150) { input.setCustomValidity('Arama 2–150 karakter olmalıdır.'); input.reportValidity(); input.setCustomValidity(''); return; }
    try { await search(query); } catch (error) { show(error.message, 'error'); }
  });
  document.getElementById('refresh-history').addEventListener('click', async () => {
    try { await loadHistory(); } catch { empty(history, 'Arama geçmişi yüklenemedi.'); }
  });
  loadHistory().catch(() => empty(history, 'Arama geçmişi yüklenemedi.'));
})();
