from __future__ import annotations

import argparse
import csv
import json
import math
import re
from collections import defaultdict
from pathlib import Path
from typing import Dict, List, Optional, Tuple

from reportlab.lib import colors
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import getSampleStyleSheet
from reportlab.lib.units import inch
from reportlab.platypus import (
    PageBreak,
    Paragraph,
    SimpleDocTemplate,
    Spacer,
    Table,
    TableStyle,
)

ALLOWED_EXTENSIONS = {".pdf", ".doc", ".docx", ".jpg", ".jpeg", ".png", ".tif", ".tiff"}

# Keep some safety margin below 12MB because later your merged file may also include
# divider pages / TOC pages / bookmarks / overhead.
DEFAULT_MAX_MB = 11.5


def normalize(text: str) -> str:
    text = text.lower()
    text = text.replace("–", " ").replace("—", " ").replace("’", "'")
    text = re.sub(r"[^\w\s]+", " ", text)
    text = re.sub(r"[_\-]+", " ", text)
    text = re.sub(r"\s+", " ", text).strip()
    return text


def safe_filename(text: str) -> str:
    text = text.replace("&", "and")
    text = re.sub(r"[^A-Za-z0-9\s._()-]+", "", text)
    text = re.sub(r"\s+", "_", text.strip())
    text = re.sub(r"_+", "_", text)
    return text.strip("_")


def clean_display_title(exhibit_id: str, title: str) -> str:
    """
    Removes duplicated exhibit prefix from the display title.

    Examples:
    A1 + 'A1 Curriculum Vitae - Joshua Seyi Ibitoye'
      -> 'Curriculum Vitae - Joshua Seyi Ibitoye'

    D1 + 'Exhibit D1 - Algora Article Review Request Email'
      -> 'Algora Article Review Request Email'
    """
    title = (title or "").strip()
    exhibit_id = (exhibit_id or "").strip()

    patterns = [
        rf"^\s*Exhibit\s+{re.escape(exhibit_id)}\s*[-–—:]?\s*",
        rf"^\s*{re.escape(exhibit_id)}\s*[-–—:]?\s*",
    ]

    for pattern in patterns:
        title = re.sub(pattern, "", title, flags=re.IGNORECASE).strip()

    return title


def exhibit_sort_key(exhibit_id: str) -> Tuple[str, int]:
    """
    A1 -> ('A',1)
    F82 -> ('F',82)
    H1 -> ('H',1)
    """
    m = re.match(r"([A-Za-z]+)(\d+)?$", exhibit_id)
    if not m:
        return exhibit_id, 0
    letter = m.group(1).upper()
    number = int(m.group(2)) if m.group(2) else 0
    return letter, number


def display_section_label(section_id: str) -> str:
    """
    F1, F2, F3, F4 should all display as F.
    Other sections display normally.
    """
    m = re.match(r"^([A-Za-z]+)(\d+)$", section_id.strip())
    if m and m.group(1).upper() == "F":
        return "F"
    return section_id.strip().upper()


def display_section_heading(section_id: str, section_title: str) -> str:
    """
    Builds display heading for master TOC sections.

    Examples:
    A -> 'Exhibit A - Academic and Professional Background'
    F1 -> 'Exhibit F - Authorship of Scholarly Articles in Professional Journals - Group 1'
    """
    sid = section_id.strip().upper()
    m = re.match(r"^(F)(\d+)$", sid)
    if m:
        return f"Exhibit F - {section_title} - Group {m.group(2)}"
    return f"Exhibit {sid} - {section_title}"


def collect_files(root: Path) -> List[Path]:
    results: List[Path] = []
    for p in root.rglob("*"):
        if p.is_file() and p.suffix.lower() in ALLOWED_EXTENSIONS:
            results.append(p)
    return sorted(results)


