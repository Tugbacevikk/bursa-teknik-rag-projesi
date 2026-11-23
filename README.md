# BTÜ Asistan

Bursa Teknik Üniversitesi öğrencilerinin İMEP, akademik takvim, yönetmelik ve form süreçleriyle ilgili sorularını resmi belgelere dayanarak yanıtlayan RAG sistemi.

Sistem yeniden yapılandırılıyor. Mimari, kararlar ve faz planı için: [PLANNING.md](PLANNING.md)

## Geliştirme ortamı

Gereken araçlar: Docker Desktop, .NET 10 SDK, Node 24.

```bash
cp .env.example .env
# JWT imza anahtarı repoda tutulmaz; bir kez üretip user-secrets'a yazın:
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)" --project backend/BtuAsistan.Api
docker compose up -d --build              # Postgres (5433) + ai-service (8001)
cd backend && dotnet tool restore && dotnet ef database update --project BtuAsistan.Api && cd ..
dotnet run --project backend/BtuAsistan.Api --launch-profile http   # API (5015)
cd web && npm install && npm start        # Angular (4200)
```

`docker compose`, `docker-compose.yml`'nin yanındaki `docker-compose.override.yml`'yi kendiliğinden okur: Postgres ve AI servisi yalnızca bu makineden erişilen portlarla (5433, 8001) ve sabit container adlarıyla (`btu-asistan-postgres`, `btu-asistan-ai`) açılır. Sunucuda bu dosya kullanılmaz; orada `docker-compose.yml` adıyla verilir ve dışarıya yalnızca ön yüz açılır (PLANNING Faz 6).

AI servisi ilk açılışta `BAAI/bge-m3` modelini Hugging Face'ten indirir (yaklaşık 2,3 GB, birkaç dakika). Model `ai-models` volume'unda saklanır; sonraki açılışlar ~10 saniyedir. İndirme bitene kadar `/health` "loading" der ve yüklenen belgeler `Pending` bekler. İnternete çıkışı olmayan bir sunucuda model bir kez indirildikten sonra `.env`'de `HF_HUB_OFFLINE=1` yapılır.

### Gemini modelini seçmek

Sohbet (Faz 4) cevaplarını Gemini yazar. Anahtar Google AI Studio'dan alınır ve yalnızca `.env`'e yazılır (`GEMINI_API_KEY`); API'ye hiç verilmez. Geliştirmede ücretsiz katman anahtarı yeterlidir; **canlıda yalnızca ücretli anahtar** kullanılır, çünkü ücretsiz katmanda Google gönderilen metinleri ürünlerini geliştirmek için kullanabilir (PLANNING C10).

Anahtar yazılıp AI servisi yeniden başlatıldıktan sonra (`docker compose up -d ai-service`), anahtarla kullanılabilen modeller şöyle listelenir:

```bash
docker compose exec ai-service python -c "
import os
from google import genai
client = genai.Client(api_key=os.environ['GEMINI_API_KEY'])
for m in client.models.list():
    if 'generateContent' in (m.supported_actions or []):
        print(m.name.removeprefix('models/'), '|', m.display_name)
"
```

Seçilen ad `.env`'de `GEMINI_MODEL`'e yazılır ve servis yeniden başlatılır. `-latest` ile biten adlar kullanılmaz: Google onları haber vermeden başka modele yönlendirir, ölçülen model ile çalışan model ayrışır. `preview` modeller de canlıda kullanılmaz. `/health` cevabında `"generator": "ok"` görünür; anahtar ya da model yoksa `"not_configured"` der ve yalnızca sohbet çalışmaz.

İlk personel hesabı yalnızca sunucuda, komut satırından oluşturulur (parola sorulur):

```bash
dotnet run --project backend/BtuAsistan.Api -- create-admin ad.soyad@btu.edu.tr
```

