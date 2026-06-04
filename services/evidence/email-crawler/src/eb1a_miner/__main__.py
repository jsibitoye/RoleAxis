from __future__ import annotations

import sys
from dataclasses import dataclass
from pathlib import Path
from eb1a_miner.pipeline.incremental import run_incremental_update
from eb1a_miner.reporting.render import export_reports
from rich.console import Console
from eb1a_miner.pipeline.scan import run_full_scan
from eb1a_miner.cli.args import build_parser
from eb1a_miner.config import Settings
from eb1a_miner.logging_setup import configure_logging

console = Console()


@dataclass(frozen=True)
class ExitCode:
    OK: int = 0
    BAD_ARGS: int = 2
    RUNTIME_ERROR: int = 1


def _ensure_dirs(settings: Settings) -> None:
    settings.data_dir.mkdir(parents=True, exist_ok=True)
    settings.cache_dir.mkdir(parents=True, exist_ok=True)
    settings.reports_dir.mkdir(parents=True, exist_ok=True)
    settings.attachments_dir.mkdir(parents=True, exist_ok=True)


def main(argv: list[str] | None = None) -> int:
    argv = argv if argv is not None else sys.argv[1:]
    parser = build_parser()
    args = parser.parse_args(argv)

    # Load config (env + defaults)
    settings = Settings()

    # Allow CLI overrides for output directories
    if args.data_dir:
        settings = settings.model_copy(update={"data_dir": Path(args.data_dir).resolve()})
        settings = settings.model_copy(
            update={
                "cache_dir": settings.data_dir / "cache",
                "reports_dir": settings.data_dir / "reports",
                "attachments_dir": settings.data_dir / "attachments",
            }
        )

    configure_logging(level=settings.log_level)
    _ensure_dirs(settings)

    try:
        if args.command == "scan":
            from eb1a_miner.pipeline.scan import run_full_scan

            run_full_scan(
                settings=settings,
                scope=args.scope,
                years=args.years,
                since=args.since,
                until=args.until,
                confidence_threshold=args.threshold,
                include_sent=args.include_sent,
                include_archive=args.include_archive,
                folders=args.folders,
                deep_extract=args.deep,
                attachments=args.attachments,
            )
            console.print("[green]Scan completed.[/green]")
            return ExitCode.OK

        if args.command == "update":
            from eb1a_miner.pipeline.incremental import run_incremental_update

            run_incremental_update(
                settings=settings,
                confidence_threshold=args.threshold,
                deep_extract=args.deep,
                attachments=args.attachments,
            )
            console.print("[green]Update completed.[/green]")
            return ExitCode.OK

        if args.command == "export":
            from eb1a_miner.reporting.render import export_reports

            export_reports(settings=settings)
            console.print("[green]Export completed.[/green]")
            return ExitCode.OK

        console.print("[red]Unknown command.[/red]")
        return ExitCode.BAD_ARGS

    except KeyboardInterrupt:
        console.print("\n[yellow]Interrupted by user.[/yellow]")
        return ExitCode.RUNTIME_ERROR
    except Exception as exc:
        console.print(f"[red]Fatal error:[/red] {exc}")
        return ExitCode.RUNTIME_ERROR


if __name__ == "__main__":
    raise SystemExit(main())