def load_json(path: Path) -> Dict:
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def load_section_csv_items(csv_path: Path) -> List[Dict]:
    items: List[Dict] = []
    if not csv_path.exists():
        return items

    with csv_path.open("r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            exhibit_id = (row.get("Exhibit ID") or "").strip()
            existing_name = (row.get("Existing Name") or "").strip()
            proper_title = (row.get("Title") or "").strip()
            if exhibit_id and proper_title:
                items.append(
                    {
                        "exhibit_id": exhibit_id,
                        "existing_name": existing_name,
                        "proper_title": proper_title,
                        "match_contains": [],
                    }
                )
    return items


def load_items_from_configs(config_dir: Path) -> Dict[str, Dict]:
    """
    Returns:
    {
      "A": {"section_title": "...", "items": [...]},
      "F": {"section_title": "...", "items": [...]},
      ...
    }
    Combines all JSONs with same section_id, so F part1-part4 are merged correctly.
    """
    sections: Dict[str, Dict] = {}

    for json_file in sorted(config_dir.glob("*.json")):
        cfg = load_json(json_file)
        section_id = str(cfg["section_id"]).upper().strip()
        section_title = str(cfg["section_title"]).strip()
        items = cfg.get("items", [])

        if section_id not in sections:
            sections[section_id] = {
                "section_id": section_id,
                "section_title": section_title,
                "items": [],
            }

        sections[section_id]["items"].extend(items)

    for section_id in sections:
        unique = {}
        for item in sections[section_id]["items"]:
            unique[item["exhibit_id"]] = item
        sections[section_id]["items"] = sorted(unique.values(), key=lambda x: exhibit_sort_key(x["exhibit_id"]))

    return sections


def maybe_load_items_from_table_csv(
    output_dir: Path,
    section_id: str,
    fallback_items: List[Dict],
) -> List[Dict]:
    """
    Prefer table CSV if it exists and appears complete.
    Otherwise fall back to config JSON items.
    """
    section_output = output_dir / f"Exhibit_{section_id}"
    csv_candidates = sorted(section_output.glob("*table.csv"))

    if not csv_candidates:
        return fallback_items

    csv_items_all: List[Dict] = []
    for csv_file in csv_candidates:
        csv_items_all.extend(load_section_csv_items(csv_file))

    if not csv_items_all:
        return fallback_items

    # If CSV count is smaller than config count, use config.
    # This avoids the F-part overwrite problem.
    config_count = len(fallback_items)
    csv_unique_count = len({item["exhibit_id"] for item in csv_items_all})

    if csv_unique_count < config_count:
        return fallback_items

    by_id = {item["exhibit_id"]: item for item in csv_items_all}
    return sorted(by_id.values(), key=lambda x: exhibit_sort_key(x["exhibit_id"]))


def locate_file_for_item(item: Dict, section_dir: Path) -> Tuple[Optional[Path], List[Path], str]:
    """
    Returns:
    (matched_file_or_none, all_matches, match_mode)

    Match priority:
    1. exact normalized existing_name == stem
    2. exact expected renamed current-script style == stem
    3. exact expected renamed future style with exhibit id == stem
    4. all match_contains tokens inside stem
    5. normalized existing_name contained in stem
    """
    files = collect_files(section_dir)
    existing_name = (item.get("existing_name") or "").strip()
    proper_title = item["proper_title"]
    exhibit_id = item["exhibit_id"]
    tokens = item.get("match_contains", []) or []

    existing_norm = normalize(existing_name)
    expected_current_norm = normalize(f"Exhibit_{safe_filename(proper_title)}")
    expected_future_norm = normalize(f"Exhibit_{exhibit_id}_{safe_filename(proper_title)}")

    exact_existing: List[Path] = []
    exact_current: List[Path] = []
    exact_future: List[Path] = []
    token_matches: List[Path] = []
    loose_existing: List[Path] = []

    for p in files:
        stem_norm = normalize(p.stem)

        if existing_norm and stem_norm == existing_norm:
            exact_existing.append(p)
            continue

        if stem_norm == expected_current_norm:
            exact_current.append(p)
            continue

        if stem_norm == expected_future_norm:
            exact_future.append(p)
            continue

        if tokens and all(normalize(t) in stem_norm for t in tokens if str(t).strip()):
            token_matches.append(p)
            continue

        if existing_norm and existing_norm in stem_norm:
            loose_existing.append(p)

    if len(exact_existing) == 1:
        return exact_existing[0], exact_existing, "EXACT_EXISTING_NAME"
    if len(exact_existing) > 1:
        return None, exact_existing, "EXACT_EXISTING_NAME"

    if len(exact_current) == 1:
        return exact_current[0], exact_current, "EXACT_CURRENT_STYLE_NAME"
    if len(exact_current) > 1:
        return None, exact_current, "EXACT_CURRENT_STYLE_NAME"

    if len(exact_future) == 1:
        return exact_future[0], exact_future, "EXACT_FUTURE_STYLE_NAME"
    if len(exact_future) > 1:
        return None, exact_future, "EXACT_FUTURE_STYLE_NAME"

    if len(token_matches) == 1:
        return token_matches[0], token_matches, "MATCH_CONTAINS"
    if len(token_matches) > 1:
        return None, token_matches, "MATCH_CONTAINS"

    if len(loose_existing) == 1:
        return loose_existing[0], loose_existing, "LOOSE_EXISTING_NAME"
    if len(loose_existing) > 1:
        return None, loose_existing, "LOOSE_EXISTING_NAME"

    return None, [], "NO_MATCH"


def size_mb(num_bytes: int) -> float:
    return num_bytes / (1024 * 1024)


def split_items_by_size(items_with_files: List[Dict], max_bytes: int) -> List[List[Dict]]:
    """
    Split sequentially. If a single file is itself over the limit, place it alone.
    """
    parts: List[List[Dict]] = []
    current: List[Dict] = []
    current_bytes = 0

    for item in items_with_files:
        file_size = item["file_size_bytes"]

        if current and current_bytes + file_size > max_bytes:
            parts.append(current)
            current = [item]
            current_bytes = file_size
        else:
            current.append(item)
            current_bytes += file_size

    if current:
        parts.append(current)

    return parts


def build_table_data(rows: List[List[str]]) -> Table:
    table = Table(rows, repeatRows=1, colWidths=[1.1 * inch, 5.9 * inch])
    table.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#D9D9D9")),
                ("TEXTCOLOR", (0, 0), (-1, 0), colors.black),
                ("FONTNAME", (0, 0), (-1, 0), "Helvetica-Bold"),
                ("FONTSIZE", (0, 0), (-1, -1), 9),
                ("GRID", (0, 0), (-1, -1), 0.5, colors.black),
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
                ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, colors.HexColor("#F7F7F7")]),
                ("LEFTPADDING", (0, 0), (-1, -1), 6),
                ("RIGHTPADDING", (0, 0), (-1, -1), 6),
                ("TOPPADDING", (0, 0), (-1, -1), 4),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
            ]
        )
    )
    return table


