# BTÜ Asistan — Kurulum ve işletme kılavuzu

Bu kılavuz sistemi okulun sunucusuna kurmak ve günlük işletmek içindir. Sistem bütünüyle Docker container'larında çalışır; sunucunun işletim sistemi Linux, Windows ya da macOS olabilir, sunucuya Docker'dan başka bir şey kurulmaz. Komutlar her sistemde aynıdır; farklı olduğu yerlerde ayrıca belirtilmiştir.

Mimari ve kararlar: `PLANNING.md`, Faz 6. Bütün ayarların listesi: `README.md`, "Canlı ortam ayarları".

> **Windows'ta komutlar PowerShell'den çalıştırılır.** Git Bash kullanılacaksa komutların başına `MSYS_NO_PATHCONV=1` konur: Git Bash, `/seed`, `/out` gibi container içi yolları Windows yoluna (`C:/Program Files/Git/...`) çevirip komutu bozar.

## 1. Ne çalışır

| Servis | Görevi | Dışarı açık mı |
|---|---|---|
| `web` | Ön yüz (Caddy): sayfaları sunar, `/api`'yi API'ye iletir, HTTPS | Evet, 80 ve 443 |
| `api` | .NET API: giriş, belgeler, arama, sohbet | Hayır |
| `ai-service` | Python: belge vektörleri (bge-m3), Gemini ile cevap | Hayır |
| `postgres` | Veritabanı (pgvector) | Hayır |
| `migrate` | Her açılışta veritabanı şemasını günceller, sonra kapanır | Hayır |
| `backup` | Her gece veritabanı yedeği (`./backups`) | Hayır |

## 2. Gereksinimler

**Donanım** (öneri; okulun gerçek kullanımıyla ölçülüp güncellenmeli):

| | En az | Önerilen |
|---|---|---|
| İşlemci | 4 çekirdek | 8 çekirdek |
| Bellek | 8 GB | 16 GB (reranker açılacaksa gerekli) |
| Boş disk | 30 GB | 50 GB (yedekler ve belgeler büyüdükçe) |

Ölçülen (2026-11-19, 3 belgeyle): AI servisi ~0,7 GB bellek kullanır (en fazla `AI_SERVICE_MEMORY`, varsayılan 6 GB), API ~80 MB, veritabanı ~30 MB, web ~15 MB. İmajlar ~3,5 GB, model ~2,3 GB yer kaplar.

**Ağ:**

| Yön | Nereye | Ne zaman |
|---|---|---|
| İçeri | Sunucunun 80 ve 443 portları | Her zaman (öğrenciler ve personel) |
| Dışarı | `generativelanguage.googleapis.com` (Gemini) | Her zaman: sohbet bununla cevap verir |
| Dışarı | `huggingface.co` | İlk açılışta model indirilirken (sonra `HF_HUB_OFFLINE=1` yapılabilir) |
| Dışarı | `acme-v02.api.letsencrypt.org` | Yalnızca `TLS_MODE=auto` iken, sertifika alınırken ve yenilenirken |
| Dışarı | Docker Hub, `mcr.microsoft.com`, npm, NuGet, PyPI, `download.pytorch.org` | İmajlar derlenirken (kurulum ve güncelleme) |

**Diğer:** Bir alan adı (ör. `asistan.btu.edu.tr`) ve ücretli bir Gemini API anahtarı. Ücretsiz katman canlıda kullanılmaz: Google ücretsiz katmanda gönderilen metinleri ürünlerini geliştirmek için kullanabilir (PLANNING C10), ayrıca dakikada 15 istekle sınırlıdır.

## 3. Docker'ın kurulması

Docker Engine ve Compose (v2) gerekir. Kurulduğunu şu iki komut gösterir:

```bash
docker --version
```

```bash
docker compose version
```

- **Linux** (Ubuntu, Debian, RHEL…): Docker Engine ve Compose eklentisi, Docker'ın resmî kurulum sayfasındaki dağıtıma özel adımlarla kurulur: <https://docs.docker.com/engine/install/>. Komutları `sudo` istemeden çalıştırmak için kullanıcı `docker` grubuna eklenebilir.
- **Windows**: Docker Desktop (WSL2 ile) kurulur: <https://docs.docker.com/desktop/setup/install/windows-install/>. Container'lar WSL2 içindeki Linux'ta çalışır. Docker Desktop, Windows 10/11 içindir; sunucu Windows Server ise Docker'ın güncel belgelerine bakılmalı ya da sistem bir Linux sanal makinesinde çalıştırılmalıdır.
- **macOS**: Docker Desktop: <https://docs.docker.com/desktop/setup/install/mac-install/>.

Docker Desktop'ın kurumlarda kullanımı lisans koşullarına bağlıdır; okul BT'si bu koşullara bakmalıdır. Linux'taki Docker Engine için böyle bir koşul yoktur.

