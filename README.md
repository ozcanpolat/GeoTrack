# GeoTrack — Akıllı Arıza ve Saha Operasyon Yönetimi

Saha arızalarının kaydını, durumunu, önceliğini ve **konumunu** tek yerde yöneten bir web uygulaması (MVP).
Kayıtlar PostgreSQL/PostGIS'te saklanır; "şu noktanın 500 m çevresindeki arızalar" sorgusu uygulamada değil,
**veritabanında** `ST_DWithin` ile çalışır.

> **Demo uygulamasıdır.** Örnek kayıtlar ve koordinatlar kurgusaldır, gerçek bir kurum verisi değildir.
> Kimlik doğrulama/yetkilendirme yoktur; çok kullanıcılı üretim güvenliği iddiası taşımaz.

## ⚠️ Doğrulama durumu (lütfen önce okuyun)

Bu kod, içinde `dotnet`, internet erişimi (NuGet) ve Docker motoru bulunmayan bir ortamda yazıldı.
Bu yüzden **henüz hiç derlenmedi, çalıştırılmadı ve test edilmedi**. Aşağıdakiler "çalışıyor" değil,
"çalışması için yazıldı ve ilk çalıştırmada doğrulanmalı" anlamındadır:

| Konu | Durum |
|---|---|
| `dotnet build` | Denenmedi — ilk çalıştırmada derleme hatası çıkabilir |
| EF Core migration | **Repoda yok**; `dotnet ef migrations add` ile sizin makinenizde üretilir (aşağıya bakın) |
| PostGIS / `ST_DWithin` | Denenmedi |
| API, Swagger, harita, Ajax | Denenmedi |
| Testler | Yazıldı, çalıştırılmadı |
| AI | **Etkin değil** (gerçek bir sağlayıcı bağlı değil) |

İlk çalıştırmada bir hata çıkarsa tam hata mesajını paylaşın; düzeltmesi genellikle tek satırdır.

## Teknolojiler

.NET 10 / ASP.NET Core Razor Pages · HTML, CSS, JavaScript (`fetch` ile Ajax) · Entity Framework Core ·
PostgreSQL + PostGIS (Npgsql + NetTopologySuite) · Leaflet (OpenStreetMap altlığı) · minimal API + OpenAPI/Swagger UI ·
xUnit · Docker Compose (yalnızca veritabanı için)

Paket sürümleri `10.0.*` olarak yazılmıştır; `dotnet restore` en güncel 10.0.x yamasını seçer.

## Ön koşullar

- .NET SDK 10 (`dotnet --version`)
- Git
- Docker Desktop (motor çalışıyor olmalı: `docker info` hata vermemeli)
- `dotnet-ef` aracı: `dotnet tool install --global dotnet-ef --version 10.*`

## Kurulum ve çalıştırma (Windows CMD, `GeoTrack` kök klasöründe)

```cmd
:: 1) Veritabanını başlat (PostgreSQL + PostGIS, kalıcı volume'lu)
docker compose up -d
docker compose ps

:: 2) Uygulama klasörüne geç ve derle
cd GeoTrack.Web
dotnet build

:: 3) Migration üret ve uygula (ilk kurulumda, bir kez)
dotnet ef migrations add InitialCreate
dotnet ef database update
dotnet ef migrations list

:: 4) Çalıştır
dotnet run --launch-profile https
```

Uygulama: <https://localhost:7080> (veya http://localhost:5080). Swagger: `/swagger`. OpenAPI JSON: `/openapi/v1.json`.

