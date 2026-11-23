import logging
from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.responses import JSONResponse, StreamingResponse
from pydantic import BaseModel, Field

from app.chat_model import GeminiChatModel
from app.config import load_settings
from app.embedding import EmbeddingModel
from app.generation import generate_events, to_ndjson
from app.prompts import Source, Turn
from app.reranker import RerankerModel

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")

settings = load_settings()
embedding_model = EmbeddingModel(settings)
reranker_model = RerankerModel(settings)
chat_model = GeminiChatModel(settings)


@asynccontextmanager
async def lifespan(_: FastAPI):
    embedding_model.start_loading()
    reranker_model.start_loading()
    yield


app = FastAPI(title="BTÜ Asistan AI Service", lifespan=lifespan)


class EmbedRequest(BaseModel):
    texts: list[str] = Field(min_length=1, max_length=settings.max_texts_per_request)


class EmbedResponse(BaseModel):
    model: str
    dimensions: int
    embeddings: list[list[float]]


class RerankRequest(BaseModel):
    query: str = Field(min_length=1)
    texts: list[str] = Field(min_length=1, max_length=settings.max_texts_per_request)


class RerankResponse(BaseModel):
    model: str
    # One per text, in the same order; higher is more relevant.
    scores: list[float]


class GenerateSource(BaseModel):
    n: int = Field(ge=1)
    title: str = Field(min_length=1)
    section: str | None = None
    text: str = Field(min_length=1, max_length=settings.max_text_length)


class GenerateTurn(BaseModel):
    role: str = Field(pattern="^(user|assistant)$")
    text: str = Field(min_length=1, max_length=settings.max_text_length)


class GenerateRequest(BaseModel):
    question: str = Field(min_length=1, max_length=settings.max_text_length)
    history: list[GenerateTurn] = Field(default_factory=list, max_length=settings.max_history)
    sources: list[GenerateSource] = Field(min_length=1, max_length=settings.max_sources)


def _not_ready() -> JSONResponse:
    if embedding_model.error:
        return JSONResponse(status_code=503, content={"status": "failed", "error": embedding_model.error})
    return JSONResponse(status_code=503, content={"status": "loading", "model": embedding_model.name})


def _too_long(texts: list[str]) -> JSONResponse | None:
    positions = [i for i, text in enumerate(texts) if len(text) > settings.max_text_length]
    if not positions:
        return None
    return JSONResponse(
        status_code=422,
        content={"detail": f"Texts longer than {settings.max_text_length} characters at positions {positions}."},
    )


# 503 until the embedding model is loaded, so Docker and the API's health check see "not
# ready yet" rather than a service that answers but cannot embed. The reranker is optional:
# its state is reported but does not make the service unhealthy.
@app.get("/health")
def health():
    if not embedding_model.ready:
        return _not_ready()
    return {
        "status": "ok",
        "model": embedding_model.name,
        "dimensions": embedding_model.dimensions,
        "reranker": reranker_model.status,
        # "not_configured": started without GEMINI_API_KEY / GEMINI_MODEL; /generate is 503.
        "generator": "ok" if chat_model.configured else "not_configured",
        "generator_model": chat_model.name or None,
    }


# Plain def, not async: FastAPI runs it on a worker thread, so a long encode does not
# block /health from answering.
@app.post("/embed", response_model=EmbedResponse)
def embed(request: EmbedRequest):
    if not embedding_model.ready:
        return _not_ready()

    if (error := _too_long(request.texts)) is not None:
        return error

    return EmbedResponse(
        model=embedding_model.name,
        dimensions=embedding_model.dimensions,
        embeddings=embedding_model.encode(request.texts),
    )


# 503 when the reranker is disabled, still loading or failed to load; the API then keeps
# the order it already has.
@app.post("/rerank", response_model=RerankResponse)
def rerank(request: RerankRequest):
    if not reranker_model.ready:
        content = {"status": reranker_model.status}
        if reranker_model.error:
            content["error"] = reranker_model.error
        return JSONResponse(status_code=503, content=content)

    if (error := _too_long(request.texts)) is not None:
        return error

    return RerankResponse(model=reranker_model.name, scores=reranker_model.score(request.query, request.texts))


# The answer as newline-delimited JSON, one event per line, written as the model writes it
# (PLANNING C5). Once the first line is out the status is 200 for good, so later failures
# arrive as an "error" event. A plain generator: Starlette iterates it on a worker thread,
# so a slow model does not hold up other requests.
@app.post("/generate")
def generate(request: GenerateRequest):
    if not chat_model.configured:
        return JSONResponse(status_code=503, content={"status": "not_configured"})

    events = generate_events(
        chat_model,
        request.question,
        [Turn(role=t.role, text=t.text) for t in request.history],
        [Source(n=s.n, title=s.title, section=s.section, text=s.text) for s in request.sources],
    )
    return StreamingResponse(to_ndjson(events), media_type="application/x-ndjson")
