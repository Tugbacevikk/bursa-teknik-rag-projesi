# Değerlendirme

Aramanın ne kadar iyi çalıştığını, cevabı bilinen sorularla ölçer (PLANNING Faz 3, S8–S10). Her arama, parçalama ya da model değişikliğinden sonra çalıştırılır; sonuçlar `results/` altında ayarlarıyla birlikte saklanır ve karşılaştırılır.

Araç, uygulamanın kendi okuma, parçalama, embedding ve arama kodunu kullanır; ölçülen arama öğrencinin kullandığı aramadır. **Ayrı bir veritabanında** (`btu_asistan_eval`) çalışır, geliştirme veritabanındaki belgelere dokunmaz.

## Çalıştırmak

Postgres ve AI servisi açık olmalıdır (`docker compose up -d`). Repo kökünden:

```bash
dotnet run --project eval/BtuAsistan.Eval
```

Reranker'ı da ölçmek için AI servisi reranker'la başlatılır (ilk seferde ~2,3 GB indirir):

```bash
RERANKER_ENABLED=1 docker compose up -d ai-service
dotnet run --project eval/BtuAsistan.Eval -- --methods keyword,vector,hybrid,hybrid-rerank --label reranker
```

Sohbetin cevaplarını ölçmek için (PLANNING Faz 4, C14; AI servisinde `GEMINI_API_KEY` ve `GEMINI_MODEL` gerekir):

```bash
dotnet run --project eval/BtuAsistan.Eval -- --answers
```

Her soru, sohbet ucunun kullandığı `ChatService` üzerinden sorulur (arama + kişisel veri maskeleme + Gemini); arama yöntemleri bu modda ölçülmez. Raporlananlar: kaynaklı sorularda cevaplanan, en az bir geçerli `[n]` içeren ve yanlışlıkla reddedilen (`NotFound`) soru sayısı; her kanıt cümlesinin modele verilen parçalarda olup olmadığı ve **atıf yapılan** bir parçada olup olmadığı; cevapsız (`unanswerable`) soruların kaçının reddedildiği; süre. Gemini'nin ücretsiz katmanı ara sıra "aşırı yük" dediği için hata alan soru 15 sn sonra bir kez daha sorulur; sorular arasında 2 sn beklenir (`--answer-delay`). Cevaplar `expected_answer` ile yan yana sonuç dosyasına yazılır; **doğru olup olmadıklarına ekip bakar**, model yargıç olarak kullanılmaz. Ölçüm sırasında açılan sohbetler sonunda silinir.

Diğer seçenekler: `dotnet run --project eval/BtuAsistan.Eval -- --help` yerine geçersiz bir seçenek verildiğinde kullanım yazılır (`--candidates`, `--rerank-candidates`, `--rrf-k`, `--keyword-prefix`, `--questions`, `--reset`, `--answers`, `--answer-delay`).

Araç ilk çalıştırmada veritabanını kurar ve belgeleri işler; sonraki çalıştırmalarda değişmeyen belgeleri (aynı içerik özeti) yeniden işlemez; vektörleri başka bir modelden kalmışsa (`Embedding:Model` değişmişse) yalnızca vektörlerini yeniler. Listeden çıkarılan belgeler silinir.

## `documents.json`

Soruların dayandığı belgeler. `id`, sorulardaki `sources[].document` değeridir; `file`, bu klasöre göre yoldur.

Şu an 8 seed belge (`data/seed`) listelidir. **Okulun resmî PDF'leri** (İMEP yönergesi, akademik takvim, önlisans/lisans eğitim yönetmeliği) ekip tarafından btu.edu.tr'den indirilip `eval/documents/` altına konur ve buraya eklenir; gerçek uzunluk ve biçimdeki belgeler, küçük seed belgelerin gösteremeyeceğini gösterir.

## `questions.json`

Biçim DocAssistant'la aynıdır.

