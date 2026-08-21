import sys
from pathlib import Path

# Add project root to sys.path
sys.path.append(str(Path(__file__).resolve().parent.parent))

import requests
from bs4 import BeautifulSoup
import re
from src.config import DATA_RAW_DIR

BTU_TARGET_PAGES = [
    {
        "name": "btu_imep_resmi_bilgiler.txt",
        "url": "https://imep.btu.edu.tr/tr/sayfa/detay/5538/btu-i%CC%87mep-i%CC%87sleyis-esaslari",
        "title": "BTÜ İMEP İşleyiş Esasları ve Kuralları"
    },
    {
        "name": "btu_ogrenci_isleri_duyurular.txt",
        "url": "https://ogrenci.btu.edu.tr/tr/sayfa/detay/4766/formlar",
        "title": "BTÜ Öğrenci İşleri Form ve Başvuru Kuralları"
    },
    {
        "name": "btu_genel_tanitim_ve_surecler.txt",
        "url": "https://imep.btu.edu.tr/tr/sayfa/detay/5534/btu-i%CC%87mep-modeli",
        "title": "BTÜ İMEP Modeli ve Sektörel Eğitim Kılavuzu"
    }
]

def crawl_btu_essential_data():
    print("BTU Resmi Sitelerinden Tum Gerekli Ogrenci Bilgileri Taraniyor...")
    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
    }

    total_scraped_files = 0

    for item in BTU_TARGET_PAGES:
        try:
            res = requests.get(item["url"], headers=headers, timeout=12)
            if res.status_code == 200:
                soup = BeautifulSoup(res.content, "html.parser")
                
                # Remove scripts, style tags
                for element in soup(["script", "style", "nav", "footer", "header"]):
                    element.extract()

                text = soup.get_text(separator="\n")
                lines = [line.strip() for line in text.splitlines() if line.strip()]
                clean_content = "\n".join(lines)

                output_file = DATA_RAW_DIR / item["name"]
                with open(output_file, "w", encoding="utf-8") as f:
                    f.write(f"# {item['title'].upper()}\n")
                    f.write(f"Kaynak URL: {item['url']}\n\n")
                    f.write(clean_content)

                print(f"Basariyla kaydedildi: {item['name']} ({len(clean_content)} karakter)")
                total_scraped_files += 1
        except Exception as e:
            print(f"Tarama Uyarisi ({item['url']}): {e}")

    print(f"Toplam {total_scraped_files} resmi sayfa icerigi 'data/raw/' klasorune aktarildi!")
    return total_scraped_files

if __name__ == "__main__":
    crawl_btu_essential_data()
