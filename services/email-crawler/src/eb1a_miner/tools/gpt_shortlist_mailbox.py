from __future__ import annotations

import argparse
import json
from datetime import datetime, timezone
from pathlib import Path

from eb1a_miner.config import Settings
from eb1a_miner.graph.auth import get_access_token
from eb1a_miner.graph.mail import iter_messages
from eb1a_miner.graph.conversations import fetch_thread_previews
from eb1a_miner.llm.openai_client import llm_rank_batch


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


def _load_processed(path: Path) -> set[str]:
    if not path.exists():
        return set()
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
        if isinstance(data, list):
            return set([str(x) for x in data])
    except Exception:
        return set()
    return set()


def _save_processed(path: Path, s: set[str]) -> None:
    path.write_text(json.dumps(sorted(list(s))), encoding="utf-8")


def _prompt_for_batch(threads: list[dict]) -> str:
    """
    We force grounded behavior:
    - decide ONLY using provided previews
    - return strict JSON
    - include exact quoted evidence snippets from previews only
    """
    schema = {
        "selected": [
            {
                "conversationId": "string",
                "score": 0,
                "category": "leadership|critical_role|recognition|speaking_media|judging_reviewing|membership|high_salary_contracts|other|none",
                "why": "short explanation grounded in the previews",
                "evidence_quotes": ["exact snippets copied from provided previews only"],
                "top_message_webLink": "string",
            }
        ],
        "rejected_count": 0,
    }

    instructions = f"""
You are screening email conversation threads for evidence that could support an EB-1A petition.
You MUST be conservative and grounded.

Rules:
- Use ONLY the data provided below.
- Do NOT invent facts.
- Evidence quotes MUST be exact text copied from the provided bodyPreview fields.
- If evidence is weak/unclear, reject it.
- Prefer threads showing leadership/authority, critical role, recognition, invitations, committees, awards, publications, contracts.
- Return STRICT JSON only. No markdown.

Return JSON matching this shape (example types only):
{json.dumps(schema, indent=2)}

Now evaluate these threads:
""".strip()

    payload = {"threads": threads}
    return instructions + "\n\n" + json.dumps(payload, ensure_ascii=False)


def _thread_stub(conversation_id: str, msgs: list[dict]) -> dict:
    """
    Create a compact object per conversation thread to send to GPT.
    """
    # pick a representative link (last message usually the most relevant in previews)
    top_link = ""
    for m in reversed(msgs):
        if m.get("webLink"):
            top_link = m["webLink"]
            break

    # compact previews: keep only meaningful lines
    compact_msgs = []
    for m in msgs:
        compact_msgs.append(
            {
                "id": m.get("id"),
                "receivedDateTime": m.get("receivedDateTime"),
                "fromName": m.get("fromName"),
                "fromEmail": m.get("fromEmail"),
                "subject": m.get("subject"),
                "bodyPreview": (m.get("bodyPreview") or "")[:800],
                "webLink": m.get("webLink"),
            }
        )

    return {
        "conversationId": conversation_id,
        "messageCount": len(msgs),
        "topWebLink": top_link,
        "messages": compact_msgs,
    }


def run(*, since: datetime | None, until: datetime | None, batch_size: int, max_threads: int, model: str) -> None:
    settings = Settings()
    token = get_access_token(settings)

    settings.reports_dir.mkdir(parents=True, exist_ok=True)
    out_jsonl = Path(settings.reports_dir) / "EB1A_GPT_Shortlist.jsonl"
    processed_path = Path(settings.reports_dir) / "EB1A_GPT_processed_conversations.json"

    processed = _load_processed(processed_path)

    folders = ["inbox", "sentitems"]

    # Build a set of conversationIds to consider by scanning previews (fast)
    conv_to_seed: dict[str, dict] = {}

    select = "id,conversationId,subject,receivedDateTime,from,hasAttachments,webLink,bodyPreview"

    for folder in folders:
        for msg in iter_messages(
            token,
            folder=folder,
            top=50,
            since=since,
            until=until,
            select=select,
            max_pages=None,
        ):
            if not msg.conversation_id:
                continue
            if msg.conversation_id in processed:
                continue
            # keep one seed per conversation id (latest encountered by order)
            if msg.conversation_id not in conv_to_seed:
                conv_to_seed[msg.conversation_id] = {
                    "conversationId": msg.conversation_id,
                    "seedMessageId": msg.id,
                }
            if max_threads and len(conv_to_seed) >= max_threads:
                break
        if max_threads and len(conv_to_seed) >= max_threads:
            break

    conv_ids = list(conv_to_seed.keys())

    # Process in batches
    batch: list[dict] = []
    for cid in conv_ids:
        # Expand whole thread previews only when we are about to send to GPT
        msgs = fetch_thread_previews(token, cid, max_messages=25)
        if not msgs:
            processed.add(cid)
            continue

        batch.append(_thread_stub(cid, msgs))

        if len(batch) >= batch_size:
            prompt = _prompt_for_batch(batch)
            result = llm_rank_batch(model=model, input_text=prompt)

            out_jsonl.write_text("", encoding="utf-8") if not out_jsonl.exists() else None
            with out_jsonl.open("a", encoding="utf-8") as f:
                f.write(json.dumps(result, ensure_ascii=False) + "\n")

            # Mark all conversations in this batch as processed (selected or not)
            for t in batch:
                processed.add(t["conversationId"])
            _save_processed(processed_path, processed)

            batch = []

    # Flush final batch
    if batch:
        prompt = _prompt_for_batch(batch)
        result = llm_rank_batch(model=model, input_text=prompt)

        out_jsonl.write_text("", encoding="utf-8") if not out_jsonl.exists() else None
        with out_jsonl.open("a", encoding="utf-8") as f:
            f.write(json.dumps(result, ensure_ascii=False) + "\n")

        for t in batch:
            processed.add(t["conversationId"])
        _save_processed(processed_path, processed)


def main() -> None:
    ap = argparse.ArgumentParser(description="Scan Inbox+Sent, expand threads, batch-rank with GPT, save JSONL results.")
    ap.add_argument("--since", default=None, help="Start date/time (YYYY-MM-DD or ISO). Optional.")
    ap.add_argument("--until", default=None, help="End date/time (YYYY-MM-DD or ISO). Optional.")
    ap.add_argument("--batch-size", type=int, default=75, help="Threads per GPT call (50-150 recommended).")
    ap.add_argument("--max-threads", type=int, default=0, help="Cap total threads scanned (0 = no cap).")
    ap.add_argument("--model", default="gpt-5", help="OpenAI model (default gpt-5).")
    args = ap.parse_args()

    since = _parse_dt(args.since)
    until = _parse_dt(args.until)

    batch_size = max(10, min(150, int(args.batch_size)))
    max_threads = int(args.max_threads)

    run(since=since, until=until, batch_size=batch_size, max_threads=max_threads, model=args.model)


if __name__ == "__main__":
    main()
