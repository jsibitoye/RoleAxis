from __future__ import annotations

import argparse
import json
import re
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Optional, Callable, TypeVar

from eb1a_miner.config import Settings
from eb1a_miner.graph.auth import get_access_token
from eb1a_miner.graph.mail import iter_messages
from eb1a_miner.graph.conversations import fetch_thread_previews, get_message_conversation_id
from eb1a_miner.graph.message_body import get_full_body_text
from eb1a_miner.llm.openai_batch_ranker import rank_messages_batch


SKIP_SENDERS = {
    "support@fieldnation.com",
    "cargurus@mail.cargurus.com",
    "messages-noreply@linkedin.com",
    "no-reply@tumblr.com",
    "usbank@notifications.usbank.com",
    "noreply@glassdoor.com",
    "uber@uber.com",
    "smartoption@soslprospect.salliemae.com",
    "hi@myworkmarket.com",
    "no-reply@e.siriusxm.com",
    "webmaster@fastweb.com",
    "linkedin@em.linkedin.com",
    "noreply@glassdoor.com",
}


def _parse_dt(s: str | None) -> datetime | None:
    if not s:
        return None
    s = s.strip()
    if len(s) == 10:
        dt = datetime.fromisoformat(s)
        return dt.replace(tzinfo=timezone.utc)
    if s.endswith("Z"):
        s = s[:-1] + "+00:00"
    dt = datetime.fromisoformat(s)
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=timezone.utc)
    return dt


def _norm_email(e: str | None) -> str:
    return (e or "").strip().lower()


def _clean_text(t: str) -> str:
    t = (t or "").replace("\r\n", "\n").replace("\r", "\n")
    t = re.sub(r"\n{4,}", "\n\n\n", t)
    return t.strip()


def _truncate(t: str, max_chars: int) -> str:
    if len(t) <= max_chars:
        return t
    return t[:max_chars] + "\n\n[TRUNCATED]\n"


def _load_processed_ids(path: Path) -> set[str]:
    if not path.exists():
        return set()
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
        if isinstance(data, list):
            return {str(x) for x in data}
    except Exception:
        return set()
    return set()


def _save_processed_ids(path: Path, ids: set[str]) -> None:
    path.write_text(json.dumps(sorted(list(ids))), encoding="utf-8")


def _append_jsonl(path: Path, obj: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as f:
        f.write(json.dumps(obj, ensure_ascii=False) + "\n")


def _extract_json_obj(text: str) -> dict[str, Any]:
    """
    rank_messages_batch() should return dict, but sometimes returns raw text.
    This tries:
      1) json.loads(text)
      2) first {...} block
    """
    text = (text or "").strip()
    if not text:
        return {}

    try:
        obj = json.loads(text)
        return obj if isinstance(obj, dict) else {}
    except Exception:
        pass

    m = re.search(r"\{.*\}", text, flags=re.DOTALL)
    if not m:
        return {}
    try:
        obj = json.loads(m.group(0))
        return obj if isinstance(obj, dict) else {}
    except Exception:
        return {}


def _ensure_result_dict(result: Any) -> dict[str, Any]:
    if isinstance(result, dict):
        return result
    if isinstance(result, str):
        return _extract_json_obj(result)
    return {}


def _batch_prompt(messages: list[dict[str, Any]]) -> str:
    schema = {
        "selected": [
            {
                "messageId": "string",
                "conversationId": "string|null",
                "score": 0,
                "category": "leadership|critical_role|recognition|speaking_media|judging_reviewing|membership|awards|publications|contracts|other|none",
                "why": "grounded explanation using email text only",
                "evidence_quotes": ["exact quotes copied from email body text only"],
                "webLink": "string",
            }
        ],
        "rejected_count": 0,
        "notes": "optional",
    }

    instructions = f"""
You are screening emails for evidence that could support an EB-1A petition.

Rules (strict):
- Use ONLY the email content provided.
- Do NOT invent facts.
- Evidence quotes MUST be exact text copied from the provided body_text.
- If uncertain or weak, reject.
- Prefer: leadership/authority, critical role, recognition, speaking invites, panels, judging/reviewing, awards, publications, contracts, senior praise, approvals, escalations.
- Return STRICT JSON only (no markdown, no extra text).
- Keep evidence_quotes short and high-signal (1-3 quotes per selected email).

Return JSON matching:
{json.dumps(schema, indent=2)}

Now evaluate this batch of emails:
""".strip()

    payload = {"emails": messages}
    return instructions + "\n\n" + json.dumps(payload, ensure_ascii=False)


def _thread_prompt(thread: dict[str, Any]) -> str:
    schema = {
        "conversationId": "string",
        "overall_score": 0,
        "recommended_use": ["leadership", "critical_role", "recognition"],
        "why": "grounded explanation",
        "evidence_quotes": ["exact quotes copied from thread bodies only"],
        "top_webLink": "string",
    }

    instructions = f"""
You are extracting the strongest EB-1A evidence from a single email conversation thread.

Rules (strict):
- Use ONLY the content provided below.
- Do NOT invent facts.
- Evidence quotes MUST be exact text copied from body_text fields.
- Return STRICT JSON only.

Return JSON matching:
{json.dumps(schema, indent=2)}
""".strip()

    return instructions + "\n\n" + json.dumps(thread, ensure_ascii=False)


def _iso(dt: Optional[datetime]) -> str:
    if not dt:
        return ""
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=timezone.utc)
    return dt.isoformat()


