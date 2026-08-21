import io
from gtts import gTTS

def text_to_speech_tr(text: str) -> bytes:
    """gTTS kullanarak metni Türkçe MP3 ses baytlarına dönüştürür."""
    try:
        clean_text = text.replace("*", "").replace("#", "").replace("`", "")
        clean_text = clean_text[:400]
        tts = gTTS(text=clean_text, lang='tr', slow=False)
        fp = io.BytesIO()
        tts.write_to_fp(fp)
        fp.seek(0)
        return fp.read()
    except Exception as e:
        print(f"TTS ses üretme hatası: {e}")
        return None