Windows ve macOS'ta Docker Desktop'ın bilgisayar açılınca kendiliğinden başlaması ayarlarından açılmalıdır; yoksa sunucu yeniden başlayınca sistem de kapalı kalır. Linux'ta Docker servisi kurulumla birlikte açılışta başlar.

## 4. Kodun sunucuya alınması

Depo sunucuda bir klasöre klonlanır (ya da ekipten alınan kopya açılır):

```bash
git clone <depo-adresi> btu-asistan
```

```bash
cd btu-asistan
```

Bundan sonraki bütün komutlar bu klasörde çalıştırılır.

## 5. `.env` dosyasının hazırlanması

Örnek dosya kopyalanır. Linux / macOS:

```bash
cp .env.example .env
```

Windows (PowerShell):

```powershell
Copy-Item .env.example .env
```

`.env`'de doldurulacaklar:

| Ayar | Ne yazılır |
|---|---|
| `POSTGRES_PASSWORD` | Veritabanı sahibinin parolası (yeni, rastgele) |
| `APP_DB_PASSWORD` | API'nin kısıtlı veritabanı hesabının parolası (yeni, rastgele) |
| `JWT_SIGNING_KEY` | Personel oturumlarının anahtarı (yeni, rastgele, en az 32 karakter) |
| `GEMINI_API_KEY` | Okulun **ücretli** Gemini anahtarı |
| `GEMINI_MODEL` | Değiştirmeden bırakılabilir (README "Gemini modelini seçmek") |
| `SITE_ADDRESS`, `TLS_MODE` | Bölüm 6 |
| `WIDGET_ALLOWED_PARENTS` | Sohbeti gösterecek okul siteleri (Bölüm 10) |
| `BACKUP_TIME`, `BACKUP_KEEP_DAYS` | Gece yedeğinin saati ve kaç gün tutulacağı (varsayılan 03:00, 14 gün) |

Rastgele parola ve anahtar, her işletim sisteminde aynı çalışan şu komutla üretilir; her biri için ayrı çalıştırılıp çıktısı `.env`'e yapıştırılır:

```bash
docker run --rm alpine sh -c "head -c 48 /dev/urandom | base64 | tr -d '/+='"
```

`change-me` ile başlayan örnek parolalar bırakılırsa API açılmayı reddeder (logda "Refusing to start"). `POSTGRES_PORT` ve `AI_SERVICE_PORT` yalnızca geliştirme içindir; sunucuda kullanılmaz, olduğu gibi bırakılabilir.

`.env`'de parolalar ve Gemini anahtarı vardır; dosyayı yalnızca sistemi yöneten okuyabilmelidir. Linux / macOS:

```bash
chmod 600 .env
```

Windows'ta dosyanın özelliklerinden (Güvenlik sekmesi) diğer kullanıcıların erişimi kaldırılır.

İsteğe bağlı API ayarları (hız sınırları, öğrenciye gösterilen metinler, saklama süresi…) gerekirse aynı klasörde `api.env` dosyasına `AD=değer` satırları olarak yazılır; liste README'dedir. Dosya yoksa varsayılanlar geçerlidir.

## 6. HTTPS'in seçilmesi

Üç durumdan biri seçilir:

**a) Sunucu internetten erişilebiliyor (en kolayı).** Alan adının DNS kaydı sunucunun adresini gösterir, 80 ve 443 portları internete açıktır. Sertifikayı sistem Let's Encrypt'ten kendisi alır ve yeniler:

```
SITE_ADDRESS=asistan.btu.edu.tr
TLS_MODE=auto
```

**b) Okul kendi sertifikasını veriyor.** Sertifika ve anahtar dosyaları (PEM biçiminde) klasördeki `certs/` altına konur:

```
SITE_ADDRESS=asistan.btu.edu.tr
TLS_MODE=files
TLS_CERT_FILE=cert.pem
TLS_KEY_FILE=key.pem
```

Sertifika yenilenince dosyalar değiştirilir ve `docker compose -f docker-compose.yml restart web` çalıştırılır.

**c) HTTPS'i okulun kendi ters vekil sunucusu yapıyor** (kampüs nginx, yük dengeleyici…). Bu sistem iç ağda düz HTTP dinler; vekilin adresi güvenilen olarak yazılır, yoksa bütün öğrenciler tek adresten geliyormuş gibi sayılır:

```
SITE_ADDRESS=http://asistan.btu.edu.tr
TLS_MODE=off
TRUSTED_PROXIES=10.0.5.20/32
```

Vekil, isteği `X-Forwarded-For` başlığıyla iletmelidir (çoğu vekil bunu kendiliğinden yapar).

