# Kod İncelemesi — 2026-11-23

Bütün depo (API, AI servisi, Angular arayüz, Docker ve belgeler) incelendi. Genel durum iyi: modüller ayrık, kararlar `PLANNING.md`'de gerekçeleriyle yazılı, güvenlik (çerez, CSRF, hız sınırı, kişisel veri maskeleme, kısıtlı veritabanı hesabı) baştan düşünülmüş, üç takımda da test var.

Bu belge üç bölümden oluşur: **düzeltilen hatalar**, **eklenenler** ve **eklenemeyen öneriler** (öncelik sırasıyla).

> **Not:** İnceleme ortamında .NET SDK'ya erişim yoktu; C# değişiklikleri eklenen CI iş akışında (`.github/workflows/ci.yml`) derlendi ve test edildi: **193/193 test geçti**, derleme hatası yok.
>
> Python (36 test) ve Angular (41 test + `ng build`) değişiklikleri burada çalıştırıldı, hepsi geçti.

---

## 1. Düzeltilen hatalar

### 1.1 Boş cevap "cevaplandı" diye kaydediliyordu (AI servisi + API)

**Sorun:** Gemini bazen hiç metin yazmadan bitirir. Örneğin güvenlik filtresi cevabı ilk kelimeden önce keser; bu durumda akışta metin yoktur. `generate_events` bu durumda yine `done` gönderiyordu. API de boş metni `Answered` olarak kaydediyordu. Sonuçta öğrenci boş bir balon görüyor, analitikte de bu soru "cevaplandı" sayılıyordu.

**Düzeltme:**
- `ai-service/app/generation.py`: Hiç metin yazılmadıysa `done` yerine `error` olayı gönderiliyor. Öğrenci "Şu an cevap veremiyorum" mesajını görüyor ve yeniden sorabiliyor.
- `backend/.../Chat/ChatService.cs`: Aynı kontrol API'de de var (savunma katmanı). Metni boş bir `AnswerFinished` geldiğinde hiçbir şey kaydedilmiyor.
- Testler: `test_an_answer_with_no_text_is_an_error_not_done` (pytest) ve `Answer_without_text_is_an_error_and_nothing_is_stored` (xUnit).

### 1.2 Hazır olmayan belgeler modele giden parça sayısını düşürüyordu (API, arama)

**Sorun:** Anahtar kelime ve vektör aramaları bütün `chunks` tablosuna bakıyor. Bu tablo, işlenmekte olan ya da az önce yeni sürümü yüklenen belgelerin parçalarını da içerebiliyor. `ChunkRetrieval` önce ilk 8 sonucu alıp sonra hazır olmayanları süzüyordu. Böyle bir parça üst sıralara girdiğinde modele 8 yerine örneğin 6 parça gidiyordu. Sayının neden düştüğü de görünmüyordu.

**Düzeltme:** `ChunkRetrieval.cs` artık birleşik listenin tamamını (en fazla 2 × `CandidateCount` = 60 parça) yüklüyor, hazır olmayanları süzüyor ve kesmeyi bundan sonra yapıyor. Normal durumda sonuç aynı kalıyor; yalnızca böyle parçalar varken fark ediyor. Daha kalıcı çözüm için bkz. öneri 3.4.

### 1.3 Akışın son olayı kaybolabiliyordu (Angular)

**Sorun:** Sunucu ya da araya giren bir vekil, akışı son olaydan sonraki boş satırı göndermeden kapatırsa `readServerSentEvents` son olayı (genellikle `done`) atıyordu. Bu olmadan cevap ekranda "yazılıyor" hâlinde kalıyordu.

**Düzeltme:** `web/src/app/api/chat.ts` akış bitince tampondaki son olayı da okuyor. Çözücünün (TextDecoder) elinde kalan baytları da boşaltıyor. Test: `still reads a last event the stream closed without its blank line`.

---

## 2. Eklenenler

### 2.1 Cevap biçimlendirmesi (Angular, `answer-format.ts`)
Model zaman zaman bu biçimleri kullanıyor; artık hepsi düzgün gösteriliyor:
- **Numaralı listeler** artık `<ol>` olarak gösteriliyor. Adımların numarası kaybolmuyor; önceden madde işaretine dönüşüyordu.
- **Başlıklar** (`### Gerekli belgeler`) kalın bir satır olarak gösteriliyor. Öğrenci artık `###` işaretlerini görmüyor. `#5 numaralı form` gibi başlık olmayan metinler olduğu gibi kalıyor.
- **Kalın yazı içindeki kaynak numaraları** (`**25 Ağustos [2]**`) artık tıklanabilen kaynak düğmesi oluyor; önceden düz `[2]` metni kalıyordu.
- Paragraf ve liste satırları için aynı şablon iki kez yazılmıştı; artık tek bir `ng-template` kullanılıyor.
- Yeni testler: numaralı liste, başlık, kalın içinde kaynak, başlık olmayan `#`.

