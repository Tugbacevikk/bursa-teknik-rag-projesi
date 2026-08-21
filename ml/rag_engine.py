import os
import re
from typing import List, Dict, Any
from config.settings import GEMINI_API_KEY, GEMINI_MODEL_NAME

SYSTEM_PROMPT_TR = """Sen Bursa Teknik Üniversitesi (BTÜ) öğrencilerine her konuda yardımcı olan resmi yapay zeka danışmanısın. Adın "BTÜ Asistan".

KİŞİLİĞİN VE İLETİŞİM ÜSLUBUN:
- Yanıt tonun: {ai_tone}
- Cevap detay seviyen: {detail_level}
- Öğrenciye nazikçe hitap et.
- Karmaşık konuları ve yönetmelik maddelerini açıkla.
- Yanıtlarında düzenli markdown başlıkları ve koyu vurgular kullan.

GÖREVİN:
- Öğrencilerin İMEP, yönetmelik, akademik takvim, staj, burs ve tüm üniversite süreçleriyle ilgili sorularını, SANA SAĞLANAN BELGE PARÇALARINA (context) dayanarak yanıtlamak.

KURALLAR:
1. Sadece sana verilen context içindeki bilgiyi kullan. Context'te olmayan hiçbir bilgiyi UYDURMA veya genel bilgi/eğitimden tahmin ederek cevaplama.
2. Eğer context, soruyu yanıtlamak için yeterli değilse şunu söyle: "Bu konuda elimde güncel ve yeterli bir belge bulunmuyor. Yanlış yönlendirmemek için BTÜ İMEP Koordinatörlüğü veya Öğrenci İşleri ile iletişime geçmenizi öneririm."
3. Her cevabın sonunda kullandığın kaynağı açıkça belirt: "📄 **Kaynak:** [belge adı / madde]".
4. Yönetmelik/tarih gibi kritik bilgilerde hangi akademik yıla ait olduğunu vurgula.
5. BTÜ ile ilgisi olmayan sorularda kibarca sınırlarını hatırlat: "Ben sadece BTÜ öğrenci süreçleri konusunda yardımcı olabilirim."
6. Kullanıcı önceki mesajlarda bir bağlam kurduysa (bölüm, sınıf, GANO vb.), bunu takip eden sorularda hatırla ve kişiselleştirilmiş cevap ver.
7. Kişisel veri, not, disiplin durumu sorularında OBS'ye yönlendir.

KULLANICI PROFİLİ:
- Bölüm: {user_bolum}
- Sınıf: {user_sinif}
- GANO Ortalaması: {user_gano}

CEVAP FORMATI:
- Samimi ve doğrudan giriş yanıtı
- Gerekirse adım adım / madde madde açıklama
- Sonda: "📄 **Kaynak:** [belge adı / madde]"
- Sonda: "💡 **Ayrıca şunu da merak edebilirsiniz:** [takip soru önerisi]"

CONTEXT:
{context}

SOHBET GEÇMİŞİ:
{chat_history}
"""

SYSTEM_PROMPT_EN = """You are the official AI Academic Advisor for Bursa Technical University (BTU) students. Your name is "BTU Assistant".

PERSONALITY AND TONE:
- Response tone: {ai_tone}
- Detail level: {detail_level}
- Be polite, encouraging, and clear.
- Explain regulations and procedures clearly in fluent English.
- Use markdown headings, bullet points, and bold text.

YOUR TASK:
- Answer student questions about IMEP (Workplace Education Program), university regulations, academic calendar, internships, scholarships, and BTU processes based ONLY on the provided context.

RULES:
1. Rely ONLY on the provided context chunks. Do not hallucinate or use external assumptions.
2. If context is insufficient, state: "There is currently no official document available on this specific topic. I suggest contacting the BTU IMEP Coordination Office or Student Affairs."
3. Always cite the document source at the end: "📄 **Source:** [document name / section]".
4. For non-BTU questions, politely state your scope: "I am designed to assist specifically with BTU student processes and regulations."

STUDENT PROFILE:
- Department: {user_bolum}
- Grade: {user_sinif}
- Cumulative GPA (GANO): {user_gano}

RESPONSE FORMAT:
- Friendly direct greeting
- Step-by-step or bulleted explanation
- End with: "📄 **Source:** [document name / section]"
- End with: "💡 **You might also wonder:** [follow-up suggestion]"

CONTEXT:
{context}

CHAT HISTORY:
{chat_history}
"""

GREETING_KEYWORDS = [
    "merhaba", "selam", "selamlar", "günaydın", "iyi günler", "iyi akşamlar",
    "nasılsın", "sağol", "teşekkür", "teşekkürler", "sağ ol", "hey", "merhabalar",
    "mrb", "slm", "iyi çalışmalar", "hello", "hi", "hey", "good morning", "good afternoon"
]