| Alan | Anlamı |
|---|---|
| `version` | Dosya biçiminin sürümü. |
| `draft` | `true`: sorular gözden geçirilmedi. Ekip gözden geçirince `false` yapılır. |
| `author` | Soruları kimin yazdığı. |
| `questions[].id` | Değişmeyen kimlik: belge kısaltması + sıra (`sss-003`); birden fazla belgeye dayananlar `x-` ile başlar. Silinen sorunun numarası yeniden kullanılmaz. |
| `questions[].type` | Aşağıdaki türlerden biri; sonuçlar türe göre ayrı raporlanır. |
| `questions[].question` | Öğrencinin soracağı biçimde. |
| `questions[].expected_answer` | Kısa ve belgeye dayalı doğru cevap (Faz 4'te cevap kalitesi için); cevapsız sorularda `null`. |
| `sources[].document` | `documents.json`'daki `id`. |
| `sources[].section` | Bölüm yolu, raporlamak için (`BÖLÜM 3: SİGORTA VE ÜCRET HAKLARI`). |
| `sources[].page` | PDF'te sayfa, raporlamak için; diğerlerinde `null`. |
| `sources[].evidence` | Cevabın geçtiği, belgeden **birebir** alınmış en fazla bir cümle. |

**Eşleştirme:** Bir kaynak, aramanın döndürdüğü parçalardan biri o belgeye aitse ve kanıt cümlesini içeriyorsa bulunmuş sayılır. Karşılaştırmada boşluklar, Markdown işaretleri (`* _ \` #`) ve büyük/küçük harf (Türkçe kurallarıyla) yok sayılır. Araç ölçmeden önce her kanıt cümlesinin belgede gerçekten geçtiğini denetler; geçmeyen varsa durur.

### Mevcut set (taslak)

57 soru, Claude tarafından 2026-11-17'de yazıldı; **ekip gözden geçirince** `draft` `false` yapılır. Sorular metne bakılarak yazıldığı için anahtar kelime aramasını kayırabilir; bunu azaltmak için kaynaklı soruların %40'ı başka sözcüklerle (`paraphrase`) yazıldı. Gözden geçirirken özellikle bakılacaklar: sorular bir öğrencinin gerçekten soracağı gibi mi, `expected_answer` doğru mu, `x-001`'deki çelişki (SSS ile yönerge farklı kişileri söylüyor) resmî kaynağa göre düzeltildi mi.

| Kısaltma | Belge (`documents.json` id) | Soru |
|---|---|---|
| `takvim` | `takvim` | 8 |
| `model` | `model` | 5 |
| `if` | `imep-formlar` | 6 |
| `esaslar` | `esaslar` | 5 |
| `sss` | `sss` | 6 |
| `yonerge` | `yonerge` | 11 |
| `oi` | `oi-formlar` | 4 |
| `tf` | `tum-formlar` | 4 |
| `x` | Birden fazla kaynak (3) ve cevapsız (5) | 8 |

### Soru türleri

| Tür | Ne sınar |
|---|---|
| `direct` | Belgedeki sözcüklerle sorulan soru. |
| `paraphrase` | Aynı bilgi başka sözcüklerle; anlam aramasını sınar. |
| `exact_term` | Sayı, kısaltma ya da özel terim (`F3`, `GANO`, `%10`); anahtar kelime aramasını sınar. |
| `short` | Arama kutusuna yazılır gibi iki üç kelime ("ara rapor teslimi"). |
| `table` | Cevabı bir tablo satırında. |
| `multi_section` | Cevabı birden fazla bölümde; kaynakların hepsi bulunmalı. |
| `unanswerable` | Belgelerde cevabı yok; Faz 4'te "belgelerde bulunamadı" denmesi beklenir. Aramada ölçülmez. |

## Ölçüler

Kaynak başına: kanıt ilk sırada mı (**ilk 1**), ilk 5'te mi, ilk 8'de mi (sohbete giden parça sayısı) ve **MRR** (bulunduğu sıranın tersi, bulunamazsa 0; ortalaması). Anahtar kelime, vektör, hibrit ve hibrit + reranker yan yana ölçülür.

Son ölçümün tablosu ve seçilen ayarlar (30 aday, k = 5, reranker kapalı): PLANNING Faz 3, adım 10. Okulun sunucusu belli olunca reranker ölçümü orada tekrarlanır (yukarıdaki reranker komutu).
