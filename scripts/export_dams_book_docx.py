"""Export DAMS complete project book PDF to editable Word (.docx)."""

from __future__ import annotations

import shutil
from pathlib import Path

from pdf2docx import Converter

ROOT = Path(__file__).resolve().parents[1]
SRC_PDF = ROOT / "docs" / "DAMS_Complete_Project_Document.pdf"
OUT_DOCX = ROOT / "docs" / "DAMS_Complete_Project_Document.docx"
ARTIFACT = Path("/opt/cursor/artifacts/DAMS_Complete_Project_Document.docx")


def main() -> None:
    if not SRC_PDF.exists():
        raise FileNotFoundError(f"Build the PDF first: {SRC_PDF}")

    cv = Converter(str(SRC_PDF))
    try:
        cv.convert(str(OUT_DOCX), start=0, end=None)
    finally:
        cv.close()

    ARTIFACT.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(OUT_DOCX, ARTIFACT)
    print(f"Created {OUT_DOCX} ({OUT_DOCX.stat().st_size / 1024 / 1024:.1f} MB)")
    print(f"Copied to {ARTIFACT}")


if __name__ == "__main__":
    main()