def _is_graph_401(err: Exception) -> bool:
    s = str(err)
    return (
        "Graph error 401" in s
        or "InvalidAuthenticationToken" in s
        or "IDX14100" in s  # JWT is not well formed, no dots
    )


def _looks_like_jwt(tok: str | None) -> bool:
    # Access tokens from AAD are typically JWTs and contain dots.
    # If we got a token with no dots, Graph rejects it with IDX14100.
    if not tok:
        return False
    return "." in tok


T = TypeVar("T")


def run(
    *,
    since: datetime | None,
    until: datetime | None,
    batch_count: int,
    batch_char_budget: int,
    per_email_char_cap: int,
    model: str,
) -> None:
    settings = Settings()

    token_box: dict[str, str] = {"token": ""}

    def refresh_token(reason: str) -> None:
        _append_jsonl(
            Path(settings.reports_dir) / "EB1A_GPT_fullbody_scan_events.jsonl",
            {"ts": datetime.now(timezone.utc).isoformat(), "event": "refresh_token", "reason": reason},
        )
        tok = get_access_token(settings)
        # If auth returned a non-JWT-ish token, try one more time.
        if not _looks_like_jwt(tok):
            tok2 = get_access_token(settings)
            tok = tok2
        token_box["token"] = tok

    # initial token
    refresh_token("startup")

    settings.reports_dir.mkdir(parents=True, exist_ok=True)

    out_batches = Path(settings.reports_dir) / "EB1A_GPT_fullbody_batches.jsonl"
    out_threads = Path(settings.reports_dir) / "EB1A_GPT_fullbody_threads.jsonl"
    processed_ids_path = Path(settings.reports_dir) / "EB1A_GPT_processed_message_ids.json"

    processed = _load_processed_ids(processed_ids_path)

    folders = ["inbox", "sentitems"]

    batch: list[dict[str, Any]] = []
    batch_chars = 0

    def call_graph(fn: Callable[..., T], *args: Any, **kwargs: Any) -> T:
        """
        Wrap any Graph call that uses token.
        If token expired mid-run, refresh token and retry.
        """
        attempts = 0
        last_err: Exception | None = None
        while attempts < 3:
            attempts += 1
            try:
                return fn(token_box["token"], *args, **kwargs)
            except RuntimeError as e:
                last_err = e
                if _is_graph_401(e):
                    refresh_token(f"graph_401_retry_{attempts}")
                    continue
                raise
            except Exception as e:
                # Non-401 errors should not trigger token refresh.
                raise
        raise RuntimeError(f"Graph call failed after retries: {last_err}")

    def flush_batch() -> None:
        nonlocal batch, batch_chars, processed

        if not batch:
            return

        prompt = _batch_prompt(batch)
        raw = rank_messages_batch(model=model, prompt=prompt)
        result = _ensure_result_dict(raw)

        _append_jsonl(
            out_batches,
            {"batch_meta": {"count": len(batch), "chars": batch_chars}, "result": result, "raw_type": type(raw).__name__},
        )

        # Thread expansion for selected items
        selected = result.get("selected", [])
        if isinstance(selected, list):
            for item in selected:
                if not isinstance(item, dict):
                    continue

                cid = item.get("conversationId")
                if not cid:
                    continue

                try:
                    previews = call_graph(fetch_thread_previews, cid, max_messages=25)
                except Exception as e:
                    _append_jsonl(out_threads, {"conversationId": cid, "error": str(e), "messages": []})
                    continue

                thread_msgs: list[dict[str, Any]] = []
                top_link = item.get("webLink", "") or ""

                for p in previews:
                    mid = getattr(p, "id", None)
                    if not mid:
                        continue

                    try:
                        _, t_body = call_graph(get_full_body_text, mid)
                    except Exception:
                        continue

                    t_body = _truncate(_clean_text(t_body), per_email_char_cap)
                    if not t_body:
                        continue

                    thread_msgs.append(
                        {
                            "messageId": mid,
                            "receivedUTC": _iso(getattr(p, "received_dt", None)),
                            "fromName": getattr(p, "from_name", "") or "",
                            "fromEmail": _norm_email(getattr(p, "from_email", None)),
                            "subject": getattr(p, "subject", "") or "",
                            "webLink": "",  # not available from thread preview call
                            "body_text": t_body,
                        }
                    )

                thread_obj = {
                    "conversationId": cid,
                    "top_webLink": top_link,
                    "messages": thread_msgs,
                }

                t_prompt = _thread_prompt(thread_obj)
                t_raw = rank_messages_batch(model=model, prompt=t_prompt)
                t_result = _ensure_result_dict(t_raw)

                _append_jsonl(out_threads, {"conversationId": cid, "result": t_result, "raw_type": type(t_raw).__name__})

        _save_processed_ids(processed_ids_path, processed)
        batch = []
        batch_chars = 0

    def iter_messages_resilient(folder: str):
        """
        Generator wrapper that restarts iter_messages() if token expires mid-pagination.
        It relies on processed_ids to avoid duplicates.
        """
        while True:
            try:
                yield from iter_messages(
                    token_box["token"],
                    folder=folder,
                    top=25,
                    since=since,
                    until=until,
                    select="id,conversationId,subject,receivedDateTime,from,toRecipients,ccRecipients,webLink",
                    max_pages=None,
                )
                return
            except RuntimeError as e:
                if _is_graph_401(e):
                    refresh_token("iter_messages_401")
                    continue
                raise

    for folder in folders:
        for msg in iter_messages_resilient(folder):
            if not msg.id or msg.id in processed:
                continue

            sender_email = _norm_email(msg.from_.address if msg.from_ else None)
            if sender_email in SKIP_SENDERS:
                processed.add(msg.id)
                continue

            # conversationId sometimes missing from list response; recover it
            conversation_id = msg.conversation_id
            if not conversation_id:
                try:
                    conversation_id = call_graph(get_message_conversation_id, msg.id)
                except Exception:
                    conversation_id = None

            # Fetch full body text
            try:
                _, body_text = call_graph(get_full_body_text, msg.id)
            except Exception:
                processed.add(msg.id)
                continue

            body_text = _clean_text(body_text)
            if not body_text:
                processed.add(msg.id)
                continue

            body_text = _truncate(body_text, per_email_char_cap)

            rec = {
                "messageId": msg.id,
                "conversationId": conversation_id,
                "folder": folder,
                "receivedUTC": msg.received_dt.isoformat() if msg.received_dt else "",
                "subject": msg.subject or "",
                "fromName": msg.from_.name if msg.from_ else "",
                "fromEmail": sender_email,
                "webLink": msg.web_link or "",
                "body_text": body_text,
            }

            batch.append(rec)
            batch_chars += len(body_text)
            processed.add(msg.id)

            if len(batch) >= batch_count or batch_chars >= batch_char_budget:
                flush_batch()

                # tiny pause to avoid hammering Graph endlessly overnight
                time.sleep(0.2)

    flush_batch()
    _save_processed_ids(processed_ids_path, processed)


def main() -> None:
    ap = argparse.ArgumentParser(description="Full-body GPT scan of Inbox+Sent, batched, with thread expansion.")
    ap.add_argument("--since", default=None, help="Start date/time (YYYY-MM-DD or ISO). Optional.")
    ap.add_argument("--until", default=None, help="End date/time (YYYY-MM-DD or ISO). Optional.")
    ap.add_argument("--batch-count", type=int, default=50, help="Max emails per GPT request.")
    ap.add_argument("--batch-char-budget", type=int, default=120000, help="Max total body chars per request.")
    ap.add_argument("--per-email-char-cap", type=int, default=6000, help="Max chars per email body sent to GPT.")
    ap.add_argument("--model", default="gpt-5", help="OpenAI model (default gpt-5).")
    args = ap.parse_args()

    since = _parse_dt(args.since)
    until = _parse_dt(args.until)

    run(
        since=since,
        until=until,
        batch_count=max(10, min(150, int(args.batch_count))),
        batch_char_budget=max(20000, int(args.batch_char_budget)),
        per_email_char_cap=max(1500, int(args.per_email_char_cap)),
        model=args.model,
    )


if __name__ == "__main__":
    main()
