import requests
from bs4 import BeautifulSoup
from pathlib import Path
from src.config import DATA_RAW_DIR

def scrape_btu_imep_announcements():
    url = "https://imep.btu.edu.tr/tr/sayfa/detay/5542/i%CC%87mep-formlar"
    try:
        response = requests.get(url, timeout=10)
        if response.status_code == 200:
            soup = BeautifulSoup(response.content, 'html.parser')
            text_content = soup.get_text(separator='\n')
            
            output_file = DATA_RAW_DIR / "btu_imep_web_duyurular.txt"
            with open(output_file, "w", encoding="utf-8") as f:
                f.write("# BTÜ İMEP WEB SİTESİ CANLI DUYURU VE SAYFA İÇERİKLERİ\n\n")
                f.write(text_content[:5000])
            print(f"✅ Web taraması başarıyla tamamlandı: {output_file.name}")
            return True
    except Exception as e:
        print(f"⚠️ Web tarama uyarısı: {e}")
        return False

if __name__ == "__main__":
    scrape_btu_imep_announcements()
