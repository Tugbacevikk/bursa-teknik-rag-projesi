import sys
from pathlib import Path

# Add project root to sys.path
sys.path.append(str(Path(__file__).resolve().parent.parent))

import requests
from bs4 import BeautifulSoup
import re
from urllib.parse import urljoin
from src.config import DATA_RAW_DIR

URLS_TO_SCRAPE = [
    "https://imep.btu.edu.tr/tr/sayfa/detay/5542/i%CC%87mep-formlar",
    "https://ogrenci.btu.edu.tr/tr/sayfa/detay/4766/formlar",
    "https://btu.edu.tr"
]

def scrape_all_btu_forms():
    print("BTU Web Sitelerinden ve Ogrenci Islerinden Formlar Taraniyor...")
    collected_forms = []

    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
    }

    for base_url in URLS_TO_SCRAPE:
        try:
            res = requests.get(base_url, headers=headers, timeout=12)
            if res.status_code == 200:
                soup = BeautifulSoup(res.content, "html.parser")
                for link in soup.find_all("a", href=True):
                    href = link.get("href", "")
                    text = link.get_text(strip=True)
                    
                    # Target form documents (.pdf, .docx, .doc or contains 'form' / 'sözleşme' / 'yönerge' / 'dilekçe')
                    if re.search(r'\.(pdf|docx|doc)$', href, re.IGNORECASE) or any(k in text.lower() for k in ["form", "sözleşme", "taslak", "yönerge", "dilekçe", "başvuru", "kayıt"]):
                        full_link = urljoin(base_url, href)
                        if len(text) > 3 and full_link not in [f["link"] for f in collected_forms]:
                            collected_forms.append({
                                "title": text,
                                "link": full_link,
                                "source_page": base_url
                            })
        except Exception as e:
            print(f"Tarama Uyarisi ({base_url}): {e}")

    # Output file
    output_file = DATA_RAW_DIR / "btu_tum_formlar.txt"
    with open(output_file, "w", encoding="utf-8") as f:
        f.write("# BURSA TEKNİK ÜNİVERSİTESİ (BTÜ) ÖĞRENCİ İŞLERİ VE İMEP TÜM RESMİ FORMLARI\n\n")
        f.write("Bu belgede BTÜ Öğrenci İşleri Daire Başkanlığı ve İMEP web sitelerinden taranan resmi öğrenci formları, dilekçeler ve sözleşmeler yer almaktadır:\n\n")

        for idx, item in enumerate(collected_forms, 1):
            f.write(f"## {idx}. {item['title']}\n")
            f.write(f"- Form / Dilekçe Adı: {item['title']}\n")
            f.write(f"- Bağlantı / İndir: {item['link']}\n")
            f.write(f"- Kaynak Sayfa: {item['source_page']}\n\n")

    print(f"Basariyla {len(collected_forms)} adet form ve belge tarandi ve '{output_file.name}' dosyasina kaydedildi!")
    return len(collected_forms)

if __name__ == "__main__":
    scrape_all_btu_forms()