OUT_OF_SCOPE_KEYWORDS = [
    "yemek tarifi", "futbol", "magazin", "fıkra", "hava durumu", "bist", "kripto",
    "oyun hilesi", "film önerisi", "sarkı sözü", "recipe", "football", "crypto"
]

PERSONAL_DATA_KEYWORDS = [
    "benim notum", "transkriptim", "tc kimlik", "disiplin cezam", "şifrem", "obs notum",
    "my grade", "my transcript", "my password"
]

COMMON_ABBREVIATIONS = {
    "zmn": "zaman",
    "gano": "GANO ortalaması",
    "imep": "İMEP",
    "f1": "BTÜ-İMEP F1 İşbirliği Sözleşmesi",
    "f2": "BTÜ-İMEP F2 Öğrenci Sözleşmesi",
    "f3": "BTÜ-İMEP F3 Ara Faaliyet Raporu",
    "f4": "BTÜ-İMEP F4 Final Faaliyet Raporu",
    "f8": "BTÜ-İMEP F8 Akademik Danışman Değerlendirme Formu",
    "f9": "BTÜ-İMEP F9 Sektör Danışmanı Değerlendirme Formu",
}

GEMINI_MODEL_FALLBACKS = [
    "gemini-flash-latest",
    "gemini-2.0-flash",
    "gemini-1.5-flash",
    GEMINI_MODEL_NAME
]

