from __future__ import annotations

from pathlib import Path
from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    # Base dirs
    data_dir: Path = Field(default_factory=lambda: Path("data").resolve())
    cache_dir: Path = Field(default_factory=lambda: (Path("data") / "cache").resolve())
    reports_dir: Path = Field(default_factory=lambda: (Path("data") / "reports").resolve())
    attachments_dir: Path = Field(default_factory=lambda: (Path("data") / "attachments").resolve())

    # Logging
    log_level: str = "INFO"

    # Microsoft identity
    ms_client_id: str | None = Field(default=None, validation_alias="MS_CLIENT_ID")
    ms_tenant: str = Field(default="common", validation_alias="MS_TENANT")
