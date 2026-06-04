from __future__ import annotations

import logging
from pathlib import Path

log = logging.getLogger(__name__)


def run_full_scan(
    *,
    settings,
    scope: str,
    years: str | None,
    since: str | None,
    until: str | None,
    confidence_threshold: int,
    include_sent: bool,
    include_archive: bool,
    folders: list[str] | None,
    deep_extract: bool,
    attachments: bool,
) -> None:
    log.info('Starting FULL scan')
    log.info('scope=%s years=%s since=%s until=%s', scope, years, since, until)
    log.info(
        'confidence_threshold=%s deep=%s attachments=%s',
        confidence_threshold,
        deep_extract,
        attachments,
    )

    marker = Path(settings.cache_dir) / 'last_full_scan.txt'
    marker.write_text('scan executed', encoding='utf-8')

    log.info('Full scan stub completed')