80 ve 443 sunucuda başka bir programca kullanılıyorsa `HTTP_PORT` ve `HTTPS_PORT` değiştirilebilir. O durumda HTTP'den HTTPS'e yönlendirme yine 443'e gider; bu yüzden HTTPS için 443 dışı bir port yalnızca (c)'de ya da denemelerde kullanılır.

## 7. İlk açılış

```bash
docker compose -f docker-compose.yml up -d --build
```

`-f docker-compose.yml` her komutta yazılır: klasördeki `docker-compose.override.yml` geliştirme içindir ve sunucuda okunmamalıdır.

İlk açılışta imajlar derlenir (internet hızına göre 5–15 dakika), sonra AI servisi modeli indirir (~2,3 GB). Durum:

```bash
docker compose -f docker-compose.yml ps
```

Hepsi hazır olunca `postgres`, `ai-service`, `api`, `web` ve `backup` "healthy" görünür; `migrate` "Exited (0)" olur (işini yapıp kapanmıştır). AI servisi model inene kadar "starting" kalır; o sırada site açılır, giriş yapılabilir, yalnızca belge işleme ve sohbet bekler.

Tarayıcıda `https://<SITE_ADDRESS>` açılınca öğrenci sohbet sayfası görünmelidir. Sistemin kendi durumu:

```bash
curl https://asistan.btu.edu.tr/health
```

(`"status":"Healthy"` hem veritabanının hem AI servisinin hazır olduğunu gösterir.)

## 8. İlk yönetici hesabı

İlk Yönetici sunucuda komut satırından açılır; parolası iki kez sorulur:

```bash
docker compose -f docker-compose.yml run --rm api create-admin ad.soyad@btu.edu.tr
```

Sonraki hesaplar bu Yöneticinin panelinden (`/panel/hesaplar`) açılır: sistem bir geçici parola üretir, Yönetici kişiye yüz yüze ya da telefonla iletir, kişi ilk girişte kendi parolasını belirler. Bütün Yönetici hesaplarına erişim kaybolursa bu komut yeniden çalıştırılır: var olan bir hesabı Yönetici yapar, parolasına dokunmaz.

## 9. Belgelerin yüklenmesi

Personel `/giris` adresinden girer, **Belgeler** ekranından yönergeleri, takvimleri, SSS'leri ve formları yükler (PDF, DOCX, Markdown, metin). Belge birkaç saniye içinde "Hazır" olur ve sohbet onu kullanmaya başlar.

Depodaki başlangıç belgeleri (`data/seed`) kaynakları doğrulandıktan sonra toplu yüklenebilir (README, "import-seed"):

```bash
docker compose -f docker-compose.yml run --rm -v ./data/seed:/seed:ro api import-seed --as ad.soyad@btu.edu.tr --dir /seed
```

(Windows'ta Git Bash'ten çalıştırılırsa başına `MSYS_NO_PATHCONV=1`; baştaki nota bakın.)

## 10. Okul sitesine sohbet kutusu

Sohbetin sade hâli `/widget` adresindedir. `.env`'de izin verilen siteler yazılır (boşlukla ayrılmış, `https://` ile):

```
WIDGET_ALLOWED_PARENTS=https://www.btu.edu.tr https://imep.btu.edu.tr
```

ve `docker compose -f docker-compose.yml up -d` ile uygulanır. Okul sitesinin sayfasına eklenecek satır:

```html
<iframe src="https://asistan.btu.edu.tr/widget" title="BTÜ Asistan sohbeti" style="width: 100%; max-width: 24rem; height: 36rem; border: 1px solid #d8dee6; border-radius: 12px" loading="lazy"></iframe>
```

Listede olmayan bir sitede tarayıcı kutuyu boş gösterir.

## 11. Günlük işletme

Durum ve loglar:

```bash
docker compose -f docker-compose.yml ps
```

```bash
docker compose -f docker-compose.yml logs --tail 100 api
```

(`api` yerine `web`, `ai-service`, `backup`, `migrate`, `postgres`.) API logları tek satır JSON'dur. Docker her servisin logunu en fazla 5 × 10 MB tutar.

Durdurma ve başlatma (veriler korunur):

```bash
docker compose -f docker-compose.yml stop
```

```bash
docker compose -f docker-compose.yml up -d
```

**Güncelleme.** Önce elle bir yedek alınır (Bölüm 12), sonra yeni kod çekilip imajlar yeniden derlenir; veritabanı şeması `migrate` ile kendiliğinden güncellenir:

```bash
git pull
```

```bash
docker compose -f docker-compose.yml up -d --build
```

**Asla** `docker compose down -v` çalıştırılmaz: `-v` veritabanını, modeli ve sertifikaları siler.

## 12. Yedek ve geri yükleme

`backup` servisi her gece `BACKUP_TIME`'da veritabanının tamamını (hesaplar, belgeler, sohbetler, geri bildirimler) `./backups` klasörüne yazar ve `BACKUP_KEEP_DAYS`'ten eski yedekleri siler. Yedekleri listeleme ve hemen bir yedek alma:

