from __future__ import annotations

from pathlib import Path
from typing import Annotated

from fastapi import Depends, FastAPI, File, Form, HTTPException, Request, UploadFile
from fastapi.responses import FileResponse, JSONResponse, RedirectResponse
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates
from sqlalchemy import func
from sqlalchemy.orm import Session

from apps.web.database import get_db, init_db
from apps.web.models import (
    Case,
    CaseInsight,
    DesktopDevice,
    DesktopSession,
    EvidenceCandidate,
    EvidenceDiscoveryJob,
    EvidenceImport,
    EvidenceItem,
    EvidenceSourcePermission,
    ExportPackage,
    SubscriptionPlan,
    User,
)
from apps.web.services.auth import (
    SESSION_COOKIE,
    authenticate_user,
    clear_session_cookie,
    create_session,
    get_current_user,
    register_user,
    require_user,
    revoke_session,
    set_session_cookie,
)
from apps.web.services.career import get_interview_assistant_status
from apps.web.services.desktop import (
    DesktopLicenseError,
    VAULT_STORAGE_MODES,
    active_device_count,
    active_session_count,
    desktop_suite_manifest,
    desktop_heartbeat,
    desktop_license,
    desktop_login,
    desktop_logout,
    ensure_user_subscription,
    plan_features,
    revoke_device,
    subscription_payload,
)
from apps.web.services.discovery import approve_candidate, build_case_intelligence, create_discovery_job, reject_candidate
from apps.web.services.email_importer import import_email_candidates
from apps.web.services.evidence_framework import (
    EVIDENCE_SOURCE_GROUPS,
    WORKSPACE_CATEGORIES,
    category_for_template,
    objective_for_template,
)
from apps.web.services.exporter import build_export_package
from apps.web.services.insights import analyze_case, parse_json_list
from apps.web.services.organizer import (
    CASE_TYPES,
    EVIDENCE_CATEGORIES,
    STATUS_OPTIONS,
    assign_exhibit_numbers,
    import_document_organizer_candidates,
    update_evidence_item,
)
from apps.web.settings import BASE_DIR, get_settings


settings = get_settings()
app = FastAPI(title=settings.app_name)

static_dir = BASE_DIR / "apps" / "web" / "static"
template_dir = BASE_DIR / "apps" / "web" / "templates"
app.mount("/static", StaticFiles(directory=static_dir), name="static")
templates = Jinja2Templates(directory=template_dir)
templates.env.filters["json_list"] = parse_json_list


@app.on_event("startup")
def on_startup() -> None:
    init_db()


def get_case_or_404(db: Session, case_id: int, user: User) -> Case:
    case = db.query(Case).filter(Case.id == case_id, Case.user_id == user.id).first()
    if not case:
        raise HTTPException(status_code=404, detail="Case not found")
    return case


def latest_insight(db: Session, case_id: int) -> CaseInsight | None:
    return (
        db.query(CaseInsight)
        .filter(CaseInsight.case_id == case_id)
        .order_by(CaseInsight.created_at.desc())
        .first()
    )


def desktop_error_response(error: DesktopLicenseError) -> JSONResponse:
    return JSONResponse(
        {"ok": False, "error": {"code": error.code, "message": error.message}},
        status_code=error.status_code,
    )


def bearer_token(request: Request) -> str:
    authorization = request.headers.get("authorization", "")
    if authorization.lower().startswith("bearer "):
        return authorization.split(" ", 1)[1].strip()
    return request.query_params.get("session_token") or request.query_params.get("token") or ""


@app.get("/")
def landing(
    request: Request,
    current_user: Annotated[User | None, Depends(get_current_user)],
) -> object:
    return templates.TemplateResponse(
        request,
        "landing.html",
        {"shell": False, "settings": settings, "current_user": current_user},
    )


@app.get("/register")
def register_page(
    request: Request,
    current_user: Annotated[User | None, Depends(get_current_user)],
) -> object:
    if current_user:
        return RedirectResponse("/app", status_code=303)
    return templates.TemplateResponse(
        request,
        "auth.html",
        {"shell": False, "mode": "register", "current_user": current_user},
    )


