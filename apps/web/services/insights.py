from __future__ import annotations

import json
from collections import Counter

from sqlalchemy.orm import Session

from apps.web.models import Case, CaseInsight, EvidenceItem


CASE_REQUIREMENTS: dict[str, list[tuple[str, list[str]]]] = {
    "NIW": [
        ("Degree evidence", ["Degrees"]),
        ("Certifications or credentials", ["Certifications"]),
        ("Publications or research", ["Publications", "Research Evidence"]),
        ("Employment evidence", ["Employment Evidence"]),
        ("Recommendation letters", ["Recommendation Letters"]),
        ("Project evidence", ["Projects"]),
        ("National importance evidence", ["Projects", "Media Mentions", "Research Evidence", "Publications"]),
    ],
    "EB-1A": [
        ("Awards", ["Awards"]),
        ("Publications", ["Publications"]),
        ("Citations", ["Citations"]),
        ("Judging or peer review", ["Peer Review/Judging Evidence"]),
        ("Media mentions", ["Media Mentions"]),
        ("Professional memberships", ["Professional Memberships"]),
        ("Original contributions", ["Projects", "Research Evidence", "Patents"]),
    ],
    "O-1": [
        ("Awards", ["Awards"]),
        ("Media coverage", ["Media Mentions"]),
        ("Critical roles", ["Employment Evidence", "Projects"]),
        ("Recommendation letters", ["Recommendation Letters"]),
        ("Publications", ["Publications"]),
        ("Professional recognition", ["Awards", "Professional Memberships", "Conference/Speaking Evidence"]),
    ],
    "Academic Promotion": [
        ("Degree evidence", ["Degrees"]),
        ("Publications", ["Publications"]),
        ("Citations", ["Citations"]),
        ("Peer review or judging", ["Peer Review/Judging Evidence"]),
        ("Research evidence", ["Research Evidence"]),
        ("Recommendation letters", ["Recommendation Letters"]),
    ],
    "Professional Portfolio": [
        ("Employment evidence", ["Employment Evidence"]),
        ("Projects", ["Projects"]),
        ("Awards or recognition", ["Awards", "Media Mentions"]),
        ("Certifications", ["Certifications"]),
        ("Speaking or public proof", ["Conference/Speaking Evidence", "Media Mentions"]),
    ],
}


def _json_list(values: list[str]) -> str:
    return json.dumps(values, ensure_ascii=False)


def analyze_case(db: Session, case: Case) -> CaseInsight:
    items = (
        db.query(EvidenceItem)
        .filter(EvidenceItem.case_id == case.id, EvidenceItem.status != "Dismissed")
        .all()
    )
    counts = Counter(item.category for item in items)
    attached_count = sum(1 for item in items if item.file_path)
    candidate_count = sum(1 for item in items if item.status == "Candidate")

    requirements = CASE_REQUIREMENTS.get(case.case_type, CASE_REQUIREMENTS["Professional Portfolio"])
    satisfied: list[str] = []
    missing: list[str] = []
    for label, categories in requirements:
        if any(counts.get(category, 0) > 0 for category in categories):
            satisfied.append(label)
        else:
            missing.append(label)

    coverage = len(satisfied) / max(len(requirements), 1)
    attachment_bonus = min(attached_count, 10) * 1.5
    score = min(100, int(round((coverage * 85) + attachment_bonus)))

    strengths = []
    if satisfied:
        strengths.append("Coverage is present for " + ", ".join(satisfied[:4]) + ".")
    if attached_count:
        strengths.append(f"{attached_count} evidence file(s) are attached and ready for packaging.")
    if counts:
        top_categories = ", ".join(category for category, _ in counts.most_common(3))
        strengths.append(f"Strongest category coverage: {top_categories}.")

    weaknesses = []
    if missing:
        weaknesses.append("Missing or thin categories: " + ", ".join(missing[:5]) + ".")
    if candidate_count:
        weaknesses.append(f"{candidate_count} imported candidate(s) still need source-file review.")
    if not attached_count:
        weaknesses.append("No uploaded files are attached yet.")

    recommendations = []
    for label in missing[:5]:
        recommendations.append(f"Add primary-source documents for {label.lower()}.")
    if candidate_count:
        recommendations.append("Convert strong imported candidates into reviewed evidence items with files.")
    if score < 70:
        recommendations.append("Prioritize category coverage before final export.")
    else:
        recommendations.append("Run attorney review on naming, ordering, and relevance notes before filing.")

    insight = CaseInsight(
        case_id=case.id,
        score=score,
        strengths=_json_list(strengths or ["Evidence intake has started."]),
        weaknesses=_json_list(weaknesses or ["No major readiness gaps detected by rule-based review."]),
        missing_evidence=_json_list(missing),
        recommendations=_json_list(recommendations),
    )
    db.add(insight)
    db.commit()
    db.refresh(insight)
    return insight


def parse_json_list(raw: str) -> list[str]:
    try:
        value = json.loads(raw or "[]")
    except json.JSONDecodeError:
        return []
    return [str(item) for item in value] if isinstance(value, list) else []

