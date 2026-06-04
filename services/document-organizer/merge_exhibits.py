from pathlib import Path
import csv
from pypdf import PdfReader, PdfWriter

TOC_ROOT = Path("toc_output")
OUTPUT_ROOT = Path("output")
EVIDENCE_ROOT = Path("exhibits")
MERGED_ROOT = Path("merged")

MERGED_ROOT.mkdir(exist_ok=True)


def append_pdf(writer, pdf_path):
    reader = PdfReader(str(pdf_path))
    start_page = len(writer.pages)

    for page in reader.pages:
        writer.add_page(page)

    return start_page, len(reader.pages)


def find_divider(section, exhibit_id):

    divider = OUTPUT_ROOT / f"Exhibit_{section}" / "divider_pdfs" / f"Exhibit_{exhibit_id}.pdf"

    if divider.exists():
        return divider

    return None


def find_evidence(section, exhibit_id):

    # F1-F4 evidence stored under exhibits/F
    if section.startswith("F"):
        folder = EVIDENCE_ROOT / "F"
    else:
        folder = EVIDENCE_ROOT / section

    for file in folder.iterdir():
        if exhibit_id.lower() in file.name.lower():
            return file

    return None


def merge_part(section_folder):

    section = section_folder.name.replace("Exhibit_", "")

    manifests = sorted(section_folder.glob("*Manifest.csv"))

    for manifest in manifests:

        part = manifest.stem.split("_")[-2]

        writer = PdfWriter()

        toc_pdf = section_folder / f"Exhibit_{section}_Part_{part}_TOC.pdf"

        print("Adding TOC:", toc_pdf)

        start_page, _ = append_pdf(writer, toc_pdf)

        # Bookmark for section TOC
        writer.add_outline_item(f"Exhibit {section} – Table of Contents", start_page)

        with open(manifest, encoding="utf-8") as f:

            reader = csv.DictReader(f)

            for row in reader:

                exhibit_id = row["Exhibit ID"]
                title = row["Title"]

                divider = find_divider(section, exhibit_id)

                if divider:

                    print("Divider:", divider)

                    start_page, _ = append_pdf(writer, divider)

                    # Bookmark to divider
                    writer.add_outline_item(f"{exhibit_id} – {title}", start_page)

                evidence = find_evidence(section, exhibit_id)

                if evidence:

                    print("Evidence:", evidence)

                    append_pdf(writer, evidence)

        output_file = MERGED_ROOT / f"Exhibit_{section}_Part_{part}.pdf"

        with open(output_file, "wb") as f:
            writer.write(f)

        print("Saved:", output_file)
        print()


def main():

    for section in TOC_ROOT.iterdir():

        if section.is_dir():

            merge_part(section)


if __name__ == "__main__":
    main()