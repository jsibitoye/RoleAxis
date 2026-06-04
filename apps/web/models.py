from __future__ import annotations

from datetime import datetime

from sqlalchemy import Boolean, DateTime, ForeignKey, Integer, String, Text
from sqlalchemy.orm import Mapped, mapped_column, relationship

from apps.web.database import Base


def utcnow() -> datetime:
    return datetime.utcnow()


class User(Base):
    __tablename__ = "users"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, index=True)
    email: Mapped[str] = mapped_column(String(255), unique=True, nullable=False, index=True)
    full_name: Mapped[str] = mapped_column(String(255), nullable=False, default="")
    company_name: Mapped[str] = mapped_column(String(255), nullable=False, default="")
    password_hash: Mapped[str] = mapped_column(Text, nullable=False, default="")
    role: Mapped[str] = mapped_column(String(80), nullable=False, default="Owner")
    is_active: Mapped[bool] = mapped_column(Boolean, nullable=False, default=True)
    last_login_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    created_at: Mapped[datetime] = mapped_column(DateTime, default=utcnow, nullable=False)

    cases: Mapped[list["Case"]] = relationship(back_populates="user")
    sessions: Mapped[list["AuthSession"]] = relationship(
        back_populates="user",
        cascade="all, delete-orphan",
    )


class AuthSession(Base):
    __tablename__ = "auth_sessions"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, index=True)
    user_id: Mapped[int] = mapped_column(ForeignKey("users.id"), nullable=False, index=True)
    token_hash: Mapped[str] = mapped_column(String(128), unique=True, nullable=False, index=True)
    expires_at: Mapped[datetime] = mapped_column(DateTime, nullable=False, index=True)
    revoked_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    created_at: Mapped[datetime] = mapped_column(DateTime, default=utcnow, nullable=False)

    user: Mapped[User] = relationship(back_populates="sessions")


class Case(Base):
    __tablename__ = "cases"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, index=True)
    user_id: Mapped[int | None] = mapped_column(ForeignKey("users.id"), nullable=True)
    title: Mapped[str] = mapped_column(String(255), nullable=False)
    workspace_category: Mapped[str] = mapped_column(String(80), nullable=False, default="Immigration")
    case_type: Mapped[str] = mapped_column(String(80), nullable=False, index=True)
    petitioner_name: Mapped[str] = mapped_column(String(255), nullable=False, default="")
    proof_objective: Mapped[str] = mapped_column(Text, nullable=False, default="")
    description: Mapped[str] = mapped_column(Text, nullable=False, default="")
    status: Mapped[str] = mapped_column(String(80), nullable=False, default="Active")
    created_at: Mapped[datetime] = mapped_column(DateTime, default=utcnow, nullable=False)
    updated_at: Mapped[datetime] = mapped_column(
        DateTime,
        default=utcnow,
        onupdate=utcnow,
        nullable=False,
    )

    user: Mapped[User | None] = relationship(back_populates="cases")
    evidence_items: Mapped[list["EvidenceItem"]] = relationship(
        back_populates="case",
        cascade="all, delete-orphan",
    )
    imports: Mapped[list["EvidenceImport"]] = relationship(
        back_populates="case",
        cascade="all, delete-orphan",
    )
    exports: Mapped[list["ExportPackage"]] = relationship(
        back_populates="case",
        cascade="all, delete-orphan",
    )
    insights: Mapped[list["CaseInsight"]] = relationship(
        back_populates="case",
        cascade="all, delete-orphan",
    )
    source_permissions: Mapped[list["EvidenceSourcePermission"]] = relationship(
        back_populates="case",
        cascade="all, delete-orphan",
    )
    discovery_jobs: Mapped[list["EvidenceDiscoveryJob"]] = relationship(
        back_populates="case",
        cascade="all, delete-orphan",
    )
    candidates: Mapped[list["EvidenceCandidate"]] = relationship(
        back_populates="case",
        cascade="all, delete-orphan",
    )


class EvidenceItem(Base):
    __tablename__ = "evidence_items"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, index=True)
    case_id: Mapped[int] = mapped_column(ForeignKey("cases.id"), nullable=False, index=True)
    exhibit_number: Mapped[str] = mapped_column(String(80), nullable=False, default="")
    title: Mapped[str] = mapped_column(String(500), nullable=False)
    original_filename: Mapped[str] = mapped_column(String(500), nullable=False, default="")
    renamed_filename: Mapped[str] = mapped_column(String(500), nullable=False, default="")
    category: Mapped[str] = mapped_column(String(120), nullable=False, default="Other Supporting Evidence")
    source: Mapped[str] = mapped_column(String(120), nullable=False, default="Upload")
    file_path: Mapped[str] = mapped_column(Text, nullable=False, default="")
    evidence_date: Mapped[str] = mapped_column(String(80), nullable=False, default="")
    confidence_score: Mapped[int] = mapped_column(Integer, nullable=False, default=0)
    relevance_score: Mapped[int] = mapped_column(Integer, nullable=False, default=0)
    description: Mapped[str] = mapped_column(Text, nullable=False, default="")
    relevance_notes: Mapped[str] = mapped_column(Text, nullable=False, default="")
    status: Mapped[str] = mapped_column(String(80), nullable=False, default="Cataloged")
    created_at: Mapped[datetime] = mapped_column(DateTime, default=utcnow, nullable=False)

    case: Mapped[Case] = relationship(back_populates="evidence_items")


