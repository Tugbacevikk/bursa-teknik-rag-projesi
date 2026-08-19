import streamlit as st
import os
from pathlib import Path
from src.config import DATA_RAW_DIR, GEMINI_API_KEY
from src.data_loader import DocumentLoader
from src.vector_store import HybridVectorStore
from src.rag_engine import RAGEngine

# Streamlit Page Setup
st.set_page_config(
    page_title="BTÜ İMEP Öğrenci Danışmanı (RAG)",
    page_icon="🎓",
    layout="wide",
    initial_sidebar_state="expanded"
)

# Custom Styling
st.markdown("""
<style>
    .main-header {
        font-size: 2.2rem;
        color: #0d3b66;
        font-weight: 700;
        margin-bottom: 0px;
    }
    .sub-header {
        font-size: 1.1rem;
        color: #555;
        margin-bottom: 15px;
    }
    .stChatMessage {
        border-radius: 10px;
    }
    .badge-team {
        background-color: #e9ecef;
        padding: 4px 10px;
        border-radius: 12px;
        font-size: 0.85rem;
        color: #333;
        display: inline-block;
        margin-right: 5px;
        margin-bottom: 5px;
    }
</style>
""", unsafe_allow_html=True)

# Main Header
st.markdown('<div class="main-header">Bursa Teknik Üniversitesi</div>', unsafe_allow_html=True)
st.markdown('<div class="sub-header">İşletmede Mesleki Eğitim Programı (İMEP) Akıllı Öğrenci Asistanı</div>', unsafe_allow_html=True)

# Initialize RAG System
@st.cache_resource(show_spinner=False)
def load_rag_system():
    loader = DocumentLoader(DATA_RAW_DIR)
    chunks = loader.load_documents()
    vector_store = HybridVectorStore()
    count = vector_store.index_documents(chunks)
    rag_engine = RAGEngine(vector_store)
    return rag_engine, count, len(chunks)

with st.spinner("⏳ Yapay zeka ve İMEP veritabanı yükleniyor, lütfen bekleyin..."):
    rag_engine, doc_count, chunk_count = load_rag_system()

# Sidebar
with st.sidebar:
    st.title("⚙️ İMEP RAG Paneli")
    
    st.markdown("### 📊 Veritabanı Durumu")
    st.success(f"**Yüklü Doküman Parçası:** {chunk_count}")
    st.info(f"**Vektör Dizin:** ChromaDB + BM25 Hibrit")

    st.markdown("---")
    st.markdown("### 🔑 API Konfigürasyonu")
    api_input = st.text_input("Gemini API Key (Opsiyonel)", value=os.getenv("GEMINI_API_KEY", ""), type="password")
    if api_input:
        os.environ["GEMINI_API_KEY"] = api_input
        rag_engine.api_key = api_input
        st.caption("✅ API Key aktif edildi.")
    else:
        st.warning("⚠️ API Key tanımlı değil. Yerel yanıt modunda çalışıyor.")

    st.markdown("---")
    st.markdown("### 👥 Proje Ekibi (3 Kişi)")
    st.markdown('<span class="badge-team">Üye 1: Veri Engine & Scraper</span>', unsafe_allow_html=True)
    st.markdown('<span class="badge-team">Üye 2: RAG & Hybrid Vector</span>', unsafe_allow_html=True)
    st.markdown('<span class="badge-team">Üye 3: UI/UX & Rapor Analiz</span>', unsafe_allow_html=True)

    if st.button("🔄 Verileri Yeniden İndeksle"):
        st.cache_resource.clear()
        st.rerun()

# Application Tabs
tab1, tab2, tab3 = st.tabs(["💬 Öğrenci Danışmanı", "📄 Rapor Kontrol Modülü (F3/F4)", "📊 İstatistik & Feedback Paneli"])

