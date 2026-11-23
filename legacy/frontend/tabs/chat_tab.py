import streamlit as st
from backend.services.tts_service import text_to_speech_tr

def render_chat_tab(rag_engine):
    """Tab 1: Chatbot & Audio Voice Player Blueprint."""
    is_en = (st.session_state.app_language == "English")

    if "messages" not in st.session_state or not st.session_state.messages:
        if is_en:
            welcome_text = f"Hello! I am **BTU Assistant**. As a **{st.session_state.bolum} ({st.session_state.sinif}, GPA: {st.session_state.gano:.2f})** student, you can ask me anything about IMEP requirements, insurance, academic calendar, internships, official F1-F9 forms, or university regulations. 🎓"
        else:
            welcome_text = f"Merhaba! Ben **BTÜ Asistan**. **{st.session_state.bolum} ({st.session_state.sinif}, GANO: {st.session_state.gano:.2f})** öğrencisi olarak İMEP başvuru koşulları, sigorta, akademik takvim, staj, **resmi F1-F9 formları** veya rapor teslimleri hakkında merak ettiğiniz tüm detayları sorabilirsiniz. 🎓"
        
        st.session_state.messages = [
            {
                "role": "assistant",
                "content": welcome_text
            }
        ]

    if "feedback" not in st.session_state:
        st.session_state["feedback"] = {"positive": 0, "negative": 0}

    st.markdown("##### 💡 " + ("Quick Topic Suggestions" if is_en else "Hızlı Konu Önerileri"))
    c1, c2, c3, c4, c5 = st.columns(5)
    preset_query = None
    if c1.button("📌 " + ("IMEP Requirements" if is_en else "İMEP Gereksinimleri")):
        preset_query = f"What are the IMEP application requirements for a {st.session_state.sinif} student with GPA {st.session_state.gano:.2f}?" if is_en else f"GANO ortalamam {st.session_state.gano:.2f} iken {st.session_state.sinif} öğrencisi olarak İMEP'e başvuru şartlarım nedir?"
    if c2.button("💰 " + ("Insurance & Salary" if is_en else "Sigorta ve Maaş")):
        preset_query = "Who pays the insurance during IMEP and are students paid?" if is_en else "İMEP'te sigortayı kim öder ve maaş verilir mi?"
    if c3.button("📅 " + ("Academic Calendar" if is_en else "Akademik Takvim")):
        preset_query = "What are the key dates for IMEP applications and report submissions?" if is_en else "İMEP başvuru, ara rapor ve final raporu teslim tarihleri nedir?"
    if c4.button("📋 " + ("Official Forms" if is_en else "Resmi Formlar")):
        preset_query = "What are the official IMEP F1 to F9 forms and where can I download them?" if is_en else "İMEP F1, F2, F3, F4, F8 ve F9 formları nelerdir ve indirme linkleri nedir?"
    if c5.button("🎓 " + ("General BTU Q&A" if is_en else "Genel BTÜ Soru-Cevap")):
        preset_query = "Can you provide general information about BTU student regulations?" if is_en else "Bursa Teknik Üniversitesi öğrenci işleri ve yönetmelikleri hakkında genel bilgi verir misin?"

    st.markdown("---")

    # Display chat history
    for i, msg in enumerate(st.session_state.messages):
        with st.chat_message(msg["role"]):
            st.markdown(msg["content"])
            if "sources" in msg and msg["sources"]:
                with st.expander("📄 " + ("Source Documents & Details" if is_en else "Kaynak Dokümanlar ve Detaylar")):
                    for idx, src in enumerate(msg["sources"], 1):
                        st.markdown(f"**{idx}. {src['source']}** - *{src['header']}*")
                        st.markdown(f"```text\n{src['text']}\n```")
                
                # Audio TTS & Feedback row
                col_audio, col_fb1, col_fb2, col_space = st.columns([4, 1, 1, 6])
                with col_audio:
                    if st.button("🔊 " + ("Listen" if is_en else "Sesli Dinle"), key=f"tts_{i}"):
                        with st.spinner("Preparing audio..."):
                            audio_bytes = text_to_speech_tr(msg["content"])
                            if audio_bytes:
                                st.audio(audio_bytes, format="audio/mp3")

                if col_fb1.button("👍", key=f"pos_{i}"):
                    st.session_state["feedback"]["positive"] += 1
                    st.toast("Feedback received! Thank you. 😊" if is_en else "Geri bildiriminiz alındı! Teşekkür ederiz. 😊")
                if col_fb2.button("👎", key=f"neg_{i}"):
                    st.session_state["feedback"]["negative"] += 1
                    st.toast("Feedback received. Thank you! 🛠️" if is_en else "Geri bildiriminiz alındı. BTÜ Asistan'ı geliştirmek için inceliyoruz! 🛠️")

    # SINGLE PERMANENT STICKY CHAT INPUT AT THE VERY BOTTOM
    input_placeholder = "Ask BTU Assistant a question..." if is_en else "BTÜ Asistan'a bir soru sorun..."
    user_question = st.chat_input(input_placeholder) or preset_query

    if user_question:
        st.session_state.messages.append({"role": "user", "content": user_question})
        with st.chat_message("user"):
            st.markdown(user_question)

        recent_history = st.session_state.messages[-10:]

        with st.chat_message("assistant"):
            spin_msg = "BTU Assistant searching documents & re-ranking..." if is_en else "BTÜ Asistan dokümanları tarıyor ve re-ranker ile değerlendiriyor..."
            with st.spinner(spin_msg):
                answer_func = getattr(rag_engine, "generate_answer", rag_engine.generate_response)
                result = answer_func(
                    user_question,
                    chat_history=recent_history,
                    user_bolum=st.session_state.bolum,
                    user_sinif=st.session_state.sinif,
                    user_gano=st.session_state.gano,
                    detail_level=st.session_state.detail_level,
                    ai_tone=st.session_state.ai_tone,
                    language=st.session_state.app_language
                )

                answer = result["answer"]
                sources = result["sources"]

                st.markdown(answer)
                
                audio_bytes = text_to_speech_tr(answer)
                if audio_bytes:
                    st.audio(audio_bytes, format="audio/mp3")

                if sources:
                    with st.expander("📄 " + ("Source Documents & Details" if is_en else "Kaynak Dokümanlar ve Detaylar")):
                        for idx, src in enumerate(sources, 1):
                            st.markdown(f"**{idx}. {src['source']}** - *{src['header']}*")
                            st.markdown(f"```text\n{src['text']}\n```")

        st.session_state.messages.append({
            "role": "assistant",
            "content": answer,
            "sources": sources
        })
