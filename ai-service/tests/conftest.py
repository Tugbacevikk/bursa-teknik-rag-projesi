"""Fakes for the models (PLANNING Testler, T4): no model is downloaded, Gemini is not called."""

from collections.abc import Iterator

import pytest
from fastapi.testclient import TestClient

from app import main
from app.chat_model import ChatModel, ChatModelUnavailable


class FakeChatModel(ChatModel):
    """Yields the given pieces; raises ChatModelUnavailable after `fail_after` pieces."""

    def __init__(self, pieces: list[str], fail_after: int | None = None, configured: bool = True, name: str = "fake-gemini"):
        self._pieces = pieces
        self._fail_after = fail_after
        self._configured = configured
        self._name = name
        self.prompts: list[tuple[str, str]] = []
        self.closed = False

    @property
    def name(self) -> str:
        return self._name

    @property
    def configured(self) -> bool:
        return self._configured

    def stream(self, system: str, prompt: str) -> Iterator[str]:
        self.prompts.append((system, prompt))
        try:
            for i, piece in enumerate(self._pieces):
                if self._fail_after is not None and i == self._fail_after:
                    raise ChatModelUnavailable("Gemini answered 503: overloaded")
                yield piece
            if self._fail_after is not None and self._fail_after >= len(self._pieces):
                raise ChatModelUnavailable("Gemini answered 503: overloaded")
        finally:
            self.closed = True


class FakeEmbeddingModel:
    """Ready (or not) like the real one; each vector is [text length, 1, 0]."""

    def __init__(self, ready: bool = True, error: str | None = None):
        self.ready = ready
        self.error = error
        self.name = "BAAI/bge-m3"
        self.dimensions = 3 if ready else 0

    def encode(self, texts: list[str]) -> list[list[float]]:
        return [[float(len(text)), 1.0, 0.0] for text in texts]


class FakeReranker:
    def __init__(self, status: str = "disabled", error: str | None = None):
        self.status = status
        self.error = error
        self.ready = status == "ok"
        self.name = "BAAI/bge-reranker-v2-m3"

    def score(self, query: str, texts: list[str]) -> list[float]:
        return [float(query in text) for text in texts]


@pytest.fixture
def models(monkeypatch):
    """The service's models replaced by fakes; a test changes them through the returned dict."""

    fakes = {
        "embedding": FakeEmbeddingModel(),
        "reranker": FakeReranker(),
        "chat": FakeChatModel(["Cevap [1]."]),
    }
    monkeypatch.setattr(main, "embedding_model", fakes["embedding"])
    monkeypatch.setattr(main, "reranker_model", fakes["reranker"])
    monkeypatch.setattr(main, "chat_model", fakes["chat"])

    def replace(name: str, fake) -> None:
        fakes[name] = fake
        monkeypatch.setattr(main, {"embedding": "embedding_model", "reranker": "reranker_model", "chat": "chat_model"}[name], fake)

    fakes["replace"] = replace
    return fakes


@pytest.fixture
def client(models) -> TestClient:
    # Not used as a context manager: the lifespan (which loads the real models) does not run.
    return TestClient(main.app)
