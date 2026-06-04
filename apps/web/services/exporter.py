from __future__ import annotations

import csv
import io
import zipfile
from datetime import datetime
from pathlib import Path

from sqlalchemy.orm import Session

from apps.web.models import Case, CaseInsight, EvidenceItem, ExportPackage
from apps.web.services.discovery import build_case_intelligence
from apps.web.services.insights import analyze_case, parse_json_list
from apps.web.services.organizer import assign_exhibit_numbers, safe_filename
from apps.web.settings import AppSettings, get_settings


def _is_safe_existing_file(path: Path, root: Path) -> bool:
    if not path.exists() or not path.is_file():
        return False
    resolved = path.resolve()
    root_resolved = root.resolve()
    return resolved == root_resolved or root_resolved in resolved.parents


def _evidence_table_csv(items: list[EvidenceItem]) -> str:
    out = io.StringIO()
    writer = csv.writer(out)
    writer.writerow(
        [
            "Exhibit Number",
            "Title",
            "Category",
            "Source",
            "Original Filename",
            "Renamed Filename",
            "Status",
            "Evidence Date",
            "Confidence Score",
            "Relevance Score",
            "Description",
            "Relevance Notes",
        ]
    )
    for item in items:
        writer.writerow(
            [
                item.exhibit_number,
                item.title,
                item.category,
                item.source,
                item.original_filename,
                item.renamed_filename,
                item.status,
                item.evidence_date,
                item.confidence_score,
                item.relevance_score,
                item.description,
                item.relevance_notes,
            ]
        )
    return out.getvalue()


def _evidence_index(case: Case, items: list[EvidenceItem]) -> str:
    lines = [f"# Evidence Index: {case.title}", ""]
    current_category = ""
    for item in items:
        if item.category != current_category:
            current_category = item.category
            lines.extend(["", f"## {current_category}", ""])
        lines.append(f"- **{item.exhibit_number}** - {item.title} ({item.status})")
    lines.append("")
    return "\n".join(lines)


def _case_summary(case: Case, items: list[EvidenceItem]) -> str:
    lines = [
        f"# Case Summary: {case.title}",
        "",
        f"- Workspace category: {case.workspace_category}",
        f"- Case type: {case.case_type}",
        f"- Proof objective: {case.proof_objective or 'Not provided'}",
        f"- Petitioner: {case.petitioner_name or 'Not provided'}",
        f"- Status: {case.status}",
        f"- Evidence items: {len(items)}",
        "",
        "## Description",
        "",
        case.description or "No description provided.",
        "",
    ]
    return "\n".join(lines)


def _insights_markdown(insight: CaseInsight) -> str:
    lines = [f"# Readiness Insights", "", f"Score: {insight.score}/100", ""]
    sections = [
        ("Strengths", parse_json_list(insight.strengths)),
        ("Weaknesses", parse_json_list(insight.weaknesses)),
        ("Missing Evidence", parse_json_list(insight.missing_evidence)),
        ("Recommendations", parse_json_list(insight.recommendations)),
    ]
    for title, values in sections:
        lines.extend([f"## {title}", ""])
        if values:
            lines.extend(f"- {value}" for value in values)
        else:
            lines.append("- None identified.")
        lines.append("")
    return "\n".join(lines)


def _timeline_markdown(intelligence: dict[str, object]) -> str:
    lines = ["# Professional Timeline", ""]
    timeline = intelligence.get("timeline", [])
    if timeline:
        for item in timeline:
            lines.append(f"- **{item['year']}** - {item['title']} ({item['category']}, {item['exhibit']})")
    else:
        lines.append("- No timeline evidence has been approved yet.")
    lines.append("")
    return "\n".join(lines)


def _roadmap_markdown(intelligence: dict[str, object]) -> str:
    lines = [
        "# Case Readiness Roadmap",
        "",
        f"Current score: {intelligence.get('score', 0)}/100",
        f"Target score: {intelligence.get('target_score', 90)}/100",
        "",
    ]
    roadmap = intelligence.get("roadmap", [])
    if roadmap:
        for index, step in enumerate(roadmap, start=1):
            lines.append(f"{index}. {step}")
    else:
        lines.append("1. Run final reviewer mode before export.")
    lines.append("")
    return "\n".join(lines)


def build_export_package(db: Session, case: Case) -> ExportPackage:
    settings: AppSettings = get_settings()
    assign_exhibit_numbers(db, case)
    db.commit()

    items = (
        db.query(EvidenceItem)
        .filter(EvidenceItem.case_id == case.id)
        .order_by(EvidenceItem.category.asc(), EvidenceItem.exhibit_number.asc(), EvidenceItem.id.asc())
        .all()
    )
    insight = (
        db.query(CaseInsight)
        .filter(CaseInsight.case_id == case.id)
        .order_by(CaseInsight.created_at.desc())
        .first()
    )
    if insight is None:
        insight = analyze_case(db, case)
    intelligence = build_case_intelligence(db, case, insight)

    timestamp = datetime.utcnow().strftime("%Y%m%d_%H%M%S")
    case_slug = safe_filename(case.title, "case")
    owner = f"user_{case.user_id}" if case.user_id else "legacy_user"
    export_dir = settings.export_root / owner / f"case_{case.id}"
    export_dir.mkdir(parents=True, exist_ok=True)
    zip_name = f"RoleAxis_{case_slug}_{timestamp}.zip"
    zip_path = export_dir / zip_name

    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as package:
        package.writestr("evidence_table.csv", _evidence_table_csv(items))
        package.writestr("evidence_index.md", _evidence_index(case, items))
        package.writestr("case_summary.md", _case_summary(case, items))
        package.writestr("readiness_insights.md", _insights_markdown(insight))
        package.writestr("professional_timeline.md", _timeline_markdown(intelligence))
        package.writestr("case_readiness_roadmap.md", _roadmap_markdown(intelligence))

        for item in items:
            if not item.file_path:
                continue
            source = Path(item.file_path)
            if not _is_safe_existing_file(source, settings.upload_root):
                continue
            folder = safe_filename(item.category, "Evidence")
            filename = safe_filename(item.renamed_filename or source.name, "evidence")
            package.write(source, f"organized_evidence/{folder}/{filename}")

    export = ExportPackage(
        case_id=case.id,
        filename=zip_name,
        file_path=str(zip_path),
        status="Ready",
    )
    db.add(export)
    db.commit()
    db.refresh(export)
    return export