- `dotnet ef migrations add` komutunu **yalnızca bir kez** çalıştırın; migration dosyaları `Migrations/` altında oluşur ve commit edilmelidir.
- `docker compose` dosyası PostGIS imajı `postgis/postgis:17-3.5` kullanır (etiket, projenin resmî README'sinde doğrulandı).
- Geliştirme ortamında (`appsettings.Development.json`) açılışta migration uygulanır ve tablo **boşsa** 6 adet `[DEMO]` kaydı eklenir.
  Kayıt varsa tekrar eklenmez. Kapatmak için `Database:SeedDemoData` / `Database:ApplyMigrationsOnStartup` değerlerini `false` yapın.

## Yapılandırma

| Anahtar | Açıklama |
|---|---|
| `ConnectionStrings:GeoTrack` (ortam değişkeni: `ConnectionStrings__GeoTrack`) | PostgreSQL bağlantı dizesi. Geliştirme değeri `appsettings.Development.json` içindedir. |
| `Database:ApplyMigrationsOnStartup` | `true` ise açılışta migration uygulanır (Development'ta `true`). |
| `Database:SeedDemoData` | `true` ise tablo boşken demo veri eklenir (Development'ta `true`). |
| `POSTGRES_DB/USER/PASSWORD/PORT` | `compose.yaml` değişkenleri; `.env.example` dosyasını `.env` olarak kopyalayıp değiştirin. |

Compose'taki ve `appsettings.Development.json`'daki parola **yalnızca yerel geliştirme** içindir. `.env` Git'e eklenmez.
Üretimde bağlantı dizesini ortam değişkeni veya gizli bilgi yöneticisiyle verin.

## Mimari notlar

- **Konum**: `Incident.Location` → PostGIS `geography(Point, 4326)`; tek doğru konum kaynağıdır. Enlem/boylam yalnızca DTO'larda vardır.
  Sıra: `X = boylam`, `Y = enlem` (`GeoFactory.CreatePoint(lat, lng)`).
- **Yakınlık**: `IsWithinDistance` → `ST_DWithin` (geography, **metre**), sonuçlar `ST_Distance` ile mesafeye göre sıralanır.
  `Location` üzerinde **GiST** indeks tanımlıdır (migration üretilince oluşur).
- **Ondalık sayılar**: Sunucu kültürü `en-US` olarak sabittir; böylece `40.21` gibi değerler Türkçe Windows'ta da doğru okunur.
- **Hatalar**: Beklenmeyen hatalar loglanır, kullanıcıya stack trace/bağlantı bilgisi gösterilmez.

## REST API

| Yöntem | Yol | Açıklama |
|---|---|---|
| GET | `/api/incidents?status=&category=&page=1&pageSize=20` | Sayfalı liste (`pageSize` en fazla 100) |
| GET | `/api/incidents/{id}` | Tek kayıt (yoksa 404) |
| POST | `/api/incidents` | Oluştur (201 / doğrulama hatasında 400) |
| PUT | `/api/incidents/{id}` | Güncelle (200 / 400 / 404) |
| DELETE | `/api/incidents/{id}` | Sil (204 / 404) |
| GET | `/api/incidents/nearby?lat=40.21&lng=28.94&radiusMeters=500` | Yarıçap içindekiler (en fazla 50 000 m) |
| POST | `/api/incidents/ai-suggestion` | AI önerisi (yapılandırılmadıysa 503) |

Değerler: `category` → `Road`, `WaterInfrastructure`, `Electricity`, `Waste`, `Other` ·
`status` → `New`, `InProgress`, `Assigned`, `Resolved` · `priority` → `Low`, `Medium`, `High`, `Critical`.

Örnek (CMD, `curl.exe`):

```cmd
curl.exe -k -X POST https://localhost:7080/api/incidents -H "Content-Type: application/json" -d "{\"title\":\"Mazgal tıkalı\",\"description\":\"Yoğun yağışta yol su altında kalıyor.\",\"category\":\"WaterInfrastructure\",\"priority\":\"High\",\"latitude\":40.21,\"longitude\":28.94}"
curl.exe -k "https://localhost:7080/api/incidents/nearby?lat=40.21&lng=28.94&radiusMeters=500"
curl.exe -k -i "https://localhost:7080/api/incidents/nearby?lat=95&lng=28.94&radiusMeters=500"
```
(`-k` yalnızca yerel geliştirme sertifikası içindir.) Son komut **400** dönmelidir.

## Testler

```cmd
:: Veritabanı gerektirmeyen testler (doğrulama, etiketler, LINQ→ST_DWithin SQL çevirisi)
dotnet test

:: Gerçek PostGIS testlerini de çalıştırmak için (migration uygulanmış olmalı):
set GEOTRACK_TEST_CONNECTION=Host=localhost;Port=5432;Database=geotrack;Username=geotrack;Password=geotrack_dev_password
dotnet test
```

`GEOTRACK_TEST_CONNECTION` yoksa gerçek PostGIS testleri **"atlandı (skipped)"** olarak görünür; bellek içi sahte veritabanıyla
geçmiş gibi gösterilmez. Bu testler `(0,0)` civarında geçici kayıtlar oluşturup test sonunda siler.

## AI yapılandırması

**Şu an etkin değildir.** Bir AI sağlayıcısı seçilmediği için `IIncidentAiService` için yalnızca
`NotConfiguredAiService` kayıtlıdır: öneri butonu "AI entegrasyonu yapılandırılmadı" der; CRUD, harita ve API etkilenmez.
Kural tabanlı sahte öneri üretilmez.

Gerçek bir sağlayıcı eklemek için:
1. `IIncidentAiService`'i uygulayan bir sınıf yazın; `categorySuggestion`, `prioritySuggestion` (enum adları), `summary`, `reason` döndürsün.
2. API anahtarını **yalnızca sunucuda** tutun: `dotnet user-secrets set "Ai:ApiKey" "..."` veya ortam değişkeni. Anahtarı Git'e/README'ye yazmayın.
3. `Program.cs` içinde `NotConfiguredAiService` kaydını kendi sınıfınızla değiştirin.

Öneriler otomatik kaydedilmez; kullanıcı "kabul et" / "reddet" der. Geçersiz kategori/öncelik içeren yanıtlar reddedilir.

## Git akışı

Her aşama ayrı commit'tir (`git log --oneline`). İlk migration'ı ürettikten sonra:

```cmd
git add .
git commit -m "feat: InitialCreate migration (geography + GiST indeks)"
```

## Demo akışı

1. Panel ve arıza listesini aç (Ajax filtreleri dene).
2. Yeni arıza oluştur (haritadan konum seç).
3. Sayfayı yenile / uygulamayı yeniden başlat → kayıt PostgreSQL'de kalıcıdır.
4. Harita sayfasında kaydı gör, bir nokta seç, 500 m çevresini sorgula.
5. Swagger'da `GET /api/incidents/nearby` çağır; geçersiz `lat=95` ile 400 göster.
6. AI yapılandırıldıysa öneri al ve onayla.

## Bilinen eksikler

- Kod derlenmedi/çalıştırılmadı; migration henüz üretilmedi (yukarıdaki durum tablosu).
- Kimlik doğrulama ve rol (yönetici/saha personeli) ayrımı yok.
- Gerçek AI sağlayıcısı bağlı değil.
- Ekran görüntüleri yok (uygulama çalıştırılamadığı için eklenmedi).
- Leaflet, Swagger UI ve OpenStreetMap karoları CDN/çevrimiçi kaynaklardan gelir; internet gerekir.
- Harita tüm kayıtları (en fazla 20 sayfa × 100) yükler; çok büyük veri kümeleri için sınır/kümeleme eklenmelidir.