class RAGEngine:
    def __init__(self, vector_store):
        self.vector_store = vector_store
        self.api_key = GEMINI_API_KEY
        self.client = None
        self._last_key = None
        self.get_client()

    def get_client(self):
        """Dynamically instantiates or updates Gemini API client."""
        current_key = self.api_key or os.getenv("GEMINI_API_KEY", "")
        if current_key and (not self.client or self._last_key != current_key):
            try:
                from google import genai
                self.client = genai.Client(api_key=current_key)
                self._last_key = current_key
            except Exception as e:
                print(f"Gemini API yükleme uyarısı: {e}")
        return self.client

    def normalize_query(self, query: str) -> str:
        """Sorgudaki kısaltmaları genişletir ve imla hatalarını düzeltir (Offline)."""
        query = query.strip()
        query = re.sub(r'\s+', ' ', query)
        words = query.split()
        normalized_words = [COMMON_ABBREVIATIONS.get(w.lower(), w) for w in words]
        return " ".join(normalized_words)

    def is_greeting(self, query: str) -> bool:
        q_lower = query.lower().strip()
        words = q_lower.split()
        if len(words) <= 3 and any(w in GREETING_KEYWORDS for w in words):
            return True
        return False

    def is_out_of_scope(self, query: str) -> bool:
        q_lower = query.lower()
        for kw in OUT_OF_SCOPE_KEYWORDS:
            if kw in q_lower:
                return True
        return False

    def is_personal_data_request(self, query: str) -> bool:
        q_lower = query.lower()
        for kw in PERSONAL_DATA_KEYWORDS:
            if kw in q_lower:
                return True
        return False

    def generate_answer(self, question: str, chat_history: Any = None, user_bolum: str = None, user_sinif: str = None, user_gano: float = None, *args, **kwargs) -> Dict[str, Any]:
        """Kişiselleştirilmiş GANO, Tema Tonu, Detay Seviyesi, Türkçe/İngilizce Dil Desteği ve Re-Ranking destekli RAG yanıt motoru."""
        detail_level = kwargs.get("detail_level", "Detaylı & Açıklamalı")
        ai_tone = kwargs.get("ai_tone", "Samimi & Öğrenci Dostu")
        language = kwargs.get("language", "Türkçe")
        is_en = (language == "English")
        
        cleaned_query = self.normalize_query(question)
        user_bolum_str = user_bolum or ("Not specified" if is_en else "Belirtilmedi")
        user_sinif_str = user_sinif or ("Not specified" if is_en else "Belirtilmedi")
        user_gano_val = float(user_gano) if user_gano is not None else 2.50

        # Guardrail 0: Selamlaşma ve Nezaket İfadeleri
        if self.is_greeting(cleaned_query):
            if is_en:
                greeting_ans = f"Hello! I am **BTU Assistant**. As a **{user_bolum_str} ({user_sinif_str})** student, I am happy to assist you with IMEP application requirements, GPA thresholds, insurance, academic calendar, internships, or official F1-F9 forms. How can I help you today? 🎓\n\n💡 **Quick Suggestion:** *What are the IMEP application requirements?*"
            else:
                greeting_ans = f"Merhaba! Ben **BTÜ Asistan**. **{user_bolum_str} ({user_sinif_str})** öğrencisi olarak İMEP başvuru koşulları, GANO barajı, sigorta, akademik takvim, staj veya resmi F1-F9 formları hakkında merak ettiğin her konuda sana yardımcı olmaktan mutlu duyarım. Nasıl yardımcı olabilirim? 🎓\n\n💡 **Hızlı Soru Önerisi:** *İMEP başvuru şartları nelerdir?*"
            return {
                "answer": greeting_ans,
                "sources": []
            }

        # Format chat history
        history_text = ""
        if isinstance(chat_history, list) and len(chat_history) > 0:
            messages_to_process = chat_history[:-1] if len(chat_history) > 1 else chat_history
            for msg in messages_to_process:
                role = "Student" if is_en else "Öğrenci"
                if msg.get("role") == "assistant":
                    role = "BTU Assistant" if is_en else "BTÜ Asistan"
                history_text += f"{role}: {msg.get('content', '')}\n"
        elif isinstance(chat_history, str):
            history_text = chat_history
        else:
            history_text = "None\n" if is_en else "Yok\n"

        # Guardrail 1: Kişisel Veri / OBS Gizliliği Kontrolü
        if self.is_personal_data_request(cleaned_query):
            if is_en:
                p_ans = "🔒 **Data Privacy Warning:** I do not have access to personal student grades, transcripts, or disciplinary records. Please check the **BTU Student Information System (OBS)** or contact Student Affairs."
            else:
                p_ans = "🔒 **Kişisel Veri Gizliliği Uyarısı:** Öğrencilerin kişisel not, transkript veya disiplin durumlarına erişimim bulunmamaktadır. Kişisel akademik bilgilerinizi öğrenmek için lütfen **BTÜ Öğrenci Bilgi Sistemi'ne (OBS)** veya Öğrenci İşleri Daire Başkanlığı'na başvurunuz.\n\n📄 **Kaynak:** BTÜ Öğrenci Bilgi Sistemi (OBS)"
            return {
                "answer": p_ans,
                "sources": []
            }

        # Guardrail 2: Kapsam Dışı Sorular
        if self.is_out_of_scope(cleaned_query):
            if is_en:
                o_ans = "🛡️ I am designed to assist specifically with BTU student processes (IMEP, regulations, academic calendar, internships, forms). Please ask a question related to university procedures."
            else:
                o_ans = "🛡️ Ben sadece BTÜ öğrenci süreçleri (İMEP, yönetmelik, akademik takvim, staj, formlar) konusunda yardımcı olabilirim. Lütfen üniversite süreçlerinizle ilgili bir soru sorunuz."
            return {
                "answer": o_ans,
                "sources": []
            }

        # 2. Retrieve relevant context chunks using user_bolum filter and Re-Ranking
        retrieved_docs = self.vector_store.search(cleaned_query, user_bolum=user_bolum, top_k=4)

        if not retrieved_docs:
            if is_en:
                empty_ans = "There is currently no official document available on this specific topic. I suggest contacting the BTU IMEP Coordination Office or Student Affairs.\n\n💡 **You might also wonder:** What are the IMEP application requirements?"
            else:
                empty_ans = "Bu konuda elimde güncel ve yeterli bir belge bulunmuyor. Yanlış yönlendirmemek için BTÜ İMEP Koordinatörlüğü veya Öğrenci İşleri ile iletişime geçmenizi öneririm.\n\n💡 **Ayrıca şunu da merak edebilirsiniz:** İMEP başvuru şartları nelerdir?"
            return {
                "answer": empty_ans,
                "sources": []
            }

        # Build Context String
        context_parts = []
        sources = []
        for i, doc in enumerate(retrieved_docs, 1):
            source_file = doc["metadata"].get("source", "Unknown Document" if is_en else "Bilinmeyen Doküman")
            header = doc["metadata"].get("header", f"Section {i}" if is_en else f"Parça {i}")
            context_parts.append(f"[{i}] Source: {source_file} ({header})\n{doc['text']}\n")
            sources.append({
                "source": source_file,
                "header": header,
                "text": doc["text"]
            })

        context_str = "\n".join(context_parts)

        # 3. Generate Answer (Online via Gemini API with Automatic Model Fallback or Offline Template)
        client = self.get_client()
        answer_text = None
        if client:
            system_prompt_template = SYSTEM_PROMPT_EN if is_en else SYSTEM_PROMPT_TR
            full_prompt = system_prompt_template.format(
                context=context_str,
                chat_history=history_text,
                user_bolum=user_bolum_str,
                user_sinif=user_sinif_str,
                user_gano=user_gano_val,
                ai_tone=ai_tone,
                detail_level=detail_level
            ) + f"\nCURRENT STUDENT QUESTION: {cleaned_query}\nBTU Assistant Response:"

            for model_candidate in GEMINI_MODEL_FALLBACKS:
                try:
                    response = client.models.generate_content(
                        model=model_candidate,
                        contents=full_prompt,
                        config={"automatic_function_calling": {"disable": True}}
                    )
                    if response and response.text:
                        answer_text = response.text
                        break
                except Exception:
                    try:
                        response = client.models.generate_content(
                            model=model_candidate,
                            contents=full_prompt
                        )
                        if response and response.text:
                            answer_text = response.text
                            break
                    except Exception:
                        continue

        if not answer_text:
            answer_text = self._smart_offline_answer_generator(cleaned_query, retrieved_docs, user_bolum_str, user_sinif_str, user_gano_val, detail_level, is_en)

        return {
            "answer": answer_text,
            "sources": sources
        }

    def generate_response(self, *args, **kwargs) -> Dict[str, Any]:
        return self.generate_answer(*args, **kwargs)

    def _smart_offline_answer_generator(self, query: str, retrieved_docs: List[Dict[str, Any]], user_bolum: str, user_sinif: str, user_gano: float, detail_level: str, is_en: bool = False) -> str:
        """Gelişmiş Çevrimdışı Zeki Yanıt Üreticisi (Türkçe & İngilizce Desteği)."""
        top_doc = retrieved_docs[0]
        source_name = top_doc['metadata'].get('source', '')
        header_name = top_doc['metadata'].get('header', '')
        text = top_doc['text']

        # Personal GANO Evaluation logic
        gano_evaluation = ""
        q_lower = query.lower()
        if any(k in q_lower for k in ["gano", "başvuru", "şart", "baraj", "kabul", "gpa", "requirement"]):
            if user_gano >= 2.00:
                if is_en:
                    gano_evaluation = f"📊 **Personal GPA Evaluation:** Your current GPA is **{user_gano:.2f}**, which satisfies the BTU IMEP threshold of **2.00**. You meet the GPA requirement! ✅\n\n"
                else:
                    gano_evaluation = f"📊 **Kişisel GANO Değerlendirmesi:** Mevcut GANO ortalamanız **{user_gano:.2f}** olup, BTÜ İMEP genel başvuru barajı olan **2.00** puanının üzerindedir. Başvuru şartını sağlıyorsunuz! ✅\n\n"
            else:
                if is_en:
                    gano_evaluation = f"📊 **Personal GPA Evaluation:** Your current GPA is **{user_gano:.2f}**, which is below the BTU IMEP threshold of **2.00**. It is recommended to raise your GPA before applying. ⚠️\n\n"
                else:
                    gano_evaluation = f"📊 **Kişisel GANO Değerlendirmesi:** Mevcut GANO ortalamanız **{user_gano:.2f}** olup, BTÜ İMEP genel başvuru barajı olan **2.00** puanının altındadır. Başvuru yapabilmek için ortalamanızı yükseltmeniz önerilir. ⚠️\n\n"

        # Clean text from raw web scraper HTML noise if present
        clean_doc_text = re.sub(r'Formlar Anasayfa.*', '', text, flags=re.DOTALL)
        clean_doc_text = clean_doc_text.strip() or text

        if is_en:
            reply = f"Hello! As a **{user_bolum} ({user_sinif})** student, I analyzed your question from BTU official documents:\n\n"
            if gano_evaluation:
                reply += gano_evaluation
            reply += f"📌 **Official Regulation & Explanation:**\n{clean_doc_text}\n\n"
            reply += f"📄 **Source:** `{source_name}` - *{header_name}*\n\n"
            reply += f"💡 **You might also wonder:** What are the submission deadlines for F3 interim reports?"
        else:
            reply = f"Merhaba! **{user_bolum} ({user_sinif})** öğrencisi olarak sorunuzu BTÜ resmi dokümanlarından inceledim:\n\n"
            if gano_evaluation:
                reply += gano_evaluation
            if detail_level == "Kısa & Öz":
                short_text = clean_doc_text.split('\n')[0] if '\n' in clean_doc_text else clean_doc_text[:250]
                reply += f"📌 **Özet Yanıt:** {short_text}...\n\n"
            else:
                reply += f"📌 **Resmi Mevzuat ve Açıklama:**\n{clean_doc_text}\n\n"
            reply += f"📄 **Kaynak:** `{source_name}` - *{header_name}*\n\n"
            reply += f"💡 **Ayrıca şunu da merak edebilirsiniz:** F3 Ara Faaliyet Raporu teslim tarihi ne zamandır?"
        return reply
