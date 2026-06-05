from __future__ import annotations

import json
import urllib.error
import urllib.request
from dataclasses import dataclass

from sqlalchemy.orm import Session

from apps.web.models import InterviewSession, InterviewTurn
from apps.web.settings import get_settings


INTERVIEW_TYPES = [
    "Behavioral",
    "Technical",
    "Executive",
    "Case Study",
    "Recruiter Screen",
    "Salary Negotiation",
]

ANSWER_MODES = [
    "Concise",
    "STAR",
    "Executive",
    "Technical Depth",
    "Recovery",
]


@dataclass(frozen=True)
class InterviewAnswer:
    answer: str
    used_ai: bool


def get_interview_session_or_404(db: Session, session_id: int, user_id: int) -> InterviewSession | None:
    return (
        db.query(InterviewSession)
        .filter(InterviewSession.id == session_id, InterviewSession.user_id == user_id)
        .first()
    )


def create_interview_session(
    db: Session,
    *,
    user_id: int,
    title: str,
    target_role: str,
    company_name: str,
    interview_type: str,
    resume_context: str,
    job_description: str,
) -> InterviewSession:
    clean_title = (title or "").strip() or "Interview Practice Session"
    session = InterviewSession(
        user_id=user_id,
        title=clean_title[:255],
        target_role=(target_role or "").strip()[:255],
        company_name=(company_name or "").strip()[:255],
        interview_type=interview_type if interview_type in INTERVIEW_TYPES else "Behavioral",
        resume_context=(resume_context or "").strip(),
        job_description=(job_description or "").strip(),
    )
    db.add(session)
    db.commit()
    db.refresh(session)
    return session


def _clip(value: str, limit: int) -> str:
    value = (value or "").strip()
    return value[-limit:] if len(value) > limit else value


def _fallback_answer(session: InterviewSession, question: str, transcript: str, mode: str) -> str:
    role = session.target_role or "the role"
    company = session.company_name or "the company"
    prompt = question or transcript or "the question"
    proof = session.resume_context[:240] if session.resume_context else "your strongest relevant project, result, or credential"
    jd = session.job_description[:220] if session.job_description else "the role requirements"

    if mode == "STAR":
        return (
            f"Use a STAR answer for: {prompt}\n\n"
            f"Situation: Briefly set up the context for {role} at {company}.\n"
            f"Task: Name the responsibility or challenge connected to {jd}.\n"
            f"Action: Explain the specific steps you took, using {proof} as the proof point.\n"
            "Result: Quantify the business, technical, customer, or team outcome.\n\n"
            "Close by connecting the result back to why you are ready for this role."
        )
    if mode == "Executive":
        return (
            f"Lead with business impact for: {prompt}\n\n"
            f"My answer would be: I focus on outcomes first. For {role}, I would connect my experience to "
            f"{company}'s priorities, show the measurable result, and then explain the operating judgment behind it.\n\n"
            "Keep the structure tight: impact, decision, execution, lesson."
        )
    if mode == "Technical Depth":
        return (
            f"Answer technically, then translate impact for: {prompt}\n\n"
            "1. State the architecture or method you used.\n"
            "2. Explain the tradeoff you considered.\n"
            "3. Name the measurable result.\n"
            "4. Tie it back to the role requirements.\n\n"
            f"Use this context as evidence: {proof}"
        )
    if mode == "Recovery":
        return (
            "Use this recovery move:\n\n"
            "That's a good question. Let me frame it clearly. The core issue is the outcome we needed, "
            "the constraint we had, and the decision I made. Then give one concrete example, one metric, "
            "and one lesson learned."
        )
    return (
        f"Suggested answer for: {prompt}\n\n"
        f"I would connect my background to {role} by highlighting one strong proof point: {proof}. "
        f"Then I would tie it to {company}'s needs and the job description: {jd}. "
        "Keep the answer specific, measurable, and calm. End with why this makes you ready for the role."
    )


def _extract_response_text(payload: dict[str, object]) -> str:
    output_text = payload.get("output_text")
    if isinstance(output_text, str) and output_text.strip():
        return output_text.strip()

    output = payload.get("output")
    if isinstance(output, list):
        chunks: list[str] = []
        for item in output:
            if not isinstance(item, dict):
                continue
            content = item.get("content")
            if not isinstance(content, list):
                continue
            for part in content:
                if not isinstance(part, dict):
                    continue
                text = part.get("text")
                if isinstance(text, str):
                    chunks.append(text)
        if chunks:
            return "\n".join(chunks).strip()
    return ""


def _openai_answer(session: InterviewSession, question: str, transcript: str, mode: str) -> str | None:
    settings = get_settings()
    if not settings.openai_api_key:
        return None

    instructions = (
        "You are RoleAxis Interview Assistant, a web-based SaaS interview coach. "
        "Help the candidate answer live interview questions with concise, professional, truthful guidance. "
        "Never invent credentials. Use the candidate context and job description only as support. "
        "Give an answer the candidate can say out loud."
    )
    user_prompt = {
        "target_role": session.target_role,
        "company": session.company_name,
        "interview_type": session.interview_type,
        "answer_mode": mode,
        "resume_context": _clip(session.resume_context, 3500),
        "job_description": _clip(session.job_description, 3500),
        "latest_question": _clip(question, 1600),
        "live_transcript": _clip(transcript, 3000),
        "format": "Use short paragraphs or bullets. Include a strong opening, 2-3 proof points, and a closing bridge.",
    }
    body = json.dumps(
        {
            "model": settings.interview_answer_model,
            "input": [
                {"role": "system", "content": instructions},
                {"role": "user", "content": json.dumps(user_prompt, ensure_ascii=False)},
            ],
            "max_output_tokens": 700,
        }
    ).encode("utf-8")
    request = urllib.request.Request(
        "https://api.openai.com/v1/responses",
        data=body,
        headers={
            "Authorization": f"Bearer {settings.openai_api_key}",
            "Content-Type": "application/json",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            payload = json.loads(response.read().decode("utf-8"))
    except (urllib.error.URLError, TimeoutError, json.JSONDecodeError):
        return None

    answer = _extract_response_text(payload)
    return answer or None


def generate_interview_answer(session: InterviewSession, question: str, transcript: str, mode: str) -> InterviewAnswer:
    mode = mode if mode in ANSWER_MODES else "Concise"
    ai_answer = _openai_answer(session, question, transcript, mode)
    if ai_answer:
        return InterviewAnswer(answer=ai_answer, used_ai=True)
    return InterviewAnswer(answer=_fallback_answer(session, question, transcript, mode), used_ai=False)


def record_interview_turn(
    db: Session,
    *,
    session: InterviewSession,
    user_id: int,
    question: str,
    transcript: str,
    mode: str,
    answer: str,
) -> InterviewTurn:
    turn = InterviewTurn(
        session_id=session.id,
        user_id=user_id,
        mode=mode if mode in ANSWER_MODES else "Concise",
        question=_clip(question, 3000),
        transcript_excerpt=_clip(transcript, 5000),
        answer=answer,
    )
    db.add(turn)
    db.commit()
    db.refresh(turn)
    return turn
