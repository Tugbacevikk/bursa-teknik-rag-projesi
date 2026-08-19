import re
from typing import List, Dict, Any
from src.config import GEMINI_API_KEY, GEMINI_MODEL_NAME

SYSTEM_PROMPT = """Sen Bursa Teknik Üniversitesi (BTÜ) İşletmede Mesleki Eğitim Programı (İMEP) Akıllı Öğrenci Danışmanısın.
Görevin, öğrencilerin İMEP ve BTÜ hakkındaki sorularını yalnızca sana sağlanan resmi referans doküman parçalarına dayanarak DOĞRU, AÇIK ve KAYNAK GÖSTEREREK yanıtlamaktır.

Kurallar:
1. Yanıtlarını YALNIZCA aşağıda verilen İMEP & BTÜ Referans Dokümanları'na dayandır.
2. Bilgi verilen referans metinde açıkça bulunmuyorsa, "Verilen İMEP yönerge ve kılavuzlarında bu konuyla ilgili net bir bilgi bulunmamaktadır. Lütfen İMEP Bölüm Koordinatörünüze danışınız." şeklinde yanıt ver. Asla tahmin yürütme veya halüsinasyon görme.
3. Cevaplarının sonunda mutlaka faydalandığın madde/bölüm veya doküman adını kaynak göster (Örn: 📄 **Kaynak:** BTU İMEP Yönergesi - Madde 5).
4. Öğrenciye nazik, yardımsever ve kurumsal bir dille hitap et.

İMEP & BTÜ Referans Dokümanları:
--------------------------
{context}
--------------------------
"""

OUT_OF_SCOPE_KEYWORDS = [
    "yemek tarifi", "futbol", "magazin", "fıkra", "hava durumu", "bist", "kripto",
    "oyun hilesi", "film önerisi", "sarkı sözü"
]

class RAGEngine:
    def __init__(self, vector_store):
        self.vector_store = vector_store
        self.api_key = GEMINI_API_KEY
        self.client = None

        if self.api_key:
            try:
                from google import genai
                self.client = genai.Client(api_key=self.api_key)
            except Exception as e:
                print(f"Gemini API yükleme uyarısı: {e}")

    def normalize_query(self, query: str) -> str:
        """Kullanıcı sorgusunu temizler ve imla hatalarını düzeltir."""
        query = query.strip()
        # Çoklu boşlukları temizle
        query = re.sub(r'\s+', ' ', query)
        return query

    def is_out_of_scope(self, query: str) -> bool:
        """Adım 1 Guardrail: Okul dışı konuları tespit eder."""
        q_lower = query.lower()
        for kw in OUT_OF_SCOPE_KEYWORDS:
            if kw in q_lower:
                return True
        return False

    def generate_response(self, query: str) -> Dict[str, Any]:
        cleaned_query = self.normalize_query(query)

        # 1. Guardrail Kontrolü (Kapsam Dışı Sorular)
        if self.is_out_of_scope(cleaned_query):
            return {
                "answer": "🛡️ **Kapsam Dışı Soru Uyarısı:** Ben Bursa Teknik Üniversitesi İMEP Akıllı Öğrenci Danışmanıyım. Yalnızca İMEP başvuru süreçleri, sigorta, mevzuat, formlar ve üniversite konularında yardımcı olabilirim. Lütfen akademik konularda bir soru sorunuz. 🎓",
                "sources": []
            }

        # 2. Retrieve relevant context chunks using Hybrid Vector Store
        retrieved_docs = self.vector_store.search(cleaned_query, top_k=4)

        if not retrieved_docs:
            return {
                "answer": "BTÜ İMEP sisteminde soruyla ilgili herhangi bir resmi doküman bulunamadı. Lütfen doküman yükleyiniz veya İMEP koordinatörlüğüne danışınız.",
                "sources": []
            }

        # Build Context String
        context_parts = []
        sources = []
        for i, doc in enumerate(retrieved_docs, 1):
            source_file = doc["metadata"].get("source", "Bilinmeyen Doküman")
            header = doc["metadata"].get("header", f"Parça {i}")
            context_parts.append(f"[{i}] Kaynak: {source_file} ({header})\n{doc['text']}\n")
            sources.append({
                "source": source_file,
                "header": header,
                "text": doc["text"]
            })

        context_str = "\n".join(context_parts)

        # 3. Generate LLM Answer
        if self.client:
            try:
                full_prompt = SYSTEM_PROMPT.format(context=context_str) + f"\nÖğrenci Soru: {cleaned_query}\nYanıt:"
                response = self.client.models.generate_content(
                    model=GEMINI_MODEL_NAME,
                    contents=full_prompt
                )
                answer_text = response.text
            except Exception as e:
                answer_text = self._fallback_answer_generator(cleaned_query, retrieved_docs)
                answer_text += f"\n\n*(Not: Gemini API bağlantısı sırasında uyarı alındı: {e}. Yerel kural bazlı yanıt üretildi.)*"
        else:
            answer_text = self._fallback_answer_generator(cleaned_query, retrieved_docs)

        return {
            "answer": answer_text,
            "sources": sources
        }

    def _fallback_answer_generator(self, query: str, retrieved_docs: List[Dict[str, Any]]) -> str:
        top_doc = retrieved_docs[0]
        source_name = top_doc['metadata'].get('source', '')
        header_name = top_doc['metadata'].get('header', '')
        
        reply = f"**BTÜ İMEP Dokümanlarından Çıkarılan Bilgi:**\n\n"
        reply += f"{top_doc['text']}\n\n"
        reply += f"📄 **Kaynak:** `{source_name}` - *{header_name}*\n"
        reply += f"\n*(İpucu: `.env` dosyasına `GEMINI_API_KEY` ekleyerek tam yapay zeka özetini aktif edebilirsiniz.)*"
        return reply
