# Product Requirements

## Product

RoleAxis is a premium professional intelligence SaaS platform for people and organizations managing high-stakes career, evidence, and professional record workflows.

## Workspaces

- **RoleAxis Evidence**: evidence workspaces, permissioned source discovery, Evidence Inbox review, exhibit ordering, readiness strategy, and export packages.
- **RoleAxis Career**: interview assistance, presentation coaching, meeting support, job search, resume analysis, and shared Career AI Core.
- **RoleAxis Vault**: profile, resume, certifications, degrees, projects, publications, awards, achievements, and reusable professional records.

## Public And Authenticated Flow

1. Public user lands on `/`.
2. User registers at `/register` or logs in at `/login`.
3. Successful registration or login lands on `/app`.
4. `/app` displays Evidence, Career, and Vault workspace cards.
5. Authenticated users open `/evidence`, `/career`, or `/vault`.

## Evidence Core Workflows

1. Create an objective-first evidence workspace.
2. Choose a workspace category and template.
3. Define what the user is trying to prove.
4. Authorize source discovery by source type, provider, and scope.
5. Review discovery candidates in the Evidence Inbox.
6. Accept or reject candidates before they become evidence.
7. Upload evidence documents manually.
8. Import candidates from the document organizer and email crawler.
9. Categorize evidence and generate `EXHIBIT_A001`-style exhibit numbers.
10. Generate readiness insights and Evidence Intelligence.
11. Export an attorney-ready ZIP package with roadmap and timeline artifacts.

## Evidence Workspace Categories

- Immigration: NIW, EB-1A, O-1, H-1B Support, PERM Support, Naturalization Support, Family Immigration Support
- Career: Promotion Package, Executive Portfolio, Internal Transfer, Job Application Portfolio, Consulting Portfolio
- Academic: Academic Promotion, Tenure Package, Research Portfolio, Grant Application, Funding Application
- Business: Startup Founder Portfolio, Investor Due Diligence, Business Acquisition Package, Vendor Qualification Package, Contract Proposal Package
- Legal: Litigation Package, Investigation Package, Insurance Claim Package, Regulatory Compliance Package, Audit Package
- Custom: Custom Evidence Workspace

## Evidence Intelligence

- Readiness score from 0 to 100
- Gap analysis against workspace requirements
- Case readiness roadmap with target score
- Evidence discovery missions
- Professional timeline
- Achievement graph
- Evidence relationship map
- Professional reputation score
- AI case reviewer concerns

## Career Core Modules

- Interview Assistant: browser-based interview rooms, transcript capture, resume/job context, answer modes, per-user answer history, and server-side AI coaching
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
- Real external source scanning without explicit connector authorization
- Attorney e-signature workflows
- Multi-organization team administration
- Native desktop dependency for SaaS interview workflows
