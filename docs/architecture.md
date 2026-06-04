# Architecture

## Overview

RoleAxis Evidence is a Python FastAPI application using server-rendered Jinja2 templates, SQLAlchemy models, and SQLite for the local MVP. The data layer is structured so the database URL can be changed to PostgreSQL later.

## Main Components

- `apps/web/main.py`: FastAPI routes and request orchestration.
- `apps/web/models.py`: SQLAlchemy models for users, cases, evidence, imports, exports, and insights.
- `apps/web/services/auth.py`: Registration, login, password hashing, and server-side sessions.
- `apps/web/services/organizer.py`: Upload validation, safe filenames, category inference, exhibit numbering, and document-organizer import.
- `apps/web/services/email_importer.py`: Safe adapter for existing email crawler analysis files.
- `apps/web/services/insights.py`: Rule-based case readiness scoring.
- `apps/web/services/exporter.py`: Attorney-ready ZIP package generation.
- `services/document-organizer`: Existing exhibit processing scripts and configs.
- `services/email-crawler`: Existing EB-1A crawler package.

## Data Model

- `User`: local owner profile.
- `AuthSession`: server-side session records issued through HttpOnly cookies.
- `Case`: evidence workspace with type, petitioner, status, and notes.
- `EvidenceItem`: evidence record with exhibit number, title, category, source, file path, notes, and status.
- `EvidenceImport`: audit row for organizer and crawler imports.
- `ExportPackage`: generated ZIP package metadata.
- `CaseInsight`: readiness score and structured recommendations.

## Security

- Uploads are constrained to configured file extensions.
- Passwords are hashed with PBKDF2-SHA256.
- Sessions are stored server-side and scoped by secure random tokens.
- Case, evidence, insight, and export routes are filtered by authenticated user ownership.
- Uploads are size-limited.
- Stored filenames are sanitized.
- Case files are written under isolated case folders.
- `.env`, uploads, exports, logs, virtual environments, and local databases are ignored.
- Email imports use existing report artifacts and do not write email bodies to application logs.

## PostgreSQL Path

Set `ROLEAXIS_DATABASE_URL` to a PostgreSQL SQLAlchemy URL and install a PostgreSQL driver such as `psycopg[binary]`. The model layer does not depend on SQLite-specific APIs.
