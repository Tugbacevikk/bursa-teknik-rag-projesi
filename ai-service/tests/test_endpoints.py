"""The service's HTTP endpoints, with the models faked."""

import json

from app.main import settings
from tests.conftest import FakeChatModel, FakeEmbeddingModel, FakeReranker

GENERATE = {
    "question": "Sigortayı kim öder?",
    "history": [{"role": "user", "text": "Merhaba"}, {"role": "assistant", "text": "Buyurun."}],
    "sources": [{"n": 1, "title": "İMEP Yönergesi", "section": "Sigorta", "text": "Rektörlük karşılar."}],
}


# 503 until the model is loaded, so Docker and the API see "not ready yet".
def test_health_is_503_while_loading(client, models):
    models["replace"]("embedding", FakeEmbeddingModel(ready=False))

    response = client.get("/health")

    assert response.status_code == 503
    assert response.json()["status"] == "loading"


def test_health_reports_a_failed_load(client, models):
    models["replace"]("embedding", FakeEmbeddingModel(ready=False, error="OSError: disk full"))

    response = client.get("/health")

    assert response.status_code == 503
    assert response.json() == {"status": "failed", "error": "OSError: disk full"}


def test_health_when_ready_names_the_models(client):
    response = client.get("/health")

    assert response.status_code == 200
    assert response.json() == {
        "status": "ok",
        "model": "BAAI/bge-m3",
        "dimensions": 3,
        "reranker": "disabled",
        "generator": "ok",
        "generator_model": "fake-gemini",
    }


# Without GEMINI_API_KEY the rest of the service works; only answers are missing.
def test_health_without_a_chat_model_says_not_configured(client, models):
    models["replace"]("chat", FakeChatModel([], configured=False, name=""))

    body = client.get("/health").json()

    assert body["generator"] == "not_configured"
    assert body["generator_model"] is None


def test_embed_returns_one_vector_per_text_with_the_model(client):
    response = client.post("/embed", json={"texts": ["bir", "üç harf"]})

    assert response.status_code == 200
    assert response.json() == {"model": "BAAI/bge-m3", "dimensions": 3, "embeddings": [[3.0, 1.0, 0.0], [7.0, 1.0, 0.0]]}


def test_embed_is_503_while_loading(client, models):
    models["replace"]("embedding", FakeEmbeddingModel(ready=False))

    assert client.post("/embed", json={"texts": ["x"]}).status_code == 503


def test_embed_refuses_too_long_texts_naming_them(client):
    texts = ["kısa", "x" * (settings.max_text_length + 1), "kısa", "y" * (settings.max_text_length + 1)]

    response = client.post("/embed", json={"texts": texts})

    assert response.status_code == 422
    assert "[1, 3]" in response.json()["detail"]


def test_embed_refuses_no_texts_and_too_many(client):
    assert client.post("/embed", json={"texts": []}).status_code == 422
    assert client.post("/embed", json={"texts": ["x"] * (settings.max_texts_per_request + 1)}).status_code == 422


def test_rerank_is_503_while_disabled(client):
    response = client.post("/rerank", json={"query": "sigorta", "texts": ["a"]})

    assert response.status_code == 503
    assert response.json() == {"status": "disabled"}


def test_rerank_scores_when_ready(client, models):
    models["replace"]("reranker", FakeReranker(status="ok"))

    response = client.post("/rerank", json={"query": "sigorta", "texts": ["sigorta primi", "takvim"]})

    assert response.json() == {"model": "BAAI/bge-reranker-v2-m3", "scores": [1.0, 0.0]}


def test_generate_without_a_chat_model_is_503(client, models):
    models["replace"]("chat", FakeChatModel([], configured=False))

    assert client.post("/generate", json=GENERATE).status_code == 503


def test_generate_streams_ndjson_events(client, models):
    models["replace"]("chat", FakeChatModel(["Sigortayı ", "Rektörlük karşılar [1]."]))

    response = client.post("/generate", json=GENERATE)

    assert response.status_code == 200
    assert response.headers["content-type"].startswith("application/x-ndjson")
    events = [json.loads(line) for line in response.text.splitlines()]
    assert events[-1] == {"type": "done", "model": "fake-gemini"}
    assert "".join(e["text"] for e in events if e["type"] == "delta") == "Sigortayı Rektörlük karşılar [1]."


def test_generate_sends_history_and_sources_to_the_model(client, models):
    model = FakeChatModel(["Cevap [1]."])
    models["replace"]("chat", model)

    client.post("/generate", json=GENERATE)

    prompt = model.prompts[0][1]
    assert '<mesaj rol="ogrenci">Merhaba</mesaj>' in prompt
    assert '<kaynak n="1" belge="İMEP Yönergesi" bolum="Sigorta">' in prompt


def test_generate_failure_arrives_as_an_error_event(client, models):
    models["replace"]("chat", FakeChatModel(["x"], fail_after=0))

    response = client.post("/generate", json=GENERATE)

    assert response.status_code == 200
    assert [json.loads(line)["type"] for line in response.text.splitlines()] == ["error"]


def test_generate_validates_the_request(client):
    def post(**change):
        return client.post("/generate", json={**GENERATE, **change}).status_code

    assert post(question="") == 422
    assert post(sources=[]) == 422
    assert post(sources=[{"n": 0, "title": "T", "text": "x"}]) == 422
    assert post(history=[{"role": "system", "text": "x"}]) == 422
    assert post(history=[{"role": "user", "text": "x"}] * (settings.max_history + 1)) == 422
