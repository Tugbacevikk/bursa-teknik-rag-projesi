import json
from collections.abc import Iterator

from app.chat_model import ChatModel, ChatModelUnavailable
from app.prompts import NOT_FOUND, SYSTEM_PROMPT, Source, Turn, build_prompt

# Markdown the model may wrap the marker in ("**BULUNAMADI**").
_DECORATION = " \t\r\n*_`."


def generate_events(model: ChatModel, question: str, history: list[Turn], sources: list[Source]) -> Iterator[dict]:
    """The answer as events (PLANNING C5): delta*, then done; or not_found; or error.

    The first characters are held back until it is clear whether the model wrote the
    not-found marker, so a student never sees "BULUNAMADI" flash on the screen.
    """
    prompt = build_prompt(question, history, sources)
    pieces = model.stream(SYSTEM_PROMPT, prompt)

    try:
        held = ""
        for piece in pieces:
            held += piece
            if len(held.lstrip(_DECORATION)) >= len(NOT_FOUND):
                break

        if held.strip(_DECORATION).startswith(NOT_FOUND):
            yield {"type": "not_found", "model": model.name}
            return

        # The model can end without writing anything (e.g. a safety filter or the output
        # limit cut it off before the first token). "done" would store an empty answer as
        # if it were one, so it is reported as an error instead.
        wrote = bool(held.strip())
        if held:
            yield {"type": "delta", "text": held}
        for piece in pieces:
            wrote = wrote or bool(piece.strip())
            yield {"type": "delta", "text": piece}

        if not wrote:
            yield {"type": "error", "message": "The chat model ended without writing an answer."}
            return

        yield {"type": "done", "model": model.name}
    except ChatModelUnavailable as ex:
        # The answer may have started already; the API drops the partial answer.
        yield {"type": "error", "message": str(ex)}
    finally:
        pieces.close()


def to_ndjson(events: Iterator[dict]) -> Iterator[str]:
    for event in events:
        yield json.dumps(event, ensure_ascii=False) + "\n"
