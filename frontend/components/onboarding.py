import streamlit as st
from pathlib import Path

def render_onboarding(logo_path: Path):
    """İlk Giriş Ekranı Blueprint Modülü (Language, Department, Grade, GPA)."""
    col_ob1, col_ob2 = st.columns([1, 4])
    with col_ob1:
        if logo_path.exists():
            st.image(str(logo_path), width=140)
    with col_ob2:
        title_text = "🎓 Welcome to BTU Assistant" if st.session_state.app_language == "English" else "🎓 BTÜ Asistan'a Hoş Geldin"
        st.title(title_text)
        sub_text = "Please select your language and academic details to get personalized responses." if st.session_state.app_language == "English" else "Sana daha doğru ve kişiselleştirilmiş cevaplar verebilmem için lütfen dilini ve akademik bilgilerini seçer misin?"
        st.write(sub_text)

    st.markdown("---")

    # 🌐 DİL SEÇİMİ (ONBOARDING)
    col_lang1, col_lang2 = st.columns([2, 2])
    with col_lang1:
        onboarding_lang = st.radio(
            "🌐 Uygulama Dili / App Language",
            ["Türkçe 🇹🇷", "English 🇬🇧"],
            horizontal=True,
            index=0 if st.session_state.app_language == "Türkçe" else 1,
            key="onboarding_lang_radio"
        )
        chosen_lang = "English" if "English" in onboarding_lang else "Türkçe"
        if chosen_lang != st.session_state.app_language:
            st.session_state.app_language = chosen_lang
            if "messages" in st.session_state:
                del st.session_state["messages"]
            st.rerun()

    is_en = (st.session_state.app_language == "English")

    col_p1, col_p2, col_p3 = st.columns(3)
    with col_p1:
        bolumler = [
            "Computer Engineering" if is_en else "Bilgisayar Mühendisliği",
            "Electrical & Electronics Engineering" if is_en else "Elektrik-Elektronik Mühendisliği",
            "Mechanical Engineering" if is_en else "Makine Mühendisliği",
            "Industrial Engineering" if is_en else "Endüstri Mühendisliği",
            "Mechatronics Engineering" if is_en else "Meksatronik Mühendisliği",
            "Civil Engineering" if is_en else "İnşaat Mühendisliği",
            "Other" if is_en else "Diğer"
        ]
        bolum = st.selectbox("Bölümünüz / Department", bolumler)

    with col_p2:
        sinif_secenekleri = ["Prep Class", "1st Grade", "2nd Grade", "3rd Grade", "4th Grade", "Master Degree"] if is_en else ["Hazırlık", "1. Sınıf", "2. Sınıf", "3. Sınıf", "4. Sınıf", "Yüksek Lisans"]
        sinif = st.selectbox("Sınıfınız / Grade", sinif_secenekleri)

    with col_p3:
        gano = st.number_input("GANO / GPA (4.00)", min_value=0.0, max_value=4.0, value=2.50, step=0.01)

    start_btn_label = "🚀 Start BTU Assistant" if is_en else "🚀 BTÜ Asistan'a Başla"
    if st.button(start_btn_label, type="primary"):
        st.session_state.bolum = bolum
        st.session_state.sinif = sinif
        st.session_state.gano = gano
        st.session_state.profile_set = True
        if "messages" in st.session_state:
            del st.session_state["messages"]
        st.rerun()
