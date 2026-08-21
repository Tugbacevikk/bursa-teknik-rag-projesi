# 🎓 Bursa Teknik Üniversitesi (BTÜ) İMEP & Öğrenci Danışmanı RAG Sistemi (BTÜ Asistan)

Bu proje, **BTÜ İşletmede Mesleki Eğitim Programı (İMEP)** ve üniversite öğrencilerine yönelik başvuru, sigorta, devamsızlık, notlandırma, akademik takvim ve resmi form teslim süreçleriyle ilgili soruları yanıtlayan **RAG (Retrieval-Augmented Generation)** tabanlı yapay zeka asistanıdır.

---

## 🛠️ Kurulum ve Çalıştırma

### 1. Yerel Olarak Çalıştırma (Python)
```bash
pip install -r requirements.txt
streamlit run app.py
```
Uygulama tarayıcınızda `http://localhost:8501` adresinde açılacaktır.

### 2. REST API Servisini Başlatma
```bash
python api.py
```
REST API `http://localhost:8000/api/ask` adresinde hizmet verir.

### 3. Docker İle Tek Komutta Çalıştırma (Production)
```bash
docker-compose up --build
```
- **Streamlit Web UI:** `http://localhost:8501`
- **FastAPI Servisi:** `http://localhost:8000`

---

## 🌐 Web Sitesine Yüzen Sohbet Baloncuğu (Chat Widget) Entegrasyonu

Üniversitenin veya İMEP'in resmi web sitesinin `<body>` etiketinin altına tek satırlık kodu ekleyerek yüzen sohbet balonunu aktif edebilirsiniz:

```html
<script src="static/widget.js"></script>
```

Canlı önizleme ve test için `widget_demo.html` dosyasını tarayıcınızda açabilirsiniz.

---

## 📁 Proje Yapısı

```
imep/
├── data/
│   ├── raw/                 # BTÜ İMEP PDF, TXT ve DOCX resmi dokümanları (46+ Form, Yönergeler)
│   └── processed/           # Ön işlenmiş veriler
├── src/
│   ├── config.py            # Ayarlar ve model parametreleri
│   ├── data_loader.py       # Doküman parçalama (Smart Chunking)
│   ├── vector_store.py      # ChromaDB + BM25 + Re-Ranker (Cross-Encoder)
│   ├── rag_engine.py        # BTÜ Asistan System Prompt ve Gemini LLM Zinciri
│   ├── pdf_generator.py     # Otomatik BTÜ Dilekçe & Form Üretici (PDF)
│   ├── audio_utils.py       # Türkçe Seslendirme (Text-to-Speech)
│   ├── btu_deep_web_crawler.py # BTÜ Resmi Web Tarayıcısı
│   └── btu_all_forms_scraper.py # BTÜ Dilekçe & Form Bağlantıları Tarayıcısı
├── static/
│   └── widget.js            # BTÜ Web Sitelerine gömülebilir Yüzen Chatbot Widget'ı
├── widget_demo.html         # Yüzen Chatbot Widget Canlı Önizleme Sayfası
├── Dockerfile               # Docker Konteyner Dosyası
├── docker-compose.yml       # Docker Compose Servis Yapılandırması
├── api.py                   # FastAPI REST API Uç Noktası
├── app.py                   # Streamlit sohbet ve öğrenci paneli
├── requirements.txt         # Gereksinimler
└── README.md
```