def generate_section_part_toc_pdf(
    out_path: Path,
    section_id: str,
    section_title: str,
    part_number: int,
    total_parts: int,
    items: List[Dict],
    total_size_bytes: int,
) -> None:
    styles = getSampleStyleSheet()
    story = []

    title = f"Joshua Seyi Ibitoye – EB1A Petition\n{display_section_heading(section_id, section_title).upper()}"
    if total_parts > 1:
        title += f"\nPART {part_number} OF {total_parts}"

    story.append(Paragraph(title.replace("\n", "<br/>"), styles["Title"]))
    story.append(Spacer(1, 0.2 * inch))

    info = f"Included exhibits: {items[0]['exhibit_id']} through {items[-1]['exhibit_id']}<br/>Estimated total evidence size in this part: {size_mb(total_size_bytes):.2f} MB"
    story.append(Paragraph(info, styles["Normal"]))
    story.append(Spacer(1, 0.2 * inch))

    rows = [["Exhibit", "Title"]]
    for item in items:
        rows.append([
            item["exhibit_id"],
            clean_display_title(item["exhibit_id"], item["proper_title"]),
        ])

    story.append(build_table_data(rows))

    doc = SimpleDocTemplate(
        str(out_path),
        pagesize=letter,
        leftMargin=0.6 * inch,
        rightMargin=0.6 * inch,
        topMargin=0.6 * inch,
        bottomMargin=0.6 * inch,
    )
    doc.build(story)


def generate_master_toc_pdf(out_path: Path, sections: Dict[str, Dict]) -> None:
    styles = getSampleStyleSheet()
    story = []

    story.append(Paragraph("Joshua Seyi Ibitoye - EB1A Petition \n- INDEX OF EXHIBITS", styles["Title"]))
    story.append(Spacer(1, 0.2 * inch))

    ordered_section_ids = sorted(sections.keys(), key=exhibit_sort_key)

    first_section = True
    for section_id in ordered_section_ids:
        section = sections[section_id]
        items = section["resolved_items"]

        if not items:
            continue

        if not first_section:
            story.append(Spacer(1, 0.15 * inch))
        first_section = False

        story.append(Paragraph(display_section_heading(section_id, section["section_title"]), styles["Heading2"]))
        story.append(Spacer(1, 0.08 * inch))

        rows = [["Exhibit", "Title"]]
        for item in items:
            rows.append([
                item["exhibit_id"],
                clean_display_title(item["exhibit_id"], item["proper_title"]),
            ])

        story.append(build_table_data(rows))
        story.append(Spacer(1, 0.18 * inch))

    doc = SimpleDocTemplate(
        str(out_path),
        pagesize=letter,
        leftMargin=0.6 * inch,
        rightMargin=0.6 * inch,
        topMargin=0.6 * inch,
        bottomMargin=0.6 * inch,
    )
    doc.build(story)


