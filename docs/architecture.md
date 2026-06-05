# Architecture

## Overview

RoleAxis is a modular SaaS platform with a FastAPI web app and service boundaries for Evidence, Career, and Vault. The local MVP uses Jinja2 templates, SQLAlchemy models, and SQLite, with a path to PostgreSQL for production.

## Web App

- `apps/web/main.py`: public pages, auth routes, platform launcher, workspace routes, and Evidence workflows.
- `apps/web/models.py`: users, sessions, cases, evidence, imports, exports, and insights.
- `apps/web/services/auth.py`: registration, login, password hashing, and server-side sessions.
- `apps/web/services/evidence_framework.py`: Evidence workspace categories, templates, source options, and requirement maps.
- `apps/web/services/discovery.py`: source permissions, discovery jobs, candidate approval, roadmap, timeline, missions, relationship map, and reputation scoring.
- `apps/web/services/organizer.py`: upload validation, safe filenames, category inference, exhibit numbering, and document-organizer import.
- `apps/web/services/email_importer.py`: safe adapter for email crawler report artifacts.
- `apps/web/services/insights.py`: current rule-based case readiness scoring.
- `apps/web/services/exporter.py`: current ZIP package generation.
- `apps/web/services/career.py`: legacy desktop Interview Assistant status adapter.
- `apps/web/services/interview.py`: web Interview Assistant sessions, server-side answer generation, OpenAI Responses API integration, and local fallback coaching.

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

The Evidence workspace is live in the web app. The Career Interview Assistant now runs as a browser-based SaaS interview room, while the Windows desktop assistant remains as a legacy/local service artifact. The remaining service folders define the planned platform ownership boundaries.

## Data Model

- `User`: account owner profile.
- `AuthSession`: server-side session records issued through HttpOnly cookies.
- `Case`: Evidence workspace with category, template, proof objective, owner, status, and notes.
- `EvidenceItem`: approved evidence record with exhibit number, title, category, source, file path, confidence, relevance, notes, and status.
- `EvidenceSourcePermission`: explicit source authorization by case, source type, provider, scope, and note.
- `EvidenceDiscoveryJob`: permissioned discovery run metadata and candidate count.
- `EvidenceCandidate`: reviewable inbox item with suggested category, exhibit number, confidence, relevance, and approval state.
- `EvidenceImport`: audit row for organizer and crawler imports.
- `ExportPackage`: generated ZIP package metadata.
- `CaseInsight`: readiness score and structured recommendations.
- `InterviewSession`: per-user browser interview room with role, company, interview type, resume context, and job description.
- `InterviewTurn`: per-user answer coaching history for each interview session.

## Security

- Authenticated pages require a valid server-side session.
- Case, evidence, insight, and export routes are filtered by authenticated user ownership.
- Source permissions, discovery jobs, and candidates are filtered by authenticated user case ownership.
- Discovery candidates never become approved evidence until the user accepts them.
- Uploads are constrained to configured file extensions and size limits.
- Stored filenames are sanitized.
- Case files are written under isolated case folders.
- `.env`, uploads, exports, logs, virtual environments, local databases, desktop build outputs, and local API-key config files are ignored.
- Email imports use existing report artifacts and do not write email bodies to application logs.
- Interview API keys stay server-side in environment variables and are never sent to the browser.
- Browser speech capture is optional and user-permissioned; manual transcript entry remains available.

## PostgreSQL Path

Set `ROLEAXIS_DATABASE_URL` to a PostgreSQL SQLAlchemy URL and install a PostgreSQL driver such as `psycopg[binary]`. The model layer does not depend on SQLite-specific APIs.
