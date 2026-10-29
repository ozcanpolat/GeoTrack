// Oluştur/Düzenle formlarındaki konum seçici: haritaya tıklayınca enlem/boylam alanları dolar.
(() => {
  const el = document.getElementById('picker-map');
  if (!el) return;
  if (typeof L === 'undefined') {
    const message = document.getElementById('picker-map-message');
    if (message) message.hidden = false;
    return;
  }

  const latInput = document.querySelector('input[name$="Latitude"]');
  const lngInput = document.querySelector('input[name$="Longitude"]');
  if (!latInput || !lngInput) return;

  const DEFAULT_CENTER = [40.21, 28.94];

  const parse = (v) => {
    const n = parseFloat(v);
    return Number.isFinite(n) ? n : null;
  };
  const valid = (la, ln) =>
    la !== null && ln !== null && Math.abs(la) <= 90 && Math.abs(ln) <= 180;

  const lat = parse(latInput.value);
  const lng = parse(lngInput.value);
  const hasPoint = valid(lat, lng);

  const map = L.map(el).setView(hasPoint ? [lat, lng] : DEFAULT_CENTER, hasPoint ? 15 : 12);
  const tiles = L.tileLayer('/tiles/{z}/{x}/{y}.png', {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> katkıda bulunanlar'
  }).addTo(map);
  tiles.on('tileerror', () => {
    const message = document.getElementById('picker-map-message');
    if (message) message.hidden = false;
  });
  if (typeof ResizeObserver !== 'undefined') new ResizeObserver(() => map.invalidateSize()).observe(el);

  let marker = null;
  function place(la, ln, pan) {
    if (marker) marker.setLatLng([la, ln]);
    else marker = L.marker([la, ln]).addTo(map);
    if (pan) map.panTo([la, ln]);
  }

  if (hasPoint) place(lat, lng, false);

  map.on('click', (e) => {
    const p = e.latlng.wrap();
    const la = Number(p.lat.toFixed(6));
    const ln = Number(p.lng.toFixed(6));
    latInput.value = la;
    lngInput.value = ln;
    place(la, ln, false);
  });

  function onManualChange() {
    const la = parse(latInput.value);
    const ln = parse(lngInput.value);
    if (valid(la, ln)) place(la, ln, true);
  }
  latInput.addEventListener('change', onManualChange);
  lngInput.addEventListener('change', onManualChange);
})();