@app.post("/register")
def register_account(
    request: Request,
    db: Annotated[Session, Depends(get_db)],
    full_name: Annotated[str, Form()],
    company_name: Annotated[str, Form()] = "",
    email: Annotated[str, Form()] = "",
    password: Annotated[str, Form()] = "",
) -> object:
    user, error = register_user(
        db,
        full_name=full_name,
        company_name=company_name,
        email=email,
        password=password,
    )
    if error or user is None:
        return templates.TemplateResponse(
            request,
            "auth.html",
            {
                "shell": False,
                "mode": "register",
                "error": error,
                "form": {
                    "full_name": full_name,
                    "company_name": company_name,
                    "email": email,
                },
            },
            status_code=400,
        )

    response = RedirectResponse("/app", status_code=303)
    set_session_cookie(response, create_session(db, user))
    return response


@app.get("/login")
def login_page(
    request: Request,
    current_user: Annotated[User | None, Depends(get_current_user)],
) -> object:
    if current_user:
        return RedirectResponse("/app", status_code=303)
    return templates.TemplateResponse(
        request,
        "auth.html",
        {"shell": False, "mode": "login", "current_user": current_user},
    )


@app.post("/login")
def login_account(
    request: Request,
    db: Annotated[Session, Depends(get_db)],
    email: Annotated[str, Form()] = "",
    password: Annotated[str, Form()] = "",
) -> object:
    user = authenticate_user(db, email, password)
    if user is None:
        return templates.TemplateResponse(
            request,
            "auth.html",
            {
                "shell": False,
                "mode": "login",
                "error": "The email or password was incorrect.",
                "form": {"email": email},
            },
            status_code=400,
        )

    response = RedirectResponse("/app", status_code=303)
    set_session_cookie(response, create_session(db, user))
    return response


@app.post("/logout")
def logout(request: Request, db: Annotated[Session, Depends(get_db)]) -> object:
    revoke_session(db, request.cookies.get(SESSION_COOKIE))
    response = RedirectResponse("/login", status_code=303)
    clear_session_cookie(response)
    return response


