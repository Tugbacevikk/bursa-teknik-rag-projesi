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
        margin-bottom: 25px;
    }
    .stChatMessage {
        border-radius: 10px;
    }
    .source-box {
        background-color: #f8f9fa;
        border-left: 4px solid #0d3b66;
        padding: 10px;
        margin-top: 5px;
        border-radius: 4px;
        font-size: 0.9rem;
    }
    .badge-team {
        background-color: #e9ecef;
        padding: 4px 10px;
        border-radius: 12px;
        font-size: 0.85rem;
        color: #333;
        display: inline-block;
        margin-right: 5px;
    }
</style>
""", unsafe_allow_html=True)

# Initialize Session State
if "messages" not in st.session_state:
    st.session_state["messages"] = [
        {
            "role": "assistant",
            "content": "Merhaba! Ben **Bursa Teknik Üniversitesi İMEP Akıllı Öğrenci Danışmanıyım**. İMEP başvuru koşulları, sigorta, devam zorunluluğu, raporlar veya notlandırma hakkında merak ettiğiniz tüm soruları sorabilirsiniz. 🎓"
        }
    ]

@st.cache_resource
def get_rag_system():
    loader = DocumentLoader(DATA_RAW_DIR)
    chunks = loader.load_documents()
    vector_store = HybridVectorStore()
    count = vector_store.index_documents(chunks)
    rag_engine = RAGEngine(vector_store)
    return rag_engine, count, len(chunks)

rag_engine, doc_count, chunk_count = get_rag_system()

# Sidebar
with st.sidebar:
    st.image("https://btu.edu.tr/img/logo.png", width=180, default_image=None)
    st.title("⚙️ İMEP RAG Paneli")
    
    st.markdown("### 📊 Veritabanı Durumu")
    st.success(f"**Yüklü Parça Sayısı:** {chunk_count}")
    st.info(f"**Vektör Dizin:** ChromaDB + BM25")

    st.markdown("---")
    st.markdown("### 🔑 API Konfigürasyonu")
    api_input = st.text_input("Gemini API Key (Opsiyonel)", value=os.getenv("GEMINI_API_KEY", ""), type="password")
    if api_input:
        os.environ["GEMINI_API_KEY"] = api_input
        rag_engine.api_key = api_input
        st.caption("✅ API Key aktif edildi.")
    else:
        st.warning("⚠️ API Key tanımlı değil. Sistem yerel referans çıkarma modunda çalışıyor.")

    st.markdown("---")
    st.markdown("### 👥 Proje Ekibi (3 Person)")
    st.markdown('<span class="badge-team">Üye 1: Veri & Chunking</span>', unsafe_allow_html=True)
    st.markdown('<span class="badge-team">Üye 2: RAG & Vector Engine</span>', unsafe_allow_html=True)
    st.markdown('<span class="badge-team">Üye 3: UI & Evaluation</span>', unsafe_allow_html=True)

    if st.button("🔄 Verileri Yeniden İndeksle"):
        st.cache_resource.clear()
        st.rerun()

# Main Header
st.markdown('<div class="main-header">Bursa Teknik Üniversitesi</div>', unsafe_allow_html=True)
st.markdown('<div class="sub-header">İşletmede Mesleki Eğitim Programı (İMEP) Akıllı Soru-Cevap Sistemi</div>', unsafe_allow_html=True)

# Quick Questions
st.markdown("##### 💡 Hızlı Sorular")
col1, col2, col3, col4 = st.columns(4)

preset_query = None
if col1.button("📌 Başvuru Şartları"):
    preset_query = "İMEP'e başvuru şartları ve GANO sınırı nedir?"
if col2.button("💰 Sigorta ve Ücret"):
    preset_query = "İMEP'te sigortayı kim öder ve maaş verilir mi?"
if col3.button("⏰ Devam Zorunluluğu"):
    preset_query = "İMEP süresi kaç haftadır ve devamsızlık sınırı nedir?"
if col4.button("📝 Rapor Teslimi"):
    preset_query = "İMEP raporu ne zaman ve nasıl teslim edilir?"

# Display Chat History
for msg in st.session_state.messages:
    with st.chat_message(msg["role"]):
        st.markdown(msg["content"])
        if "sources" in msg and msg["sources"]:
            with st.expander("📄 Kaynak Dokümanlar ve Detaylar"):
                for idx, src in enumerate(msg["sources"], 1):
                    st.markdown(f"**{idx}. {src['source']}** - *{src['header']}*")
                    st.markdown(f"```text\n{src['text']}\n```")

# Handle User Input
user_input = st.chat_input("İMEP ile ilgili sorunuzu yazın...") or preset_query

if user_input:
    # Add User message
    st.session_state.messages.append({"role": "user", "content": user_input})
    with st.chat_message("user"):
        st.markdown(user_input)

    # Generate Assistant response
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
