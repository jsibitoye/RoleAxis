# RoleAxis Evidence

RoleAxis Evidence is a local-first premium SaaS workspace for building attorney-ready evidence packages for NIW, EB-1A, O-1, academic promotion, and professional portfolio cases.

## Run Locally

```powershell
python -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt
uvicorn apps.web.main:app --reload
```

Open <http://127.0.0.1:8000>.

## What It Does

- Register and log in to an account-isolated SaaS workspace.
- Create evidence cases by matter type.
- Upload and securely store evidence files in separate case folders.
- Categorize evidence using legal-tech categories.
- Generate exhibit numbers and professional filenames.
- Import candidates from the existing document organizer configs.
- Safely import high-level candidates from the email crawler reports when present.
- Generate readiness insights using rule-based scoring.
- Export an attorney-ready ZIP with organized evidence folders, evidence table, evidence index, case summary, and readiness insights.

## Security Notes

- `.env` is ignored and `.env.example` contains safe placeholders only.
- Passwords are stored with PBKDF2-SHA256 hashes, never plaintext.
- Sessions are stored server-side and issued with HttpOnly cookies.
- Users can only access cases and export packages that belong to their account.
- Uploads and exports are ignored by git.
- Upload filenames are sanitized before storage.
- File type and size validation run before an upload is accepted.
- Email crawler imports avoid copying private message bodies into logs.

## Project Layout

```text
apps/web/                    FastAPI app, templates, static assets, services
services/document-organizer/ Existing organizer scripts and exhibit configs
services/email-crawler/      Existing EB-1A email crawler package
docs/                        Product, architecture, and roadmap docs
uploads/                     Local uploaded files, ignored by git
exports/                     Generated ZIP packages, ignored by git
data/                        Local SQLite database, ignored by git
```

## Useful URLs

- Create account: <http://127.0.0.1:8000/register>
- Login: <http://127.0.0.1:8000/login>
- Dashboard: <http://127.0.0.1:8000/dashboard>
- Cases: <http://127.0.0.1:8000/cases>
- Settings: <http://127.0.0.1:8000/settings>
