from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Iterable


@dataclass(frozen=True)
class Hit:
    category: str
    keyword: str
    snippet: str


@dataclass(frozen=True)
class Eb1aScore:
    score: int
    category: str
    hits: tuple[Hit, ...]


# Categories and keywords (tuned for leadership/authority/recognition signals)
_KEYWORDS: dict[str, list[str]] = {
    "leadership": [
        "approve", "approval", "sign off", "signoff", "decision", "final decision",
        "your guidance", "your direction", "please advise", "directed", "led by",
        "leadership", "lead", "leading", "owner", "driving", "sponsor",
        "escalate", "escalation", "blocked", "unblock", "delegate",
        "action required", "action needed", "next steps", "take ownership",
    ],
    "critical_role": [
        "critical", "key role", "key stakeholder", "point of contact", "poc",
        "responsible for", "accountable for", "on-call", "incident commander",
        "root cause", "rca", "postmortem", "sev", "severity", "outage",
        "architect", "architecture", "design review", "security review",
        "final review", "go/no-go", "launch approval",
    ],
    "recognition": [
        "congratulations", "great job", "excellent work", "outstanding",
        "thank you for", "appreciate", "impressed", "recognition",
        "award", "winner", "honor", "certified", "promotion",
        "recommendation", "reference letter", "letter of recommendation",
    ],
    "speaking_media": [
        "invited", "invitation", "speaker", "panel", "keynote", "webinar",
        "conference", "summit", "podcast", "interview", "press", "media",
        "featured", "publication", "journal", "article", "newsletter",
    ],
    "judging_reviewing": [
        "reviewer", "judge", "judging", "committee", "program committee",
        "peer review", "review request", "reviewed your submission",
        "editor", "editorial", "acceptance", "accepted paper",
    ],
    "membership": [
        "membership", "member of", "fellow", "society", "association",
        "invited member", "appointed", "advisory board",
    ],
    "high_salary_contracts": [
        "compensation", "salary", "offer", "contract", "agreement", "mou",
        "rate", "retainer", "invoice", "payment", "bonus",
    ],
}


_SENTENCE_SPLIT = re.compile(r"(?<=[.!?])\s+|\n+")
_WS = re.compile(r"\s+")
_HTML_TAGS = re.compile(r"<[^>]+>")


def _to_text(body: str) -> str:
    # best-effort HTML stripping
    s = _HTML_TAGS.sub(" ", body)
    s = _WS.sub(" ", s).strip()
    return s


def score_text(text: str, *, max_hits: int = 8) -> Eb1aScore:
    """
    Deterministic scoring from content. No hallucinations.
    Returns top evidence snippets (exact text fragments).
    """
    if not text:
        return Eb1aScore(score=0, category="none", hits=tuple())

    raw = text
    raw_lower = raw.lower()

    # sentence-ish chunks for better snippets
    chunks = [c.strip() for c in _SENTENCE_SPLIT.split(raw) if c.strip()]
    if not chunks:
        chunks = [raw]

    hits: list[Hit] = []
    cat_points: dict[str, int] = {k: 0 for k in _KEYWORDS.keys()}

    for cat, kws in _KEYWORDS.items():
        for kw in kws:
            kw_l = kw.lower()
            if kw_l in raw_lower:
                # pick a snippet chunk containing the keyword
                snippet = None
                for ch in chunks:
                    if kw_l in ch.lower():
                        snippet = ch
                        break
                if snippet is None:
                    snippet = raw[:240]

                hits.append(Hit(category=cat, keyword=kw, snippet=snippet[:400]))
                # weight: longer/more specific phrases give more signal
                pts = 6 if len(kw) >= 10 else 4
                cat_points[cat] += pts

    # Keep only strongest hits, de-dupe by snippet
    uniq = []
    seen = set()
    for h in hits:
        key = (h.category, h.keyword, h.snippet)
        if key in seen:
            continue
        seen.add(key)
        uniq.append(h)
    hits = uniq[:max_hits]

    # Category winner
    best_cat = "none"
    best_pts = 0
    for cat, pts in cat_points.items():
        if pts > best_pts:
            best_pts = pts
            best_cat = cat

    # Convert to 0–100 score (cap)
    score = min(100, best_pts * 5)  # 4–6 pts per hit -> 20–30 per hit

    return Eb1aScore(score=score, category=best_cat, hits=tuple(hits))


def extract_best_text(body_preview: str | None, body: dict | None) -> str:
    """
    Prefer bodyPreview for speed; if full body exists, append some of it.
    """
    parts: list[str] = []
    if body_preview:
        parts.append(body_preview)

    if body and isinstance(body, dict):
        content = body.get("content")
        ctype = (body.get("contentType") or "").lower()
        if isinstance(content, str) and content.strip():
            if ctype == "html":
                parts.append(_to_text(content)[:2500])
            else:
                parts.append(content[:2500])

    text = "\n".join([p for p in parts if p])
    return text.strip()
