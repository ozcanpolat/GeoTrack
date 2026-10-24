// Harita sayfası: marker'lar /api/incidents'ten (gerçek veritabanı kayıtları) gelir.
// Haritaya tıklayınca nokta seçilir, /api/incidents/nearby (PostGIS ST_DWithin) ile çevre sorgulanır,
// hızlı form fetch ile POST /api/incidents çağırır. Kullanıcı verisi yalnızca textContent ile eklenir.
(() => {
  const labels = window.GeoLabels;
  const mapEl = document.getElementById('main-map');
  if (!mapEl || typeof L === 'undefined' || !labels) return;

  const DEFAULT_CENTER = [40.21, 28.94];
  const PRIORITY_COLORS = { Low: '#64748b', Medium: '#1f6feb', High: '#f97316', Critical: '#dc2626' };

  const msg = document.getElementById('map-message');
  const selLat = document.getElementById('sel-lat');
  const selLng = document.getElementById('sel-lng');
  const radiusInput = document.getElementById('radius');
  const nearbyBtn = document.getElementById('btn-nearby');
  const nearbyMsg = document.getElementById('nearby-message');
  const nearbyList = document.getElementById('nearby-results');
  const quickForm = document.getElementById('quick-form');
  const quickMsg = document.getElementById('quick-message');

  const map = L.map(mapEl).setView(DEFAULT_CENTER, 13);
  L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> katkıda bulunanlar'
  }).addTo(map);

  const incidentLayer = L.layerGroup().addTo(map);
  const markersById = new Map();
  let selectedMarker = null;
  let radiusCircle = null;

  function setBox(el, text, kind) {
    el.hidden = !text;
    el.textContent = text || '';
    el.className = 'alert ' + (kind || '');
  }

  function problemMessage(problem) {
    if (problem && problem.errors) {
      return Object.values(problem.errors).flat().join(' ');
    }
    return (problem && (problem.detail || problem.title)) || 'İstek geçersiz.';
  }

  function popupFor(item) {
    const box = document.createElement('div');
    const title = document.createElement('strong');
    title.textContent = item.title;
    const status = document.createElement('div');
    status.textContent = 'Durum: ' + (labels.status[item.status] || item.status);
    const priority = document.createElement('div');
    priority.textContent = 'Öncelik: ' + (labels.priority[item.priority] || item.priority);
    const link = document.createElement('a');
    link.href = '/Incidents/Edit/' + encodeURIComponent(item.id);
    link.textContent = 'Düzenle';
    box.append(title, status, priority, link);
    return box;
  }

  function addMarker(item) {
    const marker = L.circleMarker([item.latitude, item.longitude], {
      radius: 9,
      color: '#ffffff',
      weight: 2,
      fillColor: PRIORITY_COLORS[item.priority] || '#1f6feb',
      fillOpacity: 0.9
    });
    marker.bindPopup(popupFor(item));
    marker.addTo(incidentLayer);
    markersById.set(item.id, marker);
  }

  async function loadAll() {
    incidentLayer.clearLayers();
    markersById.clear();
    const points = [];
    try {
      let page = 1;
      let totalPages = 1;
      while (page <= totalPages && page <= 20) {
        const res = await fetch('/api/incidents?page=' + page + '&pageSize=100', {
          headers: { Accept: 'application/json' }
        });
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const data = await res.json();
        for (const item of data.items) {
          addMarker(item);
          points.push([item.latitude, item.longitude]);
        }
        totalPages = data.totalPages || 1;
        page++;
      }
      if (points.length) map.fitBounds(L.latLngBounds(points).pad(0.2));
      setBox(msg, points.length ? '' : 'Henüz arıza kaydı yok. Haritadan nokta seçip hızlı form ile ekleyebilirsiniz.', 'info');
    } catch (err) {
      setBox(msg, 'Arıza kayıtları yüklenemedi. Veritabanı çalışıyor mu? (' + err.message + ')', 'error');
    }
  }

  function selectPoint(lat, lng) {
    selLat.value = lat;
    selLng.value = lng;
    if (selectedMarker) selectedMarker.setLatLng([lat, lng]);
    else selectedMarker = L.marker([lat, lng]).addTo(map);
    nearbyBtn.disabled = false;
  }

  map.on('click', (e) => {
    const p = e.latlng.wrap();
    selectPoint(Number(p.lat.toFixed(6)), Number(p.lng.toFixed(6)));
  });

  function onManualCoordinates() {
    const la = parseFloat(selLat.value);
    const ln = parseFloat(selLng.value);
    if (Number.isFinite(la) && Number.isFinite(ln) && Math.abs(la) <= 90 && Math.abs(ln) <= 180) {
      selectPoint(la, ln);
      map.panTo([la, ln]);
    }
  }
  selLat.addEventListener('change', onManualCoordinates);
  selLng.addEventListener('change', onManualCoordinates);

  function renderNearby(items) {
    nearbyList.replaceChildren();
    for (const item of items) {
      const li = document.createElement('li');
      const link = document.createElement('a');
      link.href = '#';
      link.textContent = item.title;
      link.addEventListener('click', (ev) => {
        ev.preventDefault();
        const m = markersById.get(item.id);
        map.setView([item.latitude, item.longitude], Math.max(map.getZoom(), 16));
        if (m) m.openPopup();
      });
      const dist = document.createElement('div');
      dist.className = 'dist';
      dist.textContent = Math.round(item.distanceMeters ?? 0) + ' m · ' +
        (labels.status[item.status] || item.status) + ' · ' +
        (labels.priority[item.priority] || item.priority);
      li.append(link, dist);
      nearbyList.appendChild(li);
    }
  }

  nearbyBtn.addEventListener('click', async () => {
    const lat = parseFloat(selLat.value);
    const lng = parseFloat(selLng.value);
    const radius = parseFloat(radiusInput.value);
    if (![lat, lng, radius].every(Number.isFinite)) {
      nearbyMsg.textContent = 'Geçerli bir nokta ve yarıçap girin.';
      return;
    }

    const params = new URLSearchParams({
      lat: String(lat),
      lng: String(lng),
      radiusMeters: String(radius)
    });
    nearbyMsg.textContent = 'Aranıyor…';
    nearbyList.replaceChildren();

    try {
      const res = await fetch('/api/incidents/nearby?' + params.toString(), {
        headers: { Accept: 'application/json' }
      });
      if (res.status === 400) {
        nearbyMsg.textContent = problemMessage(await res.json());
        return;
      }
      if (!res.ok) throw new Error('HTTP ' + res.status);

      const items = await res.json();
      if (radiusCircle) radiusCircle.remove();
      radiusCircle = L.circle([lat, lng], { radius, color: '#1f6feb', fillOpacity: 0.08 }).addTo(map);
      map.fitBounds(radiusCircle.getBounds());

      nearbyMsg.textContent = items.length
        ? items.length + ' arıza bulundu (' + radius + ' m içinde).'
        : radius + ' m içinde arıza bulunamadı.';
      renderNearby(items);
    } catch (err) {
      nearbyMsg.textContent = 'Sorgu başarısız oldu. (' + err.message + ')';
    }
  });

  quickForm.addEventListener('submit', async (ev) => {
    ev.preventDefault();
    const lat = parseFloat(selLat.value);
    const lng = parseFloat(selLng.value);
    if (!Number.isFinite(lat) || !Number.isFinite(lng)) {
      setBox(quickMsg, 'Önce haritadan bir nokta seçin.', 'error');
      return;
    }

    const body = {
      title: document.getElementById('q-title').value.trim(),
      description: document.getElementById('q-description').value.trim(),
      category: document.getElementById('q-category').value || null,
      priority: document.getElementById('q-priority').value,
      status: 'New',
      latitude: lat,
      longitude: lng
    };

    setBox(quickMsg, 'Kaydediliyor…', 'info');
    try {
      const res = await fetch('/api/incidents', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify(body)
      });

      if (res.status === 201) {
        const created = await res.json();
        addMarker(created);
        quickForm.reset();
        setBox(quickMsg, 'Arıza kaydı oluşturuldu (#' + created.id + ').', 'success');
      } else if (res.status === 400) {
        setBox(quickMsg, problemMessage(await res.json()), 'error');
      } else {
        throw new Error('HTTP ' + res.status);
      }
    } catch (err) {
      setBox(quickMsg, 'Kayıt oluşturulamadı. (' + err.message + ')', 'error');
    }
  });

  loadAll();
})();
