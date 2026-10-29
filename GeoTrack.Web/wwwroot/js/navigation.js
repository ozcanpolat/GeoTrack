(() => {
  const badge = document.getElementById('notification-unread-count');
  if (!badge) return;

  function refreshCount() {
    fetch('/api/notifications/unread-count', { headers: { Accept: 'application/json' } })
      .then((response) => response.ok ? response.json() : Promise.reject())
      .then(({ count }) => {
        const unread = Number(count) || 0;
        badge.hidden = unread === 0;
        badge.textContent = unread > 99 ? '99+' : String(unread);
        badge.setAttribute('aria-label', `${unread} okunmamış bildirim`);
      })
      .catch(() => { badge.hidden = true; });
  }
  refreshCount();
  window.addEventListener('geo:notifications-updated', refreshCount);
})();
