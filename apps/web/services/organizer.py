from __future__ import annotations

import json
import re
import uuid
from pathlib import Path

from sqlalchemy.orm import Session

from apps.web.models import Case, EvidenceImport, EvidenceItem
from apps.web.services.evidence_framework import CASE_TYPES
from apps.web.settings import AppSettings, get_settings


EVIDENCE_CATEGORIES = [
    "Degrees",
    "Certifications",
    "Publications",
    "Citations",
    "Awards",
    "Recommendation Letters",
    "Employment Evidence",
    "Projects",
    "Media Mentions",
    "Professional Memberships",
    "Conference/Speaking Evidence",
    "Peer Review/Judging Evidence",
    "Patents",
    "Research Evidence",
    "Other Supporting Evidence",
]

STATUS_OPTIONS = [
    "Candidate",
    "Cataloged",
    "Needs Review",
    "Ready",
    "Packaged",
]

KEYWORD_CATEGORIES: list[tuple[tuple[str, ...], str]] = [
    (("degree", "diploma", "transcript", "university", "bachelor", "master", "phd"), "Degrees"),
    (("certificate", "certification", "credential", "license"), "Certifications"),
    (("publication", "paper", "journal", "article", "conference paper"), "Publications"),
    (("citation", "google scholar", "cited", "h-index"), "Citations"),
    (("award", "honor", "fellowship", "winner", "recognition"), "Awards"),
    (("recommendation", "reference letter", "letter of support"), "Recommendation Letters"),
    (("employment", "offer letter", "paystub", "role", "promotion", "verification"), "Employment Evidence"),
    (("project", "product", "launch", "implementation", "deployment"), "Projects"),
    (("media", "press", "interview", "featured", "news"), "Media Mentions"),
    (("membership", "member", "association", "society"), "Professional Memberships"),
    (("speaker", "speaking", "lecture", "panel", "conference", "keynote"), "Conference/Speaking Evidence"),
    (("review", "reviewer", "judge", "judging", "peer review"), "Peer Review/Judging Evidence"),
    (("patent", "inventor", "provisional"), "Patents"),
    (("research", "grant", "study", "lab", "experiment"), "Research Evidence"),
]


def category_letter(category: str) -> str:
    try:
        index = EVIDENCE_CATEGORIES.index(category)
    except ValueError:
        index = len(EVIDENCE_CATEGORIES) - 1
    return chr(ord("A") + min(index, 25))


def clean_display_text(value: str) -> str:
    replacements = {
        "\u2013": "-",
        "\u2014": "-",
        "\u2018": "'",
        "\u2019": "'",
        "\u201c": '"',
        "\u201d": '"',
        "\u00a0": " ",
    }
    for old, new in replacements.items():
        value = value.replace(old, new)
    value = re.sub(r"\s+", " ", value)
    return value.strip()


def safe_filename(value: str, fallback: str = "evidence") -> str:
    name = clean_display_text(value or "")
    name = name.replace("&", "and")
    name = re.sub(r"[^A-Za-z0-9\s._()-]+", "", name)
    name = re.sub(r"\s+", "_", name.strip())
    name = re.sub(r"_+", "_", name).strip("._-")
    return name[:180] or fallback


def professional_title_from_filename(filename: str) -> str:
    stem = Path(filename or "Evidence").stem
    stem = re.sub(r"^[A-Za-z]{0,4}[-_ ]?\d+[-_ ]*", "", stem)
    stem = re.sub(r"[_\-]+", " ", stem)
    stem = clean_display_text(stem)
    return stem.title() if stem else "Evidence Item"


def infer_category(*values: str) -> str:
    haystack = " ".join(value or "" for value in values).lower()
    for keywords, category in KEYWORD_CATEGORIES:
        if any(keyword in haystack for keyword in keywords):
            return category
    return "Other Supporting Evidence"


def build_renamed_filename(item: EvidenceItem) -> str:
    suffix = Path(item.original_filename or item.file_path or "").suffix.lower() or ".pdf"
    exhibit = safe_filename(item.exhibit_number or "Exhibit", "Exhibit")
    category = safe_filename(item.category, "Evidence")
    title = safe_filename(item.title, "Evidence")
    return f"{exhibit}_{category}_{title}{suffix}"


def ensure_case_storage(case: Case, settings: AppSettings | None = None) -> Path:
    settings = settings or get_settings()
    owner = f"user_{case.user_id}" if case.user_id else "legacy_user"
    case_dir = settings.upload_root / owner / f"case_{case.id}" / "incoming"
    case_dir.mkdir(parents=True, exist_ok=True)
    return case_dir


def assert_safe_child(path: Path, root: Path) -> None:
    resolved = path.resolve()
    root_resolved = root.resolve()
    if resolved != root_resolved and root_resolved not in resolved.parents:
        raise ValueError("Unsafe storage path rejected.")


def validate_upload(filename: str, content: bytes, settings: AppSettings | None = None) -> None:
    settings = settings or get_settings()
    suffix = Path(filename or "").suffix.lower()
    if suffix not in settings.allowed_upload_extensions:
        allowed = ", ".join(sorted(settings.allowed_upload_extensions))
        raise ValueError(f"Unsupported file type. Allowed: {allowed}")
    if not content:
        raise ValueError("Uploaded file is empty.")
    if len(content) > settings.max_upload_bytes:
        raise ValueError(f"File is larger than {settings.max_upload_mb} MB.")


