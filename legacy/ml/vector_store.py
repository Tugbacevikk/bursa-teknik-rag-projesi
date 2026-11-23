import os
from typing import List, Dict, Any
from rank_bm25 import BM25Okapi
import chromadb
from sentence_transformers import SentenceTransformer
from config.settings import CHROMA_DB_DIR, EMBEDDING_MODEL_NAME

class HybridVectorStore:
    def __init__(self):
        self.chroma_client = chromadb.PersistentClient(path=str(CHROMA_DB_DIR))
        self.collection = self.chroma_client.get_or_create_collection(name="btu_rag_docs")
        self.embedding_model = SentenceTransformer(EMBEDDING_MODEL_NAME)
        self.bm25 = None
        self.documents_metadata = []
        self._reranker = None

    @property
    def reranker(self):
        """CrossEncoder Re-Ranker lazy loading."""
        if self._reranker is None:
            try:
                from sentence_transformers import CrossEncoder
                self._reranker = CrossEncoder("cross-encoder/ms-marco-MiniLM-L-6-v2")
            except Exception as e:
                print(f"Re-ranker yükleme uyarısı: {e}")
        return self._reranker

    def index_documents(self, chunks: List[Dict[str, Any]]):
        """Dokümanları vektör veritabanına ve BM25 indeksine ekler."""
        if not chunks:
            return 0

        existing_count = self.collection.count()
        if existing_count > 0:
            results = self.collection.get()
            self.documents_metadata = [
                {"id": doc_id, "text": doc_text, "metadata": meta}
                for doc_id, doc_text, meta in zip(results["ids"], results["documents"], results["metadatas"])
            ]
            corpus = [doc["text"].split() for doc in self.documents_metadata]
            self.bm25 = BM25Okapi(corpus)
            return existing_count

        ids = [c["id"] for c in chunks]
        texts = [c["text"] for c in chunks]
        metadatas = [c["metadata"] for c in chunks]
        embeddings = self.embedding_model.encode(texts).tolist()

        self.collection.add(ids=ids, documents=texts, metadatas=metadatas, embeddings=embeddings)
        self.documents_metadata = chunks
        corpus = [t.split() for t in texts]
        self.bm25 = BM25Okapi(corpus)
        return len(chunks)

    def search(self, query: str, user_bolum: str = None, top_k: int = 4) -> List[Dict[str, Any]]:
        """Hibrit Arama (Dense Vector + BM25 Sparse + Re-Ranking)."""
        if not self.documents_metadata:
            return []

        # 1. Vector Search
        query_embedding = self.embedding_model.encode([query]).tolist()
        vector_results = self.collection.query(query_embeddings=query_embedding, n_results=top_k * 2)

        retrieved_ids = set()
        candidates = []

        if vector_results and vector_results["ids"]:
            for doc_id, doc_text, meta in zip(vector_results["ids"][0], vector_results["documents"][0], vector_results["metadatas"][0]):
                retrieved_ids.add(doc_id)
                candidates.append({"id": doc_id, "text": doc_text, "metadata": meta})

        # 2. BM25 Search
        if self.bm25:
            tokenized_query = query.split()
            bm25_scores = self.bm25.get_scores(tokenized_query)
            top_bm25_indices = sorted(range(len(bm25_scores)), key=lambda i: bm25_scores[i], reverse=True)[:top_k * 2]
            for idx in top_bm25_indices:
                doc = self.documents_metadata[idx]
                if doc["id"] not in retrieved_ids:
                    candidates.append(doc)
                    retrieved_ids.add(doc["id"])

        # 3. Cross-Encoder Re-Ranking
        if self.reranker and candidates:
            pairs = [[query, doc["text"]] for doc in candidates]
            scores = self.reranker.predict(pairs)
            for i, score in enumerate(scores):
                candidates[i]["rerank_score"] = float(score)
            candidates = sorted(candidates, key=lambda x: x.get("rerank_score", 0), reverse=True)

        return candidates[:top_k]
