import os
from dataclasses import dataclass


@dataclass(frozen=True)
class Settings:
    # Reported in every /embed answer; the API refuses vectors from any other model.
    embedding_model: str
    # Longer inputs are cut. Chunks are at most ~1,500 characters (~400 tokens); the
    # limit bounds memory for anything unexpected.
    max_sequence_length: int
    # Texts encoded together inside one request.
    batch_size: int
    # Texts accepted in one /embed or /rerank request.
    max_texts_per_request: int
    max_text_length: int

    # Off unless asked for: the reranker needs about 2.3 GB more memory and is slow on CPU.
    reranker_enabled: bool
    reranker_model: str
    # Question and chunk together, in tokens. A chunk is about 400 tokens; 512 leaves room
    # for the question and keeps each pair cheap.
    reranker_max_length: int
    reranker_batch_size: int

    # Answers (PLANNING Faz 4, C5). Without a key /generate answers 503; embedding and
    # reranking work as before. Use a paid key in production: on the free tier Google may
    # use what is sent to improve its products (C10).
    gemini_api_key: str
    gemini_model: str
    # Low: the answer should follow the sources, not vary from one asking to the next.
    generation_temperature: float
    generation_max_output_tokens: int
    generation_timeout_seconds: int
    # Limits on one /generate request.
    max_sources: int
    max_history: int


def load_settings() -> Settings:
    return Settings(
        embedding_model=os.getenv("EMBEDDING_MODEL", "BAAI/bge-m3"),
        max_sequence_length=int(os.getenv("EMBEDDING_MAX_SEQUENCE_LENGTH", "1024")),
        batch_size=int(os.getenv("EMBEDDING_BATCH_SIZE", "8")),
        max_texts_per_request=int(os.getenv("EMBEDDING_MAX_TEXTS", "256")),
        max_text_length=int(os.getenv("EMBEDDING_MAX_TEXT_LENGTH", "20000")),
        reranker_enabled=os.getenv("RERANKER_ENABLED", "0").strip().lower() in ("1", "true", "yes"),
        reranker_model=os.getenv("RERANKER_MODEL", "BAAI/bge-reranker-v2-m3"),
        reranker_max_length=int(os.getenv("RERANKER_MAX_LENGTH", "512")),
        reranker_batch_size=int(os.getenv("RERANKER_BATCH_SIZE", "8")),
        gemini_api_key=os.getenv("GEMINI_API_KEY", "").strip(),
        gemini_model=os.getenv("GEMINI_MODEL", "").strip(),
        generation_temperature=float(os.getenv("GENERATION_TEMPERATURE", "0.2")),
        generation_max_output_tokens=int(os.getenv("GENERATION_MAX_OUTPUT_TOKENS", "2048")),
        generation_timeout_seconds=int(os.getenv("GENERATION_TIMEOUT_SECONDS", "60")),
        max_sources=int(os.getenv("GENERATION_MAX_SOURCES", "20")),
        max_history=int(os.getenv("GENERATION_MAX_HISTORY", "10")),
    )
