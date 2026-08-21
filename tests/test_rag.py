import sys
from pathlib import Path

# Add project root to sys.path
sys.path.append(str(Path(__file__).resolve().parent.parent))

from src.config import DATA_RAW_DIR
from src.data_loader import DocumentLoader
from src.vector_store import HybridVectorStore
from src.rag_engine import RAGEngine

def test_pipeline():
    print("=== 1. Doküman Yükleme Testi ===")
    loader = DocumentLoader(DATA_RAW_DIR)
    chunks = loader.load_documents()
    print(f"Toplam yüklenen chunk sayısı: {len(chunks)}")
    assert len(chunks) > 0, "HATA: Hiç doküman yüklenemedi!"

    print("\n=== 2. Hibrit Vektör İndeksleme Testi ===")
    store = HybridVectorStore()
    indexed_count = store.index_documents(chunks)
    print(f"İndekslenen chunk sayısı: {indexed_count}")

    print("\n=== 3. Vektör + BM25 Arama Testi ===")
    test_query = "İMEP sigortasını kim öder?"
    results = store.search(test_query, top_k=2)
    print(f"Sorgu: '{test_query}' için bulunan sonuç sayısı: {len(results)}")
    for r in results:
        print(f" - [{r['metadata']['source']}] {r['metadata']['header']}: {r['text'][:80]}...")

    print("\n=== 4. RAG Motoru Yanıt Testi ===")
    engine = RAGEngine(store)
    response = engine.generate_response(test_query)
    print("Üretilen Yanıt:\n", response["answer"])
    print("\n=== TEST BAŞARIYLA TAMAMLANTI ===")

if __name__ == "__main__":
    test_pipeline()
