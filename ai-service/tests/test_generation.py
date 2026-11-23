"""The answer as events (PLANNING C5, C7): delta*, then done; or not_found; or error."""

import json

import pytest

from app.generation import generate_events, to_ndjson
from app.prompts import NOT_FOUND, SYSTEM_PROMPT, Source
from tests.conftest import FakeChatModel

SOURCES = [Source(1, "İMEP Yönergesi", "Sigorta", "Rektörlük karşılar.")]


def events(model: FakeChatModel) -> list[dict]:
    return list(generate_events(model, "Sigortayı kim öder?", [], SOURCES))


def test_answer_streams_as_deltas_then_done():
    model = FakeChatModel(["Sigortayı ", "Rektörlük ", "karşılar [1]."])

    result = events(model)

    assert result[-1] == {"type": "done", "model": "fake-gemini"}
    deltas = [e for e in result if e["type"] == "delta"]
    assert "".join(e["text"] for e in deltas) == "Sigortayı Rektörlük karşılar [1]."
    assert len(deltas) >= 2
    assert model.prompts[0][0] == SYSTEM_PROMPT


@pytest.mark.parametrize(
    "pieces",
    [
        [NOT_FOUND],
        ["BULU", "NAMADI"],
        [f"**{NOT_FOUND}**"],
        ["  \n", NOT_FOUND, "."],
        [f"`{NOT_FOUND}`"],
    ],
)
def test_not_found_marker_becomes_not_found_and_is_never_shown(pieces):
    result = events(FakeChatModel(pieces))

    assert result == [{"type": "not_found", "model": "fake-gemini"}]


# Text that starts like the marker but is an answer is passed on whole.
def test_answer_starting_like_the_marker_is_an_answer():
    result = events(FakeChatModel(["Bulunan ", "belgelere göre [1]."]))

    assert "".join(e["text"] for e in result if e["type"] == "delta") == "Bulunan belgelere göre [1]."
    assert result[-1]["type"] == "done"


def test_answer_shorter_than_the_marker_is_an_answer():
    result = events(FakeChatModel(["Evet [1]."]))

    assert result == [{"type": "delta", "text": "Evet [1]."}, {"type": "done", "model": "fake-gemini"}]


# While the start is held back nothing reaches the student; the error says why.
def test_failure_before_anything_is_shown_is_only_an_error():
    result = events(FakeChatModel(["Sigor"], fail_after=1))

    assert result == [{"type": "error", "message": "Gemini answered 503: overloaded"}]


# After some text the error still ends the answer; the API drops the partial text (C9).
def test_failure_mid_answer_ends_with_an_error():
    result = events(FakeChatModel(["Sigortayı Rektörlük ", "karşılar", " ve ayrıca"], fail_after=2))

    assert result[-1] == {"type": "error", "message": "Gemini answered 503: overloaded"}
    assert all(e["type"] != "done" for e in result)


def test_model_stream_is_closed_however_it_ends():
    for model in (FakeChatModel(["Cevap [1]."]), FakeChatModel([NOT_FOUND, "fazlası"]), FakeChatModel(["x"], fail_after=0)):
        events(model)
        assert model.closed


def test_ndjson_is_one_json_object_per_line_with_turkish_letters_as_is():
    lines = list(to_ndjson(iter([{"type": "delta", "text": "Öğrenci işleri"}, {"type": "done", "model": "m"}])))

    assert lines == ['{"type": "delta", "text": "Öğrenci işleri"}\n', '{"type": "done", "model": "m"}\n']
    assert [json.loads(line)["type"] for line in lines] == ["delta", "done"]


@pytest.mark.parametrize("pieces", [[], [""], ["  ", "\n"]])
def test_an_answer_with_no_text_is_an_error_not_done(pieces):
    # e.g. Gemini's safety filter stops the answer before the first token: an empty
    # "done" would be stored and shown as an (empty) answer.
    result = events(FakeChatModel(pieces))

    assert result[-1]["type"] == "error"
    assert not any(e["type"] == "done" for e in result)
