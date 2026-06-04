from __future__ import annotations

import logging
from pathlib import Path

log = logging.getLogger(__name__)


def export_reports(*, settings) -> None:
    log.info('Exporting reports')

    report = Path(settings.reports_dir) / 'EvidenceReport.xlsx'
    report.write_text('stub report', encoding='utf-8')

    log.info('Export stub completed')
