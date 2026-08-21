import streamlit as st
import os
from pathlib import Path

def render_sidebar(logo_path: Path, rag_engine, chunk_count: int):
    """Sol Menü (Sidebar) Blueprint Modülü."""
    with st.sidebar:
        if logo_path.exists():
            st.image(str(logo_path), width=150)
        st.title("⚙️ BTÜ Asistan Paneli")
        
        st.markdown("### 🌐 Dil / Language")
        selected_lang = st.selectbox(
            "Uygulama Dili / App Language",
            ["Türkçe", "English"],
            index=0 if st.session_state.app_language == "Türkçe" else 1,
            key="sb_app_language_selector"
        )
        if selected_lang != st.session_state.app_language:
            st.session_state.app_language = selected_lang
            if "messages" in st.session_state:
                del st.session_state["messages"]
            st.rerun()

        is_en = (st.session_state.app_language == "English")

        st.markdown("---")
        st.markdown("### 👤 " + ("Student Profile" if is_en else "Öğrenci Profili"))
        st.markdown(f'<div class="profile-card"><b>{"Dept" if is_en else "Bölüm"}:</b> {st.session_state.bolum}<br><b>{"Grade" if is_en else "Sınıf"}:</b> {st.session_state.sinif}<br><b>GPA:</b> {st.session_state.gano:.2f}</div>', unsafe_allow_html=True)
        if st.button("✏️ " + ("Change Profile" if is_en else "Profili Değiştir")):
            st.session_state.profile_set = False
            st.rerun()

        st.markdown("---")
        st.markdown("### 🎨 " + ("Preferences" if is_en else "Arayüz & Yapay Zeka Tercihleri"))
        
        # UI Theme
        ui_theme_choice = st.selectbox(
            "Theme / Tema",
            ["BTÜ Kurumsal (Lacivert)", "Karanlık Mod (Dark Mode)"],
            key="ui_theme_selector"
        )
        if ui_theme_choice != st.session_state.ui_theme:
            st.session_state.ui_theme = ui_theme_choice
            st.rerun()

        # AI Tone
        st.session_state.ai_tone = st.selectbox(
            "AI Tone / Konuşma Tonu",
            ["Samimi & Öğrenci Dostu", "Resmi & Akademik"],
            key="ai_tone_selector"
        )

        # Detail Level
        st.session_state.detail_level = st.radio(
            "Detail Level / Detay Düzeyi",
            ["Detaylı & Açıklamalı", "Kısa & Öz"],
            key="detail_level_selector"
        )

        st.markdown("---")
        st.markdown("### 📊 " + ("Database Status" if is_en else "Veritabanı Durumu"))
        st.success(f"**{'Loaded Chunks' if is_en else 'Yüklü Doküman Parçası'}:** {chunk_count}")
        st.info(f"**{'Vector Index' if is_en else 'Vektör Dizin'}:** ChromaDB + BM25 + Re-Ranker")

        st.markdown("---")
        st.markdown("### 🔑 " + ("API Config" if is_en else "API Konfigürasyonu"))
        api_input = st.text_input("Gemini API Key (" + ("Optional" if is_en else "Opsiyonel") + ")", value=os.getenv("GEMINI_API_KEY", ""), type="password")
        if api_input:
            os.environ["GEMINI_API_KEY"] = api_input
            rag_engine.api_key = api_input
            rag_engine.get_client()
            st.caption("✅ Gemini API Active.")
        else:
            st.warning("⚠️ API Key optional. Offline mode ready.")

        st.markdown("---")
        # Export Chat History
        if "messages" in st.session_state and len(st.session_state.messages) > 1:
            st.markdown("### 📥 " + ("Chat History" if is_en else "Sohbet İşlemleri"))
            export_text = f"# BTU Assistant History ({st.session_state.bolum} - {st.session_state.sinif} - GPA: {st.session_state.gano:.2f})\n\n"
            for msg in st.session_state.messages:
                role = "Student" if msg["role"] == "user" else "BTU Assistant"
                export_text += f"### {role}:\n{msg['content']}\n\n---\n"
            
            st.download_button(
                label="📄 " + ("Download Chat (.md)" if is_en else "Sohbeti İndir (.md)"),
                data=export_text,
                file_name="btu_assistant_chat_history.md",
                mime="text/markdown"
            )
            if st.button("🧹 " + ("Clear History" if is_en else "Sohbet Geçmişini Temizle")):
                st.session_state.messages = []
                st.rerun()

        st.markdown("---")
        if st.button("🔄 " + ("Re-index Data" if is_en else "Verileri Yeniden İndeksle")):
            st.cache_resource.clear()
            if "rag_engine" in st.session_state:
                del st.session_state["rag_engine"]
            st.rerun()
