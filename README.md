# RoleAxis

RoleAxis is a professional intelligence SaaS platform with three workspaces:

- **RoleAxis Evidence**: case evidence, readiness insights, and attorney-ready export packages.
- **RoleAxis Career**: interview, presentation, meeting, job-search, and resume intelligence.
- **RoleAxis Vault**: canonical professional profile, credentials, achievements, and document memory.

The current web app ships the full account flow, `/app` launcher, a working Evidence workspace, and an integrated Career Interview Assistant service.

## Run Locally

```powershell
python -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt
uvicorn apps.web.main:app --reload
```

Open <http://127.0.0.1:8000>.

## Main URLs

- Public landing: <http://127.0.0.1:8000/>
- Register: <http://127.0.0.1:8000/register>
- Login: <http://127.0.0.1:8000/login>
- App launcher: <http://127.0.0.1:8000/app>
- Evidence: <http://127.0.0.1:8000/evidence>
- Career: <http://127.0.0.1:8000/career>
- Vault: <http://127.0.0.1:8000/vault>

## What Works Now

- Register and log in to an account-isolated SaaS workspace.
- Land on `/app` after login or registration.
- Open RoleAxis Evidence from the app launcher.
- Create evidence cases by matter type.
- Upload and securely store evidence files in separate case folders.
- Categorize evidence, generate exhibit numbers, and produce professional filenames.
- Import candidates from the document organizer configs.
- Import high-level candidates from email crawler reports when present.
- Generate readiness insights using rule-based scoring.
- Export an attorney-ready ZIP with organized folders, evidence table, evidence index, case summary, and readiness insights.
- Open RoleAxis Career and verify the integrated Interview Assistant source/build status.
- Open RoleAxis Vault module placeholders for profile, resume, credentials, projects, publications, awards, and achievements.

## Interview Assistant

The existing Windows desktop assistant lives at `services/career/interview-assistant/`.

```powershell
dotnet build services\career\interview-assistant\RoleAxis.InterviewAssistant.csproj --configuration Release
```

For local development, set `OPENAI_API_KEY` or copy `config.example.json` to an ignored local `config.json`. Do not commit real API keys.

## Project Layout

```text
apps/web/                         FastAPI app, templates, static assets, and web services
services/evidence/email-crawler/   Existing EB-1A email crawler package
services/evidence/document-organizer/
services/evidence/ai-analysis/     Future Evidence AI service boundary
services/evidence/evidence-packager/
services/career/interview-assistant/
services/career/presentation-assistant/
services/career/meeting-assistant/
services/career/job-search/
services/career/resume-analyzer/
services/career/ai-core/
services/vault/profile-engine/
services/vault/document-storage/
services/vault/achievement-tracker/
docs/                             Product, architecture, and roadmap docs
uploads/                          Local uploaded files, ignored by git
exports/                          Generated ZIP packages, ignored by git
data/                             Local SQLite database, ignored by git
```

## Security Notes

- `.env` is ignored and `.env.example` contains safe placeholders only.
- Passwords are stored with PBKDF2-SHA256 hashes, never plaintext.
- Sessions are stored server-side and issued with HttpOnly cookies.
- Users can only access cases and export packages that belong to their account.
- Uploads, exports, databases, build outputs, logs, and local API-key config files are ignored by git.
- Upload filenames are sanitized before storage.
- File type and size validation run before an upload is accepted.