def assign_exhibit_numbers(db: Session, case: Case) -> None:
    items = (
        db.query(EvidenceItem)
        .filter(EvidenceItem.case_id == case.id)
        .order_by(EvidenceItem.created_at.asc(), EvidenceItem.id.asc())
        .all()
    )
    used_by_prefix: dict[str, int] = {}
    for item in items:
        prefix = f"EXHIBIT_{category_letter(item.category)}"
        match = re.search(r"(\d+)$", item.exhibit_number or "")
        if item.exhibit_number and item.exhibit_number.startswith(prefix) and match:
            used_by_prefix[prefix] = max(used_by_prefix.get(prefix, 0), int(match.group(1)))

    for item in items:
        if not item.exhibit_number:
            prefix = f"EXHIBIT_{category_letter(item.category)}"
            next_number = used_by_prefix.get(prefix, 0) + 1
            item.exhibit_number = f"{prefix}{next_number:03d}"
            used_by_prefix[prefix] = next_number
        item.renamed_filename = build_renamed_filename(item)
    db.flush()


def save_uploaded_evidence(
    db: Session,
    case: Case,
    *,
    original_filename: str,
    content: bytes,
    category: str | None,
    source: str,
    description: str,
    relevance_notes: str,
) -> EvidenceItem:
    settings = get_settings()
    validate_upload(original_filename, content, settings)

    case_dir = ensure_case_storage(case, settings)
    safe_original = safe_filename(original_filename, "upload")
    stored_name = f"{uuid.uuid4().hex}_{safe_original}"
    destination = case_dir / stored_name
    assert_safe_child(destination, settings.upload_root)
    destination.write_bytes(content)

    selected_category = category if category in EVIDENCE_CATEGORIES else infer_category(original_filename)
    item = EvidenceItem(
        case_id=case.id,
        title=professional_title_from_filename(original_filename),
        original_filename=original_filename,
        category=selected_category,
        source=source or "Upload",
        file_path=str(destination),
        confidence_score=100,
        relevance_score=78 if selected_category == "Other Supporting Evidence" else 86,
        description=clean_display_text(description or ""),
        relevance_notes=clean_display_text(relevance_notes or ""),
        status="Cataloged",
    )
    db.add(item)
    db.flush()
    assign_exhibit_numbers(db, case)
    db.commit()
    db.refresh(item)
    return item


def import_document_organizer_candidates(db: Session, case: Case) -> EvidenceImport:
    settings = get_settings()
    config_dir = settings.document_organizer_config_dir
    if not config_dir.exists():
        record = EvidenceImport(
            case_id=case.id,
            source="Document Organizer",
            status="Skipped",
            imported_count=0,
            details="Document organizer config directory was not found.",
        )
        db.add(record)
        db.commit()
        db.refresh(record)
        return record

    imported = 0
    for config_path in sorted(config_dir.glob("*.json")):
        try:
            payload = json.loads(config_path.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            continue

        section_title = clean_display_text(str(payload.get("section_title", "")))
        for raw_item in payload.get("items", []):
            exhibit_id = clean_display_text(str(raw_item.get("exhibit_id", "")))
            title = clean_display_text(str(raw_item.get("proper_title", ""))) or "Organizer Candidate"
            if not exhibit_id:
                continue
            exists = (
                db.query(EvidenceItem)
                .filter(
                    EvidenceItem.case_id == case.id,
                    EvidenceItem.source == "Document Organizer",
                    EvidenceItem.exhibit_number == exhibit_id,
                )
                .first()
            )
            if exists:
                continue

            item = EvidenceItem(
                case_id=case.id,
                exhibit_number=exhibit_id,
                title=title,
                original_filename=clean_display_text(str(raw_item.get("existing_name", ""))),
                renamed_filename=f"Exhibit_{safe_filename(exhibit_id)}_{safe_filename(title)}.pdf",
                category=infer_category(title, section_title),
                source="Document Organizer",
                file_path="",
                confidence_score=76,
                relevance_score=74,
                description=f"Imported from organizer section: {section_title}",
                relevance_notes="Candidate imported from the existing exhibit organizer configuration.",
                status="Candidate",
            )
            db.add(item)
            imported += 1

    record = EvidenceImport(
        case_id=case.id,
        source="Document Organizer",
        status="Completed",
        imported_count=imported,
        details=f"Scanned organizer configs in {config_dir.name}.",
    )
    db.add(record)
    db.flush()
    assign_exhibit_numbers(db, case)
    db.commit()
    db.refresh(record)
    return record


def update_evidence_item(
    db: Session,
    item: EvidenceItem,
    *,
    title: str,
    category: str,
    status: str,
    description: str,
    relevance_notes: str,
) -> EvidenceItem:
    item.title = clean_display_text(title or item.title)
    item.category = category if category in EVIDENCE_CATEGORIES else item.category
    item.status = status if status in STATUS_OPTIONS else item.status
    item.description = clean_display_text(description or "")
    item.relevance_notes = clean_display_text(relevance_notes or "")
    item.renamed_filename = build_renamed_filename(item)
    db.commit()
    db.refresh(item)
    return item
