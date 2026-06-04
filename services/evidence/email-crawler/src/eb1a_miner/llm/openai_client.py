from __future__ import annotations

import json
import os
from typing import Any

from openai import OpenAI


def _client() -> OpenAI:
    # OpenAI SDK reads OPENAI_API_KEY from env automatically, but we hard-fail if missing.
    if not os.getenv("OPENAI_API_KEY"):
        raise RuntimeError("OPENAI_API_KEY is not set. Put it in your .env and reload the shell.")
    return OpenAI()


def llm_rank_batch(*, model: str, input_text: str) -> dict[str, Any]:
    """
    Calls Responses API and expects JSON output.
    We keep parsing strict on our side: must be valid JSON.
    """
    c = _client()
    resp = c.responses.create(
        model=model,
        input=input_text,
    )

    # Responses API returns content items; easiest robust approach is grab output_text.
    text = resp.output_text
    if not text:
        raise RuntimeError("OpenAI returned empty output_text")

    # Must be JSON
    try:
        return json.loads(text)
    except json.JSONDecodeError as e:
        # Print first chunk for debugging
        head = text[:500].replace("\n", " ")
        raise RuntimeError(f"LLM did not return valid JSON. Head: {head}") from e
