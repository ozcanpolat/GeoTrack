// Adım adım bildirim formu: Konum → Detaylar → Fotoğraf → Önizle & Gönder.
// JavaScript çalışmazsa tüm bölümler görünür kalır ve form normal gönderilir. Kullanıcı verisi yalnızca textContent ile eklenir.
(() => {
  const form = document.getElementById('create-form');
  if (!form) return;
  form.classList.add('wizard-on');
  const TOTAL = 4, MAX_FILES = 5, MAX_BYTES = 5 * 1024 * 1024, TYPES = ['image/jpeg', 'image/png', 'image/webp'];
  const steps = [...form.querySelectorAll('[data-step]')];
  const dots = [...document.querySelectorAll('#wizard-steps li')];
  const prev = document.getElementById('wiz-prev'), next = document.getElementById('wiz-next'), submit = document.getElementById('wiz-submit');
  const msg = document.getElementById('wizard-message');
  const fileInput = document.getElementById('photos');
  const thumbs = document.getElementById('photo-previews');
  const q = (suffix) => form.querySelector(`[name$="${suffix}"]`);
  let cur = 1;

  const error = (text) => { msg.hidden = !text; msg.textContent = text || ''; };

  function validate(n) {
    if (n === 1) {
      const la = parseFloat(q('Latitude').value), ln = parseFloat(q('Longitude').value);
      if (!Number.isFinite(la) || !Number.isFinite(ln) || Math.abs(la) > 90 || Math.abs(ln) > 180) return 'Haritadan bir konum seçin veya geçerli enlem/boylam girin.';
    }
    if (n === 2) {
      if (q('Title').value.trim().length < 3) return 'Başlık en az 3 karakter olmalıdır.';
      if (q('Description').value.trim().length < 10) return 'Açıklama en az 10 karakter olmalıdır.';
      if (!q('Category').value) return 'Kategori seçin.';
    }
    return '';
  }

  function dt(label, value) {
    const wrap = document.createElement('div');
    const t = document.createElement('dt'); t.textContent = label;
    const d = document.createElement('dd'); d.textContent = value;
    wrap.append(t, d);
    return wrap;
  }
  const selText = (el) => el.options[el.selectedIndex]?.text || '';

  function renderPreview() {
    const box = document.getElementById('preview');
    box.replaceChildren(
      dt('Başlık', q('Title').value.trim()),
      dt('Açıklama', q('Description').value.trim()),
      dt('Kategori', selText(q('Category'))),
      dt('Öncelik', selText(q('Priority'))),
      dt('Konum', q('Latitude').value + ', ' + q('Longitude').value),
      dt('Fotoğraf', (fileInput.files.length || 0) + ' adet'));
    const pv = document.getElementById('preview-photos');
    pv.replaceChildren(...[...thumbs.querySelectorAll('img')].map((i) => { const c = document.createElement('img'); c.src = i.src; c.alt = 'Seçilen fotoğraf'; return c; }));
  }

  function show(n) {
    cur = n;
    steps.forEach((s) => { s.hidden = Number(s.dataset.step) !== n; });
    dots.forEach((d, i) => { d.classList.toggle('active', i + 1 === n); d.classList.toggle('done', i + 1 < n); });
    prev.hidden = n === 1;
    next.hidden = n === TOTAL;
    submit.hidden = n !== TOTAL;
    error('');
    if (n === TOTAL) renderPreview();
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  next.addEventListener('click', () => {
    const problem = validate(cur);
    if (problem) return error(problem);
    show(cur + 1);
  });
  prev.addEventListener('click', () => show(cur - 1));
  form.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && e.target.tagName === 'INPUT' && cur < TOTAL) { e.preventDefault(); next.click(); }
  });
  form.addEventListener('submit', (e) => {
    for (let n = 1; n <= 2; n++) {
      const problem = validate(n);
      if (problem) { e.preventDefault(); show(n); error(problem); return; }
    }
    if (submit.disabled) { e.preventDefault(); return; } // çift tıklama
    submit.disabled = true;
    submit.textContent = 'Kaydediliyor…';
    prev.disabled = true;
    document.getElementById('wiz-status').hidden = false;
  });
  window.addEventListener('pageshow', () => { submit.disabled = false; submit.textContent = 'Kaydet ve gönder'; prev.disabled = false; document.getElementById('wiz-status').hidden = true; });

  // Fotoğraf seçimi: doğrulama + küçük resimler + tek tek kaldırma
  function syncFiles(files) {
    const keep = new DataTransfer();
    files.forEach((f) => keep.items.add(f));
    fileInput.files = keep.files;
    thumbs.replaceChildren();
    files.forEach((f, idx) => {
      const fig = document.createElement('figure');
      const img = document.createElement('img');
      img.src = URL.createObjectURL(f);
      img.alt = 'Seçilen fotoğraf ' + (idx + 1);
      const btn = document.createElement('button');
      btn.type = 'button'; btn.className = 'btn danger'; btn.textContent = 'Kaldır';
      btn.addEventListener('click', () => syncFiles(files.filter((_, i) => i !== idx)));
      fig.append(img, btn);
      thumbs.appendChild(fig);
    });
  }
  fileInput.addEventListener('change', () => {
    const all = [...fileInput.files];
    const good = all.filter((f) => TYPES.includes(f.type) && f.size <= MAX_BYTES).slice(0, MAX_FILES);
    if (good.length < all.length) error('Yalnızca JPG/PNG/WebP, en çok 5 MB ve en fazla 5 fotoğraf kabul edilir; uygun olmayanlar çıkarıldı.');
    else error('');
    syncFiles(good);
  });

  // Sunucu doğrulama hatasıyla dönüldüyse ilgili adımdan başla
  const firstError = form.querySelector('.field-error:not(:empty), .validation-summary-errors');
  show(firstError ? 2 : 1);
})();
