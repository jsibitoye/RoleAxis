from __future__ import annotations

from functools import lru_cache
from pathlib import Path

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


BASE_DIR = Path(__file__).resolve().parents[2]


class AppSettings(BaseSettings):
    app_name: str = "RoleAxis Evidence"
    environment: str = Field(default="local", alias="ROLEAXIS_ENV")
    database_url: str = Field(
        default=f"sqlite:///{(BASE_DIR / 'data' / 'roleaxis.db').as_posix()}",
        alias="ROLEAXIS_DATABASE_URL",
    )
    upload_root: Path = Field(default=BASE_DIR / "uploads", alias="ROLEAXIS_UPLOAD_ROOT")
    export_root: Path = Field(default=BASE_DIR / "exports", alias="ROLEAXIS_EXPORT_ROOT")
    max_upload_mb: int = Field(default=50, alias="ROLEAXIS_MAX_UPLOAD_MB")
    document_organizer_config_dir: Path = BASE_DIR / "services" / "document-organizer" / "configs"
    email_crawler_reports_dir: Path = BASE_DIR / "services" / "email-crawler" / "data" / "reports"
    allowed_upload_extensions_raw: str = Field(
        default=".pdf,.doc,.docx,.jpg,.jpeg,.png,.tif,.tiff,.txt,.csv,.xlsx",
        alias="ROLEAXIS_ALLOWED_UPLOAD_EXTENSIONS",
    )

    model_config = SettingsConfigDict(env_file=BASE_DIR / ".env", extra="ignore")

    @property
    def allowed_upload_extensions(self) -> set[str]:
        return {
            item.strip().lower()
            for item in self.allowed_upload_extensions_raw.split(",")
            if item.strip()
        }

    @property
    def max_upload_bytes(self) -> int:
        return self.max_upload_mb * 1024 * 1024


@lru_cache
def get_settings() -> AppSettings:
    settings = AppSettings()
    settings.upload_root.mkdir(parents=True, exist_ok=True)
    settings.export_root.mkdir(parents=True, exist_ok=True)
    (BASE_DIR / "data").mkdir(parents=True, exist_ok=True)
    return settings

