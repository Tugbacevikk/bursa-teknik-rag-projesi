import streamlit as st

def render_report_tab(rag_engine):
    """Tab 3: F3/F4 Report Inspector Blueprint."""
    st.subheader("📄 İMEP Rapor Format ve İçerik Kontrolü (F3 Ara / F4 Final Raporu)")
    st.markdown("Hazırladığınız **F3 Ara Faaliyet Raporu** veya **F4 Final Raporu** dosyanızı (TXT / PDF) yükleyin. BTÜ Asistan raporunuzu İMEP standartlarına göre analiz etsin.")
    
    uploaded_file = st.file_uploader("Rapor Dosyanızı Yükleyin", type=["txt", "pdf"])
    if uploaded_file is not None:
        file_text = uploaded_file.read().decode("utf-8", errors="ignore")
        st.success(f"**Dosya Başarıyla Yüklendi:** {uploaded_file.name} ({len(file_text)} karakter)")
        
        if st.button("🔍 Raporu İMEP Standartlarına Göre İncele"):
            with st.spinner("BTÜ Asistan raporu analiz ediyor..."):
                recent_history = st.session_state.messages[-10:] if "messages" in st.session_state else []
                analysis_query = f"Öğrencinin hazırladığı şu İMEP raporunu değerlendir: {file_text[:1500]}"
                answer_func = getattr(rag_engine, "generate_answer", rag_engine.generate_response)
                analysis_res = answer_func(
                    analysis_query,
                    chat_history=recent_history,
                    user_bolum=st.session_state.bolum,
                    user_sinif=st.session_state.sinif,
                    user_gano=st.session_state.gano,
                    detail_level=st.session_state.detail_level,
                    ai_tone=st.session_state.ai_tone,
                    language=st.session_state.app_language
                )
                st.markdown("### 📊 Rapor İnceleme Sonucu")
                st.info(analysis_res["answer"])
