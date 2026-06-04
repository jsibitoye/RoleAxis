from __future__ import annotations

from collections.abc import Generator

from sqlalchemy import create_engine, text
from sqlalchemy.orm import DeclarativeBase, Session, sessionmaker

from apps.web.settings import get_settings


settings = get_settings()

connect_args = {"check_same_thread": False} if settings.database_url.startswith("sqlite") else {}
engine = create_engine(settings.database_url, connect_args=connect_args, future=True)
SessionLocal = sessionmaker(bind=engine, autoflush=False, autocommit=False, future=True)


class Base(DeclarativeBase):
    pass


def get_db() -> Generator[Session, None, None]:
    db = SessionLocal()
    try:
        yield db
    finally:
        db.close()


def init_db() -> None:
    from apps.web import models  # noqa: F401

    Base.metadata.create_all(bind=engine)
    ensure_schema_compatibility()


def ensure_schema_compatibility() -> None:
    """Additive SQLite migrations for local MVP databases created by earlier builds."""
    if engine.dialect.name != "sqlite":
        return

    with engine.begin() as conn:
        tables = {
            row[0]
            for row in conn.execute(text("SELECT name FROM sqlite_master WHERE type='table'")).all()
        }
        if "users" not in tables:
            return

        user_columns = {row[1] for row in conn.execute(text("PRAGMA table_info(users)")).all()}
        user_additions = {
            "company_name": "ALTER TABLE users ADD COLUMN company_name VARCHAR(255) NOT NULL DEFAULT ''",
            "password_hash": "ALTER TABLE users ADD COLUMN password_hash TEXT NOT NULL DEFAULT ''",
            "role": "ALTER TABLE users ADD COLUMN role VARCHAR(80) NOT NULL DEFAULT 'Owner'",
            "is_active": "ALTER TABLE users ADD COLUMN is_active BOOLEAN NOT NULL DEFAULT 1",
            "last_login_at": "ALTER TABLE users ADD COLUMN last_login_at DATETIME",
        }

        for column_name, ddl in user_additions.items():
            if column_name not in user_columns:
                conn.execute(text(ddl))

        if "cases" in tables:
            case_columns = {row[1] for row in conn.execute(text("PRAGMA table_info(cases)")).all()}
            case_additions = {
                "workspace_category": (
                    "ALTER TABLE cases ADD COLUMN workspace_category "
                    "VARCHAR(80) NOT NULL DEFAULT 'Immigration'"
                ),
                "proof_objective": "ALTER TABLE cases ADD COLUMN proof_objective TEXT NOT NULL DEFAULT ''",
            }
            for column_name, ddl in case_additions.items():
                if column_name not in case_columns:
                    conn.execute(text(ddl))

        if "evidence_items" in tables:
            evidence_columns = {row[1] for row in conn.execute(text("PRAGMA table_info(evidence_items)")).all()}
            evidence_additions = {
                "evidence_date": "ALTER TABLE evidence_items ADD COLUMN evidence_date VARCHAR(80) NOT NULL DEFAULT ''",
                "confidence_score": "ALTER TABLE evidence_items ADD COLUMN confidence_score INTEGER NOT NULL DEFAULT 0",
                "relevance_score": "ALTER TABLE evidence_items ADD COLUMN relevance_score INTEGER NOT NULL DEFAULT 0",
            }
            for column_name, ddl in evidence_additions.items():
                if column_name not in evidence_columns:
                    conn.execute(text(ddl))