Biçimlendirici hâlâ HTML üretmiyor; modelin yazdığı hiçbir şey sayfada kod olarak çalışamaz.

### 2.2 Sürekli entegrasyon (CI): `.github/workflows/ci.yml`
Her push'ta ve her PR'da üç iş çalışıyor:
- **API:** `dotnet build` (değerlendirme aracı dahil bütün çözüm) ve `dotnet test`. Testcontainers kendi Postgres'ini açıyor.
- **AI servisi:** `pytest`. Modeller testlerde sahte olduğu için torch ve sentence-transformers kurulmuyor; iş 1 dakikanın altında bitiyor.
- **Web:** `ng test` ve `ng build`.

### 2.3 Bağımlılık güncellemeleri: `.github/dependabot.yml`
NuGet, pip, npm, Docker imajları ve GitHub Actions için ayda bir güncelleme PR'ı açılıyor. Güncellemeler ekosistem başına gruplanıyor, böylece onlarca değil birkaç PR geliyor. Güvenlik açıkları için PR beklemeden hemen açılıyor. CI her PR'ı test ediyor.

---

## 3. Eklenemeyen öneriler

Bu maddeler ya migration (veritabanı şeması değişikliği) gerektirdiği, ya ölçüm istediği, ya da ekibin karar vermesi gerektiği için koda eklenmedi.

### Yüksek öncelik

**3.1 Belge bilgilerini dosyasız düzenleme**
Şu an bir belgenin başlığını, birimini, `sourceUrl`'sini ya da geçerlilik dönemini değiştirmenin tek yolu dosyayı yeniden yüklemek. `data/seed/sources.json`'daki 8 belgenin `sourceUrl`'si boş ve canlıya çıkmadan doldurulması gerekiyor (P14). Bu yüzden bu eksik yakında sorun çıkaracak.
- Önerilen: `PATCH /api/documents/{id}`, gövde `{ title?, unit?, kind?, sourceUrl?, validityPeriod? }`. Doğrulama için `DocumentMetadata.Validate` aynen kullanılır; değişiklik denetim kaydına (`audit_log`) yazılır.
- Parçalar yeniden işlenmeli: bağlam başlığı (`Belge: … (2026-2027) > …`) başlığı ve dönemi içeriyor. En basit yol, belgeyi `Pending` yapıp kuyruğa vermek.
- Panelde belge satırına "Bilgileri düzenle" penceresi.

**3.2 Parola değişince eski oturumların kapanması**
Hesap kapatma her istekte veritabanından kontrol ediliyor, bu iyi. Ama parola değiştiğinde ya da Admin "parolayı sıfırla" dediğinde, daha önce verilmiş JWT'ler 60 dakika boyunca geçerli kalıyor. Parolası çalınan bir hesapta saldırganın oturumu, parola sıfırlandıktan sonra da sürüyor.
- Önerilen: `users.security_stamp` sütunu (migration). Token'a claim olarak eklenir, `ActiveStaffRequirement` her istekte bu değeri karşılaştırır. Parola değişince ya da sıfırlanınca, rol değişince damga yenilenir.

**3.3 Gemini'nin bitiş nedenini kaydetmek**
`max_output_tokens` (2048) dolduğunda cevap yarıda kesiliyor ama `done` olarak kaydediliyor. Güvenlik filtresi devreye girdiğinde de nedeni hiçbir yerde görünmüyor.
- Önerilen: `chat_model.py` son parçadaki `candidates[0].finish_reason` değerini okusun. `MAX_TOKENS` ise `done` olayına `"truncated": true` eklensin, `SAFETY` ise log yazılsın. API bunu `messages`'a yazsın ve analitikte "yarım kalan cevaplar" olarak göstersin.

### Orta öncelik

**3.4 Aramalarda durum filtresi SQL'de olsun**
1.2'deki düzeltme sonucu doğru yapıyor ama hazır olmayan belgelerin parçaları hâlâ aday listesinde yer tutuyor. `KeywordChunkSearch` ve `VectorChunkSearch` sorgularına `JOIN documents d … WHERE d.status = 'Done' AND c.document_version = d.version` eklenirse bu parçalar hiç aday olmaz. Bu, hibrit sonuçları değiştirebileceği için değerlendirme aracıyla (`eval`) yeniden ölçülmeli.

