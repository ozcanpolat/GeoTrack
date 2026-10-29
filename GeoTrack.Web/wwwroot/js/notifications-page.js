(() => {
  const list = document.getElementById('notification-list');
  const state = document.getElementById('connection-state');
  if (!list || !state) return;
  function setState(text, kind) { state.textContent = text; state.className = `alert ${kind}`; }
  function empty() { const li = document.createElement('li'); li.className = 'empty-state'; li.textContent = 'Henüz bildiriminiz yok.'; list.replaceChildren(li); }
  async function load() {
    const response = await fetch('/api/notifications', { headers: { Accept: 'application/json' } });
    if (!response.ok) throw new Error(`Bildirimler yüklenemedi (HTTP ${response.status}).`);
    const items = await response.json(); list.replaceChildren();
    window.dispatchEvent(new Event('geo:notifications-updated'));
    if (!items.length) return empty();
    for (const item of items) {
      const li = document.createElement('li');
      const row = document.createElement('div'); row.className = 'page-head';
      const content = document.createElement('div');
      const text = document.createElement('strong'); text.textContent = item.message;
      const date = document.createElement('div'); date.className = 'muted'; date.textContent = new Date(item.createdAt).toLocaleString('tr-TR');
      content.append(text, date); row.append(content);
      if (!item.isRead) {
        const button = document.createElement('button'); button.className = 'btn'; button.type = 'button'; button.textContent = 'Okundu işaretle';
        button.addEventListener('click', async () => {
          button.disabled = true;
          try { const res = await fetch(`/api/notifications/${encodeURIComponent(item.id)}/read`, { method: 'POST' }); if (!res.ok) throw new Error(); await load(); }
          catch { button.disabled = false; setState('Bildirim güncellenemedi. Yeniden deneyin.', 'error'); }
        }); row.append(button);
      } else { const badge = document.createElement('span'); badge.className = 'badge status-Resolved'; badge.textContent = 'Okundu'; row.append(badge); }
      li.append(row); list.append(li);
    }
  }
  document.getElementById('refresh-notifications').addEventListener('click', () => load().catch(error => setState(error.message, 'error')));
  load().then(() => setState('Bildirimler güncel.', 'success')).catch(error => setState(error.message, 'error'));
  if (window.signalR) {
    const connection = new signalR.HubConnectionBuilder().withUrl('/hubs/notifications').withAutomaticReconnect().build();
    connection.on('notification', () => load().catch(error => setState(error.message, 'error')));
    connection.onreconnecting(() => setState('Canlı bağlantı yeniden kuruluyor. Bildirimleri yenileyerek kontrol edebilirsiniz.', 'warning'));
    connection.onreconnected(() => setState('Canlı bildirim bağlantısı kuruldu.', 'success'));
    connection.onclose(() => setState('Canlı bağlantı kapalı. Listeyi yenileme ile güncel tutabilirsiniz.', 'warning'));
    connection.start().then(() => setState('Canlı bildirim bağlantısı kuruldu.', 'success')).catch(() => setState('Canlı bağlantı kurulamadı. Bildirim listesi yenilenerek çalışmaya devam eder.', 'warning'));
  } else setState('Canlı bildirim istemcisi yüklenemedi. Listeyi yenileme ile kontrol edebilirsiniz.', 'warning');
})();
