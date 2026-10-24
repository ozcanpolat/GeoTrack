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
| EF Core migration | `Migrations/` altında **elle yazıldı** (dotnet ile üretilmedi); önce doğrulayın (aşağıya bakın) |
| PostGIS / `ST_DWithin` | Denenmedi |
| API, Swagger, harita, Ajax | Denenmedi |
| Dockerfile / `compose --profile app` | Denenmedi |
| Testler | Yazıldı, çalıştırılmadı |
| AI | Kod yazıldı (Anthropic), **anahtar ve model verilmezse kapalı**; gerçek bir API çağrısı hiç denenmedi |

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

:: 3) Elle yazılmış migration'ı doğrula, sonra uygula
dotnet ef migrations list
dotnet ef migrations has-pending-model-changes
dotnet ef database update

:: 4) Çalıştır
dotnet run --launch-profile https
```

Uygulama: <https://localhost:7080> (veya http://localhost:5080). Swagger: `/swagger`. OpenAPI JSON: `/openapi/v1.json`.

- **Migration doğrulaması:** `InitialCreate` dotnet ile üretilmedi. `has-pending-model-changes` "değişiklik yok" demelidir.
  Fark bildirirse `Migrations/` klasöründeki 3 dosyayı silip `dotnet ef migrations add InitialCreate` çalıştırın
  (veritabanı henüz boşken güvenlidir; `database update` uygulamadan önce yapın).
- `docker compose` dosyası PostGIS imajı `postgis/postgis:17-3.5` kullanır (etiket, projenin resmî README'sinde doğrulandı).
- Geliştirme ortamında (`appsettings.Development.json`) açılışta migration uygulanır ve tablo **boşsa** 6 adet `[DEMO]` kaydı eklenir.
  Kayıt varsa tekrar eklenmez. Kapatmak için `Database:SeedDemoData` / `Database:ApplyMigrationsOnStartup` değerlerini `false` yapın.

## Docker ile tek komut (veritabanı + uygulama)

```cmd
docker compose --profile app up -d --build
docker compose logs -f web
```

Uygulama: <http://localhost:8080> · Swagger: <http://localhost:8080/swagger>. Bu mod açılışta migration uygular ve
(tablo boşsa) demo veri ekler. Durdurmak için `docker compose --profile app down` (veri volume'da kalır).

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

AI **isteğe bağlıdır** ve varsayılan olarak kapalıdır. `Ai:ApiKey` **ve** `Ai:Model` ikisi birden verilirse Anthropic
Messages API kullanılır; biri eksikse öneri butonu "AI entegrasyonu yapılandırılmadı" der, CRUD/harita/API etkilenmez.
Model adı koda gömülü değildir: hesabınızda erişiminiz olan bir model kimliğini siz verirsiniz. Kural tabanlı sahte öneri üretilmez.

Yerel geliştirmede (User Secrets, Git'e girmez):

```cmd
cd GeoTrack.Web
dotnet user-secrets set "Ai:ApiKey" "BURAYA_KENDI_ANAHTARINIZ"
dotnet user-secrets set "Ai:Model" "BURAYA_MODEL_KIMLIGI"
```

Docker modunda `.env` dosyanıza `AI_API_KEY=` ve `AI_MODEL=` değerlerini yazın.

- Anahtar yalnızca sunucuda kalır; tarayıcıya gönderilmez, loga/hata mesajına yazılmaz.
- Yanıt JSON olarak istenir, `AiResponseParser` ile doğrulanır; geçersiz kategori/öncelik reddedilir (ayrıştırıcı birim testleriyle kapsanır).
- Öneri otomatik kaydedilmez; kullanıcı "kabul et" / "reddet" der. Sağlayıcı hata verirse form önerisiz çalışır.
- Başka bir sağlayıcı için `IIncidentAiService`'i uygulayıp `Program.cs` kaydını değiştirmek yeterlidir.
- **Gerçek bir AI çağrısı bu kod yazılırken hiç denenmedi**; anahtarınızla ilk denemede sonucu doğrulayın.

## Git akışı

Her aşama ayrı commit'tir (`git log --oneline`). Remote (GitHub) deposu oluşturulmadı ve push yapılmadı.

## Demo akışı

1. Panel ve arıza listesini aç (Ajax filtreleri dene).
2. Yeni arıza oluştur (haritadan konum seç).
3. Sayfayı yenile / uygulamayı yeniden başlat → kayıt PostgreSQL'de kalıcıdır.
4. Harita sayfasında kaydı gör, bir nokta seç, 500 m çevresini sorgula.
5. Swagger'da `GET /api/incidents/nearby` çağır; geçersiz `lat=95` ile 400 göster.
6. AI yapılandırıldıysa öneri al ve onayla.

## Bilinen eksikler

- Kod derlenmedi/çalıştırılmadı; migration elle yazıldı (yukarıdaki durum tablosu).
- Kimlik doğrulama ve rol (yönetici/saha personeli) ayrımı yok; çok kullanıcılı üretim güvenliği iddiası yoktur.
- AI kodu yazıldı ama gerçek bir API çağrısıyla denenmedi.
- Ekran görüntüleri yok (uygulama çalıştırılamadığı için eklenmedi); çalıştırdıktan sonra buraya ekleyin.
- Leaflet, Swagger UI ve OpenStreetMap karoları CDN/çevrimiçi kaynaklardan gelir; internet gerekir.
- Harita tüm kayıtları (en fazla 20 sayfa × 100) yükler; çok büyük veri kümeleri için sınır/kümeleme eklenmelidir.
- API testlerinin 404/201 gibi veritabanı gerektiren yolları yalnızca `GEOTRACK_TEST_CONNECTION` ile gerçek PostGIS üzerinde çalışır.
