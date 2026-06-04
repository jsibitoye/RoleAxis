from __future__ import annotations

import json
import os
from pathlib import Path
from typing import Any, Dict

from dotenv import load_dotenv
from openai import OpenAI


def _load_env() -> None:
    """
    Ensure OPENAI_API_KEY is available in the process environment.
    """
    if os.getenv("OPENAI_API_KEY"):
        return

    # Prefer current working directory .env
    cwd_env = Path.cwd() / ".env"
    if cwd_env.exists():
        load_dotenv(cwd_env, override=False)
        if os.getenv("OPENAI_API_KEY"):
            return

    # Fallback: project root relative to this file
    # .../src/eb1a_miner/llm/openai_batch_ranker.py -> root is 4 parents up
    proj_env = Path(__file__).resolve().parents[4] / ".env"
    if proj_env.exists():
        load_dotenv(proj_env, override=False)


def _client() -> OpenAI:
    _load_env()
    key = os.getenv("OPENAI_API_KEY")
    if not key:
        raise RuntimeError(
            "OPENAI_API_KEY is not set. Put it in .env (project root) "
            "or export it in your shell environment."
        )
    return OpenAI(api_key=key)


def _extract_json(text: str) -> Dict[str, Any]:
    """
    Extract and parse a JSON object from model output.

    We accept:
    - Pure JSON
    - JSON wrapped in ```json ... ```
    - Text that contains one JSON object somewhere
    """
    t = text.strip()

    # Strip common fenced blocks
    if t.startswith("```"):
        # remove opening fence line
        parts = t.split("\n", 1)
        t = parts[1] if len(parts) > 1 else t
        # remove closing fence
        if t.endswith("```"):
            t = t[: -3].strip()

    # Fast path: pure JSON
    try:
        obj = json.loads(t)
        if isinstance(obj, dict):
            return obj
    except Exception:
        pass

    # Slow path: find first {...} block
    start = t.find("{")
    end = t.rfind("}")
    if start != -1 and end != -1 and end > start:
        candidate = t[start : end + 1]
        obj = json.loads(candidate)
        if isinstance(obj, dict):
            return obj

    raise ValueError(f"Model did not return valid JSON. Got (first 500 chars): {text[:500]!r}")


def rank_messages_batch(*, model: str, prompt: str) -> Dict[str, Any]:
    """
    Calls OpenAI and returns a parsed JSON dict.

    gpt_fullbody_scan.py expects a dict with keys like:
      - selected: [...]
      - rejected: [...]
      - notes: ...
    """
    c = _client()

    resp = c.responses.create(
        model=model,
        # Force JSON output at the API level when possible
        text={"format": {"type": "json_object"}},
        input=prompt,
    )

    raw = resp.output_text
    parsed = _extract_json(raw)
    return parsed
