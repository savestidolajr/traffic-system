#!/usr/bin/env python3
"""
Focused tests for scripts/export-prompts.py's capture-parsing functions. No existing Python
test harness exists in this repo (no pytest.ini/pyproject.toml, no conftest.py, no other
scripts/test_*.py) — this file is deliberately self-contained: it loads export-prompts.py by
path (the filename is hyphenated, so it cannot be `import`ed normally) and exercises pure
functions directly, with no network/Azure CLI/local-host dependency.

Run:
    python3 -m pytest scripts/test_export_prompts.py -v

or, without pytest:
    python3 scripts/test_export_prompts.py
"""

from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
from pathlib import Path

_SCRIPT_PATH = Path(__file__).resolve().parent / "export-prompts.py"
_spec = importlib.util.spec_from_file_location("export_prompts", _SCRIPT_PATH)
assert _spec is not None and _spec.loader is not None
export_prompts = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(export_prompts)


# ─── extract_response_text: Responses API shape, reasoning before message ──

def test_extract_response_text_message_after_reasoning_item():
    """The exact regression shape from job e62fffd5 (2026-07): a reasoning item with no
    visible content is emitted BEFORE the message item that carries the actual verdict text.
    Before the fix, SUMMARY.md reported review verdicts as "?" and 0 issues because the parser
    did not walk every output item for content[*].text — it must return the message's text,
    not empty text from (or because of) the leading reasoning item."""
    verdict_text = (
        '{"verdict":"NEEDS_REVISION","issues":["Smith Street Motorway eastbound invents a '
        'direction not in the data."]}'
    )
    resp_json = json.dumps({
        "output": [
            {"id": "rs_1", "type": "reasoning", "content": [], "summary": []},
            {
                "id": "msg_1",
                "type": "message",
                "status": "completed",
                "role": "assistant",
                "content": [
                    {"type": "output_text", "annotations": [], "logprobs": [], "text": verdict_text}
                ],
            },
        ]
    })

    assert export_prompts.extract_response_text(resp_json) == verdict_text


def test_extract_response_text_reasoning_only_no_message_returns_empty():
    """A reasoning item with no following message (e.g. a refused/incomplete turn) must not
    raise or fabricate text — a naive `output[0]` read would still find the reasoning item and
    could crash on missing keys or return the wrong content; the correct result is empty."""
    resp_json = json.dumps({
        "output": [{"id": "rs_1", "type": "reasoning", "content": [], "summary": []}]
    })

    assert export_prompts.extract_response_text(resp_json) == ""


def test_extract_response_text_multiple_message_content_chunks_are_joined():
    resp_json = json.dumps({
        "output": [
            {"id": "rs_1", "type": "reasoning", "content": [], "summary": []},
            {
                "id": "msg_1",
                "type": "message",
                "content": [
                    {"type": "output_text", "text": "Part one."},
                    {"type": "output_text", "text": "Part two."},
                ],
            },
        ]
    })

    assert export_prompts.extract_response_text(resp_json) == "Part one.\nPart two."


def test_extract_response_text_chat_completions_shape_still_supported():
    """Older captures (pre-Responses-API) used the Chat Completions `choices[0].message.content`
    shape — the docstring promises both are handled, so the reasoning-item fix must not have
    dropped support for it."""
    resp_json = json.dumps({
        "choices": [{"message": {"content": "Legacy chat-completions text."}}]
    })

    assert export_prompts.extract_response_text(resp_json) == "Legacy chat-completions text."


def test_extract_response_text_malformed_json_returns_empty():
    assert export_prompts.extract_response_text("not json") == ""


# ─── End-to-end: read_capture_file + parse_capture_semantics ───────────────

def test_read_capture_file_extracts_review_verdict_after_reasoning_item():
    """The literal SUMMARY.md regression: a Review-stage capture whose ResponseBody has a
    reasoning item before the message must still yield a populated parsedVerdict (real verdict
    + real issues), not the "?" / 0-issues placeholder the bug produced."""
    verdict_text = json.dumps({
        "verdict": "NEEDS_REVISION",
        "issues": ["Pacific Motorway northbound is not supported by the source data."],
    })
    response_body = json.dumps({
        "output": [
            {"id": "rs_1", "type": "reasoning", "content": [], "summary": []},
            {
                "id": "msg_1",
                "type": "message",
                "content": [{"type": "output_text", "text": verdict_text}],
            },
        ]
    })
    capture = {
        "JobId": "job-1",
        "Stage": "Review",
        "Attempt": 1,
        "Model": "gpt-5.6-luna",
        "SystemInstructions": "You are a reviewer.",
        "RenderedPrompt": "Review this script.",
        "RequestBody": "{}",
        "ResponseBody": response_body,
        "ElapsedMs": 1234,
        "TimestampUtc": "2026-07-08T00:00:00Z",
    }

    with tempfile.TemporaryDirectory() as tmp:
        path = Path(tmp) / "capture.json"
        path.write_text(json.dumps(capture), encoding="utf-8")

        parsed = export_prompts.read_capture_file(path)
        assert parsed is not None
        assert parsed["responseText"] == verdict_text

        export_prompts.parse_capture_semantics(parsed)
        assert parsed["parsedVerdict"]["verdict"] == "NEEDS_REVISION"
        assert parsed["parsedVerdict"]["issues"] == [
            "Pacific Motorway northbound is not supported by the source data."
        ]


if __name__ == "__main__":
    failures = 0
    tests = [v for k, v in sorted(globals().items()) if k.startswith("test_") and callable(v)]
    for test in tests:
        try:
            test()
            print(f"PASS {test.__name__}")
        except AssertionError as exc:
            failures += 1
            print(f"FAIL {test.__name__}: {exc}")
    if failures:
        print(f"{failures}/{len(tests)} tests failed")
        sys.exit(1)
    print(f"All {len(tests)} tests passed")
