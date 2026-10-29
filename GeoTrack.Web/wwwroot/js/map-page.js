// Harita sayfası: marker'lar /api/incidents'ten (gerçek veritabanı kayıtları) gelir.
// Haritaya tıklayınca nokta seçilir, /api/incidents/nearby (PostGIS ST_DWithin) ile çevre sorgulanır,
// hızlı form fetch ile POST /api/incidents çağırır. Kullanıcı verisi yalnızca textContent ile eklenir.
(() => {
  const labels = window.GeoLabels;
  const mapEl = document.getElementById('main-map');
  if (!mapEl) return;
  const msg = document.getElementById('map-message');
  if (typeof L === 'undefined' || !labels) {
    if (msg) {
      msg.hidden = false;
      msg.className = 'alert error';
      msg.textContent = 'Harita bileşeni yüklenemedi. Sayfayı yenileyin; olay listesi çalışmaya devam eder.';
    }
    return;
  }

  const DEFAULT_CENTER = [40.21, 28.94];
  const PRIORITY_COLORS = { Low: '#64748b', Medium: '#1f6feb', High: '#f97316', Critical: '#dc2626' };

  const selLat = document.getElementById('sel-lat');
  const selLng = document.getElementById('sel-lng');
  const radiusInput = document.getElementById('radius');
  const nearbyBtn = document.getElementById('btn-nearby');
  const nearbyMsg = document.getElementById('nearby-message');
  const nearbyList = document.getElementById('nearby-results');
  const quickForm = document.getElementById('quick-form');
  const quickMsg = document.getElementById('quick-message');
  const filterStatus = document.getElementById('map-status');
  const filterCategory = document.getElementById('map-category');
  const filterPriority = document.getElementById('map-priority');
  const filterSearch = document.getElementById('map-search');
  const filterFrom = document.getElementById('map-from');
  const filterTo = document.getElementById('map-to');

  const map = L.map(mapEl).setView(DEFAULT_CENTER, 13);
  const tiles = L.tileLayer('/tiles/{z}/{x}/{y}.png', {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> katkıda bulunanlar'
  }).addTo(map);
  let tileFailureShown = false;
  tiles.on('tileerror', () => {
    if (tileFailureShown) return;
    tileFailureShown = true;
    setBox(msg, 'Harita altlığı yüklenemedi. İnternet bağlantısını kontrol edin; kayıt listesi ve konum işlemleri kullanılabilir.', 'warning');
  });

  const incidentLayer = (typeof L.markerClusterGroup === 'function'
    ? L.markerClusterGroup({ showCoverageOnHover: false, maxClusterRadius: 40, spiderfyOnMaxZoom: true, spiderfyDistanceMultiplier: 1.8, zoomToBoundsOnClick: true })
    : L.layerGroup()).addTo(map);
  const markersById = new Map();
  let selectedMarker = null;
  let radiusCircle = null;
  let mapRequestSeq = 0;

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
    const color = PRIORITY_COLORS[item.priority] || '#1f6feb';
    const marker = L.marker([item.latitude, item.longitude], {
      title: item.title,
      icon: L.divIcon({
        className: 'incident-dot',
        html: '<span style="background:' + color + '"></span>',
        iconSize: [20, 20],
        iconAnchor: [10, 10],
        popupAnchor: [0, -10]
      })
    });
    marker.bindPopup(popupFor(item));
    marker.addTo(incidentLayer);
    markersById.set(item.id, marker);
  }

  async function loadAll() {
    const requestSeq = ++mapRequestSeq;
    const points = [];
    const incidents = [];
    try {
      let page = 1;
      let totalPages = 1;
      let totalCount = 0;
      if (filterFrom?.value && filterTo?.value && filterFrom.value > filterTo.value) {
        setBox(msg, 'Başlangıç tarihi bitiş tarihinden sonra olamaz.', 'error');
        return;
      }
      while (page <= totalPages && page <= 20) {
        const params = new URLSearchParams({ page: String(page), pageSize: '100' });
        if (filterStatus?.value) params.set('status', filterStatus.value);
        if (filterCategory?.value) params.set('category', filterCategory.value);
        if (filterPriority?.value) params.set('priority', filterPriority.value);
        if (filterSearch?.value.trim()) params.set('q', filterSearch.value.trim());
        if (filterFrom?.value) params.set('fromUtc', new Date(filterFrom.value + 'T00:00:00').toISOString());
        if (filterTo?.value) {
          const endExclusive = new Date(filterTo.value + 'T00:00:00');
          endExclusive.setDate(endExclusive.getDate() + 1);
          params.set('toUtc', endExclusive.toISOString());
        }
        const res = await fetch('/api/incidents?' + params, {
          headers: { Accept: 'application/json' }
        });
        if (requestSeq !== mapRequestSeq) return;
        if (!res.ok) throw new Error(problemMessage(await res.json().catch(() => null)) + ' (HTTP ' + res.status + ')');
        const data = await res.json();
        if (requestSeq !== mapRequestSeq) return;
        totalCount = data.totalCount || 0;
        incidents.push(...data.items);
        totalPages = data.totalPages || 1;
        page++;
      }
      if (requestSeq !== mapRequestSeq) return;
      incidentLayer.clearLayers();
      markersById.clear();
      for (const item of incidents) {
        addMarker(item);
        points.push([item.latitude, item.longitude]);
      }
      if (points.length) map.fitBounds(L.latLngBounds(points).pad(0.2));
      const limitNotice = totalPages > 20 ? ' Haritada ilk 2.000 kayıt gösteriliyor; liste görünümünü kullanarak sayfalı gezin.' : '';
      setBox(msg, (points.length ? `${points.length} / ${totalCount} olay gösteriliyor.` : 'Filtrelerle eşleşen olay bulunamadı.') + limitNotice, points.length ? 'info' : 'warning');
      if (!tileFailureShown && points.length) msg.hidden = true;
    } catch (err) {
      if (requestSeq !== mapRequestSeq) return;
      setBox(msg, 'Arıza kayıtları yüklenemedi. Veritabanı çalışıyor mu? (' + err.message + ')', 'error');
    }
  }

  [filterStatus, filterCategory, filterPriority].filter(Boolean).forEach((filter) => filter.addEventListener('change', loadAll));
  [filterFrom, filterTo].filter(Boolean).forEach((filter) => filter.addEventListener('change', loadAll));
  let mapSearchTimer;
  filterSearch?.addEventListener('input', () => {
    clearTimeout(mapSearchTimer);
    mapSearchTimer = setTimeout(loadAll, 250);
  });
  if (typeof ResizeObserver !== 'undefined') new ResizeObserver(() => map.invalidateSize()).observe(mapEl);

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