def write_part_manifest_csv(out_path: Path, items: List[Dict]) -> None:
    with out_path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow(["Exhibit ID", "Title", "Source File", "Size (MB)"])
        for item in items:
            writer.writerow(
                [
                    item["exhibit_id"],
                    clean_display_title(item["exhibit_id"], item["proper_title"]),
                    str(item["file_path"]),
                    f"{size_mb(item['file_size_bytes']):.2f}",
                ]
            )


def process_sections(
    config_dir: Path,
    output_dir: Path,
    toc_output_dir: Path,
    max_mb: float,
) -> None:
    toc_output_dir.mkdir(parents=True, exist_ok=True)
    max_bytes = int(max_mb * 1024 * 1024)

    sections = load_items_from_configs(config_dir)

    unresolved_report = toc_output_dir / "toc_unresolved_or_ambiguous.txt"
    unresolved_lines: List[str] = []

    for section_id, section in sections.items():
        section_title = section["section_title"]

        # Prefer table CSV if present and complete, otherwise use config JSONs
        # Table CSV lookup
        table_section = section_id
        source_items = maybe_load_items_from_table_csv(output_dir, table_section, section["items"])

        # Evidence folder logic
        if section_id.startswith("F"):
            section_dir = Path("exhibits") / "F"
        else:
            section_dir = Path("exhibits") / section_id
        if not section_dir.exists():
            unresolved_lines.append(f"Exhibit {section_id}: folder not found -> {section_dir}")
            section["resolved_items"] = []
            continue

        resolved_items: List[Dict] = []

        for item in source_items:
            matched_file, matches, match_mode = locate_file_for_item(item, section_dir)

            if matched_file is None:
                if not matches:
                    unresolved_lines.append(
                        f"{item['exhibit_id']}: NO MATCH | existing_name={item.get('existing_name','')} | proper_title={item['proper_title']}"
                    )
                else:
                    unresolved_lines.append(
                        f"{item['exhibit_id']}: MULTIPLE MATCHES ({match_mode}) | " + " | ".join(str(p) for p in matches)
                    )
                continue

            resolved_items.append(
                {
                    "exhibit_id": item["exhibit_id"],
                    "proper_title": item["proper_title"],
                    "file_path": matched_file,
                    "file_size_bytes": matched_file.stat().st_size,
                }
            )

        resolved_items = sorted(resolved_items, key=lambda x: exhibit_sort_key(x["exhibit_id"]))
        section["resolved_items"] = resolved_items

        if not resolved_items:
            continue

        parts = split_items_by_size(resolved_items, max_bytes)

        section_out_dir = toc_output_dir / f"Exhibit_{section_id}"
        section_out_dir.mkdir(parents=True, exist_ok=True)

        for idx, part_items in enumerate(parts, start=1):
            part_total = sum(x["file_size_bytes"] for x in part_items)

            toc_pdf = section_out_dir / f"Exhibit_{section_id}_Part_{idx}_TOC.pdf"
            manifest_csv = section_out_dir / f"Exhibit_{section_id}_Part_{idx}_Manifest.csv"

            generate_section_part_toc_pdf(
                out_path=toc_pdf,
                section_id=section_id,
                section_title=section_title,
                part_number=idx,
                total_parts=len(parts),
                items=part_items,
                total_size_bytes=part_total,
            )
            write_part_manifest_csv(manifest_csv, part_items)

    unresolved_report.write_text("\n".join(unresolved_lines), encoding="utf-8")

    master_pdf = toc_output_dir / "MASTER_INDEX_OF_EXHIBITS_A_to_K.pdf"
    generate_master_toc_pdf(master_pdf, sections)

    print(f"Done. TOC PDFs saved to: {toc_output_dir}")
    print(f"Master TOC: {master_pdf}")
    print(f"Unresolved / ambiguous report: {unresolved_report}")


def main() -> None:
    parser = argparse.ArgumentParser(description="Build section TOC PDFs and a master TOC, split by size.")
    parser.add_argument("--config-dir", default="configs", help="Directory containing exhibit JSON files")
    parser.add_argument("--output-dir", default="output", help="Directory where Exhibit_*_table.csv files may exist")
    parser.add_argument("--toc-output-dir", default="toc_output", help="Directory to save generated TOC PDFs")
    parser.add_argument("--max-mb", type=float, default=DEFAULT_MAX_MB, help="Maximum total evidence size per part")
    args = parser.parse_args()

    process_sections(
        config_dir=Path(args.config_dir),
        output_dir=Path(args.output_dir),
        toc_output_dir=Path(args.toc_output_dir),
        max_mb=args.max_mb,
    )


if __name__ == "__main__":
    main()