class EvidenceImport(Base):
    __tablename__ = "evidence_imports"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, index=True)
    case_id: Mapped[int] = mapped_column(ForeignKey("cases.id"), nullable=False, index=True)
    source: Mapped[str] = mapped_column(String(120), nullable=False)
    status: Mapped[str] = mapped_column(String(80), nullable=False, default="Completed")
    imported_count: Mapped[int] = mapped_column(Integer, nullable=False, default=0)
    details: Mapped[str] = mapped_column(Text, nullable=False, default="")
    created_at: Mapped[datetime] = mapped_column(DateTime, default=utcnow, nullable=False)

    case: Mapped[Case] = relationship(back_populates="imports")


class ExportPackage(Base):
    __tablename__ = "export_packages"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, index=True)
    case_id: Mapped[int] = mapped_column(ForeignKey("cases.id"), nullable=False, index=True)
    filename: Mapped[str] = mapped_column(String(500), nullable=False)
    file_path: Mapped[str] = mapped_column(Text, nullable=False)
    status: Mapped[str] = mapped_column(String(80), nullable=False, default="Ready")
    created_at: Mapped[datetime] = mapped_column(DateTime, default=utcnow, nullable=False)

    case: Mapped[Case] = relationship(back_populates="exports")


class CaseInsight(Base):
    __tablename__ = "case_insights"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, index=True)
    case_id: Mapped[int] = mapped_column(ForeignKey("cases.id"), nullable=False, index=True)
    score: Mapped[int] = mapped_column(Integer, nullable=False, default=0)
    strengths: Mapped[str] = mapped_column(Text, nullable=False, default="[]")
    weaknesses: Mapped[str] = mapped_column(Text, nullable=False, default="[]")
    missing_evidence: Mapped[str] = mapped_column(Text, nullable=False, default="[]")
    recommendations: Mapped[str] = mapped_column(Text, nullable=False, default="[]")
    created_at: Mapped[datetime] = mapped_column(DateTime, default=utcnow, nullable=False)

    case: Mapped[Case] = relationship(back_populates="insights")


class EvidenceSourcePermission(Base):
    __tablename__ = "evidence_source_permissions"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, index=True)
    case_id: Mapped[int] = mapped_column(ForeignKey("cases.id"), nullable=False, index=True)
    source_type: Mapped[str] = mapped_column(String(120), nullable=False)
    provider: Mapped[str] = mapped_column(String(120), nullable=False)
    scope: Mapped[str] = mapped_column(String(160), nullable=False)
    permission_note: Mapped[str] = mapped_column(Text, nullable=False, default="")
    status: Mapped[str] = mapped_column(String(80), nullable=False, default="Authorized")
    created_at: Mapped[datetime] = mapped_column(DateTime, default=utcnow, nullable=False)

    case: Mapped[Case] = relationship(back_populates="source_permissions")
    discovery_jobs: Mapped[list["EvidenceDiscoveryJob"]] = relationship(
        back_populates="source_permission",
        cascade="all, delete-orphan",
    )


class EvidenceDiscoveryJob(Base):
    __tablename__ = "evidence_discovery_jobs"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, index=True)
    case_id: Mapped[int] = mapped_column(ForeignKey("cases.id"), nullable=False, index=True)
    source_permission_id: Mapped[int | None] = mapped_column(
        ForeignKey("evidence_source_permissions.id"),
        nullable=True,
        index=True,
    )
    source_summary: Mapped[str] = mapped_column(String(255), nullable=False)
    status: Mapped[str] = mapped_column(String(80), nullable=False, default="Completed")
    candidates_found: Mapped[int] = mapped_column(Integer, nullable=False, default=0)
    created_at: Mapped[datetime] = mapped_column(DateTime, default=utcnow, nullable=False)

    case: Mapped[Case] = relationship(back_populates="discovery_jobs")
    source_permission: Mapped[EvidenceSourcePermission | None] = relationship(back_populates="discovery_jobs")
    candidates: Mapped[list["EvidenceCandidate"]] = relationship(
        back_populates="discovery_job",
        cascade="all, delete-orphan",
    )


class EvidenceCandidate(Base):
    __tablename__ = "evidence_candidates"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, index=True)
    case_id: Mapped[int] = mapped_column(ForeignKey("cases.id"), nullable=False, index=True)
    discovery_job_id: Mapped[int | None] = mapped_column(
        ForeignKey("evidence_discovery_jobs.id"),
        nullable=True,
        index=True,
    )
    title: Mapped[str] = mapped_column(String(500), nullable=False)
    category: Mapped[str] = mapped_column(String(120), nullable=False, default="Other Supporting Evidence")
    suggested_exhibit_number: Mapped[str] = mapped_column(String(80), nullable=False, default="")
    confidence_score: Mapped[int] = mapped_column(Integer, nullable=False, default=0)
    relevance_score: Mapped[int] = mapped_column(Integer, nullable=False, default=0)
    source_type: Mapped[str] = mapped_column(String(120), nullable=False, default="")
    source_detail: Mapped[str] = mapped_column(String(255), nullable=False, default="")
    evidence_date: Mapped[str] = mapped_column(String(80), nullable=False, default="")
    description: Mapped[str] = mapped_column(Text, nullable=False, default="")
    status: Mapped[str] = mapped_column(String(80), nullable=False, default="Pending")
    created_at: Mapped[datetime] = mapped_column(DateTime, default=utcnow, nullable=False)

    case: Mapped[Case] = relationship(back_populates="candidates")
    discovery_job: Mapped[EvidenceDiscoveryJob | None] = relationship(back_populates="candidates")
