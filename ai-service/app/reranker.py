import logging
import threading

from app.config import Settings

logger = logging.getLogger(__name__)


class RerankerModel:
    """Scores how well each text answers a question, reading the two together.

    A cross-encoder: far more accurate than comparing two separately made vectors, and far
    slower, so it only reorders a short list of candidates found by the cheaper searches.
    Loaded only when enabled (RERANKER_ENABLED=1): the model takes about 2.3 GB of memory.
    Like the embedding model, it loads on a background thread.
    """

    def __init__(self, settings: Settings) -> None:
        self._settings = settings
        self._model = None
        self._error: str | None = None
        self._lock = threading.Lock()

    @property
    def enabled(self) -> bool:
        return self._settings.reranker_enabled

    @property
    def name(self) -> str:
        return self._settings.reranker_model

    @property
    def ready(self) -> bool:
        return self._model is not None

    @property
    def error(self) -> str | None:
        return self._error

    @property
    def status(self) -> str:
        if not self.enabled:
            return "disabled"
        if self.ready:
            return "ok"
        return "failed" if self._error else "loading"

    def start_loading(self) -> None:
        if self.enabled:
            threading.Thread(target=self._load, name="reranker-loader", daemon=True).start()

    def _load(self) -> None:
        try:
            from sentence_transformers import CrossEncoder

            logger.info("Loading reranker %s", self.name)
            model = CrossEncoder(self.name, device="cpu", max_length=self._settings.reranker_max_length)

            # Warm-up, so the first real request is not the slow one.
            model.predict([("hazır mı?", "hazır")], show_progress_bar=False)
            self._model = model
            logger.info("Reranker ready")
        except Exception as ex:  # noqa: BLE001 - reported through /health, not raised
            logger.exception("Could not load the reranker")
            self._error = f"{type(ex).__name__}: {ex}"

    def score(self, query: str, texts: list[str]) -> list[float]:
        # Higher is more relevant. The model's own default activation is kept; callers only
        # compare scores of the same request with each other.
        with self._lock:
            scores = self._model.predict(
                [(query, text) for text in texts],
                batch_size=self._settings.reranker_batch_size,
                show_progress_bar=False,
                convert_to_numpy=True,
            )
        return [float(s) for s in scores]
