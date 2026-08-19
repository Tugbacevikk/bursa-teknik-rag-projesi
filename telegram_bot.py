"""
BTÜ İMEP Telegram Bot Entegrasyon Modülü
Gereksinim: pip install python-telegram-bot
Kullanım: TELEGRAM_BOT_TOKEN değişkenini tanımladıktan sonra python telegram_bot.py ile çalıştırın.
"""

import os
from dotenv import load_dotenv
from src.config import DATA_RAW_DIR
from src.data_loader import DocumentLoader
from src.vector_store import HybridVectorStore
from src.rag_engine import RAGEngine

load_dotenv()
TELEGRAM_TOKEN = os.getenv("TELEGRAM_BOT_TOKEN", "")

def main():
    if not TELEGRAM_TOKEN:
        print("⚠️ TELEGRAM_BOT_TOKEN bulunamadı. Lütfen .env dosyasına ekleyin.")
        return

    try:
        from telegram import Update
        from telegram.ext import ApplicationBuilder, CommandHandler, MessageHandler, filters, ContextTypes
    except ImportError:
        print("⚠️ python-telegram-bot kütüphanesi eksik. Yüklemek için: pip install python-telegram-bot")
        return

    # System Init
    loader = DocumentLoader(DATA_RAW_DIR)
    chunks = loader.load_documents()
    vector_store = HybridVectorStore()
    vector_store.index_documents(chunks)
    rag_engine = RAGEngine(vector_store)

    async def start(update: Update, context: ContextTypes.DEFAULT_TYPE):
        await update.message.reply_text(
            "🎓 Merhaba! Ben Bursa Teknik Üniversitesi (BTÜ) İMEP Akıllı Öğrenci Danışmanıyım.\n\n"
            "İMEP başvuru koşulları, sigorta, akademik takvim veya formlar hakkındaki sorularınızı bana yazabilirsiniz!"
        )

    async def handle_message(update: Update, context: ContextTypes.DEFAULT_TYPE):
        user_query = update.message.text
        result = rag_engine.generate_response(user_query)
        answer = result["answer"]
        await update.message.reply_text(answer)

    app = ApplicationBuilder().token(TELEGRAM_TOKEN).build()
    app.add_handler(CommandHandler("start", start))
    app.add_handler(MessageHandler(filters.TEXT & ~filters.COMMAND, handle_message))

    print("🤖 BTÜ İMEP Telegram Botu çalışıyor...")
    app.run_polling()

if __name__ == "__main__":
    main()
