from __future__ import annotations

import re
from collections import Counter
from datetime import datetime

from sqlalchemy.orm import Session

from apps.web.models import (
    Case,
    CaseInsight,
    EvidenceCandidate,
    EvidenceDiscoveryJob,
    EvidenceItem,
    EvidenceSourcePermission,
)
from apps.web.services.evidence_framework import CASE_REQUIREMENTS
from apps.web.services.insights import analyze_case, parse_json_list
from apps.web.services.organizer import assign_exhibit_numbers, clean_display_text, infer_category


SOURCE_BLUEPRINTS: dict[str, list[tuple[str, str, str]]] = {
    "Local Computer": [
        ("Degree certificate discovered in {provider}", "Degrees", "Potential education record found in the approved local scope."),
        ("Employment verification record from {provider}", "Employment Evidence", "Potential employment proof found in local files."),
        ("Project outcome summary in {provider}", "Projects", "Potential project evidence found in a local document."),
    ],
    "Email": [
        ("Recommendation letter thread from {provider}", "Recommendation Letters", "Email subject and sender pattern suggest a support letter."),
        ("Award notification from {provider}", "Awards", "Message metadata suggests a recognition or award notice."),
        ("Speaking invitation from {provider}", "Conference/Speaking Evidence", "Message appears related to a speaking or panel invitation."),
    ],
    "Cloud Storage": [
        ("Published work archive in {provider}", "Publications", "Cloud filename pattern suggests a publication or paper."),
        ("Certification file in {provider}", "Certifications", "Cloud file appears to be a certificate or credential."),
        ("Media mention asset in {provider}", "Media Mentions", "Cloud item appears connected to public recognition."),
    ],
    "Professional Profiles": [
        ("Public profile achievement from {provider}", "Awards", "Public profile signals a professional achievement."),
        ("Publication or citation signal from {provider}", "Publications", "Profile source suggests a publication or citation record."),
        ("Project contribution from {provider}", "Projects", "Profile activity suggests a notable project contribution."),
    ],
    "Manual Upload": [
        ("Uploaded evidence bundle from {provider}", "Other Supporting Evidence", "Manual upload source is ready for review."),
        ("Presentation or portfolio artifact from {provider}", "Projects", "Uploaded file type suggests a portfolio artifact."),
    ],
}


