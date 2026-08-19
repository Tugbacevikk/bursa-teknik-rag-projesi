# 🎓 Bursa Teknik Üniversitesi (BTÜ) İMEP Öğrenci Danışmanı RAG Sistemi

Bu proje, **BTÜ İşletmede Mesleki Eğitim Programı (İMEP)** öğrencilerine yönelik başvuru, sigorta, devamsızlık, notlandırma ve rapor teslim süreçleriyle ilgili soruları yanıtlayan **RAG (Retrieval-Augmented Generation)** tabanlı yapay zeka asistanıdır.

---

## 🛠️ Kurulum ve Çalıştırma

### 1. Bağımlılıkların Yüklenmesi
```bash
pip install -r requirements.txt
```

### 2. Ortam Değişkenleri (.env)
Kök dizinde `.env` dosyası oluşturup Google Gemini API anahtarınızı ekleyin:
```env
GEMINI_API_KEY=your_gemini_api_key_here
```
*(API anahtarı girilmediğinde sistem yine çalışır ve yerel referans çıkarma moduna geçer).*

### 3. Uygulamayı Başlatma (Streamlit Arayüzü)
```bash
streamlit run app.py
```
Uygulama tarayıcınızda `http://localhost:8501` adresinde açılacaktır.

---

## 📁 Proje Yapısı

```
imep/
├── data/
│   ├── raw/                 # BTÜ İMEP PDF, TXT ve DOCX resmi dokümanları
│   └── processed/           # Ön işlenmiş veriler
├── src/
│   ├── config.py            # Ayarlar ve model parametreleri
│   ├── data_loader.py       # Doküman parçalama (Smart Chunking)
│   ├── vector_store.py      # ChromaDB + BM25 Hibrit Vektör Arama (RRF)
│   └── rag_engine.py        # System Prompt ve Gemini LLM Zinciri
├── tests/
│   └── test_rag.py          # Otomatik birim ve RAG testleri
├── app.py                   # Streamlit kullanıcı sohbet arayüzü
├── requirements.txt         # Gereksinimler
└── README.md
```

---


---

## 🧪 Testleri Çalıştırma
```bash
python tests/test_rag.py
```
