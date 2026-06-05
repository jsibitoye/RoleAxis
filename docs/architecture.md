# Architecture

## Overview

RoleAxis is a modular SaaS platform with a FastAPI web control plane, local desktop assistants, and service boundaries for Evidence, Career, and Vault. The local MVP uses Jinja2 templates, SQLAlchemy models, and SQLite, with a path to PostgreSQL for production.

## Web App

- `apps/web/main.py`: public pages, auth routes, platform launcher, workspace routes, and Evidence workflows.
- `apps/web/models.py`: users, sessions, cases, evidence, imports, exports, and insights.
- `apps/web/services/auth.py`: registration, login, password hashing, and server-side sessions.
- `apps/web/services/desktop.py`: subscription plans, user subscriptions, device registration, hashed desktop sessions, heartbeat, logout, license checks, and revocation.
- `apps/web/services/evidence_framework.py`: Evidence workspace categories, templates, source options, and requirement maps.
- `apps/web/services/discovery.py`: source permissions, discovery jobs, candidate approval, roadmap, timeline, missions, relationship map, and reputation scoring.
- `apps/web/services/organizer.py`: upload validation, safe filenames, category inference, exhibit numbering, and document-organizer import.
- `apps/web/services/email_importer.py`: safe adapter for email crawler report artifacts.
- `apps/web/services/insights.py`: current rule-based case readiness scoring.
- `apps/web/services/exporter.py`: current ZIP package generation.
- `apps/web/services/career.py`: Career service status adapters, starting with Interview Assistant.

## Hybrid Desktop Architecture

RoleAxis Cloud is the SaaS system of record for accounts, plans, device limits, and license state. Desktop assistants are installed and run locally when the workflow needs workstation audio, files, or fast local interaction.

- The Interview Assistant remains in `services/career/interview-assistant/`.
- The desktop app authenticates through `POST /api/desktop/login` and receives a plaintext session token once.
- The web app stores only `session_token_hash`.
- The desktop app heartbeats through `POST /api/desktop/heartbeat`.
- The desktop app calls `GET /api/desktop/license` before unlocking paid features.
- Revoking a device in `/account/devices` ends active desktop sessions for that device.
- `POST /api/desktop/logout` ends the current desktop session.

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

The Evidence workspace is live in the web app. The Career Interview Assistant is integrated as a licensed Windows desktop service and build artifact, not as a browser-hosted interview room. The remaining service folders define the planned platform ownership boundaries.

## Data Model

- `User`: account owner profile.
- `AuthSession`: server-side session records issued through HttpOnly cookies.
- `SubscriptionPlan`: local plan catalog with price, device limit, active session limit, and feature list.
- `UserSubscription`: account plan state with active period and status.
- `DesktopDevice`: user-scoped registered local workstation with fingerprint, platform, version, status, and revocation state.
- `DesktopSession`: user-scoped desktop session with hashed token, status, heartbeat, and logout/end time.
- `Case`: Evidence workspace with category, template, proof objective, owner, status, and notes.
- `EvidenceItem`: approved evidence record with exhibit number, title, category, source, file path, confidence, relevance, notes, and status.
- `EvidenceSourcePermission`: explicit source authorization by case, source type, provider, scope, and note.
- `EvidenceDiscoveryJob`: permissioned discovery run metadata and candidate count.
- `EvidenceCandidate`: reviewable inbox item with suggested category, exhibit number, confidence, relevance, and approval state.
- `EvidenceImport`: audit row for organizer and crawler imports.
- `ExportPackage`: generated ZIP package metadata.
- `CaseInsight`: readiness score and structured recommendations.

## Security

- Authenticated pages require a valid server-side session.
- Desktop API calls require a valid desktop session token after login.
- Desktop tokens are stored as SHA-256 hashes, never plaintext.
- Device records are scoped by user and cannot be listed or revoked across accounts.
- Revoked devices cannot continue heartbeat or license checks.
- Inactive subscriptions block desktop feature access.
- Case, evidence, insight, and export routes are filtered by authenticated user ownership.
- Source permissions, discovery jobs, and candidates are filtered by authenticated user case ownership.
- Discovery candidates never become approved evidence until the user accepts them.
- Uploads are constrained to configured file extensions and size limits.
- Stored filenames are sanitized.
- Case files are written under isolated case folders.
- `.env`, uploads, exports, logs, virtual environments, local databases, desktop build outputs, and local API-key config files are ignored.
- Vault storage is local-first. Large local Vault documents are not uploaded by default; current cloud state stores only the selected storage mode.
- Email imports use existing report artifacts and do not write email bodies to application logs.

## PostgreSQL Path

Set `ROLEAXIS_DATABASE_URL` to a PostgreSQL SQLAlchemy URL and install a PostgreSQL driver such as `psycopg[binary]`. The model layer does not depend on SQLite-specific APIs.