```bash
docker compose -f docker-compose.yml run --rm backup list
```

```bash
docker compose -f docker-compose.yml run --rm backup now
```

**Yedekler sunucu dışına kopyalanmalıdır.** Sunucunun diski bozulursa aynı diskteki yedekler de gider; `./backups` klasörünün okulun yedekleme düzenine (ağ sürücüsü, yedekleme yazılımı) eklenmesi BT'nin işidir.

Yedeklerde öğrencilerin soruları vardır (KVKK). Linux / macOS'ta dosyalar yalnızca sahibince okunacak şekilde yazılır; klasörün kendisi de kısıtlanır:

```bash
chmod 700 backups
```

Windows'ta dosya izinleri container'dan taşınmaz; `backups` klasörünün erişimi klasörün özelliklerinden (Güvenlik sekmesi) yalnızca yöneticilere bırakılır.

**Geri yükleme** (veritabanının yerine bir yedek konur; sonradan girilen her şey kaybolur):

```bash
docker compose -f docker-compose.yml stop web api
```

```bash
docker compose -f docker-compose.yml run --rm backup restore btu-asistan-2026-11-20-0300.dump
```

```bash
docker compose -f docker-compose.yml up -d
```

Geri yükleme tek işlemdir: yarıda hata çıkarsa veritabanı olduğu gibi kalır. Sunucu baştan kuruluyorsa önce Bölüm 5–7 izlenir (aynı `.env` parolalarıyla), sonra yedek bu adımlarla geri yüklenir. Yılda bir kez bir yedeğin ayrı bir makinede geri yüklenip denenmesi önerilir; denenmemiş yedek güvence değildir.

## 13. İnternet erişimi olmayan sunucu

Gemini için dışarı çıkış her zaman gerekir. Hugging Face'e çıkış yoksa model başka bir makinede indirilip taşınır:

1. İnternetli bir makinede sistem bir kez açılır ve AI servisi "healthy" olana kadar beklenir.
2. Modelin volume'u bir dosyaya alınır:

   ```bash
   docker run --rm -v btu-asistan_ai-models:/models -v ./:/out alpine tar czf /out/ai-models.tar.gz -C /models .
   ```

3. `ai-models.tar.gz` sunucunun proje klasörüne kopyalanır ve volume'a açılır:

   ```bash
   docker run --rm -v btu-asistan_ai-models:/models -v ./:/in alpine tar xzf /in/ai-models.tar.gz -C /models
   ```

4. `.env`'de `HF_HUB_OFFLINE=1` yapılır, sistem açılır.

(Volume adı `btu-asistan_ai-models`, compose dosyasındaki proje adından gelir; `docker volume ls` ile görülebilir.)

İmajların derlenmesi de internet ister; o da yoksa imajlar internetli makinede derlenip `docker save` / `docker load` ile taşınır.

## 14. Sorun giderme

| Belirti | Bakılacak yer |
|---|---|
| Site hiç açılmıyor | `ps`'te `web` çalışıyor mu; sunucunun güvenlik duvarında 80/443 açık mı; DNS kaydı doğru mu. `TLS_MODE=auto`'da `logs web` sertifika hatası gösterir (port 80'in internetten erişilmesi gerekir) |
| Tarayıcı "güvenli değil" diyor | `TLS_MODE=internal` kalmış olabilir (yalnızca deneme içindir); `files`'ta sertifika zinciri eksik olabilir |
| `api` sürekli yeniden başlıyor | `logs api`: "Refusing to start" → `.env`'de `change-me` parola kalmış; "SigningKey field is required" → `JWT_SIGNING_KEY` boş |
| `migrate` "Exited (1)" | `logs migrate`; çoğunlukla `POSTGRES_PASSWORD` veritabanı ilk kurulduğundaki parolayla aynı değil (parola veritabanı içinde saklanır, `.env`'den sonradan değiştirmek yetmez) |
| Belgeler "Sırada" kalıyor | AI servisi model indiriyor ya da çalışmıyor: `ps`, `logs ai-service` |
| Sohbet "Şu an cevap veremiyorum" diyor | `curl …/health`'te AI servisi; `logs ai-service`'te Gemini hatası (anahtar yanlış, kota doldu) |
| Bir sınıf "çok soru sordunuz" uyarısı alıyor | Okulun tek dış adresinin arkasındaki öğrenciler sınırı paylaşır; `api.env`'de `RateLimiting__ChatPerMinute` / `ChatPerHour` artırılır. Okulun vekili varsa `TRUSTED_PROXIES` doğru mu (Bölüm 6c) |
| Gece yedeği yok | `logs backup`: "backup FAILED"; disk dolu olabilir |
