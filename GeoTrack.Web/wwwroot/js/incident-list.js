// Arıza listesi: filtre/sayfa değişince sayfa yenilenmeden /api/incidents'ten veri çeker (Ajax / fetch).
// Kullanıcı verisi DOM'a yalnızca textContent ile eklenir (innerHTML kullanılmaz).
(() => {
  const labels = window.GeoLabels;
  const statusSelect = document.getElementById('filter-status');
  const categorySelect = document.getElementById('filter-category');
  const prioritySelect = document.getElementById('filter-priority');
  const searchInput = document.getElementById('filter-search');
  const fromInput = document.getElementById('filter-from');
  const toInput = document.getElementById('filter-to');
  const sortSelect = document.getElementById('filter-sort');
  const clearFilters = document.getElementById('clear-filters');
  const rows = document.getElementById('incident-rows');
  const message = document.getElementById('list-message');
  const prevBtn = document.getElementById('prev-page');
  const nextBtn = document.getElementById('next-page');
  const pageInfo = document.getElementById('page-info');

  if (!rows || !labels) return;

  const PAGE_SIZE = 10;
  let page = 1;
  let totalPages = 1;
  let requestSeq = 0;

  function showMessage(text, kind) {
    message.hidden = !text;
    message.textContent = text || '';
    message.className = 'alert ' + (kind || '');
  }

  function textCell(text) {
    const td = document.createElement('td');
    td.textContent = text;
    return td;
  }

  function badgeCell(kind, value, label) {
    const td = document.createElement('td');
    const span = document.createElement('span');
    span.className = 'badge ' + kind + '-' + String(value).replace(/[^A-Za-z]/g, '');
    span.textContent = label;
    td.appendChild(span);
    return td;
  }

  function actionCell(id) {
    const td = document.createElement('td');
    td.className = 'right';
    const edit = document.createElement('a');
    edit.href = '/Incidents/Edit/' + encodeURIComponent(id);
    edit.textContent = 'Düzenle';
    const del = document.createElement('a');
    del.href = '/Incidents/Delete/' + encodeURIComponent(id);
    del.textContent = 'Sil';
    td.append(edit, del);
    return td;
  }

  function formatDate(iso) {
    const d = new Date(iso);
    return Number.isNaN(d.getTime())
      ? ''
      : d.toLocaleString('tr-TR', { dateStyle: 'short', timeStyle: 'short' });
  }

  function render(data) {
    rows.replaceChildren();

    if (!data.items.length) {
      const tr = document.createElement('tr');
      const td = document.createElement('td');
      td.colSpan = 6;
      td.textContent = 'Kriterlere uyan arıza kaydı bulunamadı.';
      tr.appendChild(td);
      rows.appendChild(tr);
    }

    for (const item of data.items) {
      const tr = document.createElement('tr');
      tr.append(
        textCell(item.title),
        textCell(labels.category[item.category] || item.category),
        badgeCell('status', item.status, labels.status[item.status] || item.status),
        badgeCell('priority', item.priority, labels.priority[item.priority] || item.priority),
        textCell(formatDate(item.createdAt)),
        actionCell(item.id)
      );
      rows.appendChild(tr);
    }

    totalPages = Math.max(1, data.totalPages || 1);
    pageInfo.textContent = 'Sayfa ' + data.page + ' / ' + totalPages + ' (' + data.totalCount + ' kayıt)';
    prevBtn.disabled = data.page <= 1;
    nextBtn.disabled = data.page >= totalPages;
  }

  async function load() {
    const seq = ++requestSeq;
    const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) });
    if (statusSelect.value) params.set('status', statusSelect.value);
    if (categorySelect.value) params.set('category', categorySelect.value);
    if (prioritySelect?.value) params.set('priority', prioritySelect.value);
    if (searchInput?.value.trim()) params.set('q', searchInput.value.trim());
    if (fromInput?.value && toInput?.value && fromInput.value > toInput.value) {
      showMessage('Başlangıç tarihi bitiş tarihinden sonra olamaz.', 'error');
      return;
    }
    if (fromInput?.value) params.set('fromUtc', new Date(fromInput.value + 'T00:00:00').toISOString());
    if (toInput?.value) {
      const endExclusive = new Date(toInput.value + 'T00:00:00');
      endExclusive.setDate(endExclusive.getDate() + 1);
      params.set('toUtc', endExclusive.toISOString());
    }
    if (sortSelect?.value) params.set('sort', sortSelect.value);

    showMessage('Yükleniyor…', 'info');
    try {
      const res = await fetch('/api/incidents?' + params.toString(), {
        headers: { Accept: 'application/json' }
      });
      if (seq !== requestSeq) return; // daha yeni bir istek başlatıldı
      if (!res.ok) {
        const problem = await res.json().catch(() => null);
        const detail = problem?.errors ? Object.values(problem.errors).flat().join(' ') : (problem?.detail || problem?.title);
        throw new Error((detail || 'Sunucu isteği reddetti.') + ' (HTTP ' + res.status + ')');
      }
      const data = await res.json();
      if (seq !== requestSeq) return;
      showMessage('');
      render(data);
    } catch (err) {
      if (seq !== requestSeq) return;
      showMessage('Liste yüklenemedi. Veritabanı çalışıyor mu? (' + err.message + ')', 'error');
    }
  }

  statusSelect.addEventListener('change', () => { page = 1; load(); });
  categorySelect.addEventListener('change', () => { page = 1; load(); });
  prioritySelect?.addEventListener('change', () => { page = 1; load(); });
  fromInput?.addEventListener('change', () => { page = 1; load(); });
  toInput?.addEventListener('change', () => { page = 1; load(); });
  sortSelect?.addEventListener('change', () => { page = 1; load(); });
  let searchTimer;
  searchInput?.addEventListener('input', () => {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(() => { page = 1; load(); }, 250);
  });
  clearFilters?.addEventListener('click', () => {
    statusSelect.value = '';
    categorySelect.value = '';
    if (prioritySelect) prioritySelect.value = '';
    if (searchInput) searchInput.value = '';
    if (fromInput) fromInput.value = '';
    if (toInput) toInput.value = '';
    if (sortSelect) sortSelect.value = 'recent';
    page = 1;
    load();
  });
  prevBtn.addEventListener('click', () => { if (page > 1) { page--; load(); } });
  nextBtn.addEventListener('click', () => { if (page < totalPages) { page++; load(); } });

  load();
})();
