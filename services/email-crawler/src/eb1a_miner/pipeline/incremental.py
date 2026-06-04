from __future__ import annotations

import logging
from pathlib import Path

log = logging.getLogger(__name__)


def run_incremental_update(
    *,
    settings,
    confidence_threshold: int,
    deep_extract: bool,
    attachments: bool,
) -> None:
    log.info('Starting INCREMENTAL update')
    log.info(
        'confidence_threshold=%s deep=%s attachments=%s',
        confidence_threshold,
        deep_extract,
        attachments,
    )

    marker = Path(settings.cache_dir) / 'last_incremental_update.txt'
    marker.write_text('incremental update executed', encoding='utf-8')

    log.info('Incremental update stub completed')
