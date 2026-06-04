from __future__ import annotations

import argparse
import csv
from datetime import datetime, timezone
from pathlib import Path

from eb1a_miner.config import Settings
from eb1a_miner.graph.auth import get_access_token
from eb1a_miner.graph.mail import iter_messages
from eb1a_miner.eb1a.heuristics import score_text


def _parse_dt(s: str | None) -> datetime | None:
    if not s:
        return None
    s = s.strip()
    # Accept YYYY-MM-DD
    if len(s) == 10:
        dt = datetime.fromisoformat(s)
        return dt.replace(tzinfo=timezone.utc)
    # Accept ISO
    if s.endswith("Z"):
        s = s[:-1] + "+00:00"
    dt = datetime.fromisoformat(s)
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=timezone.utc)
    return dt


def run(*, since: datetime | None, until: datetime | None, min_score: int, limit: int) -> Path:
    settings = Settings()
    token = get_access_token(settings)

    settings.reports_dir.mkdir(parents=True, exist_ok=True)
    out_path = Path(settings.reports_dir) / "Inbox_EB1A_Shortlist.csv"

    # Pull previews in the list call. No per-message GET calls.
    select = "id,subject,receivedDateTime,from,toRecipients,ccRecipients,hasAttachments,webLink,bodyPreview"

    rows: list[dict] = []
    seen = 0

    for msg in iter_messages(
        token,
        folder="inbox",
        top=50,
        since=since,
        until=until,
        select=select,
        max_pages=None,
    ):
        seen += 1
        if limit and seen > limit:
            break

        text = (msg.body_preview or "").strip()
        if not text:
            continue

        scored = score_text(text)
        if scored.score < min_score:
            continue

        evidence = " | ".join([h.snippet for h in scored.hits])
        why = "; ".join([f"{h.category}:{h.keyword}" for h in scored.hits])

        from_addr = (msg.from_.address if msg.from_ else None) or ""
        from_name = (msg.from_.name if msg.from_ else None) or ""

        rows.append(
            {
                "Score": scored.score,
                "Category": scored.category,
                "ReceivedUTC": msg.received_dt.isoformat() if msg.received_dt else "",
                "Subject": msg.subject or "",
                "FromName": from_name,
                "FromEmail": from_addr,
                "Why_EB1A_Worthy": why,
                "Evidence_Snippets": evidence,
                "WebLink": msg.web_link or "",
                "MessageID": msg.id,
            }
        )

    rows.sort(key=lambda r: int(r["Score"]), reverse=True)

    with out_path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(
            f,
            fieldnames=[
                "Score",
                "Category",
                "ReceivedUTC",
                "Subject",
                "FromName",
                "FromEmail",
                "Why_EB1A_Worthy",
                "Evidence_Snippets",
                "WebLink",
                "MessageID",
            ],
        )
        writer.writeheader()
        for r in rows:
            writer.writerow(r)

    return out_path


def main() -> None:
    ap = argparse.ArgumentParser(
        description="Shortlist Inbox emails likely useful for EB-1A (preview-only, no attachments)."
    )
    ap.add_argument("--since", default=None, help="Start date/time (YYYY-MM-DD or ISO). Optional.")
    ap.add_argument("--until", default=None, help="End date/time (YYYY-MM-DD or ISO). Optional.")
    ap.add_argument("--min-score", type=int, default=60, help="Minimum score (0-100) to include.")
    ap.add_argument("--limit", type=int, default=0, help="Max messages to scan (0 = no cap).")
    args = ap.parse_args()

    since = _parse_dt(args.since)
    until = _parse_dt(args.until)

    out = run(since=since, until=until, min_score=args.min_score, limit=args.limit)
    print(f"Wrote: {out}")


if __name__ == "__main__":
    main()