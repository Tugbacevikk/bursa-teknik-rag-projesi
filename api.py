from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from typing import List, Dict, Any
import os
from src.config import DATA_RAW_DIR
from src.data_loader import DocumentLoader
from src.vector_store import HybridVectorStore
from src.rag_engine import RAGEngine

app = FastAPI(
    title="BTÜ İMEP RAG API Servisi",
    description="Bursa Teknik Üniversitesi İMEP Öğrenci Danışmanı REST API Uç Noktaları",
    version="1.0.0"
)

# Global RAG Engine Initialization
loader = DocumentLoader(DATA_RAW_DIR)
chunks = loader.load_documents()
vector_store = HybridVectorStore()
vector_store.index_documents(chunks)
rag_engine = RAGEngine(vector_store)

class QueryRequest(BaseModel):
    question: str

class SourceItem(BaseModel):
    source: str
    header: str
    text: str

class QueryResponse(BaseModel):
    answer: str
    sources: List[SourceItem]

@app.get("/")
def read_root():
    return {
        "status": "online",
        "service": "BTÜ İMEP RAG Chatbot API",
        "indexed_chunks": len(chunks)
    }

@app.post("/api/ask", response_model=QueryResponse)
def ask_question(request: QueryRequest):
    if not request.question.strip():
        raise HTTPException(status_code=400, detail="Soru boş olamaz.")
    
    result = rag_engine.generate_response(request.question)
    return result

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)
