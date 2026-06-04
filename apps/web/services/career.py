from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from apps.web.settings import get_settings


@dataclass(frozen=True)
class InterviewAssistantStatus:
    service_dir: Path
    project_file: Path
    config_example: Path
    release_dir: Path
    installer_dir: Path
    source_exists: bool
    project_exists: bool
    config_example_exists: bool
    release_build_exists: bool
    installer_exists: bool


def get_interview_assistant_status() -> InterviewAssistantStatus:
    settings = get_settings()
    service_dir = settings.interview_assistant_dir
    project_file = service_dir / "RoleAxis.InterviewAssistant.csproj"
    config_example = service_dir / "config.example.json"
    release_dir = service_dir / "bin" / "Release" / "net8.0-windows"
    installer_dir = service_dir / "installer-output"
    installer_exists = installer_dir.exists() and any(installer_dir.glob("*.exe"))

    return InterviewAssistantStatus(
        service_dir=service_dir,
        project_file=project_file,
        config_example=config_example,
        release_dir=release_dir,
        installer_dir=installer_dir,
        source_exists=(service_dir / "Program.cs").exists(),
        project_exists=project_file.exists(),
        config_example_exists=config_example.exists(),
        release_build_exists=release_dir.exists() and any(release_dir.glob("*.dll")),
        installer_exists=installer_exists,
    )
