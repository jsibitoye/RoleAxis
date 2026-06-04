from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class WorkspaceTemplate:
    category: str
    name: str
    objective_prompt: str


WORKSPACE_CATEGORIES: dict[str, list[WorkspaceTemplate]] = {
    "Immigration": [
        WorkspaceTemplate("Immigration", "NIW", "I deserve NIW approval."),
        WorkspaceTemplate("Immigration", "EB-1A", "I qualify for EB-1A classification."),
        WorkspaceTemplate("Immigration", "O-1", "I qualify for O-1 classification."),
        WorkspaceTemplate("Immigration", "H-1B Support", "My role and credentials support H-1B filing."),
        WorkspaceTemplate("Immigration", "PERM Support", "My background supports a PERM filing record."),
        WorkspaceTemplate("Immigration", "Naturalization Support", "I am ready for naturalization review."),
        WorkspaceTemplate("Immigration", "Family Immigration Support", "My family immigration package is complete."),
    ],
    "Career": [
        WorkspaceTemplate("Career", "Promotion Package", "I deserve promotion."),
        WorkspaceTemplate("Career", "Executive Portfolio", "I am ready for executive review."),
        WorkspaceTemplate("Career", "Internal Transfer", "I deserve an internal transfer."),
        WorkspaceTemplate("Career", "Job Application Portfolio", "I am the strongest candidate for this role."),
        WorkspaceTemplate("Career", "Consulting Portfolio", "I can prove my consulting credibility."),
    ],
    "Academic": [
        WorkspaceTemplate("Academic", "Academic Promotion", "I deserve academic promotion."),
        WorkspaceTemplate("Academic", "Tenure Package", "I deserve tenure."),
        WorkspaceTemplate("Academic", "Research Portfolio", "My research record is strong and coherent."),
        WorkspaceTemplate("Academic", "Grant Application", "My work deserves grant funding."),
        WorkspaceTemplate("Academic", "Funding Application", "My work deserves funding."),
    ],
    "Business": [
        WorkspaceTemplate("Business", "Startup Founder Portfolio", "I can prove founder credibility."),
        WorkspaceTemplate("Business", "Investor Due Diligence", "This company is investment-ready."),
        WorkspaceTemplate("Business", "Business Acquisition Package", "This business is acquisition-ready."),
        WorkspaceTemplate("Business", "Vendor Qualification Package", "This business qualifies as a trusted vendor."),
        WorkspaceTemplate("Business", "Contract Proposal Package", "This proposal deserves selection."),
    ],
    "Legal": [
        WorkspaceTemplate("Legal", "Litigation Package", "This matter is supported by strong evidence."),
        WorkspaceTemplate("Legal", "Investigation Package", "The investigation record is complete."),
        WorkspaceTemplate("Legal", "Insurance Claim Package", "This claim is supported by evidence."),
        WorkspaceTemplate("Legal", "Regulatory Compliance Package", "This record supports compliance."),
        WorkspaceTemplate("Legal", "Audit Package", "This audit file is ready for review."),
    ],
    "Custom": [
        WorkspaceTemplate("Custom", "Custom Evidence Workspace", "I can prove this objective."),
    ],
}

CASE_TYPES = [template.name for templates in WORKSPACE_CATEGORIES.values() for template in templates]
TEMPLATE_CATEGORY_BY_NAME = {
    template.name: template.category
    for templates in WORKSPACE_CATEGORIES.values()
    for template in templates
}
TEMPLATE_OBJECTIVE_BY_NAME = {
    template.name: template.objective_prompt
    for templates in WORKSPACE_CATEGORIES.values()
    for template in templates
}

EVIDENCE_SOURCE_GROUPS = [
    {
        "name": "Local Computer",
        "providers": ["Entire Computer", "Documents Folder", "Desktop", "Downloads", "Custom Folder"],
        "scopes": ["One-time discovery", "Specific folder only", "Manual review required"],
    },
    {
        "name": "Email",
        "providers": ["Gmail", "Outlook", "Microsoft 365"],
        "scopes": ["Last 12 Months", "Last 24 Months", "Entire Mailbox", "Custom Date Range"],
    },
    {
        "name": "Cloud Storage",
        "providers": ["Google Drive", "OneDrive", "Dropbox", "Box"],
        "scopes": ["Entire Drive", "Specific Folder", "Specific Files"],
    },
    {
        "name": "Professional Profiles",
        "providers": ["LinkedIn", "Google Scholar", "ORCID", "ResearchGate", "GitHub", "Personal Website"],
        "scopes": ["Public achievements", "Publications and citations", "Projects and contributions"],
    },
    {
        "name": "Manual Upload",
        "providers": ["PDF", "DOCX", "PPTX", "PNG", "JPG", "TXT", "ZIP"],
        "scopes": ["User-selected files", "Evidence inbox review"],
    },
]

