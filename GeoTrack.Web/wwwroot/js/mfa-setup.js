(() => {
  const key = document.getElementById('authenticator-key');
  const timeStatus = document.getElementById('mfa-clock-status');

  async function copyText(elementId, buttonId) {
    const value = document.getElementById(elementId)?.textContent?.trim();
    const button = document.getElementById(buttonId);
    if (!value || !button) return;
    try {
      await navigator.clipboard.writeText(value);
      const previous = button.textContent;
      button.textContent = 'Kopyalandı';
      setTimeout(() => { button.textContent = previous; }, 1800);
    } catch {
      button.textContent = 'Kopyalanamadı; metni seçip kopyalayın';
    }
  }

  document.getElementById('copy-authenticator-key')?.addEventListener('click', () => copyText('authenticator-key', 'copy-authenticator-key'));
  document.getElementById('copy-authenticator-uri')?.addEventListener('click', () => copyText('authenticator-uri', 'copy-authenticator-uri'));

  if (key && timeStatus) {
    const server = Date.parse(timeStatus.dataset.serverUtc || '');
    if (!Number.isFinite(server)) {
      timeStatus.textContent = 'Sunucu saat bilgisi okunamadı; cihazın tarih/saat ayarını otomatik eşitleyin.';
      timeStatus.className = 'alert warning';
      return;
    }
    const seconds = Math.round((Date.now() - server) / 1000);
    if (Math.abs(seconds) > 120) {
      timeStatus.textContent = `Cihaz saati sunucu saatinden yaklaşık ${Math.abs(seconds)} saniye farklı. Otomatik tarih ve saati açıp sayfayı yenileyin.`;
      timeStatus.className = 'alert warning';
    } else {
      timeStatus.textContent = `Cihaz ve sunucu saati uyumlu (yaklaşık ${Math.abs(seconds)} saniye fark).`;
      timeStatus.className = 'alert success';
    }
  }
})();
