# Gelebilecek Özellikler — BTÜ Asistan

Bu belge, sistemin Faz 6'dan sonra büyüyebileceği yönleri toplar. Kaynakları:
- `PLANNING.md`'deki "Kapsam dışı (sonraya)" maddeleri ve açık kararlar (A3, D1),
- eski prototipte olup yeni sisteme alınmayan özellikler (`legacy/`),
- kod incelemesinde görülen eksikler (`KOD_INCELEME.md`, bölüm 3).

Hiçbiri henüz onaylanmış değildir. Ekip seçtiklerini `PLANNING.md`'ye yeni bir faz olarak taşır; her biri için önce karar tablosu yazılır, sonra yapılır (mevcut çalışma biçimi).

**Efor:** K = küçük (1–2 gün), O = orta (yaklaşık 1 hafta), B = büyük (birkaç hafta).
**Öncelik:** 🔴 canlıya çıkmadan önce ya da hemen sonra · 🟠 ilk dönem içinde · 🟢 ileride.

## Özet tablo

| # | Özellik | Alan | Öncelik | Efor |
|---|---------|------|---------|------|
| 1.1 | Belge bilgilerini dosyasız düzenleme | Personel paneli | 🔴 | K |
| 1.2 | Geçerlilik süresi dolan belgelerin uyarısı ve aramadan düşmesi | Belgeler | 🔴 | O |
| 1.3 | Belge onay akışı (Editör yükler, Admin onaylar) | Belgeler | 🟠 | O |
| 1.4 | Eski sürümlerin arşivi ve geri alma | Belgeler | 🟢 | O |
| 1.5 | Taranmış PDF için OCR | Belgeler | 🟠 | O |
| 1.6 | Tablo algılama (PDF/DOCX) | Belgeler | 🟠 | O |
| 1.7 | Okul sitesinden belge değişikliği takibi | Belgeler | 🟢 | B |
| 2.1 | Takip sorularını geçmişe göre yeniden yazma | Sohbet | 🟠 | O |
| 2.2 | "İlgili birime ulaş" kartı | Sohbet | 🟠 | K |
| 2.3 | Önerilen sorular ve sık sorulanlar | Sohbet | 🟠 | K |
| 2.4 | Kaynak önizlemesi (sayfa/bölüm metni) | Sohbet | 🟢 | O |
| 2.5 | Cevabı paylaşma ve yazdırma | Sohbet | 🟢 | K |
| 2.6 | Erişilebilirlik ve İngilizce arayüz | Sohbet | 🟠 | O |
| 3.1 | Cevapsız sorulardan "eksik belge" listesi | Analitik | 🔴 | O |
| 3.2 | Benzer soruları gruplama | Analitik | 🟠 | O |
| 3.3 | Analitiği CSV/Excel olarak dışa aktarma | Analitik | 🟠 | K |
| 3.4 | Haftalık özet e-postası | Analitik | 🟢 | K |
| 3.5 | Panelden değerlendirme çalıştırma | Kalite | 🟢 | O |
| 4.1 | "Parolamı unuttum" ve e-postayla geçici parola | Hesaplar | 🟠 | O |
| 4.2 | Okulun SSO / LDAP girişi | Hesaplar | 🟢 | B |
| 4.3 | Oturumların parola değişince kapanması | Güvenlik | 🔴 | K |
| 5.1 | Dilekçe / form doldurma yardımcısı | Eski özellik | 🟢 | B |
| 5.2 | Telegram botu | Eski özellik | 🟢 | O |
| 5.3 | Sesli soru ve sesli cevap | Eski özellik | 🟢 | O |
| 5.4 | İMEP rapor ön inceleme | Eski özellik | 🟢 | B |
| 6.1 | İMEP Atama Sistemi ile bağlantı | Entegrasyon | 🟢 | B |
| 6.2 | Duyuru akışı | Entegrasyon | 🟢 | O |
| 7.1 | Yerel sohbet modeli (Gemini'siz çalışma) | Altyapı | 🟠 | B |
| 7.2 | İzleme ve uyarılar | Altyapı | 🟠 | O |
| 7.3 | Yedeğin sunucu dışına kopyalanması | Altyapı | 🟠 | K |
| 7.4 | Otomatik dağıtım | Altyapı | 🟢 | O |

---

## 1. Belgeler ve personel paneli

### 1.1 Belge bilgilerini dosyasız düzenleme 🔴 K
**Neden:** `sources.json`'daki 8 belgenin `sourceUrl`'si boş. Bunları ya da başlık, birim, dönem bilgisini düzeltmenin şu anki tek yolu dosyayı yeniden yüklemek.
**Nasıl:** `PATCH /api/documents/{id}` ucu (başlık, birim, tür, `sourceUrl`, dönem) ve panelde "Bilgileri düzenle" penceresi. Bağlam başlığı (`Belge: … (2026-2027) > …`) başlık ve dönemi içerdiği için belge yeniden işlenir. Değişiklik denetim kaydına yazılır.

### 1.2 Geçerlilik süresi dolan belgeler 🔴 O
**Neden:** "2025-2026 Akademik Takvimi" bir sonraki yıl da aramada çıkarsa öğrenci yanlış tarihe göre hareket eder. Bu, sistemin verebileceği en zararlı yanlış cevaptır.
**Nasıl:**
- Belgeye `valid_until` (tarih) alanı eklenir.
- Süresi yaklaşan belgeler panelde uyarıyla görünür; isteğe bağlı olarak ilgili personele e-posta gider.
- Süresi dolan belge ya aramadan düşer ya da kaynak kartında "Bu belge 2025-2026 dönemine aittir" uyarısıyla gösterilir. Hangisinin seçileceği ekibin kararıdır.

### 1.3 Belge onay akışı 🟠 O
**Neden:** Şu an her Editör'ün yüklediği belge doğrudan öğrencilere açılıyor. Okulda "resmî bilgiyi kim yayınladı?" sorusu önemli.
**Nasıl:** Yeni durum: `AwaitingApproval`. Admin belgeyi panelde arama ekranıyla deneyip onaylar ya da gerekçe yazarak reddeder; her iki işlem de denetim kaydına yazılır.

### 1.4 Eski sürümlerin arşivi 🟢 O
**Neden:** Yeni sürüm yüklenince eski dosya kayboluyor. "Geçen yıl yönergede ne yazıyordu?" sorusuna cevap verilemiyor, yanlış yüklenen bir sürüm de geri alınamıyor.
**Nasıl:** `document_files` tablosunda sürüm numarasıyla saklama. Aramaya yalnızca güncel sürüm girer. Panelde sürüm geçmişi ve "bu sürüme dön" düğmesi.

### 1.5 Taranmış PDF için OCR 🟠 O
**Neden:** Okulun bazı imzalı belgeleri taranmış PDF; bunlar şu an `Failed` oluyor.
**Nasıl:** AI servisine `/ocr` ucu (Tesseract `tur` dil paketi ya da PaddleOCR). OCR'dan gelen metin daha az güvenilir olduğu için panelde "OCR ile okundu" etiketi ve parçaları gözden geçirme ekranı.

### 1.6 Tablo algılama 🟠 O
**Neden:** Takvimler ve ücret tabloları satır satır paragrafa dönüşüyor. "Bahar dönemi ders kayıt tarihi" gibi sorular tablodan gelen bilgiye dayanıyor.
**Nasıl:** DOCX tabloları zaten yapısal olarak okunabiliyor. PDF için tablo çıkarma aracı (ör. Camelot) kullanılabilir. Tablo, Markdown tablosu olarak tek parça yapılır ve başlık satırı her parçada tekrarlanır. `eval`'e `table` türü sorular eklenir.

### 1.7 Okul sitesinden belge değişikliği takibi 🟢 B
**Neden:** imep.btu.edu.tr'deki bir sayfa ya da PDF değiştiğinde sistem bunu bilmiyor. Eski prototipte bir tarayıcı (`legacy/src/scraper.py`) vardı.
**Nasıl:** Otomatik içe aktarma yerine yalnızca **haber verme**: `sourceUrl`'si dolu belgelerin adresi günde bir kez indirilir, içerik özeti değişmişse panelde "Kaynakta yeni sürüm olabilir" uyarısı çıkar. Belgeyi personel yükler; böylece onaysız bilgi sisteme girmez.

---

## 2. Öğrenci sohbeti

### 2.1 Takip sorularını yeniden yazma 🟠 O
**Neden:** Yalnızca son soru aranıyor (C4). "Peki bahar döneminde?" gibi bir takip sorusunda arama neyin sorulduğunu bilmiyor.
**Nasıl:** Geçmiş varsa, aramadan önce modelden soruyu tek başına anlaşılır hâle getirmesi istenir ("İMEP başvuru tarihi bahar döneminde ne zaman?"). Bu soru başına bir Gemini çağrısı daha demektir; etkisi `eval`'e eklenecek çok adımlı sorularla ölçülmeli.

### 2.2 "İlgili birime ulaş" kartı 🟠 K
**Neden:** "Bulunamadı" ve kişisel durum sorularında (not, GANO) öğrenciye yalnızca "birime başvurun" deniyor.
**Nasıl:** Birimler için küçük bir tablo (ad, e-posta, telefon, çalışma saatleri, web sayfası); Admin panelden düzenler. Cevap "bulunamadı" olduğunda ya da kaynaklar tek bir birimi gösterdiğinde altta o birimin kartı görünür.

### 2.3 Önerilen sorular ve sık sorulanlar 🟠 K
**Neden:** Sohbet ekranındaki örnek sorular sabit. Analitikteki "en sık sorulan sorular" listesi zaten var.
**Nasıl:** Personel bu listeden seçtiği soruları sohbet ekranında öne çıkarır. Dönemlere göre değişebilir; örneğin İMEP başvuru haftasında başvuru soruları öne alınır.

### 2.4 Kaynak önizlemesi 🟢 O
**Neden:** Kaynak kartı belge adını ve bölümü gösteriyor ama öğrenci bilginin geçtiği metni göremiyor. `sourceUrl` de çoğu zaman sayfaya değil belgenin tamamına gidiyor.
**Nasıl:** Karta tıklanınca o parçanın metni açılır; PDF ise ilgili sayfanın görüntüsü de gösterilebilir. Öğrenciye iç kimlik gösterilmemesi kuralı korunur: istek sohbet ve kaynak numarasıyla yapılır.

### 2.5 Cevabı paylaşma ve yazdırma 🟢 K
"Bağlantıyı kopyala" (sohbet kimliğiyle; C3'e göre kimliği bilen sohbeti okuyabiliyor) ve yazdırma görünümü. Kimliğin paylaşılması sohbeti başkasına da açacağı için ekranda uyarı gösterilmeli.

### 2.6 Erişilebilirlik ve İngilizce arayüz 🟠 O
- Model zaten sorunun dilinde cevap veriyor (C7). Değişim öğrencileri için arayüz metinleri de İngilizce olmalı (Angular i18n).
- WCAG 2.1 AA denetimi: klavyeyle kullanım, ekran okuyucuda akış hâlindeki cevabın duyurulması (`aria-live`), renk kontrastı.

---

## 3. Analitik ve kalite

### 3.1 Cevapsız sorulardan "eksik belge" listesi 🔴 O
**Neden:** Analitik cevapsız soruları tek tek listeliyor; personelin bundan "hangi belgeyi eklemeliyim?" sonucunu kendisi çıkarması gerekiyor.
**Nasıl:** Cevapsız sorular gruplanır (3.2) ve her grup bir "eksik konu" olur: soru sayısı, örnek sorular, "belge ekledim" işareti. İşaretlenen konudaki sorular `eval`'e aday soru olarak önerilir.

### 3.2 Benzer soruları gruplama 🟠 O
**Neden:** "İMEP ne zaman başlıyor?" ile "imep başlangıç tarihi" iki ayrı satır sayılıyor; en sık sorulanlar listesi bu yüzden dağınık.
**Nasıl:** Sorular zaten bge-m3 ile vektöre çevriliyor. Benzerlik eşiğiyle gece çalışan basit bir kümeleme yapılır; kümeye en merkezdeki soru ad olarak verilir. (Plandaki "anlam benzerliğiyle soru gruplama" maddesi.)

### 3.3 Dışa aktarma 🟠 K
Analitik listelerinin CSV/Excel olarak indirilmesi, böylece birim raporlarına eklenebilir. Sorular maskeli olduğu için yeni bir kişisel veri riski yok; yine de dosyaya "90 gün saklama" notu yazılır.

### 3.4 Haftalık özet e-postası 🟢 K
Personele her pazartesi: soru sayısı, cevaplanma oranı, 👎 sayısı, yeni eksik konular. E-posta altyapısı 4.1 ile birlikte kurulur.

### 3.5 Panelden değerlendirme çalıştırma 🟢 O
**Neden:** `eval` aracı şu an komut satırından çalışıyor. Belge eklendikten sonra arama kalitesinin düşmediğini personelin görebilmesi gerekiyor.
**Nasıl:** Admin panelde "Değerlendirmeyi çalıştır" düğmesi (arka plan işi). Sonuçlar önceki çalışmayla karşılaştırılarak gösterilir. Değerlendirme kendi veritabanında çalıştığı için canlı veriye dokunmaz.

---

## 4. Hesaplar ve güvenlik

### 4.1 "Parolamı unuttum" 🟠 O
Okulun SMTP sunucusuyla tek kullanımlık ve kısa süreli sıfırlama bağlantısı. Kullanılmış ya da süresi dolmuş bağlantı reddedilir, istekler hız sınırına tabidir. Bilinmeyen e-posta için de aynı yanıt verilir (girişteki gibi). Şu an parolasını unutan Admin'e başvuruyor.

### 4.2 Okulun SSO / LDAP girişi 🟢 B
Açık karar A2. Okulun bir kimlik altyapısı varsa personel girişi oraya bağlanır (OpenID Connect ya da LDAP). Rol ve hesap kapatma yine bu sistemde kalır.

### 4.3 Oturumların parola değişince kapanması 🔴 K
`KOD_INCELEME.md` 3.2: `security_stamp` sütunu. Parola değişince, sıfırlanınca ya da rol değişince eski token'lar geçersiz olur. Küçük bir iş ama güvenlik açısından önemli.

---

## 5. Eski prototipten gelen özellikler (D1)

Bunlar Faz 5'e alınmadı ve "ileride ayrı ayrı değerlendirilir" kararı verildi. Her biri için ilk soru: okul bunu istiyor mu, kişisel veri riski ne?

### 5.1 Dilekçe / form doldurma yardımcısı 🟢 B
Öğrenci sohbette "staj erteleme dilekçesi" der; sistem ilgili resmî formu bulur, alanları sorar ve doldurulmuş PDF'i indirir.
**Risk:** Ad, numara gibi kişisel veriler gerekir. Bu veriler **yalnızca tarayıcıda** kalmalı: PDF tarayıcıda üretilir, ne Gemini'ye ne veritabanına gider. Form şablonları personel tarafından yüklenir ve alanları işaretlenir.

### 5.2 Telegram botu 🟢 O
API'nin aynı sohbet hattını kullanan ince bir istemci. Telegram kullanıcı kimliği sohbet kimliğine eşlenir; hız sınırı IP yerine kullanıcı başına uygulanır. Sorular Telegram sunucularından da geçeceği için aydınlatma metni güncellenmeli.

### 5.3 Sesli soru ve sesli cevap 🟢 O
Tarayıcıda mikrofonla soru: AI servisinde yerel faster-whisper ile Türkçe konuşmayı yazıya çevirme. Cevabı seslendirme: Piper TTS (Türkçe ses), cevap tamamlanınca. Ses dosyası saklanmaz, yalnızca metin saklanır. Erişilebilirlik için de değerli (2.6).

### 5.4 İMEP rapor ön inceleme 🟢 B
Öğrenci staj raporunu yükler; sistem yönergedeki biçim kurallarına göre eksikleri listeler (bölümler, sayfa sayısı, imza sayfası). **Not vermez**, yalnızca kontrol listesi çıkarır (C7'deki "kişisel durumu değerlendirme" kuralı). Rapor saklanmaz. Rapor içeriği Gemini'ye gideceği için öğrencinin açık onayı alınmalı ya da yerel model kullanılmalı (7.1).

---

## 6. Entegrasyonlar

### 6.1 İMEP Atama Sistemi ile bağlantı 🟢 B
BTÜ Asistan, İMEP yerleştirme sürecini otomatikleştiren atama sisteminin öğrenci destek tarafı olarak düşünülmüştü.
- **İlk adım (kişisel veri yok):** Atama sürecinin genel bilgileri (takvim, kurallar, kontenjan açıklamaları) belge olarak yüklenir; öğrenciler sürecin nasıl işlediğini sorar.
- **İkinci adım (kimlik gerekir):** "Benim yerleştirme sonucum ne?" sorusu, öğrencinin okul hesabıyla giriş yapmasını gerektirir (4.2'ye bağlı). Bu, anonim sohbet kararını (C1) değiştirir; ayrı bir faz ve KVKK değerlendirmesi ister.

### 6.2 Duyuru akışı 🟢 O
Öğrenci İşleri ve İMEP duyuruları kısa ömürlü belgeler olarak eklenir (tür: `Announcement`, bitiş tarihiyle; 1.2). Sohbet ekranında "Son duyurular" şeridi gösterilir; süresi dolan duyuru kendiliğinden kalkar.

---

## 7. Altyapı ve işletme

### 7.1 Yerel sohbet modeli 🟠 B
**Neden:** Sorular Gemini'ye gidiyor (C10); okul verinin yurt dışına çıkmasını istemeyebilir; kota ve maliyet de dışa bağımlı.
**Nasıl:** Plandaki karar 11 sayesinde AI servisinde tek yeni sınıf yeter: `ChatModel`'in Ollama ya da vLLM üzerinden çalışan bir gerçeklemesi (ör. Türkçede iyi, 7–14 milyar parametreli bir açık model). Okul sunucusunda GPU yoksa hız yetmeyebilir; karar `eval --answers` ile cevap kalitesi ve süresi ölçülerek verilmeli. Gemini yedek sağlayıcı olarak kalabilir.

### 7.2 İzleme ve uyarılar 🟠 O
`/health` zaten var. Uptime Kuma gibi basit bir araçla "AI servisi 5 dakikadır yanıt vermiyor", "disk %90 dolu", "gece yedeği alınmadı" uyarıları e-posta ya da Telegram ile gönderilir. Prometheus gibi büyük bir sistem gerekmez.

### 7.3 Yedeğin sunucu dışına kopyalanması 🟠 K
Gece yedeği şu an aynı sunucuda (E8). Sunucu bozulursa yedek de gider. Okul BT'sinin depolama alanına şifreli kopya gönderilmeli (ör. `restic`); yedekte öğrenci soruları olduğu için şifreleme şart.

### 7.4 Otomatik dağıtım 🟢 O
CI artık var (`.github/workflows/ci.yml`). Bir sonraki adım: `main`'e birleştirilen sürüm için imajların derlenip kayıt alanına (ör. GitHub Container Registry) gönderilmesi. Sunucuda `docker compose pull && up -d` ile güncelleme yapılır; migration zaten kendiliğinden uygulanıyor (E6).

---

## Önerilen sıra

1. **Canlıya çıkış için:** 1.1 → 4.3 → 1.2 → `sources.json` ve soru setinin tamamlanması.
2. **İlk dönem:** 3.1 + 3.2 (eksik belgeleri bulmak), 2.2, 2.3, 7.2, 7.3.
3. **İkinci dönem:** 2.1, 1.3, 1.5/1.6, 4.1, 2.6.
4. **Ayrı kararlar gerektirenler:** 5.x, 6.1, 7.1. Her biri için önce okulla görüşülmeli ve kişisel veri değerlendirmesi yapılmalı.
