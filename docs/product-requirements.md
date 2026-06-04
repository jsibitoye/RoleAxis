# Product Requirements

## Product

RoleAxis is a premium professional intelligence SaaS platform for people and organizations managing high-stakes career, evidence, and professional record workflows.

## Workspaces

- **RoleAxis Evidence**: evidence cases, uploads, exhibit ordering, readiness insights, and export packages.
- **RoleAxis Career**: interview assistance, presentation coaching, meeting support, job search, resume analysis, and shared Career AI Core.
- **RoleAxis Vault**: profile, resume, certifications, degrees, projects, publications, awards, achievements, and reusable professional records.

## Public And Authenticated Flow

1. Public user lands on `/`.
2. User registers at `/register` or logs in at `/login`.
3. Successful registration or login lands on `/app`.
4. `/app` displays Evidence, Career, and Vault workspace cards.
5. Authenticated users open `/evidence`, `/career`, or `/vault`.

## Evidence Core Workflows

1. Create a case workspace.
2. Upload evidence documents.
3. Import candidates from the document organizer.
4. Import safe candidate summaries from the email crawler.
5. Categorize evidence.
6. Generate exhibit numbers and professional filenames.
7. Generate readiness insights.
8. Export an attorney-ready ZIP package.

## Supported Evidence Case Types

- NIW
- EB-1A
- O-1
- Academic Promotion
- Professional Portfolio

## Career Core Modules

- Interview Assistant
- Presentation Assistant
- Meeting Assistant
- Job Search
- Resume Analyzer
- Career AI Core

## Vault Core Modules

- Professional Profile
- Resume
- Certifications
- Degrees
- Projects
- Publications
- Awards
- Achievements

## Non-Goals For Current Local MVP

- Payment processing
- Cloud object storage
- Email verification and account recovery
- Live email OAuth setup inside the web UI
- Attorney e-signature workflows
- Multi-organization team administration