def _category_letter(category: str) -> str:
    categories = [
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
    try:
        index = categories.index(category)
    except ValueError:
        index = len(categories) - 1
    return chr(ord("A") + min(index, 25))


def suggested_exhibit_number(db: Session, case: Case, category: str) -> str:
    prefix = f"EXHIBIT_{_category_letter(category)}"
    existing_numbers = [
        value
        for (value,) in db.query(EvidenceItem.exhibit_number)
        .filter(EvidenceItem.case_id == case.id, EvidenceItem.exhibit_number.like(f"{prefix}%"))
        .all()
    ]
    candidate_numbers = [
        value
        for (value,) in db.query(EvidenceCandidate.suggested_exhibit_number)
        .filter(
            EvidenceCandidate.case_id == case.id,
            EvidenceCandidate.suggested_exhibit_number.like(f"{prefix}%"),
        )
        .all()
    ]
    max_number = 0
    for value in existing_numbers + candidate_numbers:
        match = re.search(r"(\d+)$", value or "")
        if match:
            max_number = max(max_number, int(match.group(1)))
    return f"{prefix}{max_number + 1:03d}"


def create_discovery_job(
    db: Session,
    case: Case,
    *,
    source_type: str,
    provider: str,
    scope: str,
    permission_note: str,
) -> EvidenceDiscoveryJob:
    source_type = clean_display_text(source_type)
    provider = clean_display_text(provider)
    scope = clean_display_text(scope)
    permission_note = clean_display_text(permission_note)

    permission = EvidenceSourcePermission(
        case_id=case.id,
        source_type=source_type,
        provider=provider,
        scope=scope,
        permission_note=permission_note,
    )
    db.add(permission)
    db.flush()

    job = EvidenceDiscoveryJob(
        case_id=case.id,
        source_permission_id=permission.id,
        source_summary=f"{source_type} / {provider} / {scope}",
        status="Completed",
    )
    db.add(job)
    db.flush()

    blueprints = SOURCE_BLUEPRINTS.get(source_type, SOURCE_BLUEPRINTS["Manual Upload"])
    created = 0
    for index, (title_template, category, description) in enumerate(blueprints, start=1):
        title = title_template.format(provider=provider)
        exists = (
            db.query(EvidenceCandidate)
            .filter(
                EvidenceCandidate.case_id == case.id,
                EvidenceCandidate.title == title,
                EvidenceCandidate.source_detail == scope,
            )
            .first()
        )
        if exists:
            continue

        category = category or infer_category(title, description)
        confidence = min(96, 68 + (index * 7) + (8 if case.case_type.lower() in title.lower() else 0))
        relevance = min(98, 62 + (index * 8) + (10 if category in [req for _, cats in CASE_REQUIREMENTS.get(case.case_type, []) for req in cats] else 0))
        candidate = EvidenceCandidate(
            case_id=case.id,
            discovery_job_id=job.id,
            title=title,
            category=category,
            suggested_exhibit_number=suggested_exhibit_number(db, case, category),
            confidence_score=confidence,
            relevance_score=relevance,
            source_type=source_type,
            source_detail=scope,
            evidence_date=str(datetime.utcnow().year),
            description=description,
            status="Pending",
        )
        db.add(candidate)
        db.flush()
        created += 1

    job.candidates_found = created
    db.commit()
    db.refresh(job)
    return job


def approve_candidate(db: Session, case: Case, candidate: EvidenceCandidate) -> EvidenceItem:
    existing = (
        db.query(EvidenceItem)
        .filter(
            EvidenceItem.case_id == case.id,
            EvidenceItem.title == candidate.title,
            EvidenceItem.source == f"Discovered: {candidate.source_type}",
        )
        .first()
    )
    if existing is not None:
        candidate.status = "Approved"
        db.commit()
        db.refresh(existing)
        return existing

    item = EvidenceItem(
        case_id=case.id,
        exhibit_number=candidate.suggested_exhibit_number,
        title=candidate.title,
        original_filename="",
        renamed_filename="",
        category=candidate.category,
        source=f"Discovered: {candidate.source_type}",
        file_path="",
        evidence_date=candidate.evidence_date,
        confidence_score=candidate.confidence_score,
        relevance_score=candidate.relevance_score,
        description=candidate.description,
        relevance_notes=(
            f"Approved from Evidence Inbox. Source scope: {candidate.source_detail}. "
            f"Confidence {candidate.confidence_score}%, relevance {candidate.relevance_score}%."
        ),
        status="Needs Review",
    )
    candidate.status = "Approved"
    db.add(item)
    db.flush()
    assign_exhibit_numbers(db, case)
    db.commit()
    db.refresh(item)
    return item


def reject_candidate(db: Session, candidate: EvidenceCandidate) -> None:
    candidate.status = "Rejected"
    db.commit()


def _requirements_for_case(case: Case) -> list[tuple[str, list[str]]]:
    return CASE_REQUIREMENTS.get(case.case_type, CASE_REQUIREMENTS["Custom Evidence Workspace"])


def _item_year(item: EvidenceItem) -> int:
    for value in [item.evidence_date, item.title, item.description, item.relevance_notes, str(item.created_at.year)]:
        match = re.search(r"(19|20)\d{2}", value or "")
        if match:
            return int(match.group(0))
    return item.created_at.year


def build_case_intelligence(db: Session, case: Case, insight: CaseInsight | None = None) -> dict[str, object]:
    items = (
        db.query(EvidenceItem)
        .filter(EvidenceItem.case_id == case.id, EvidenceItem.status != "Dismissed")
        .order_by(EvidenceItem.created_at.asc())
        .all()
    )
    pending_candidates = (
        db.query(EvidenceCandidate)
        .filter(EvidenceCandidate.case_id == case.id, EvidenceCandidate.status == "Pending")
        .order_by(EvidenceCandidate.confidence_score.desc(), EvidenceCandidate.created_at.desc())
        .all()
    )
    insight = insight or analyze_case(db, case)
    requirements = _requirements_for_case(case)
    category_counts = Counter(item.category for item in items)
    approved_categories = set(category_counts)

    gap_rows = []
    missions = []
    for label, categories in requirements:
        count = sum(category_counts.get(category, 0) for category in categories)
        target = 2 if "letter" in label.lower() or "support" in label.lower() else 1
        complete = count >= target
        gap_rows.append(
            {
                "label": label,
                "count": count,
                "target": target,
                "complete": complete,
                "categories": ", ".join(categories),
            }
        )
        if not complete:
            missions.append(
                {
                    "title": f"Find {label}",
                    "progress": f"{count} of {target} found",
                    "next_step": f"Approve candidates or upload primary-source documents for {categories[0]}.",
                }
            )

    roadmap = []
    target_score = min(95, max(90, insight.score + 18))
    for mission in missions[:5]:
        roadmap.append(mission["next_step"])
    if pending_candidates:
        roadmap.insert(0, f"Review {len(pending_candidates)} pending Evidence Inbox candidate(s).")
    if insight.score >= 85:
        roadmap.append("Run final reviewer mode before exporting the package.")

    timeline = [
        {
            "year": _item_year(item),
            "title": item.title,
            "category": item.category,
            "exhibit": item.exhibit_number or "Pending",
        }
        for item in items
    ]
    timeline.sort(key=lambda row: (row["year"], row["title"]))

    graph_nodes = [
        {"label": category, "count": count, "active": category in approved_categories}
        for category, count in category_counts.most_common()
    ]
    if not graph_nodes:
        graph_nodes = [{"label": label, "count": 0, "active": False} for label, _ in requirements[:5]]

    relationships = []
    relation_rules = [
        ("Publications", "Citations", "Publication -> Citation"),
        ("Publications", "Conference/Speaking Evidence", "Publication -> Conference"),
        ("Projects", "Recommendation Letters", "Project -> Recommendation Letter"),
        ("Awards", "Media Mentions", "Award -> Media Mention"),
        ("Research Evidence", "Publications", "Research -> Publication"),
    ]
    for left, right, label in relation_rules:
        relationships.append(
            {
                "label": label,
                "strength": min(category_counts.get(left, 0), category_counts.get(right, 0)),
                "complete": bool(category_counts.get(left, 0) and category_counts.get(right, 0)),
            }
        )

    public_signals = sum(
        category_counts.get(category, 0)
        for category in ["Publications", "Citations", "Media Mentions", "Awards", "Conference/Speaking Evidence", "Projects"]
    )
    reputation_score = min(100, (public_signals * 12) + min(len(pending_candidates), 5) * 3)

    reviewer_concerns = parse_json_list(insight.weaknesses)
    if pending_candidates:
        reviewer_concerns.append("Pending candidates should be accepted, rejected, or converted before final export.")
    if not timeline:
        reviewer_concerns.append("No professional timeline can be built until evidence is uploaded or approved.")

    return {
        "score": insight.score,
        "target_score": target_score,
        "gap_rows": gap_rows,
        "missions": missions[:6],
        "roadmap": roadmap[:6],
        "timeline": timeline[:8],
        "graph_nodes": graph_nodes[:8],
        "relationships": relationships,
        "reputation_score": reputation_score,
        "reviewer_concerns": reviewer_concerns[:6],
        "pending_candidate_count": len(pending_candidates),
    }
