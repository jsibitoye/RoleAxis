# Architecture

## Overview

RoleAxis is a modular SaaS platform with a FastAPI web app and service boundaries for Evidence, Career, and Vault. The local MVP uses Jinja2 templates, SQLAlchemy models, and SQLite, with a path to PostgreSQL for production.

## Web App

- `apps/web/main.py`: public pages, auth routes, platform launcher, workspace routes, and Evidence workflows.
- `apps/web/models.py`: users, sessions, cases, evidence, imports, exports, and insights.
- `apps/web/services/auth.py`: registration, login, password hashing, and server-side sessions.
- `apps/web/services/organizer.py`: upload validation, safe filenames, category inference, exhibit numbering, and document-organizer import.
- `apps/web/services/email_importer.py`: safe adapter for email crawler report artifacts.
- `apps/web/services/insights.py`: current rule-based case readiness scoring.
- `apps/web/services/exporter.py`: current ZIP package generation.
- `apps/web/services/career.py`: Career service status adapters, starting with Interview Assistant.

## Service Boundaries

```text
services/evidence/
  email-crawler/
  document-organizer/
  ai-analysis/
  evidence-packager/

services/career/
  interview-assistant/
  presentation-assistant/
  meeting-assistant/
  job-search/
  resume-analyzer/
  ai-core/

services/vault/
  profile-engine/
  document-storage/
  achievement-tracker/
```

The Evidence workspace is live in the web app. The Career Interview Assistant is integrated as a Windows desktop service and build artifact. The remaining service folders define the planned platform ownership boundaries.

## Data Model

- `User`: account owner profile.
- `AuthSession`: server-side session records issued through HttpOnly cookies.
- `Case`: Evidence workspace case with type, petitioner, status, and notes.
- `EvidenceItem`: evidence record with exhibit number, title, category, source, file path, notes, and status.
- `EvidenceImport`: audit row for organizer and crawler imports.
- `ExportPackage`: generated ZIP package metadata.
- `CaseInsight`: readiness score and structured recommendations.

## Security

- Authenticated pages require a valid server-side session.
- Case, evidence, insight, and export routes are filtered by authenticated user ownership.
- Uploads are constrained to configured file extensions and size limits.
- Stored filenames are sanitized.
- Case files are written under isolated case folders.
- `.env`, uploads, exports, logs, virtual environments, local databases, desktop build outputs, and local API-key config files are ignored.
- Email imports use existing report artifacts and do not write email bodies to application logs.

## PostgreSQL Path

Set `ROLEAXIS_DATABASE_URL` to a PostgreSQL SQLAlchemy URL and install a PostgreSQL driver such as `psycopg[binary]`. The model layer does not depend on SQLite-specific APIs.