**3.5 Belge işlenirken öğrenci sorusu bekliyor**
AI servisinde `/embed` tek kilitle çalışıyor (`_encode_lock`). Bir belge işlenirken API, 16 parçalık gruplar gönderiyor; bu da 16 × ~0,6 sn ≈ 10 sn eder. O sırada gelen bir öğrenci sorusunun vektörü bu grubun arkasında bekliyor.
- Seçenekler: (a) API belge işlerken daha küçük gruplar göndersin (`Embedding:BatchSize` = 4). (b) AI serviste tek metinlik istekler (sorular) kuyruğun önüne alınsın. (c) Sohbetteki vektör araması için ayrı ve kısa bir zaman aşımı konsun (şu an belge işlemeyle aynı, 120 sn), süre aşılınca arama yalnızca anahtar kelimeyle devam etsin.
- Önce ölçülmeli: bir belge yüklenirken sohbet sorusunun süresine bakılmalı.

**3.6 IPv6'da hız sınırı**
Sınır IP adresi başına uygulanıyor. IPv6'da tek bir kullanıcının elinde genellikle koca bir /64 bloğu olur ve adres değiştirerek sınırı aşabilir. Öneri: IPv6 adreslerinde bölümleme anahtarı olarak /64 öneki kullanılsın.

**3.7 Zincirli sınırlayıcının yan etkisi**
`ChatPolicy`, dakika ve saat sınırlarını zincirliyor. Saat sınırı reddettiğinde dakika penceresindeki hak da harcanmış oluyor. Pratikte etkisi küçük ama sayılar ayarlanırken bilinmeli.

### Düşük öncelik

**3.8 TC kimlik numarasının boşluklu yazımı**
`12345678901` maskeleniyor ama `123 456 789 01` ya da `123-456-789-01` maskelenmiyor. Kontrol haneleri doğrulandığı için boşluklu biçimi de aramak yanlış eşleşme riskini çok az artırır. `PersonalDataMaskerTests`'e örneklerle eklenebilir.

**3.9 Anahtar kelime aramasında tam kelime eşleşmesi**
Plandaki gözleme göre ("yemekhane saatleri" sorusu "saat" geçen parçayı buluyor), "belgelerde yok" kararı modele bırakılmış durumda. Bu doğru bir karar. Yine de soru setine ilgisiz ama tek kelimesi eşleşen sorular eklenip `unanswerable` reddi ölçülmeye devam edilmeli.

**3.10 Vektör indeksi**
Şu anki tam arama okulun ölçeğinde (binlerce parça) yeterli. Parça sayısı 50.000'i geçerse pgvector HNSW indeksi düşünülebilir; isabet kaybı `eval` ile ölçülmeli.

**3.11 Biçim ve bütçe uyarıları (önceden de vardı)**
- `npx prettier --check src` 9 dosyada uyarı veriyor; büyük olasılıkla satır sonu farkı (Windows'ta CRLF). Bir kez `npx prettier --write src` çalıştırılıp commit'lenirse ve CI'a `prettier --check` adımı eklenirse bir daha oluşmaz.
- `chat-page.scss`, Angular'ın 4 kB stil bütçesini ~400 bayt aşıyor (uyarı, hata değil). Bütçe 6 kB'a çıkarılabilir ya da ortak stiller ayrı dosyaya alınabilir.

### Belgeler ve süreç

**3.12** `PLANNING.md`'nin başındaki "Çalışma yalnızca yereldedir; GitHub'a push yapılmaz." satırı artık geçerli değil; güncellenmeli. Repo herkese açıksa `eval/results` ve `data/seed` dosyalarının da açıkta olduğu unutulmamalı. Bu dosyalarda kişisel veri yok, ama resmî olmayan metinler resmî kaynak gibi görünebilir.

**3.13** Planda bekleyen insan işleri: `sources.json`'daki `sourceUrl` ve `validityPeriod` değerlerinin doldurulması, `eval/questions.json`'ın `draft: true` durumundan çıkarılması, Faz 4 ve 5'teki onay bekleyen kararlar, okul sunucusunda Linux denemesi (Faz 6).

**3.14** Testlerin bir kısmının açıklamasında "Claude yazdı; ekip gözden geçirecek" notu var. Bu gözden geçirme yapılınca not kaldırılmalı.