BASE_REQUIREMENTS: dict[str, list[tuple[str, list[str]]]] = {
    "Immigration": [
        ("Identity and professional profile", ["Degrees", "Employment Evidence", "Certifications"]),
        ("Education or credentials", ["Degrees", "Certifications"]),
        ("Achievements or recognition", ["Awards", "Media Mentions"]),
        ("Recommendation letters", ["Recommendation Letters"]),
        ("Projects or original contributions", ["Projects", "Research Evidence", "Patents"]),
        ("Public proof", ["Publications", "Citations", "Conference/Speaking Evidence", "Media Mentions"]),
    ],
    "Career": [
        ("Employment history", ["Employment Evidence"]),
        ("Projects and outcomes", ["Projects"]),
        ("Leadership or recognition", ["Awards", "Media Mentions", "Conference/Speaking Evidence"]),
        ("Credentials", ["Degrees", "Certifications"]),
        ("References or endorsements", ["Recommendation Letters"]),
    ],
    "Academic": [
        ("Education credentials", ["Degrees"]),
        ("Publications", ["Publications"]),
        ("Citations", ["Citations"]),
        ("Research evidence", ["Research Evidence"]),
        ("Peer review or judging", ["Peer Review/Judging Evidence"]),
        ("Recommendation letters", ["Recommendation Letters"]),
    ],
    "Business": [
        ("Company or founder credibility", ["Employment Evidence", "Projects"]),
        ("Customer or market proof", ["Projects", "Media Mentions"]),
        ("Awards or recognition", ["Awards"]),
        ("Credentials and compliance", ["Certifications", "Professional Memberships"]),
        ("References or contracts", ["Recommendation Letters", "Other Supporting Evidence"]),
    ],
    "Legal": [
        ("Primary source documents", ["Other Supporting Evidence", "Employment Evidence", "Projects"]),
        ("Chronology support", ["Employment Evidence", "Projects", "Media Mentions"]),
        ("Corroborating records", ["Recommendation Letters", "Certifications", "Awards"]),
        ("Expert or third-party support", ["Recommendation Letters", "Professional Memberships"]),
    ],
    "Custom": [
        ("Primary proof", ["Other Supporting Evidence"]),
        ("Credentials", ["Degrees", "Certifications"]),
        ("Achievements", ["Awards", "Projects"]),
        ("Third-party support", ["Recommendation Letters", "Media Mentions"]),
    ],
}

CASE_REQUIREMENTS: dict[str, list[tuple[str, list[str]]]] = {
    "NIW": [
        ("Degree evidence", ["Degrees"]),
        ("Certifications or credentials", ["Certifications"]),
        ("Publications or research", ["Publications", "Research Evidence"]),
        ("Employment evidence", ["Employment Evidence"]),
        ("Recommendation letters", ["Recommendation Letters"]),
        ("Project evidence", ["Projects"]),
        ("National importance evidence", ["Projects", "Media Mentions", "Research Evidence", "Publications"]),
    ],
    "EB-1A": [
        ("Awards", ["Awards"]),
        ("Publications", ["Publications"]),
        ("Citations", ["Citations"]),
        ("Judging or peer review", ["Peer Review/Judging Evidence"]),
        ("Media mentions", ["Media Mentions"]),
        ("Professional memberships", ["Professional Memberships"]),
        ("Original contributions", ["Projects", "Research Evidence", "Patents"]),
    ],
    "O-1": [
        ("Awards", ["Awards"]),
        ("Media coverage", ["Media Mentions"]),
        ("Critical roles", ["Employment Evidence", "Projects"]),
        ("Recommendation letters", ["Recommendation Letters"]),
        ("Publications", ["Publications"]),
        ("Professional recognition", ["Awards", "Professional Memberships", "Conference/Speaking Evidence"]),
    ],
    "Academic Promotion": BASE_REQUIREMENTS["Academic"],
    "Professional Portfolio": BASE_REQUIREMENTS["Career"],
}

for template_name, category in TEMPLATE_CATEGORY_BY_NAME.items():
    CASE_REQUIREMENTS.setdefault(template_name, BASE_REQUIREMENTS.get(category, BASE_REQUIREMENTS["Custom"]))


def category_for_template(template_name: str) -> str:
    return TEMPLATE_CATEGORY_BY_NAME.get(template_name, "Custom")


def objective_for_template(template_name: str) -> str:
    return TEMPLATE_OBJECTIVE_BY_NAME.get(template_name, "I can prove this objective.")
