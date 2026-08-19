import os
from pathlib import Path
from typing import List, Dict, Any
import chromadb
from rank_bm25 import BM25Okapi
from sentence_transformers import SentenceTransformer
from src.config import CHROMA_DB_DIR, EMBEDDING_MODEL_NAME, TOP_K_RESULTS

class HybridVectorStore:
    def __init__(self, persist_dir: Path = CHROMA_DB_DIR):
        self.persist_dir = persist_dir
        self.persist_dir.mkdir(parents=True, exist_ok=True)
        
        # Initialize ChromaDB Persistent Client
        self.client = chromadb.PersistentClient(path=str(self.persist_dir))
        self.collection = self.client.get_or_create_collection(
            name="btu_imep_collection"
        )
        
        # Initialize Embedding Model
        self.embedder = SentenceTransformer(EMBEDDING_MODEL_NAME)
        
        # BM25 Sparse Index Variables
        self.bm25 = None
        self.documents_cache = []

    def index_documents(self, chunks: List[Dict[str, Any]]) -> int:
        if not chunks:
            return 0

        ids = [c["id"] for c in chunks]
        texts = [c["text"] for c in chunks]
        metadatas = [c["metadata"] for c in chunks]

        # Generate dense embeddings
        embeddings = self.embedder.encode(texts, show_progress_bar=False).tolist()

        # Add to ChromaDB
        self.collection.upsert(
            ids=ids,
            documents=texts,
            embeddings=embeddings,
            metadatas=metadatas
        )

        # Build BM25 Index
        self.documents_cache = chunks
        tokenized_corpus = [self._tokenize(t) for t in texts]
        self.bm25 = BM25Okapi(tokenized_corpus)

        return len(chunks)

    def _tokenize(self, text: str) -> List[str]:
        # Basic Turkish-aware normalization & tokenization
        text = text.lower().replace('ı', 'i').replace('ğ', 'g').replace('ü', 'u').replace('ş', 's').replace('ö', 'o').replace('ç', 'c')
        return [w for w in text.split() if len(w) > 2]

    def search(self, query: str, top_k: int = TOP_K_RESULTS) -> List[Dict[str, Any]]:
        if self.collection.count() == 0:
            return []

        # 1. Vector Search
        query_embedding = self.embedder.encode([query]).tolist()
        vector_results = self.collection.query(
            query_embeddings=query_embedding,
            n_results=min(top_k * 2, self.collection.count())
        )

        vector_docs = []
        if vector_results and vector_results.get("documents"):
            docs = vector_results["documents"][0]
            metas = vector_results["metadatas"][0]
            ids = vector_results["ids"][0]
            for doc_id, doc_text, meta in zip(ids, docs, metas):
                vector_docs.append({
                    "id": doc_id,
                    "text": doc_text,
                    "metadata": meta
                })

        # If BM25 is not built yet, return vector search results
        if not self.bm25 or not self.documents_cache:
            return vector_docs[:top_k]

        # 2. BM25 Search
        tokenized_query = self._tokenize(query)
        bm25_scores = self.bm25.get_scores(tokenized_query)
        top_bm25_indices = sorted(range(len(bm25_scores)), key=lambda i: bm25_scores[i], reverse=True)[:top_k * 2]

        bm25_docs = [self.documents_cache[i] for i in top_bm25_indices if bm25_scores[i] > 0]

        # 3. Reciprocal Rank Fusion (RRF)
        rrf_scores: Dict[str, float] = {}
        doc_map: Dict[str, Dict[str, Any]] = {}

        k_rrf = 60
        for rank, item in enumerate(vector_docs):
            doc_id = item["id"]
            doc_map[doc_id] = item
            rrf_scores[doc_id] = rrf_scores.get(doc_id, 0.0) + (1.0 / (k_rrf + rank + 1))

        for rank, item in enumerate(bm25_docs):
            doc_id = item["id"]
            doc_map[doc_id] = item
            rrf_scores[doc_id] = rrf_scores.get(doc_id, 0.0) + (1.0 / (k_rrf + rank + 1))

        # Sort by RRF score
        sorted_ids = sorted(rrf_scores.keys(), key=lambda d: rrf_scores[d], reverse=True)
        final_results = [doc_map[doc_id] for doc_id in sorted_ids[:top_k]]

        return final_results
