import logging
import threading

from app.config import Settings

logger = logging.getLogger(__name__)


class EmbeddingModel:
    """Loads the model in the background and turns texts into unit-length vectors.

    Loading takes from seconds (cached) to many minutes (first download, ~2.3 GB), so it
    runs on its own thread: the server answers /health with "loading" meanwhile instead
    of not answering at all.
    """

    def __init__(self, settings: Settings) -> None:
        self._settings = settings
        self._model = None
        self._dimensions = 0
        self._error: str | None = None
        # One encode at a time: parallel requests would each hold a full batch of
        # activations in memory, and on CPU they would not finish sooner anyway.
        self._encode_lock = threading.Lock()

    @property
    def name(self) -> str:
        return self._settings.embedding_model

    @property
    def ready(self) -> bool:
        return self._model is not None

    @property
    def error(self) -> str | None:
        return self._error

    @property
    def dimensions(self) -> int:
        return self._dimensions

    def start_loading(self) -> None:
        threading.Thread(target=self._load, name="model-loader", daemon=True).start()

    def _load(self) -> None:
        try:
            # Imported here: the import alone takes seconds and should not delay startup.
            from sentence_transformers import SentenceTransformer

            logger.info("Loading embedding model %s", self.name)
            model = SentenceTransformer(self.name, device="cpu")
            model.max_seq_length = self._settings.max_sequence_length

            # Measured rather than asked: also warms the model up, so the first real
            # request is not the slow one.
            self._dimensions = len(model.encode(["hazır"], normalize_embeddings=True)[0])
            self._model = model
            logger.info("Embedding model ready: %d dimensions", self._dimensions)
        except Exception as ex:  # noqa: BLE001 - reported through /health, not raised
            logger.exception("Could not load the embedding model")
            self._error = f"{type(ex).__name__}: {ex}"

    def encode(self, texts: list[str]) -> list[list[float]]:
        with self._encode_lock:
            vectors = self._model.encode(
                texts,
                batch_size=self._settings.batch_size,
                # Unit length: cosine similarity then does not depend on text length.
                normalize_embeddings=True,
                convert_to_numpy=True,
                show_progress_bar=False,
            )
        return vectors.tolist()
