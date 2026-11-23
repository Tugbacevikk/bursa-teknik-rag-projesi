import logging
from abc import ABC, abstractmethod
from collections.abc import Iterator

from app.config import Settings

logger = logging.getLogger(__name__)


class ChatModelUnavailable(Exception):
    """The provider could not be reached, refused the request or stopped answering."""


class ChatModel(ABC):
    """Writes an answer from instructions and a prompt, piece by piece (PLANNING #11, C5).

    The rest of the service only sees this interface: moving to another provider or a
    local model means one new class here.
    """

    @property
    @abstractmethod
    def name(self) -> str: ...

    # False when the service was started without what the provider needs (e.g. an API key);
    # /generate then answers 503 and /health reports it.
    @property
    @abstractmethod
    def configured(self) -> bool: ...

    # Yields the answer's text as it is written. Raises ChatModelUnavailable.
    @abstractmethod
    def stream(self, system: str, prompt: str) -> Iterator[str]: ...


class GeminiChatModel(ChatModel):
    def __init__(self, settings: Settings) -> None:
        self._settings = settings
        self._client = None
        if self.configured:
            from google import genai
            from google.genai import types

            self._client = genai.Client(
                api_key=settings.gemini_api_key,
                http_options=types.HttpOptions(timeout=settings.generation_timeout_seconds * 1000),
            )

    @property
    def name(self) -> str:
        return self._settings.gemini_model

    @property
    def configured(self) -> bool:
        return bool(self._settings.gemini_api_key and self._settings.gemini_model)

    def stream(self, system: str, prompt: str) -> Iterator[str]:
        from google.genai import errors, types

        config = types.GenerateContentConfig(
            system_instruction=system,
            temperature=self._settings.generation_temperature,
            max_output_tokens=self._settings.generation_max_output_tokens,
        )
        try:
            for chunk in self._client.models.generate_content_stream(
                model=self.name, contents=prompt, config=config
            ):
                # A chunk can carry no text (e.g. only usage data at the end).
                if chunk.text:
                    yield chunk.text
        except errors.APIError as ex:
            # The key, the model name, a quota or Google's side; the message says which.
            logger.warning("Gemini refused the request: %s %s", ex.code, ex.message)
            raise ChatModelUnavailable(f"Gemini answered {ex.code}: {ex.message}") from ex
        except Exception as ex:  # noqa: BLE001 - network errors and timeouts of the client
            logger.warning("Gemini could not be reached: %s", ex)
            raise ChatModelUnavailable(f"Gemini could not be reached: {type(ex).__name__}") from ex
