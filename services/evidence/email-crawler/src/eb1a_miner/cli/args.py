from __future__ import annotations

import argparse
from typing import Sequence


def _add_common_scan_flags(parser: argparse.ArgumentParser) -> None:
    parser.add_argument(
        "--confidence-threshold",
        dest="threshold",
        type=int,
        default=70,
        help="Minimum confidence (0-100) to treat an email as relevant.",
    )
    parser.add_argument(
        "--deep",
        action="store_true",
        help="Run deep extraction (Stage B) on relevant emails.",
    )
    parser.add_argument(
        "--attachments",
        action="store_true",
        help="Download and analyze relevant attachments.",
    )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="eb1a_scan",
        description="Outlook EB-1A Evidence Miner",
    )

    parser.add_argument(
        "--data-dir",
        type=str,
        help="Base data directory (overrides default).",
    )

    subparsers = parser.add_subparsers(dest="command", required=True)

    # -----------------
    # FULL SCAN
    # -----------------
    scan = subparsers.add_parser(
        "scan",
        help="Run a full mailbox scan.",
    )

    scan.add_argument(
        "--scope",
        choices=["all", "inbox", "sent"],
        default="all",
        help="Mailbox scope to scan.",
    )
    scan.add_argument(
        "--years",
        type=str,
        help="Year range, e.g. 2019-2025.",
    )
    scan.add_argument(
        "--since",
        type=str,
        help="ISO date to start scanning from (YYYY-MM-DD).",
    )
    scan.add_argument(
        "--until",
        type=str,
        help="ISO date to stop scanning at (YYYY-MM-DD).",
    )
    scan.add_argument(
        "--include-sent",
        action="store_true",
        default=True,
        help="Include Sent Items folder.",
    )
    scan.add_argument(
        "--include-archive",
        action="store_true",
        help="Include Archive folders if present.",
    )
    scan.add_argument(
        "--folders",
        nargs="*",
        help="Specific folder names to scan (overrides scope).",
    )

    _add_common_scan_flags(scan)

    # -----------------
    # INCREMENTAL UPDATE
    # -----------------
    update = subparsers.add_parser(
        "update",
        help="Scan only new or changed messages since last run.",
    )
    _add_common_scan_flags(update)

    # -----------------
    # EXPORT ONLY
    # -----------------
    export = subparsers.add_parser(
        "export",
        help="Rebuild reports from stored evidence without rescanning mailbox.",
    )

    return parser
