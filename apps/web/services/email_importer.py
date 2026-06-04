from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from sqlalchemy.orm import Session

from apps.web.models import Case, EvidenceImport, EvidenceItem
from apps.web.services.organizer import clean_display_text, infer_category, safe_filename
from apps.web.settings import get_settings


EMAIL_CATEGORY_MAP = {
    "leadership": "Employment Evidence",
    "critical_role": "Employment Evidence",
    "recognition": "Awards",
    "speaking_media": "Conference/Speaking Evidence",
    "judging_reviewing": "Peer Review/Judging Evidence",
    "membership": "Professional Memberships",
    "awards": "Awards",
    "publications": "Publications",
    "contracts": "Projects",
    "other": "Other Supporting Evidence",
}


def _load_jsonl(path: Path) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    if not path.exists():
        return rows
    for line in path.read_text(encoding="utf-8", errors="ignore").splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            obj = json.loads(line)
        except json.JSONDecodeError:
            continue
        if isinstance(obj, dict):
            rows.append(obj)
    return rows


def _category_from_raw(raw: str) -> str:
    raw = (raw or "other").strip().lower()
    return EMAIL_CATEGORY_MAP.get(raw, infer_category(raw))


def _already_exists(db: Session, case_id: int, title: str, notes: str) -> bool:
    return (
        db.query(EvidenceItem)
        .filter(
            EvidenceItem.case_id == case_id,
            EvidenceItem.source == "Email Crawler",
            EvidenceItem.title == title,
            EvidenceItem.relevance_notes == notes,
        )
        .first()
        is not None
    )


def import_email_candidates(db: Session, case: Case, limit: int = 40) -> EvidenceImport:
    settings = get_settings()
    reports_dir = settings.email_crawler_reports_dir
    if not reports_dir.exists():
        record = EvidenceImport(
            case_id=case.id,
            source="Email Crawler",
            status="Skipped",
            imported_count=0,
            details="Email crawler reports directory was not found. Integration is ready for future crawler runs.",
        )
        db.add(record)
        db.commit()
        db.refresh(record)
        return record

    candidates: list[dict[str, str]] = []

    batch_path = reports_dir / "EB1A_GPT_fullbody_batches.jsonl"
    for row in _load_jsonl(batch_path):
        result = row.get("result") if isinstance(row.get("result"), dict) else {}
        selected = result.get("selected") if isinstance(result.get("selected"), list) else []
        for item in selected:
            if not isinstance(item, dict):
                continue
            raw_category = str(item.get("category") or "other")
            why = clean_display_text(str(item.get("why") or ""))[:900]
            title = f"Email Evidence Candidate - {raw_category.replace('_', ' ').title()}"
            candidates.append(
                {
                    "title": title,
                    "category": _category_from_raw(raw_category),
                    "notes": why,
                    "description": "Imported from the local email crawler analysis output.",
                }
            )
            if len(candidates) >= limit:
                break
        if len(candidates) >= limit:
            break

    thread_path = reports_dir / "EB1A_GPT_fullbody_threads.jsonl"
    for row in _load_jsonl(thread_path):
        result = row.get("result") if isinstance(row.get("result"), dict) else {}
        if not result:
            continue
        uses = result.get("recommended_use") if isinstance(result.get("recommended_use"), list) else []
        raw_category = str(uses[0] if uses else "other")
        why = clean_display_text(str(result.get("why") or ""))[:900]
        if not why:
            continue
        title = f"Email Thread Candidate - {raw_category.replace('_', ' ').title()}"
        candidates.append(
            {
                "title": title,
                "category": _category_from_raw(raw_category),
                "notes": why,
                "description": "Imported from a ranked email conversation thread. Review source mail before filing.",
            }
        )
        if len(candidates) >= limit:
            break

    imported = 0
    for candidate in candidates:
        if _already_exists(db, case.id, candidate["title"], candidate["notes"]):
            continue
        item = EvidenceItem(
            case_id=case.id,
            title=candidate["title"],
            original_filename="",
            renamed_filename=f"Email_Candidate_{safe_filename(candidate['title'])}.pdf",
            category=candidate["category"],
            source="Email Crawler",
            file_path="",
            confidence_score=72,
            relevance_score=80,
            description=candidate["description"],
            relevance_notes=candidate["notes"],
            status="Candidate",
        )
        db.add(item)
        imported += 1

    record = EvidenceImport(
        case_id=case.id,
        source="Email Crawler",
        status="Completed" if imported else "No New Candidates",
        imported_count=imported,
        details="Imported high-level candidates only. Private email bodies are not copied into app logs.",
    )
    db.add(record)
    db.commit()
    db.refresh(record)
    return record