@app.get("/app")
def platform_app(
    request: Request,
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    workspaces = [
        {
            "name": "Evidence",
            "href": "/evidence",
            "status": "Live",
            "description": "Immigration evidence, attorney-ready packages, and case readiness insights.",
        },
        {
            "name": "Career",
            "href": "/career",
            "status": "Expanding",
            "description": "Interview, presentation, meeting, job-search, and resume intelligence.",
        },
        {
            "name": "Vault",
            "href": "/vault",
            "status": "Local-first",
            "description": "A private professional memory layer for credentials, projects, publications, and achievements.",
        },
        {
            "name": "Desktop Apps",
            "href": "/downloads",
            "status": "Command suite",
            "description": "Download RoleAxis Desktop for local interview, meeting, presentation, vault, and evidence workflows.",
        },
    ]
    return templates.TemplateResponse(
        request,
        "app.html",
        {"workspaces": workspaces, "active": "app", "current_user": current_user},
    )


@app.get("/downloads")
def downloads_page(
    request: Request,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    status = get_interview_assistant_status()
    subscription = ensure_user_subscription(db, current_user)
    suite = desktop_suite_manifest()
    return templates.TemplateResponse(
        request,
        "downloads.html",
        {
            "status": status,
            "subscription": subscription,
            "plan": subscription.plan,
            "features": plan_features(subscription.plan),
            "suite": suite,
            "device_count": active_device_count(db, current_user),
            "active_session_count": active_session_count(db, current_user),
            "active": "downloads",
            "workspace": "account",
            "current_user": current_user,
        },
    )


@app.get("/account/devices")
def account_devices_page(
    request: Request,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    subscription = ensure_user_subscription(db, current_user)
    devices = (
        db.query(DesktopDevice)
        .filter(DesktopDevice.user_id == current_user.id)
        .order_by(DesktopDevice.created_at.desc())
        .all()
    )
    sessions = (
        db.query(DesktopSession)
        .filter(DesktopSession.user_id == current_user.id)
        .order_by(DesktopSession.started_at.desc())
        .limit(20)
        .all()
    )
    return templates.TemplateResponse(
        request,
        "devices.html",
        {
            "subscription": subscription,
            "plan": subscription.plan,
            "devices": devices,
            "sessions": sessions,
            "device_count": active_device_count(db, current_user),
            "active_session_count": active_session_count(db, current_user),
            "active": "devices",
            "workspace": "account",
            "current_user": current_user,
        },
    )


@app.post("/account/devices/{device_id}/revoke")
def revoke_account_device(
    device_id: int,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    try:
        revoke_device(db, current_user, device_id)
    except DesktopLicenseError as exc:
        raise HTTPException(status_code=exc.status_code, detail=exc.message) from exc
    return RedirectResponse("/account/devices", status_code=303)


@app.get("/subscription")
def subscription_page(
    request: Request,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    subscription = ensure_user_subscription(db, current_user)
    plans = db.query(SubscriptionPlan).order_by(SubscriptionPlan.id.asc()).all()
    return templates.TemplateResponse(
        request,
        "subscription.html",
        {
            "subscription": subscription,
            "plan": subscription.plan,
            "plans": plans,
            "plan_features": plan_features,
            "device_count": active_device_count(db, current_user),
            "active_session_count": active_session_count(db, current_user),
            "active": "subscription",
            "workspace": "account",
            "current_user": current_user,
        },
    )


@app.get("/dashboard", include_in_schema=False)
def legacy_dashboard_redirect() -> object:
    return RedirectResponse("/app", status_code=303)


@app.get("/evidence")
def evidence_dashboard(
    request: Request,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    cases = (
        db.query(Case)
        .filter(Case.user_id == current_user.id)
        .order_by(Case.updated_at.desc())
        .limit(6)
        .all()
    )
    case_count = db.query(func.count(Case.id)).filter(Case.user_id == current_user.id).scalar() or 0
    evidence_count = (
        db.query(func.count(EvidenceItem.id))
        .join(Case, EvidenceItem.case_id == Case.id)
        .filter(Case.user_id == current_user.id)
        .scalar()
        or 0
    )
    export_count = (
        db.query(func.count(ExportPackage.id))
        .join(Case, ExportPackage.case_id == Case.id)
        .filter(Case.user_id == current_user.id)
        .scalar()
        or 0
    )
    candidate_count = (
        db.query(func.count(EvidenceCandidate.id))
        .join(Case, EvidenceCandidate.case_id == Case.id)
        .filter(Case.user_id == current_user.id, EvidenceCandidate.status == "Pending")
        .scalar()
        or 0
    )
    source_count = (
        db.query(func.count(EvidenceSourcePermission.id))
        .join(Case, EvidenceSourcePermission.case_id == Case.id)
        .filter(Case.user_id == current_user.id)
        .scalar()
        or 0
    )
    latest_scores = (
        db.query(CaseInsight.score)
        .join(Case, CaseInsight.case_id == Case.id)
        .filter(Case.user_id == current_user.id)
        .order_by(CaseInsight.created_at.desc())
        .limit(12)
        .all()
    )
    avg_score = round(sum(score for (score,) in latest_scores) / len(latest_scores)) if latest_scores else 0
    return templates.TemplateResponse(
        request,
        "dashboard.html",
        {
            "cases": cases,
            "case_count": case_count,
            "evidence_count": evidence_count,
            "export_count": export_count,
            "candidate_count": candidate_count,
            "source_count": source_count,
            "avg_score": avg_score,
            "active": "dashboard",
            "workspace": "evidence",
            "current_user": current_user,
        },
    )


@app.get("/cases", include_in_schema=False)
def legacy_cases_redirect() -> object:
    return RedirectResponse("/evidence/cases", status_code=303)


@app.get("/evidence/cases")
def cases_page(
    request: Request,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    cases = db.query(Case).filter(Case.user_id == current_user.id).order_by(Case.updated_at.desc()).all()
    return templates.TemplateResponse(
        request,
        "cases.html",
        {"cases": cases, "active": "cases", "workspace": "evidence", "current_user": current_user},
    )


@app.get("/cases/new", include_in_schema=False)
def legacy_new_case_redirect() -> object:
    return RedirectResponse("/evidence/cases/create", status_code=303)


@app.get("/evidence/cases/create")
def new_case_page(
    request: Request,
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    return templates.TemplateResponse(
        request,
        "case_new.html",
        {
            "case_types": CASE_TYPES,
            "workspace_categories": WORKSPACE_CATEGORIES,
            "active": "cases",
            "workspace": "evidence",
            "current_user": current_user,
        },
    )


@app.post("/evidence/cases")
def create_case(
    request: Request,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
    title: Annotated[str, Form()],
    case_type: Annotated[str, Form()],
    petitioner_name: Annotated[str, Form()] = "",
    proof_objective: Annotated[str, Form()] = "",
    description: Annotated[str, Form()] = "",
) -> object:
    if case_type not in CASE_TYPES:
        return templates.TemplateResponse(
            request,
            "case_new.html",
            {
                "case_types": CASE_TYPES,
                "workspace_categories": WORKSPACE_CATEGORIES,
                "error": "Choose a supported case type.",
                "active": "cases",
                "workspace": "evidence",
                "current_user": current_user,
            },
            status_code=400,
        )
    case = Case(
        user_id=current_user.id,
        title=title.strip(),
        workspace_category=category_for_template(case_type),
        case_type=case_type,
        petitioner_name=petitioner_name.strip(),
        proof_objective=proof_objective.strip() or objective_for_template(case_type),
        description=description.strip(),
        status="Active",
    )
    db.add(case)
    db.commit()
    db.refresh(case)
    return RedirectResponse(f"/evidence/cases/{case.id}", status_code=303)


@app.get("/evidence/cases/{case_id}")
def case_detail(
    request: Request,
    case_id: int,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    case = get_case_or_404(db, case_id, current_user)
    evidence_items = (
        db.query(EvidenceItem)
        .filter(EvidenceItem.case_id == case.id)
        .order_by(EvidenceItem.created_at.desc())
        .limit(6)
        .all()
    )
    imports = (
        db.query(EvidenceImport)
        .filter(EvidenceImport.case_id == case.id)
        .order_by(EvidenceImport.created_at.desc())
        .limit(4)
        .all()
    )
    export = (
        db.query(ExportPackage)
        .filter(ExportPackage.case_id == case.id)
        .order_by(ExportPackage.created_at.desc())
        .first()
    )
    insight = latest_insight(db, case.id)
    pending_candidate_count = (
        db.query(func.count(EvidenceCandidate.id))
        .filter(EvidenceCandidate.case_id == case.id, EvidenceCandidate.status == "Pending")
        .scalar()
        or 0
    )
    source_count = (
        db.query(func.count(EvidenceSourcePermission.id))
        .filter(EvidenceSourcePermission.case_id == case.id)
        .scalar()
        or 0
    )
    evidence_count = db.query(func.count(EvidenceItem.id)).filter(EvidenceItem.case_id == case.id).scalar() or 0
    attached_count = (
        db.query(func.count(EvidenceItem.id))
        .filter(EvidenceItem.case_id == case.id, EvidenceItem.file_path != "")
        .scalar()
        or 0
    )
    return templates.TemplateResponse(
        request,
        "case_detail.html",
        {
            "case": case,
            "evidence_items": evidence_items,
            "imports": imports,
            "export": export,
            "insight": insight,
            "evidence_count": evidence_count,
            "attached_count": attached_count,
            "pending_candidate_count": pending_candidate_count,
            "source_count": source_count,
            "active": "cases",
            "workspace": "evidence",
            "current_user": current_user,
        },
    )


@app.get("/evidence/cases/{case_id}/sources")
def source_center_page(
    request: Request,
    case_id: int,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    case = get_case_or_404(db, case_id, current_user)
    permissions = (
        db.query(EvidenceSourcePermission)
        .filter(EvidenceSourcePermission.case_id == case.id)
        .order_by(EvidenceSourcePermission.created_at.desc())
        .all()
    )
    jobs = (
        db.query(EvidenceDiscoveryJob)
        .filter(EvidenceDiscoveryJob.case_id == case.id)
        .order_by(EvidenceDiscoveryJob.created_at.desc())
        .limit(8)
        .all()
    )
    candidates = (
        db.query(EvidenceCandidate)
        .filter(EvidenceCandidate.case_id == case.id)
        .order_by(EvidenceCandidate.status.asc(), EvidenceCandidate.confidence_score.desc())
        .all()
    )
    return templates.TemplateResponse(
        request,
        "source_center.html",
        {
            "case": case,
            "source_groups": EVIDENCE_SOURCE_GROUPS,
            "permissions": permissions,
            "jobs": jobs,
            "candidates": candidates,
            "active": "cases",
            "workspace": "evidence",
            "current_user": current_user,
        },
    )


@app.post("/evidence/cases/{case_id}/sources/discover")
def run_discovery(
    case_id: int,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
    source_type: Annotated[str, Form()],
    provider: Annotated[str, Form()],
    scope: Annotated[str, Form()],
    permission_note: Annotated[str, Form()] = "",
) -> object:
    case = get_case_or_404(db, case_id, current_user)
    create_discovery_job(
        db,
        case,
        source_type=source_type,
        provider=provider,
        scope=scope,
        permission_note=permission_note,
    )
    return RedirectResponse(f"/evidence/cases/{case.id}/sources", status_code=303)


@app.post("/evidence/cases/{case_id}/candidates/{candidate_id}/approve")
def approve_evidence_candidate(
    case_id: int,
    candidate_id: int,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    case = get_case_or_404(db, case_id, current_user)
    candidate = (
        db.query(EvidenceCandidate)
        .filter(EvidenceCandidate.case_id == case.id, EvidenceCandidate.id == candidate_id)
        .first()
    )
    if not candidate:
        raise HTTPException(status_code=404, detail="Candidate not found")
    approve_candidate(db, case, candidate)
    return RedirectResponse(f"/evidence/cases/{case.id}/sources", status_code=303)


@app.post("/evidence/cases/{case_id}/candidates/{candidate_id}/reject")
def reject_evidence_candidate(
    case_id: int,
    candidate_id: int,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    case = get_case_or_404(db, case_id, current_user)
    candidate = (
        db.query(EvidenceCandidate)
        .filter(EvidenceCandidate.case_id == case.id, EvidenceCandidate.id == candidate_id)
        .first()
    )
    if not candidate:
        raise HTTPException(status_code=404, detail="Candidate not found")
    reject_candidate(db, candidate)
    return RedirectResponse(f"/evidence/cases/{case.id}/sources", status_code=303)


@app.get("/evidence/cases/{case_id}/upload")
def upload_page(
    request: Request,
    case_id: int,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    case = get_case_or_404(db, case_id, current_user)
    return templates.TemplateResponse(
        request,
        "upload.html",
        {
            "case": case,
            "categories": EVIDENCE_CATEGORIES,
            "active": "cases",
            "workspace": "evidence",
            "current_user": current_user,
        },
    )


@app.post("/evidence/cases/{case_id}/upload")
async def upload_evidence(
    request: Request,
    case_id: int,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
    files: Annotated[list[UploadFile], File()],
    category: Annotated[str, Form()] = "",
    source: Annotated[str, Form()] = "Upload",
    description: Annotated[str, Form()] = "",
    relevance_notes: Annotated[str, Form()] = "",
) -> object:
    case = get_case_or_404(db, case_id, current_user)
    created = 0
    try:
        for upload in files:
            if not upload.filename:
                continue
            content = await upload.read()
            from apps.web.services.organizer import save_uploaded_evidence

            save_uploaded_evidence(
                db,
                case,
                original_filename=upload.filename,
                content=content,
                category=category,
                source=source,
                description=description,
                relevance_notes=relevance_notes,
            )
            created += 1
    except ValueError as exc:
        return templates.TemplateResponse(
            request,
            "upload.html",
            {
                "case": case,
                "categories": EVIDENCE_CATEGORIES,
                "error": str(exc),
                "active": "cases",
                "workspace": "evidence",
                "current_user": current_user,
            },
            status_code=400,
        )
    return RedirectResponse(f"/evidence/cases/{case.id}/table?uploaded={created}", status_code=303)


@app.get("/evidence/cases/{case_id}/table")
def evidence_table(
    request: Request,
    case_id: int,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    case = get_case_or_404(db, case_id, current_user)
    assign_exhibit_numbers(db, case)
    db.commit()
    items = (
        db.query(EvidenceItem)
        .filter(EvidenceItem.case_id == case.id)
        .order_by(EvidenceItem.exhibit_number.asc(), EvidenceItem.created_at.asc())
        .all()
    )
    return templates.TemplateResponse(
        request,
        "evidence.html",
        {
            "case": case,
            "items": items,
            "categories": EVIDENCE_CATEGORIES,
            "statuses": STATUS_OPTIONS,
            "active": "cases",
            "workspace": "evidence",
            "current_user": current_user,
        },
    )


@app.post("/evidence/cases/{case_id}/evidence/{item_id}/update")
def update_evidence(
    case_id: int,
    item_id: int,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
    title: Annotated[str, Form()],
    category: Annotated[str, Form()],
    status: Annotated[str, Form()],
    description: Annotated[str, Form()] = "",
    relevance_notes: Annotated[str, Form()] = "",
) -> object:
    case = get_case_or_404(db, case_id, current_user)
    item = (
        db.query(EvidenceItem)
        .filter(EvidenceItem.case_id == case.id, EvidenceItem.id == item_id)
        .first()
    )
    if not item:
        raise HTTPException(status_code=404, detail="Evidence item not found")
    update_evidence_item(
        db,
        item,
        title=title,
        category=category,
        status=status,
        description=description,
        relevance_notes=relevance_notes,
    )
    return RedirectResponse(f"/evidence/cases/{case.id}/table", status_code=303)


@app.post("/evidence/cases/{case_id}/organize")
def organize_case(
    case_id: int,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    case = get_case_or_404(db, case_id, current_user)
    assign_exhibit_numbers(db, case)
    db.commit()
    return RedirectResponse(f"/evidence/cases/{case.id}/table", status_code=303)


@app.post("/evidence/cases/{case_id}/imports/document-organizer")
def import_from_document_organizer(
    case_id: int,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    case = get_case_or_404(db, case_id, current_user)
    import_document_organizer_candidates(db, case)
    return RedirectResponse(f"/evidence/cases/{case.id}", status_code=303)


@app.post("/evidence/cases/{case_id}/imports/email")
def import_from_email_crawler(
    case_id: int,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    case = get_case_or_404(db, case_id, current_user)
    import_email_candidates(db, case)
    return RedirectResponse(f"/evidence/cases/{case.id}", status_code=303)


@app.get("/evidence/cases/{case_id}/insights")
def insights_page(
    request: Request,
    case_id: int,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    case = get_case_or_404(db, case_id, current_user)
    insight = latest_insight(db, case.id) or analyze_case(db, case)
    return templates.TemplateResponse(
        request,
        "insights.html",
        {"case": case, "insight": insight, "active": "cases", "workspace": "evidence", "current_user": current_user},
    )


@app.get("/evidence/cases/{case_id}/intelligence")
def evidence_intelligence_page(
    request: Request,
    case_id: int,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    case = get_case_or_404(db, case_id, current_user)
    insight = latest_insight(db, case.id) or analyze_case(db, case)
    intelligence = build_case_intelligence(db, case, insight)
    return templates.TemplateResponse(
        request,
        "intelligence.html",
        {
            "case": case,
            "insight": insight,
            "intelligence": intelligence,
            "active": "cases",
            "workspace": "evidence",
            "current_user": current_user,
        },
    )


@app.post("/evidence/cases/{case_id}/insights/generate")
def generate_insights(
    case_id: int,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    case = get_case_or_404(db, case_id, current_user)
    analyze_case(db, case)
    return RedirectResponse(f"/evidence/cases/{case.id}/insights", status_code=303)


@app.get("/evidence/cases/{case_id}/export")
def export_page(
    request: Request,
    case_id: int,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    case = get_case_or_404(db, case_id, current_user)
    packages = (
        db.query(ExportPackage)
        .filter(ExportPackage.case_id == case.id)
        .order_by(ExportPackage.created_at.desc())
        .all()
    )
    return templates.TemplateResponse(
        request,
        "export.html",
        {"case": case, "packages": packages, "active": "cases", "workspace": "evidence", "current_user": current_user},
    )


@app.post("/evidence/cases/{case_id}/export")
def generate_export(
    case_id: int,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    case = get_case_or_404(db, case_id, current_user)
    package = build_export_package(db, case)
    return RedirectResponse(f"/packages/{package.id}/download", status_code=303)


@app.get("/career")
def career_dashboard(
    request: Request,
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    modules = [
        {
            "name": "Interview Assistant",
            "href": "/career/interview-assistant",
            "status": "Desktop ready",
            "description": "Realtime interview transcription and answer assistance with resume and job-description context.",
        },
        {
            "name": "Presentation Assistant",
            "href": "/downloads",
            "status": "Desktop ready",
            "description": "Prepare, rehearse, answer live questions, and generate follow-up notes inside RoleAxis Desktop.",
        },
        {
            "name": "Meeting Assistant",
            "href": "/downloads",
            "status": "Desktop ready",
            "description": "Capture live transcript, decisions, risks, action items, and follow-up emails locally.",
        },
        {
            "name": "Job Search",
            "href": "#",
            "status": "Planned",
            "description": "Track target roles, fit, outreach, and application readiness.",
        },
        {
            "name": "Resume Analyzer",
            "href": "#",
            "status": "Planned",
            "description": "Score resumes against roles and generate targeted improvement plans.",
        },
    ]
    return templates.TemplateResponse(
        request,
        "career.html",
        {"modules": modules, "active": "career", "workspace": "career", "current_user": current_user},
    )


@app.get("/career/interview-assistant")
def interview_assistant_page(
    request: Request,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    status = get_interview_assistant_status()
    subscription = ensure_user_subscription(db, current_user)
    return templates.TemplateResponse(
        request,
        "interview_assistant.html",
        {
            "status": status,
            "subscription": subscription,
            "plan": subscription.plan,
            "features": plan_features(subscription.plan),
            "device_count": active_device_count(db, current_user),
            "active_session_count": active_session_count(db, current_user),
            "active": "career",
            "workspace": "career",
            "current_user": current_user,
        },
    )


@app.get("/career/interview-assistant/context.json")
def interview_context_export(
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> JSONResponse:
    subscription = ensure_user_subscription(db, current_user)
    return JSONResponse(
        {
            "version": 1,
            "generated_for": {
                "user_id": current_user.id,
                "email": current_user.email,
                "full_name": current_user.full_name,
            },
            "subscription": subscription_payload(db, current_user, subscription),
            "vault": {
                "storage_mode": current_user.vault_storage_mode,
                "large_documents_upload_by_default": False,
                "context_sources": [],
            },
            "interview": {
                "sessions": [],
                "resume": None,
                "job_description": None,
                "notes": "Placeholder context export for the local desktop Interview Assistant.",
            },
        }
    )


@app.get("/vault")
def vault_dashboard(
    request: Request,
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    modules = [
        "Professional Profile",
        "Resume",
        "Certifications",
        "Degrees",
        "Projects",
        "Publications",
        "Awards",
        "Achievements",
    ]
    return templates.TemplateResponse(
        request,
        "vault.html",
        {
            "modules": modules,
            "storage_modes": VAULT_STORAGE_MODES,
            "active": "vault",
            "workspace": "vault",
            "current_user": current_user,
        },
    )


@app.get("/vault/settings")
def vault_settings_page(
    request: Request,
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    return templates.TemplateResponse(
        request,
        "vault_settings.html",
        {
            "storage_modes": VAULT_STORAGE_MODES,
            "active": "vault-settings",
            "workspace": "vault",
            "current_user": current_user,
        },
    )


@app.post("/vault/settings")
def update_vault_settings(
    request: Request,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
    vault_storage_mode: Annotated[str, Form()],
) -> object:
    if vault_storage_mode not in VAULT_STORAGE_MODES:
        return templates.TemplateResponse(
            request,
            "vault_settings.html",
            {
                "storage_modes": VAULT_STORAGE_MODES,
                "error": "Choose a supported Vault storage mode.",
                "active": "vault-settings",
                "workspace": "vault",
                "current_user": current_user,
            },
            status_code=400,
        )
    current_user.vault_storage_mode = vault_storage_mode
    db.commit()
    return RedirectResponse("/vault/settings", status_code=303)


@app.post("/api/desktop/login")
async def desktop_login_api(
    request: Request,
    db: Annotated[Session, Depends(get_db)],
) -> JSONResponse:
    try:
        payload = await request.json()
    except Exception:
        return desktop_error_response(
            DesktopLicenseError("Send a valid JSON desktop login payload.", status_code=400, code="invalid_json")
        )
    if not isinstance(payload, dict):
        return desktop_error_response(
            DesktopLicenseError("Send a valid JSON desktop login payload.", status_code=400, code="invalid_json")
        )

    try:
        result = desktop_login(
            db,
            login=str(payload.get("login") or payload.get("email") or payload.get("username") or ""),
            password=str(payload.get("password") or ""),
            device_name=str(payload.get("device_name") or "Local workstation"),
            device_fingerprint=str(payload.get("device_fingerprint") or ""),
            platform=str(payload.get("platform") or ""),
            app_version=str(payload.get("app_version") or ""),
        )
    except DesktopLicenseError as exc:
        return desktop_error_response(exc)
    return JSONResponse({"ok": True, **result})


@app.post("/api/desktop/heartbeat")
async def desktop_heartbeat_api(
    request: Request,
    db: Annotated[Session, Depends(get_db)],
) -> JSONResponse:
    try:
        payload = await request.json()
    except Exception:
        return desktop_error_response(
            DesktopLicenseError("Send a valid JSON heartbeat payload.", status_code=400, code="invalid_json")
        )
    if not isinstance(payload, dict):
        return desktop_error_response(
            DesktopLicenseError("Send a valid JSON heartbeat payload.", status_code=400, code="invalid_json")
        )

    try:
        result = desktop_heartbeat(
            db,
            token=str(payload.get("session_token") or payload.get("token") or ""),
            device_fingerprint=str(payload.get("device_fingerprint") or ""),
            app_version=str(payload.get("app_version") or ""),
            capabilities=[
                str(item)
                for item in payload.get("capabilities", [])
                if str(item).strip()
            ]
            if isinstance(payload.get("capabilities"), list)
            else [],
            local_state=payload.get("local_state") if isinstance(payload.get("local_state"), dict) else {},
        )
    except DesktopLicenseError as exc:
        return desktop_error_response(exc)
    return JSONResponse(result)


@app.post("/api/desktop/logout")
async def desktop_logout_api(
    request: Request,
    db: Annotated[Session, Depends(get_db)],
) -> JSONResponse:
    try:
        payload = await request.json()
    except Exception:
        return desktop_error_response(
            DesktopLicenseError("Send a valid JSON logout payload.", status_code=400, code="invalid_json")
        )
    if not isinstance(payload, dict):
        return desktop_error_response(
            DesktopLicenseError("Send a valid JSON logout payload.", status_code=400, code="invalid_json")
        )

    try:
        result = desktop_logout(db, token=str(payload.get("session_token") or payload.get("token") or ""))
    except DesktopLicenseError as exc:
        return desktop_error_response(exc)
    return JSONResponse(result)


@app.get("/api/desktop/license")
def desktop_license_api(
    request: Request,
    db: Annotated[Session, Depends(get_db)],
) -> JSONResponse:
    try:
        result = desktop_license(
            db,
            token=bearer_token(request),
            device_fingerprint=request.query_params.get("device_fingerprint") or "",
        )
    except DesktopLicenseError as exc:
        return desktop_error_response(exc)
    return JSONResponse({"ok": True, **result})


@app.get("/api/desktop/sync")
def desktop_sync_api(
    request: Request,
    db: Annotated[Session, Depends(get_db)],
) -> JSONResponse:
    try:
        result = desktop_license(
            db,
            token=bearer_token(request),
            device_fingerprint=request.query_params.get("device_fingerprint") or "",
        )
    except DesktopLicenseError as exc:
        return desktop_error_response(exc)
    return JSONResponse({"ok": True, "sync_status": "Connected", **result})


@app.get("/downloads/desktop/installer")
def download_desktop_installer(
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    subscription = ensure_user_subscription(db, current_user)
    if not subscription.plan or subscription.plan.name == "Free":
        raise HTTPException(status_code=403, detail="A licensed desktop plan is required.")

    status = get_interview_assistant_status()
    if not status.installer_exists or status.installer_file is None or not status.installer_file.exists():
        raise HTTPException(status_code=404, detail="Desktop installer artifact has not been built yet.")

    return FileResponse(
        status.installer_file,
        filename=status.installer_filename,
        media_type="application/vnd.microsoft.portable-executable",
    )


@app.get("/packages/{package_id}/download")
def download_package(
    package_id: int,
    db: Annotated[Session, Depends(get_db)],
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    package = (
        db.query(ExportPackage)
        .join(Case, ExportPackage.case_id == Case.id)
        .filter(ExportPackage.id == package_id, Case.user_id == current_user.id)
        .first()
    )
    if not package:
        raise HTTPException(status_code=404, detail="Package not found")
    path = Path(package.file_path)
    if not path.exists():
        raise HTTPException(status_code=404, detail="Package file not found")
    return FileResponse(path, filename=package.filename, media_type="application/zip")


@app.get("/settings")
def settings_page(
    request: Request,
    current_user: Annotated[User, Depends(require_user)],
) -> object:
    return templates.TemplateResponse(
        request,
        "settings.html",
        {
            "user": current_user,
            "settings": settings,
            "active": "settings",
            "current_user": current_user,
        },
    )
