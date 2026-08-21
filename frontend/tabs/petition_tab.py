import streamlit as st
from backend.services.pdf_service import generate_btu_petition_pdf, generate_btu_petition_text

def render_petition_tab():
    """Tab 2: Petition & Form Generator Blueprint."""
    is_en = (st.session_state.app_language == "English")
    st.subheader("📝 " + ("Automatic BTU Petition & Form Generator" if is_en else "Otomatik BTÜ Dilekçe & Resmi Form Doldurucu"))
    st.markdown("Bilgilerinizi girin, resmi **BTÜ Öğrenci Dilekçenizi** otomatik doldurulmuş PDF ve metin olarak anında indirin!")

    col_df1, col_df2 = st.columns(2)
    with col_df1:
        s_name = st.text_input("Öğrenci Adı Soyadı / Student Full Name", value=st.session_state.get("ogrenci_adi", ""))
        s_id = st.text_input("Öğrenci Numarası / Student ID", value=st.session_state.get("ogrenci_no", ""))
        p_type = st.selectbox("Dilekçe / Başvuru Türü", [
            "BTÜ Mazeretli Sınav Dilekçesi",
            "BTÜ-İMEP İzin Talebi Dilekçesi",
            "BTÜ Ders Muafiyet Dilekçesi",
            "BTÜ Kayıt Dondurma Talebi Dilekçesi",
            "BTÜ Tek Ders Sınavı Başvuru Dilekçesi"
        ])
    
    with col_df2:
        dept = st.text_input("Bölümünüz / Department", value=st.session_state.bolum)
        cls_year = st.text_input("Sınıfınız / Grade", value=st.session_state.sinif)
        reason = st.text_area("Talebiniz / Mazeret Açıklamanız", value="Aşağıda belirttiğim haklı gerekçeler nedeniyle mazeret sınavına/talebe kabulünü arz ederim.")

    if st.button("📄 Otomatik Dilekçe Oluştur & İndir", type="primary"):
        if not s_name or not s_id:
            st.error("Lütfen Ad Soyad ve Öğrenci Numarası alanlarını doldurunuz.")
        else:
            st.session_state.ogrenci_adi = s_name
            st.session_state.ogrenci_no = s_id
            
            petition_text = generate_btu_petition_text(s_name, s_id, dept, cls_year, p_type, reason)
            pdf_bytes = generate_btu_petition_pdf(s_name, s_id, dept, cls_year, p_type, reason)
            
            st.success("✅ Resmi BTÜ Dilekçeniz Başarıyla Oluşturuldu!")
            
            col_d1, col_d2 = st.columns(2)
            with col_d1:
                st.download_button(
                    label="📥 Resmi PDF Dilekçesini İndir",
                    data=pdf_bytes,
                    file_name=f"BTU_Dilekce_{s_id}.pdf",
                    mime="application/pdf"
                )
            with col_d2:
                st.download_button(
                    label="📝 Metin Dosyası İndir (.txt)",
                    data=petition_text,
                    file_name=f"BTU_Dilekce_{s_id}.txt",
                    mime="text/plain"
                )
            
            with st.expander("📄 Oluşturulan Resmi Dilekçe Önizlemesi"):
                st.code(petition_text, language="text")
