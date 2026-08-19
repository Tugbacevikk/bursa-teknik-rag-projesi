import os
from pathlib import Path
from dotenv import load_dotenv

# Load environment variables
load_dotenv()

BASE_DIR = Path(__file__).resolve().parent.parent
DATA_RAW_DIR = BASE_DIR / "data" / "raw"
DATA_PROCESSED_DIR = BASE_DIR / "data" / "processed"
CHROMA_DB_DIR = BASE_DIR / "chroma_db"

# RAG Settings
CHUNK_SIZE = 500
CHUNK_OVERLAP = 100
TOP_K_RESULTS = 4

# API Keys
GEMINI_API_KEY = os.getenv("GEMINI_API_KEY", "")

# Default Embedding & LLM Models
EMBEDDING_MODEL_NAME = "sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2"
GEMINI_MODEL_NAME = "gemini-2.5-flash"
