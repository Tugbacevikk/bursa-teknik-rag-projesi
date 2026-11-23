import io
from fpdf import FPDF

def latinize_tr_text(text: str) -> str:
    """Standard FPDF1/2 Helvetica encoding helper for Turkish characters."""
    tr_map = {
        'ğ': 'g', 'Ğ': 'G',
        'ı': 'i', 'İ': 'I',
        'ş': 's', 'Ş': 'S',
        'ü': 'u', 'Ü': 'U',
        'ö': 'o', 'Ö': 'O',
        'ç': 'c', 'Ç': 'C'
    }
    for tr_char, latin_char in tr_map.items():
        text = text.replace(tr_char, latin_char)
    return text

def generate_btu_petition_pdf(student_name: str, student_id: str, department: str, class_year: str, petition_type: str, reason: str) -> bytes:
    """Resmi BTÜ Öğrenci Dilekçesini PDF formatında üretir."""
    pdf = FPDF()
    pdf.add_page()
    pdf.set_font("Helvetica", "B", 14)

    header_title = latinize_tr_text("BURSA TEKNIK UNIVERSITESI REKTORLUGUNA")
    pdf.cell(0, 10, header_title, ln=True, align="C")

    pdf.set_font("Helvetica", "B", 12)
    dept_title = latinize_tr_text(f"({department.upper()} BOLUM BASKANLIGINA)")
    pdf.cell(0, 8, dept_title, ln=True, align="C")
    pdf.ln(10)

    pdf.set_font("Helvetica", "", 11)
    p1 = latinize_tr_text(f"Bölümünüz {class_year} sınıf, {student_id} numaralı öğrencisiyim. Aşağıda belirttiğim gerekçe doğrultusunda {petition_type} talebimin değerlendirilmesini arz ederim.")
    pdf.multi_cell(0, 7, p1)
    pdf.ln(8)

    pdf.set_font("Helvetica", "B", 11)
    pdf.cell(0, 7, latinize_tr_text("Mazeret / Talep Açıklaması:"), ln=True)
    pdf.set_font("Helvetica", "", 10)
    pdf.multi_cell(0, 6, latinize_tr_text(reason))
    pdf.ln(15)

    pdf.set_font("Helvetica", "B", 11)
    pdf.cell(0, 6, latinize_tr_text(f"Ad Soyad: {student_name}"), ln=True, align="R")
    pdf.cell(0, 6, latinize_tr_text(f"Öğrenci No: {student_id}"), ln=True, align="R")
    pdf.cell(0, 6, latinize_tr_text("İmza: ........................"), ln=True, align="R")

    return bytes(pdf.output())

def generate_btu_petition_text(student_name: str, student_id: str, department: str, class_year: str, petition_type: str, reason: str) -> str:
    """Resmi BTÜ Dilekçesini Metin formatında üretir."""
    text = f"""BURSA TEKNİK ÜNİVERSİTESİ REKTÖRLÜĞÜNE
({department.upper()} BÖLÜM BAŞKANLIĞINA)

Bölümünüz {class_year} sınıf, {student_id} numaralı öğrencisiyim. Aşağıda belirttiğim gerekçe doğrultusunda {petition_type} talebimin değerlendirilmesini arz ederim.

TALEB/MAZERET AÇIKLAMASI:
{reason}

Öğrenci Adı Soyadı: {student_name}
Öğrenci Numarası: {student_id}
Tarih: ..................
İmza: ..................
"""
    return text