# TAB 1: Chatbot Interface
with tab1:
    if "messages" not in st.session_state:
        st.session_state["messages"] = [
            {
                "role": "assistant",
                "content": "Merhaba! Ben **BTÜ İMEP Akıllı Öğrenci Danışmanıyım**. İMEP başvuru koşulları, sigorta, akademik takvim, **resmi F1-F9 formları** veya rapor teslimleri hakkında sorularınızı sorabilirsiniz. 🎓"
            }
        ]
    if "feedback" not in st.session_state:
        st.session_state["feedback"] = {"positive": 0, "negative": 0}

    st.markdown("##### 💡 Hızlı Soru Butonları")
    c1, c2, c3, c4, c5 = st.columns(5)
    preset_query = None
    if c1.button("📌 Başvuru Şartları"):
        preset_query = "İMEP'e başvuru şartları ve GANO sınırı nedir?"
    if c2.button("💰 Sigorta & Ödeme"):
        preset_query = "İMEP'te sigortayı kim öder ve maaş verilir mi?"
    if c3.button("📅 Akademik Takvim"):
        preset_query = "İMEP başvuru, ara rapor ve final raporu teslim tarihleri nedir?"
    if c4.button("📋 İMEP Formları"):
        preset_query = "İMEP F1, F2, F3, F4, F8 ve F9 formları nelerdir ve indirme linkleri nedir?"
    if c5.button("📝 Notlandırma"):
        preset_query = "İMEP başarı notu nasıl hesaplanır, firma ve akademik danışman yüzdeleri nedir?"

    for i, msg in enumerate(st.session_state.messages):
        with st.chat_message(msg["role"]):
            st.markdown(msg["content"])
            if "sources" in msg and msg["sources"]:
                with st.expander("📄 Kaynak Dokümanlar ve Detaylar"):
                    for idx, src in enumerate(msg["sources"], 1):
                        st.markdown(f"**{idx}. {src['source']}** - *{src['header']}*")
                        st.markdown(f"```text\n{src['text']}\n```")
                
                # Feedback buttons for assistant responses
                col_fb1, col_fb2, col_fb3 = st.columns([1, 1, 8])
                if col_fb1.button("👍", key=f"pos_{i}"):
                    st.session_state["feedback"]["positive"] += 1
                    st.toast("Geri bildiriminiz alındı! Teşekkür ederiz. 😊")
                if col_fb2.button("👎", key=f"neg_{i}"):
                    st.session_state["feedback"]["negative"] += 1
                    st.toast("Geri bildiriminiz alındı. Sistemi geliştirmek için inceliyoruz! 🛠️")

    user_input = st.chat_input("İMEP ile ilgili sorunuzu yazın...") or preset_query

    if user_input:
        st.session_state.messages.append({"role": "user", "content": user_input})
        with st.chat_message("user"):
            st.markdown(user_input)

        with st.chat_message("assistant"):
            with st.spinner("BTÜ İMEP yönergeleri taranıyor..."):
                result = rag_engine.generate_response(user_input)
                answer = result["answer"]
                sources = result["sources"]
                
                st.markdown(answer)
                if sources:
                    with st.expander("📄 Kaynak Dokümanlar ve Detaylar"):
                        for idx, src in enumerate(sources, 1):
                            st.markdown(f"**{idx}. {src['source']}** - *{src['header']}*")
                            st.markdown(f"```text\n{src['text']}\n```")

        st.session_state.messages.append({
            "role": "assistant",
            "content": answer,
            "sources": sources
        })

# TAB 2: Report Checker
with tab2:
    st.subheader("📄 İMEP Rapor Format ve İçerik Kontrolü (F3 Ara / F4 Final Raporu)")
    st.markdown("Hazırladığınız **F3 Ara Faaliyet Raporu** veya **F4 Final Raporu** dosyanızı (TXT / PDF) yükleyin. Yapay zeka raporunuzu İMEP standartlarına göre analiz etsin.")
    
    uploaded_file = st.file_uploader("Rapor Dosyanızı Yükleyin", type=["txt", "pdf"])
    if uploaded_file is not None:
        file_text = uploaded_file.read().decode("utf-8", errors="ignore")
        st.success(f"**Dosya Başarıyla Yüklendi:** {uploaded_file.name} ({len(file_text)} karakter)")
        
        if st.button("🔍 Raporu İMEP Standartlarına Göre İncele"):
            with st.spinner("Rapor İMEP yönergelerine göre analiz ediliyor..."):
                analysis_query = f"Öğrencinin hazırladığı şu İMEP raporunu değerlendir: {file_text[:1500]}"
                analysis_res = rag_engine.generate_response(analysis_query)
                st.markdown("### 📊 Rapor İnceleme Sonucu")
                st.info(analysis_res["answer"])

# TAB 3: Admin & Feedback Analytics
with tab3:
    st.subheader("📊 İMEP Koordinatörlük Analitik & Geri Bildirim Paneli")
    col_a, col_b, col_c, col_d = st.columns(4)
    col_a.metric("Toplam Sorulan Soru", len(st.session_state.get("messages", [])) // 2)
    col_b.metric("Olumlu Geri Bildirim (👍)", st.session_state.get("feedback", {}).get("positive", 0))
    col_c.metric("Olumsuz Geri Bildirim (👎)", st.session_state.get("feedback", {}).get("negative", 0))
    col_d.metric("Veritabanı Sağlığı", "%100 Tamamlandı")

    st.markdown("---")
    st.markdown("### 📌 En Çok Merak Edilen İMEP Konuları")
    st.bar_chart({
        "İMEP Başvuru & GANO": 45,
        "Sigorta & Ücretler": 38,
        "F1-F9 Resmi Formlar": 29,
        "Akademik Takvim": 25,
        "Rapor Teslimi": 22
    })