Sunucuda (container'larla) aynı komut API imajından çalışır:

```bash
docker compose -f docker-compose.yml run --rm api create-admin ad.soyad@btu.edu.tr
```

Sonraki hesapları bir Admin panelden açar: sunucu bir geçici parola üretir, yalnızca bir kez gösterir; hesabın sahibi ilk girişte kendi parolasını belirlemeden başka bir şey yapamaz. Hesaplar silinmez, kapatılır (denetim kaydı kalsın diye). Parolasını unutan için Admin "parolayı sıfırla" ile yeni bir geçici parola üretir. Bütün Admin hesapları kapanırsa ya da parola unutulursa `create-admin` yeniden çalıştırılır (var olan hesabı Admin yapar, parolasına dokunmaz).

Başlangıç belgeleri (`data/seed`) yalnızca kaynakları doğrulandıktan sonra içe aktarılır. `data/seed/sources.json`'da her belgenin `sourceUrl`'sini (ve tarih içerenlerde `validityPeriod`'ü) doldurun, sonra:

```bash
dotnet run --project backend/BtuAsistan.Api -- import-seed --as ad.soyad@btu.edu.tr
```

Sunucuda seed klasörü container'a salt okunur bağlanır:

```bash
docker compose -f docker-compose.yml run --rm -v ./data/seed:/seed:ro api import-seed --as ad.soyad@btu.edu.tr --dir /seed
```

(Windows'ta Git Bash `/seed`'i Windows yoluna çevirir; komut PowerShell'den ya da başına `MSYS_NO_PATHCONV=1` konarak çalıştırılır.)

Kaynağı boş olan belgeler atlanır ve listelenir. Komut tekrar çalıştırılabilir; daha önce aktarılmış dosyalar atlanır.

`http://localhost:4200` adresi sistem durumunu gösterir. Angular geliştirme sunucusu `/api` ve `/health` isteklerini API'ye yönlendirir (`web/proxy.conf.json`).

## Testler

Üç takım, üçü de yalnızca Docker'ın çalışıyor olmasını ister (gerçek modeller indirilmez, Gemini çağrılmaz). Ne neyi denediği: `PLANNING.md`, "Testler".

**API** (xUnit; birim testleri ve gerçek Postgres'e karşı entegrasyon testleri). Testcontainers her çalışmada kendi `pgvector` container'ını açar ve sonunda siler; geliştirme veritabanına dokunulmaz. `backend` klasöründen çalıştırılır (yeni test çalıştırıcısını seçen `global.json` orada):

```bash
cd backend
```

```bash
dotnet test --project BtuAsistan.Api.Tests
```

**AI servisi** (pytest), servisin kendi imajında:

```bash
docker compose run --rm --build ai-service-tests
```

**Angular** (Vitest):

```bash
cd web
```

```bash
npx ng test --watch=false
```

## Canlı ortam ayarları

Sunucuya kurulum, HTTPS, yedek ve günlük işletme adım adım: **[docs/kurulum.md](docs/kurulum.md)**.

Canlı ortamda hiçbir gizli bilgi dosyada durmaz; API hepsini ortam değişkenlerinden okur. Sunucuda (`docker-compose.yml`) bağlantı cümlesi, `Jwt__SigningKey` (`.env`'deki `JWT_SIGNING_KEY`'den), `AiService__BaseUrl` ve `ReverseProxy__Enabled` compose'da verilir; aşağıdaki diğer ayarlar gerekirse depo kökündeki `api.env` dosyasına `AD=değer` satırları olarak yazılır (dosya yoksa varsayılanlar geçerlidir; `.env` API'ye bütün olarak verilmez, çünkü Gemini anahtarı yalnızca AI servisine gider). `appsettings.Development.json` ve user-secrets yalnızca `Development` ortamında yüklenir. API, örnek parolalarla (`change-me…`) canlı ortamda başlamayı reddeder.

| Değişken | Zorunlu | Açıklama |
|----------|---------|----------|
| `ASPNETCORE_ENVIRONMENT` | Evet | `Production` |
| `ConnectionStrings__Default` | Evet | Kısıtlı hesap (`btu_asistan_app`) ile bağlantı cümlesi |
| `ConnectionStrings__Migrations` | Yalnızca migration sırasında | Tablo sahibi hesap; API çalışırken tanımlanmaz |
| `Jwt__SigningKey` | Evet | En az 32 karakter; `openssl rand -base64 48` ile üretilir |
| `AiService__BaseUrl` | Evet | Örn. `http://ai-service:8000` (iç ağ) |
| `Cors__AllowedOrigins__0` | Arayüz başka adresteyse | Personel arayüzünün adresi; birden fazlası `__1`, `__2`… |
| `ReverseProxy__Enabled` | Ön yüz sunucusunun arkasında (compose'da açık) | `true` ise istemci adresi `X-Forwarded-For`'dan alınır; yalnızca `ReverseProxy__KnownNetworks__0`, `__1`… ağlarından gelirse (varsayılan Docker'ın özel ağları 10/8, 172.16/12, 192.168/16). Hız sınırları gerçek öğrenci adresini görür |
| `RateLimiting__LoginPermitLimit`, `RateLimiting__LoginWindowSeconds` | Hayır | Giriş hız sınırı (varsayılan 60 saniyede 10) |
| `Reranker__Enabled` | Hayır | Varsayılan `false`. `true` ise arama sonuçları reranker'la yeniden sıralanır: sıralama belirgin iyileşir (MRR 0,72 → 0,82) ama soru başına ~7 sn eklenir (16 çekirdekte). AI servisi de `RERANKER_ENABLED=1` ile başlatılmalıdır; ilk açılışta model indirilir (~2,3 GB), ~1,2 GB ek bellek kullanır. Karar ve ölçüm: PLANNING Faz 3, adım 10 |
| `RateLimiting__ChatPerMinute`, `RateLimiting__ChatPerHour` | Hayır | Anonim sohbette IP başına soru sınırı (varsayılan dakikada 5, saatte 30). Aşılınca 429 ve `Retry-After`. Okulun tek dış adresinin arkasındaki bir sınıf bu sınırı paylaşır; ters vekil sunucuda forwarded headers ayarlanmadan (Faz 6) bütün öğrenciler tek adres sayılır |
| `Chat__MaxConcurrentAnswers`, `Chat__MaxQueuedAnswers` | Hayır | Aynı anda yazılan cevap sayısı (varsayılan 4) ve sırada bekleyebilen soru sayısı (20). Sıra doluysa 503 |
| `Chat__NotFoundMessage`, `Chat__UnavailableMessage`, `Chat__BusyMessage` | Hayır | Öğrenciye gösterilen sabit metinler: belgelerde cevap yok, cevap yazılamadı, çok yoğun |
| `RateLimiting__FeedbackPerMinute` | Hayır | Anonim 👍/👎 için IP başına dakikadaki istek sınırı (varsayılan 20) |
| `Analytics__TimeZone` | Hayır | Analitikte günlerin hangi saat dilimine göre sayılacağı (IANA adı, varsayılan `Europe/Istanbul`) |
| `Chat__RetentionDays` | Hayır | Sohbetlerin saklanma süresi; son mesajı bundan eski sohbetler mesajlarıyla birlikte silinir (varsayılan 90 gün) |
| `Chat__RetentionIntervalHours`, `Chat__RetentionBatchSize` | Hayır | Silme işinin kaç saatte bir çalıştığı (açılışta da çalışır; varsayılan 24) ve tek seferde silinen sohbet sayısı (varsayılan 1000) |
| `Search__KeywordPrefixLength` | Hayır | Varsayılan `0`. Postgres'in Türkçe kök bulucusu bazı biçimleri farklı köklere indirdiği için ("kaza"/"kazası") anahtar kelime araması onları kaçırır; `N` verilirse kökün ilk `N` harfiyle başlayan kelimeler de bulunur. 57 soruda (2026-11-20) belirgin iyileşme göstermediği için kapalı; resmî belgeler ve gözden geçirilmiş soru setiyle yeniden ölçülebilir (`eval -- --keyword-prefix N`) |
| `Embedding__Model` | Hayır | Varsayılan `BAAI/bge-m3`. Değiştirilecekse AI servisinde `EMBEDDING_MODEL` de aynı yapılır ve ikisi yeniden başlatılır. API açılışta eski modelle üretilmiş vektörleri arka planda kendiliğinden yeniler (logda "Re-embedding"); bitene kadar o parçalar yalnızca anahtar kelimeyle bulunur. Model 1024 boyutlu olmalıdır; farklı boyut migration ister |

Container sağlık kontrolü `/health/live`'ı kullanır (yalnızca "API ayakta"); `/health` Postgres ve AI servisinin durumunu da verir. `Production`'da loglar tek satır JSON'dur (`docker compose logs api`).

Ortam değişkeni adlarındaki `__`, ayar dosyasındaki iç içe bölümleri ayırır: `Jwt__SigningKey`, `"Jwt": { "SigningKey": … }` demektir.

## Okul sitesine gömmek

Sohbetin sade hâli `/widget` adresindedir (üst çubukta personel bağlantısı yok, dar kutuya göre). Okul sitesi sayfasına tek satır ekler:

```html
<iframe src="https://<btu-asistan-adresi>/widget" title="BTÜ Asistan sohbeti" style="width: 100%; max-width: 24rem; height: 36rem; border: 1px solid #d8dee6; border-radius: 12px" loading="lazy"></iframe>
```

Hangi sitelerin sohbeti çerçeveleyebileceğini ön yüzü sunan sunucunun `Content-Security-Policy: frame-ancestors` başlığı belirler; listede olmayan sitede tarayıcı çerçeveyi açmaz. Geliştirmede bu başlığı `ng serve` gönderir (`web/angular.json`) ve yalnızca `http://localhost:4300`'e izin verir. Denemek için `ng serve` çalışırken:

```bash
python -m http.server 4300 --directory web/widget-demo
```

sonra `http://localhost:4300` açılır (`web/widget-demo/index.html`, okul sitesinin yerine geçen bir sayfa). Sunucuda başlığı ön yüz (Caddy, `web/caddy/Caddyfile`) gönderir: `/widget` için `.env`'deki `WIDGET_ALLOWED_PARENTS` (ör. `https://www.btu.edu.tr https://imep.btu.edu.tr`; varsayılan `'none'`, hiçbir site), diğer bütün sayfalar için `frame-ancestors 'none'` (personel paneli başka bir sitede çerçevelenemez).

## Klasörler

| Klasör | İçerik |
|--------|--------|
| `backend/` | .NET API |
| `ai-service/` | Python yapay zekâ servisi (FastAPI) |
| `web/` | Angular ön yüz |
| `backend/BtuAsistan.Api.Tests/` | API testleri |
| `ai-service/tests/` | AI servisi testleri |
| `web/widget-demo/` | Widget'ı yerelde denemek için okul sitesi yerine geçen sayfa |
| `docker/` | Container başlangıç betikleri |
| `data/seed/` | Başlangıç belgeleri |
| `legacy/` | Eski Streamlit prototipi (yalnızca başvuru için, çalıştırılmaz) |
