import streamlit as st
import os
from pathlib import Path

# Blueprint & Layer Imports
from config.settings import DATA_RAW_DIR, GEMINI_API_KEY
from ml.data_loader import DocumentLoader
from ml.vector_store import HybridVectorStore
from ml.rag_engine import RAGEngine

from frontend.components.onboarding import render_onboarding
from frontend.components.sidebar import render_sidebar
from frontend.tabs.chat_tab import render_chat_tab
from frontend.tabs.petition_tab import render_petition_tab
from frontend.tabs.report_tab import render_report_tab
from frontend.tabs.analytics_tab import render_analytics_tab

# Streamlit Page Setup
st.set_page_config(
    page_title="BTÜ Asistan - Öğrenci Danışmanı (RAG)",
    page_icon="🎓",
    layout="wide",
    initial_sidebar_state="expanded"
)

LOGO_PATH = Path(__file__).parent / "btu_logo.png"

# Session State Initializations
if "profile_set" not in st.session_state:
    st.session_state.profile_set = False
    st.session_state.bolum = None
    st.session_state.sinif = None
    st.session_state.gano = 2.50
    st.session_state.ogrenci_adi = ""
    st.session_state.ogrenci_no = ""

if "app_language" not in st.session_state:
    st.session_state.app_language = "Türkçe"

if "ui_theme" not in st.session_state:
    st.session_state.ui_theme = "BTÜ Kurumsal (Lacivert)"

if "ai_tone" not in st.session_state:
    st.session_state.ai_tone = "Samimi & Öğrenci Dostu"

if "detail_level" not in st.session_state:
    st.session_state.detail_level = "Detaylı & Açıklamalı"

# Theme Based Custom CSS (Safe Injection)
if st.session_state.ui_theme == "Karanlık Mod (Dark Mode)":
    st.markdown("""
    <style>
        .stApp {
            background-color: #121212 !important;
            color: #e0e0e0 !important;
        }
        .main-header {
            font-size: 2.3rem;
            color: #64b5f6 !important;
            font-weight: 800;
            margin-bottom: 0px;
        }
        .sub-header {
            font-size: 1.1rem;
            color: #bbb !important;
        }
        .profile-card {
            background-color: #1e1e1e !important;
            border-left: 4px solid #64b5f6 !important;
            color: #e0e0e0 !important;
            padding: 12px;
            border-radius: 8px;
            margin-bottom: 15px;
        }
        .stChatMessage {
            border-radius: 12px;
            background-color: #1e1e1e !important;
            color: #fff !important;
        }
    </style>
    """, unsafe_allow_html=True)
else:
    st.markdown("""
    <style>
        .main-header {
            font-size: 2.3rem;
            color: #0d3b66;
            font-weight: 800;
            margin-bottom: 0px;
            line-height: 1.2;
        }
        .sub-header {
            font-size: 1.1rem;
            color: #444;
            margin-bottom: 0px;
        }
        .stChatMessage {
            border-radius: 12px;
            box-shadow: 0 2px 6px rgba(0,0,0,0.04);
        }
        .profile-card {
            background-color: #eef4f8;
            border-left: 4px solid #0d3b66;
            padding: 12px;
            border-radius: 8px;
            margin-bottom: 15px;
        }
    </style>
    """, unsafe_allow_html=True)

# -------------------------------------------------------------
# 👤 ONBOARDING BLUEPRINT (Profil & İlk Giriş)
# -------------------------------------------------------------
if not st.session_state.profile_set:
    render_onboarding(LOGO_PATH)
    st.stop()

# -------------------------------------------------------------
# MAIN APP (Profil Seçildikten Sonra)
# -------------------------------------------------------------
col_h1, col_h2 = st.columns([1, 6])
with col_h1:
    if LOGO_PATH.exists():
        st.image(str(LOGO_PATH), width=110)
with col_h2:
    st.markdown('<div class="main-header">Bursa Teknik Üniversitesi</div>', unsafe_allow_html=True)
    subtitle = "BTÜ Asistan - Official Student AI Advisor" if st.session_state.app_language == "English" else "BTÜ Asistan - Resmi Öğrenci Akıllı Danışmanı"
    st.markdown(f'<div class="sub-header">{subtitle}</div>', unsafe_allow_html=True)

st.markdown("---")

# Initialize ML RAG System
def load_rag_system():
    loader = DocumentLoader(DATA_RAW_DIR)
    chunks = loader.load_documents()
    vector_store = HybridVectorStore()
    count = vector_store.index_documents(chunks)
    rag_engine = RAGEngine(vector_store)
    return rag_engine, count, len(chunks)

if "rag_engine" not in st.session_state:
    st.cache_resource.clear()
    rag_engine, doc_count, chunk_count = load_rag_system()
    st.session_state.rag_engine = rag_engine
    st.session_state.chunk_count = chunk_count

rag_engine = st.session_state.rag_engine
chunk_count = st.session_state.chunk_count

# -------------------------------------------------------------
# SIDEBAR BLUEPRINT
# -------------------------------------------------------------
render_sidebar(LOGO_PATH, rag_engine, chunk_count)

# -------------------------------------------------------------
# APPLICATION TABS BLUEPRINTS
# -------------------------------------------------------------
tab1, tab2, tab3, tab4 = st.tabs([
    "💬 BTÜ Asistan Sohbet / BTU Assistant Chat", 
    "📝 Dilekçe Oluşturucu / Petition Generator", 
    "📄 Rapor Müfettişi / Report Inspector", 
    "📊 Analiz ve Geri Bildirim / Analytics"
])

with tab1:
    render_chat_tab(rag_engine)

with tab2:
    render_petition_tab()

with tab3:
    render_report_tab(rag_engine)

with tab4:
    render_analytics_tab()
