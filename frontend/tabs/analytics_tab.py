import streamlit as st

def render_analytics_tab():
    """Tab 4: Admin & Feedback Analytics Blueprint."""
    st.subheader("📊 BTÜ Asistan İstatistik & Geri Bildirim Paneli")
    col_a, col_b, col_c, col_d = st.columns(4)
    col_a.metric("Toplam Sorulan Soru", len(st.session_state.get("messages", [])) // 2)
    col_b.metric("Olumlu Geri Bildirim (👍)", st.session_state.get("feedback", {}).get("positive", 0))
    col_c.metric("Olumsuz Geri Bildirim (👎)", st.session_state.get("feedback", {}).get("negative", 0))
    col_d.metric("Veritabanı Sağlığı", "%100 Tamamlandı")

    st.markdown("---")
    st.markdown("### 📌 En Çok Merak Edilen Konular")
    st.bar_chart({
        "İMEP Başvuru & GANO": 45,
        "Sigorta & Ücretler": 38,
        "F1-F9 Resmi Formlar": 29,
        "Akademik Takvim": 25,
        "Rapor Teslimi": 22
    })
