from __future__ import annotations

import argparse
import csv
import json
import re
from pathlib import Path
from typing import Dict, List, Optional

from reportlab.lib.pagesizes import letter
from reportlab.lib.units import inch
from reportlab.pdfgen import canvas


ALLOWED_EXTENSIONS = {".pdf", ".doc", ".docx", ".jpg", ".jpeg", ".png", ".tif", ".tiff"}


def safe_filename(text: str) -> str:
    text = text.replace("&", "and")
    text = re.sub(r"[^A-Za-z0-9\s._()-]+", "", text)
    text = re.sub(r"\s+", "_", text.strip())
    text = re.sub(r"_+", "_", text)
    return text.strip("_")


def normalize(text: str) -> str:
    text = text.lower()
    text = re.sub(r"[_\-]+", " ", text)
    text = re.sub(r"\s+", " ", text).strip()
    return text


def load_json(path: Path) -> Dict:
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def collect_files(root: Path) -> List[Path]:
    results: List[Path] = []
    for p in root.rglob("*"):
        if p.is_file() and p.suffix.lower() in ALLOWED_EXTENSIONS:
            results.append(p)
    return sorted(results)


def file_matches(path: Path, tokens: List[str]) -> bool:
    name = normalize(path.stem)
    return all(normalize(token) in name for token in tokens)


def build_new_name(exhibit_id: str, title: str, suffix: str) -> str:
    print(suffix)
    return f"Exhibit_{safe_filename(title)}{suffix.lower()}"



def generate_divider_pdf(out_path: Path, exhibit_id: str, proper_title: str) -> None:
    c = canvas.Canvas(str(out_path), pagesize=letter)
    width, height = letter

    x = 1.35 * inch
    y = height - 2.15 * inch

    c.setFont("Helvetica-Bold", 16)
    c.drawString(x, y, f"EXHIBIT {exhibit_id}")

    y -= 0.45 * inch
    c.setFont("Helvetica-Bold", 13)

    words = proper_title.split()
    lines: List[str] = []
    current: List[str] = []
    max_chars = 48

    for word in words:
        test = " ".join(current + [word])
        if len(test) <= max_chars:
            current.append(word)
        else:
            lines.append(" ".join(current))
            current = [word]
    if current:
        lines.append(" ".join(current))

    for line in lines:
        c.drawString(x, y, line.upper())
        y -= 0.34 * inch

    c.save()


def generate_section_outputs(config: Dict, output_root: Path) -> None:
    section_id = config["section_id"]
    section_title = config["section_title"]
    root_folder = Path(config["root_folder"])
    dry_run = config.get("dry_run", True)
    items = config["items"]

    if not root_folder.exists():
        raise FileNotFoundError(f"Root folder not found: {root_folder}")

    section_output = output_root / f"Exhibit_{section_id}"
    divider_dir = section_output / "divider_pdfs"
    section_output.mkdir(parents=True, exist_ok=True)
    divider_dir.mkdir(parents=True, exist_ok=True)

    files = collect_files(root_folder)

    # Divider PDFs
    for item in items:
        divider_path = divider_dir / f"Exhibit_{item['exhibit_id']}.pdf"
        generate_divider_pdf(divider_path, item["exhibit_id"], item["proper_title"])

    # Section table CSV + MD
    csv_table = section_output / f"Exhibit_{section_id}_table.csv"
    md_table = section_output / f"Exhibit_{section_id}_table.md"

    with csv_table.open("w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow(["Exhibit ID", "Section", "Existing Name", "Proper Title", "Recommended Filename"])
        for item in items:
            writer.writerow([
                item["exhibit_id"],
                section_title,
                item.get("existing_name", ""),
                item["proper_title"],
                build_new_name(item["exhibit_id"], item["proper_title"], ".pdf"),
            ])

    with md_table.open("w", encoding="utf-8") as f:
        f.write(f"# Exhibit {section_id}\n\n")
        f.write(f"## {section_title}\n\n")
        f.write("| Exhibit | Existing Name | Proper Title | Recommended Filename |\n")
        f.write("|---|---|---|---|\n")
        for item in items:
            f.write(
                f"| {item['exhibit_id']} | {item.get('existing_name', '')} | {item['proper_title']} | "
                f"{build_new_name(item['exhibit_id'], item['proper_title'], '.pdf')} |\n"
            )

    # Rename log
    rename_log = section_output / f"Exhibit_{section_id}_rename_log.csv"
    unmatched = section_output / f"Exhibit_{section_id}_unmatched.txt"
    ambiguous = section_output / f"Exhibit_{section_id}_ambiguous.txt"

    unmatched_lines: List[str] = []
    ambiguous_lines: List[str] = []

    with rename_log.open("w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow(["Exhibit ID", "Old Path", "New Path", "Status"])

        for item in items:
            exhibit_id = item["exhibit_id"]
            proper_title = item["proper_title"]

            tokens = item.get("match_contains", [])
            if not tokens and item.get("existing_name"):
                tokens = [item["existing_name"]]

            matches: List[Path] = []
            for p in files:
                if section_output in p.parents:
                    continue
                if file_matches(p, tokens):
                    matches.append(p)

            if len(matches) == 0:
                unmatched_lines.append(f"{exhibit_id}: {tokens}")
                writer.writerow([exhibit_id, "", "", "NO MATCH"])
                continue

            if len(matches) > 1:
                ambiguous_lines.append(f"{exhibit_id}: " + " | ".join(str(m) for m in matches))
                writer.writerow([exhibit_id, "", "", "MULTIPLE MATCHES"])
                continue

            old_path = matches[0]
            new_name = build_new_name(exhibit_id, proper_title, old_path.suffix)
            new_path = old_path.with_name(new_name)

            status = "READY"
            if old_path.name == new_name:
                status = "ALREADY RENAMED"
            elif new_path.exists():
                status = "TARGET EXISTS"
            elif not dry_run:
                old_path.rename(new_path)
                status = "RENAMED"

            writer.writerow([exhibit_id, str(old_path), str(new_path), status])

    unmatched.write_text("\n".join(unmatched_lines), encoding="utf-8")
    ambiguous.write_text("\n".join(ambiguous_lines), encoding="utf-8")

    print(f"Done: Exhibit {section_id}")
    print(f"Output: {section_output}")
    print(f"Dry run: {dry_run}")


def resolve_config_path(section: Optional[str], config_path: Optional[str], config_dir: str) -> Path:
    if config_path:
        path = Path(config_path)
        if not path.exists():
            raise FileNotFoundError(f"Config not found: {path}")
        return path

    if section:
        section = section.upper().strip()
        path = Path(config_dir) / f"exhibit_{section}.json"
        if not path.exists():
            raise FileNotFoundError(f"Section config not found: {path}")
        return path

    raise ValueError("You must provide either --section or --config")


def main() -> None:
    parser = argparse.ArgumentParser(description="Process exhibit files by section or config file.")
    parser.add_argument("--section", help="Section letter, e.g. A, B, C, D")
    parser.add_argument("--config", help="Explicit config file path, e.g. configs/exhibit_F_part2.json")
    parser.add_argument("--config-dir", default="configs", help="Directory containing section config files")
    parser.add_argument("--output-dir", default="output", help="Directory for generated output")
    args = parser.parse_args()

    config_path = resolve_config_path(args.section, args.config, args.config_dir)
    config = load_json(config_path)

    output_root = Path(args.output_dir)
    
    output_root.mkdir(parents=True, exist_ok=True)
    generate_section_outputs(config, output_root)


if __name__ == "__main__":
    main()