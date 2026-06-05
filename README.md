# RoleAxis

RoleAxis is a professional intelligence SaaS platform with three workspaces:

- **RoleAxis Evidence**: objective-first evidence intelligence, discovery, readiness strategy, and attorney-ready export packages.
- **RoleAxis Career**: interview, presentation, meeting, job-search, and resume intelligence.
- **RoleAxis Vault**: canonical professional profile, credentials, achievements, and document memory.

The current web app ships the full account flow, `/app` launcher, a working Evidence workspace, and the SaaS control plane for local desktop assistants.

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
- Evidence workspace create: <http://127.0.0.1:8000/evidence/cases/create>
- Career: <http://127.0.0.1:8000/career>
- Downloads: <http://127.0.0.1:8000/downloads>
- Devices: <http://127.0.0.1:8000/account/devices>
- Subscription: <http://127.0.0.1:8000/subscription>
- Vault: <http://127.0.0.1:8000/vault>
- Vault settings: <http://127.0.0.1:8000/vault/settings>

## What Works Now

- Register and log in to an account-isolated SaaS workspace.
- Land on `/app` after login or registration.
- Open RoleAxis Evidence from the app launcher.
- Create objective-first Evidence workspaces across Immigration, Career, Academic, Business, Legal, and Custom categories.
- Define what the workspace is trying to prove before collecting files.
- Authorize source discovery for local computer, email, cloud storage, professional profiles, and manual upload workflows.
- Review Evidence Inbox candidates before anything becomes approved evidence.
- Upload and securely store evidence files in separate case folders.
- Categorize evidence, generate `EXHIBIT_A001`-style exhibit numbers, and produce professional filenames.
- Import candidates from the document organizer configs.
- Import high-level candidates from email crawler reports when present.
- Generate readiness insights using rule-based scoring.
- Open Evidence Intelligence for gap analysis, roadmap, discovery missions, professional timeline, achievement graph, relationship map, reputation score, and reviewer concerns.
- Export an attorney-ready ZIP with organized folders, evidence table, evidence index, case summary, readiness insights, professional timeline, and case readiness roadmap.
- Open RoleAxis Career and verify the desktop Interview Assistant source/build status.
- Open Downloads, Devices, and Subscription pages for desktop licensing and device management.
- Use desktop API endpoints for login, heartbeat, logout, and license checks.
- Revoke desktop devices from the web account area.
- Open RoleAxis Vault module placeholders and choose a local-first Vault storage mode.

## Interview Assistant

The Interview Assistant remains a local Windows desktop app at `services/career/interview-assistant/`. RoleAxis Cloud handles account login, subscription status, device registration, session limits, and revocation. The realtime assistant itself runs locally because audio capture, desktop workflow context, and fast response loops belong on the workstation.

```powershell
dotnet build services\career\interview-assistant\RoleAxis.InterviewAssistant.csproj --configuration Release
```

For local development, set `OPENAI_API_KEY` or copy `config.example.json` to an ignored local `config.json`. Do not commit real API keys.

The desktop app should call:

- `POST /api/desktop/login`
- `POST /api/desktop/heartbeat`
- `POST /api/desktop/logout`
- `GET /api/desktop/license`

Local seeded test users can receive Pro subscriptions when `ROLEAXIS_SEED_LOCAL_PRO_SUBSCRIPTIONS=true` in `.env`.

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
- Desktop session tokens are stored as hashes and the plaintext token is returned only once at desktop login.
- Revoked desktop devices are blocked from heartbeat and license checks.
- Users can only access cases and export packages that belong to their account.
- Users can only see and revoke desktop devices registered to their own account.
- Uploads, exports, databases, build outputs, logs, and local API-key config files are ignored by git.
- Vault is local-first and does not upload large local documents by default.
- Upload filenames are sanitized before storage.
- File type and size validation run before an upload is accepted.
