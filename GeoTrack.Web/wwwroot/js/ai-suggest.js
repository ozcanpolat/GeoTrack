// AI önerisi: sunucudaki /api/incidents/ai-suggestion çağrılır (API anahtarı istemciye hiç gelmez).
// Öneri otomatik uygulanmaz; kullanıcı "kabul et" veya "reddet" der.
(() => {
  const labels = window.GeoLabels;
  const btn = document.getElementById('btn-ai');
  if (!btn || !labels) return;

  const titleInput = document.querySelector('input[name$="Title"]');
  const descInput = document.querySelector('textarea[name$="Description"]');
  const categorySelect = document.querySelector('select[name$="Category"]');
  const prioritySelect = document.querySelector('select[name$="Priority"]');

  const message = document.getElementById('ai-message');
  const result = document.getElementById('ai-result');
  const summaryEl = document.getElementById('ai-summary');
  const reasonEl = document.getElementById('ai-reason');
  const categoryEl = document.getElementById('ai-category');
  const priorityEl = document.getElementById('ai-priority');
  const acceptBtn = document.getElementById('ai-accept');
  const rejectBtn = document.getElementById('ai-reject');

  let current = null;

  function show(text, kind) {
    message.hidden = !text;
    message.textContent = text || '';
    message.className = 'alert ' + (kind || '');
  }

  btn.addEventListener('click', async () => {
    result.hidden = true;
    current = null;
    show('Öneri alınıyor…', 'info');

    try {
      const res = await fetch('/api/incidents/ai-suggestion', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify({
          title: titleInput.value.trim(),
          description: descInput.value.trim()
        })
      });

      if (res.status === 400) {
        show('Önce geçerli bir başlık (en az 3) ve açıklama (en az 10 karakter) girin.', 'error');
        return;
      }
      if (res.status === 503) {
        show('AI önerisi şu anda kullanılamıyor. Kaydı AI önerisi olmadan oluşturabilirsiniz.', 'info');
        return;
      }
      if (!res.ok) {
        show('AI servisi şu an kullanılamıyor. Kaydı önerisiz oluşturabilirsiniz.', 'error');
        return;
      }

      const s = await res.json();
      if (!labels.category[s.categorySuggestion] || !labels.priority[s.prioritySuggestion]) {
        show('AI yanıtı geçersiz; öneri gösterilmedi.', 'error');
        return;
      }

      current = s;
      summaryEl.textContent = s.summary || '';
      reasonEl.textContent = s.reason ? 'Gerekçe: ' + s.reason : '';
      categoryEl.textContent = labels.category[s.categorySuggestion];
      priorityEl.textContent = labels.priority[s.prioritySuggestion];
      result.hidden = false;
      show('');
    } catch (err) {
      show('AI isteği başarısız oldu. (' + err.message + ')', 'error');
    }
  });

  acceptBtn.addEventListener('click', () => {
    if (!current) return;
    categorySelect.value = current.categorySuggestion;
    prioritySelect.value = current.prioritySuggestion;
    result.hidden = true;
    show('Öneri forma uygulandı. Kaydetmeden önce kontrol edebilirsiniz.', 'success');
  });

  rejectBtn.addEventListener('click', () => {
    current = null;
    result.hidden = true;
    show('Öneri reddedildi.', 'info');
  });
})();
