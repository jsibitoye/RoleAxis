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

        columns = {row[1] for row in conn.execute(text("PRAGMA table_info(users)")).all()}
        additions = {
            "company_name": "ALTER TABLE users ADD COLUMN company_name VARCHAR(255) NOT NULL DEFAULT ''",
            "password_hash": "ALTER TABLE users ADD COLUMN password_hash TEXT NOT NULL DEFAULT ''",
            "role": "ALTER TABLE users ADD COLUMN role VARCHAR(80) NOT NULL DEFAULT 'Owner'",
            "is_active": "ALTER TABLE users ADD COLUMN is_active BOOLEAN NOT NULL DEFAULT 1",
            "last_login_at": "ALTER TABLE users ADD COLUMN last_login_at DATETIME",
        }

        for column_name, ddl in additions.items():
            if column_name not in columns:
                conn.execute(text(ddl))

