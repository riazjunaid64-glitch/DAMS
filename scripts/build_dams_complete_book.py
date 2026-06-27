"""Assemble DAMS complete project book PDF following Zostel Inn (Doc_fixed) structure."""

from __future__ import annotations

import io
from dataclasses import dataclass
from pathlib import Path

import fitz
from reportlab.lib.pagesizes import letter
from reportlab.lib.units import inch
from reportlab.pdfgen import canvas

ROOT = Path(__file__).resolve().parents[1]
UPLOAD = Path("/home/ubuntu/.cursor/projects/workspace/uploads")
OUT_PDF = ROOT / "docs" / "DAMS_Complete_Project_Document.pdf"
ARTIFACT = Path("/opt/cursor/artifacts/DAMS_Complete_Project_Document.pdf")

# Author / institution (from DAMS proposal)
AUTHOR = "Muhammad Junaid Riaz"
SUPERVISOR = "Mr. Khawar Maqsood"
COORDINATOR = "Mr. Khawaja Tahir"
UNIVERSITY = "Federal Urdu University of Arts, Science & Technology, Islamabad"
SESSION = "SESSION [2022-2026]"
SYSTEM = "Deen Associate Management System"
CLIENT = "Deen Associate"

# Section dates mapped from Zostel sample (Doc_fixed) — NOT one date for all
DATES = {
    "proposal": "15th October, 2025",
    "revision": "15th October, 2025",
    "srs": "27th November, 2025",
    "use_case": "27th November, 2025",
    "fully_dressed": "27th November, 2025",
    "design_phase": "27th November, 2025",
    "domain": "27th November, 2025",
    "package": "27th November, 2025",
    "ssd": "27th November, 2025",
    "er": "27th November, 2025",
    "normalization": "27th November, 2025",
    "relational": "27th November, 2025",
    "class_diagram": "27th November, 2025",
    "construction": "27th November, 2025",
    "project_code": "27th November, 2025",
    "sample_queries": "27th November, 2025",
    "test_cases": "25th November, 2025",
    "user_manual": "27th November, 2025",
}


@dataclass
class BookSection:
    title: str
    toc_level: int = 1
    page: int = 0


def src(name: str) -> Path:
    p = UPLOAD / name
    if not p.exists():
        raise FileNotFoundError(f"Missing source PDF: {p}")
    return p


def pdf_page_bytes(fn) -> bytes:
    buf = io.BytesIO()
    fn(buf)
    buf.seek(0)
    return buf.read()


def rl_cover_main(buf: io.BytesIO) -> None:
    w, h = letter
    c = canvas.Canvas(buf, pagesize=letter)
    c.setFont("Times-Bold", 20)
    c.drawCentredString(w / 2, h - 160, SYSTEM)
    c.setFont("Times-Bold", 14)
    c.drawCentredString(w / 2, h - 220, "Developed By")
    c.setFont("Times-Bold", 16)
    c.drawCentredString(w / 2, h - 255, AUTHOR)
    c.setFont("Times-Roman", 13)
    c.drawCentredString(w / 2, h - 295, "BS (CS) in Software Systems")
    c.drawCentredString(w / 2, h - 335, "Supervised By")
    c.setFont("Times-Bold", 14)
    c.drawCentredString(w / 2, h - 365, SUPERVISOR)
    c.setFont("Times-Roman", 13)
    c.drawCentredString(w / 2, h - 405, "Project Coordinator")
    c.setFont("Times-Bold", 14)
    c.drawCentredString(w / 2, h - 435, COORDINATOR)
    c.setFont("Times-Roman", 12)
    c.drawCentredString(w / 2, h - 480, "Department of Computer Science")
    c.drawCentredString(w / 2, h - 500, UNIVERSITY)
    c.drawCentredString(w / 2, h - 540, SESSION)
    c.showPage()
    c.save()


def rl_cover_presentation(buf: io.BytesIO) -> None:
    w, h = letter
    c = canvas.Canvas(buf, pagesize=letter)
    c.setFont("Times-Bold", 14)
    y = h - 180
    for line in [
        "A Project Presented to",
        "",
        "Federal Urdu University of Arts, Science & Technology",
        "",
        "In Partial Fulfillment of the Requirement for the Degree",
        "",
        "Bachelor of Computer Science",
        "(Software Systems)",
        "",
        "By",
        AUTHOR,
        "",
        DATES["proposal"],
        "",
        f"({UNIVERSITY})",
    ]:
        c.drawCentredString(w / 2, y, line)
        y -= 22
    c.showPage()
    c.save()


def rl_text_page(buf: io.BytesIO, heading: str, paragraphs: list[str]) -> None:
    w, h = letter
    c = canvas.Canvas(buf, pagesize=letter)
    c.setFont("Times-Bold", 16)
    c.drawString(1 * inch, h - 1 * inch, heading)
    c.setFont("Times-Roman", 11)
    y = h - 1.45 * inch
    width = w - 2 * inch
    for para in paragraphs:
        words = para.split()
        line = ""
        for word in words:
            test = (line + " " + word).strip()
            if c.stringWidth(test, "Times-Roman", 11) <= width:
                line = test
            else:
                if line:
                    c.drawString(1 * inch, y, line)
                    y -= 14
                line = word
        if line:
            c.drawString(1 * inch, y, line)
            y -= 20
        if y < 1.2 * inch:
            c.showPage()
            c.setFont("Times-Roman", 11)
            y = h - 1 * inch
    c.showPage()
    c.save()


def rl_title_page(buf: io.BytesIO, title: str, date: str, *, proposed: bool = False) -> None:
    w, h = letter
    c = canvas.Canvas(buf, pagesize=letter)
    c.setFont("Times-Bold", 18)
    c.drawCentredString(w / 2, h - 200, title)
    c.setFont("Times-Bold", 14)
    c.drawCentredString(w / 2, h - 240, "FOR")
    c.setFont("Times-Bold", 18)
    c.drawCentredString(w / 2, h - 280, SYSTEM)
    c.setFont("Times-Roman", 13)
    c.drawCentredString(w / 2, h - 330, "VERSION 1.0")
    c.drawCentredString(w / 2, h - 380, "Prepared By" if not proposed else "Proposed By")
    c.setFont("Times-Bold", 14)
    c.drawCentredString(w / 2, h - 415, AUTHOR)
    c.setFont("Times-Roman", 13)
    c.drawCentredString(w / 2, h - 470, date)
    c.showPage()
    c.save()


def rl_chapter_page(buf: io.BytesIO, text: str) -> None:
    w, h = letter
    c = canvas.Canvas(buf, pagesize=letter)
    c.setFont("Times-Bold", 18)
    lines = [ln.strip() for ln in text.split("\n") if ln.strip()]
    y = h / 2 + (len(lines) - 1) * 12
    for line in lines:
        c.drawCentredString(w / 2, y, line)
        y -= 28
    c.showPage()
    c.save()


def rl_section_header(buf: io.BytesIO, text: str) -> None:
    w, h = letter
    c = canvas.Canvas(buf, pagesize=letter)
    c.setFont("Times-Bold", 14)
    c.drawCentredString(w / 2, h - 72, text)
    c.showPage()
    c.save()


def rl_toc_page(buf: io.BytesIO, entries: list[tuple[str, int, int]]) -> None:
    """entries: (title, level, page)"""
    w, h = letter
    c = canvas.Canvas(buf, pagesize=letter)
    c.setFont("Times-Bold", 16)
    c.drawCentredString(w / 2, h - 72, "Table of Contents")
    c.setFont("Times-Roman", 10)
    y = h - 110
    left = 0.85 * inch
    right = w - 0.85 * inch
    for title, level, page in entries:
        indent = left + (level - 1) * 16
        font = "Times-Bold" if level == 1 else "Times-Roman"
        size = 10 if level == 1 else 9
        c.setFont(font, size)
        title_width = c.stringWidth(title, font, size)
        page_str = str(page)
        page_w = c.stringWidth(page_str, font, size)
        avail = right - indent - page_w - 8
        if title_width > avail:
            # truncate gracefully
            while title and c.stringWidth(title + "...", font, size) > avail:
                title = title[:-1]
            title = title + "..."
        dots_w = max(right - indent - title_width - page_w - 4, 10)
        dot_count = max(int(dots_w / c.stringWidth(".", font, size)), 3)
        line = title + " " + ("." * dot_count) + " " + page_str
        c.drawString(indent, y, line)
        y -= 13 if level == 1 else 11
        if y < 0.9 * inch:
            c.showPage()
            c.setFont("Times-Roman", 10)
            y = h - 72
    c.showPage()
    c.save()


def insert_bytes(doc: fitz.Document, pdf_bytes: bytes) -> None:
    doc.insert_pdf(fitz.open(stream=pdf_bytes, filetype="pdf"))


def insert_file(doc: fitz.Document, path: Path, skip: int = 0) -> int:
    """Append PDF pages; return number of pages inserted."""
    s = fitz.open(str(path))
    n = max(len(s) - skip, 0)
    if n:
        doc.insert_pdf(s, from_page=skip, to_page=len(s) - 1)
    s.close()
    return n


def page_count(doc: fitz.Document) -> int:
    return len(doc)


class BookBuilder:
    def __init__(self) -> None:
        self.doc = fitz.open()
        self.sections: list[BookSection] = []

    def current_page(self) -> int:
        return page_count(self.doc) + 1

    def mark(self, title: str, level: int = 1) -> None:
        self.sections.append(BookSection(title=title, toc_level=level, page=self.current_page()))

    def add_bytes(self, pdf_bytes: bytes) -> None:
        insert_bytes(self.doc, pdf_bytes)

    def add_pages(self, builder_fn) -> None:
        self.add_bytes(pdf_page_bytes(builder_fn))

    def add_pdf(self, path: Path, skip: int = 0) -> None:
        insert_file(self.doc, path, skip=skip)

    def build_front_matter(self) -> None:
        self.add_pages(rl_cover_main)
        self.add_pages(rl_cover_presentation)

        self.add_pages(
            lambda b: rl_text_page(
                b,
                "Declaration",
                [
                    "I hereby declare that this software, neither as a whole nor as a part, has been "
                    "copied from any source. I have developed this software and the accompanied report "
                    "entirely on the basis of my personal efforts. If any part of this project is proved "
                    "to be copied from any source, I will stand by the consequences. No portion of the work "
                    "presented has been submitted in support of any application for any other degree or "
                    "qualification of this or any other university or institute of learning.",
                    AUTHOR,
                ],
            )
        )

        self.add_pages(
            lambda b: rl_text_page(
                b,
                "Dedication",
                ["Dedicated to my beloved parents,", "Teachers", "And", "The fellows"],
            )
        )

        self.add_pages(
            lambda b: rl_text_page(
                b,
                "Acknowledgement",
                [
                    "Thanks to Almighty Allah for giving me knowledge, power and strength to accomplish this task. "
                    "I learned a lot while doing this project and this will certainly help me in my forthcoming life.",
                    f"I am thankful to my supervisor {SUPERVISOR} for his help and support in all phases of my project. "
                    "His encouragement helped me during times of difficulty.",
                    "I would also like to thank the project coordinator and all of my teachers and friends for their support.",
                ],
            )
        )

        self.add_pages(
            lambda b: rl_text_page(
                b,
                "Project in Brief",
                [
                    f"Project Title: {SYSTEM}",
                    f"Organization: {UNIVERSITY}",
                    f"Client: {CLIENT}",
                    "Objectives:",
                    "• Develop a digital platform to manage real-estate projects, units, bookings, and payments.",
                    "• Provide separate admin and client interfaces for transparent project and sales management.",
                    "• Automate installment schedules, employee management, and finance reporting.",
                    "• Replace manual record keeping with a centralized web application.",
                ],
            )
        )

        self.add_pages(
            lambda b: rl_text_page(
                b,
                "ABSTRACT",
                [
                    f"The {SYSTEM} (DAMS) is a web-based real estate management platform designed for "
                    f"{CLIENT}. It supports project publishing, unit inventory, customer booking requests, "
                    "confirmed sales, installment tracking, payment receipts, employee attendance and salary "
                    "management, and finance dashboards.",
                    "The system uses React for the frontend, .NET 8 Web API for the backend, and Microsoft "
                    "SQL Server for data storage. JWT authentication and role-based access control protect admin "
                    "and client operations.",
                    "By replacing manual spreadsheets and paper forms with a centralized application, DAMS "
                    "improves transparency for customers and efficiency for administrators.",
                ],
            )
        )

        self.add_pages(
            lambda b: rl_text_page(
                b,
                "Revision History",
                [
                    "Version 1.0 — Complete project documentation for Deen Associate Management System.",
                    f"Author: {AUTHOR}",
                    f"Date: {DATES['revision']}",
                ],
            )
        )

    def build_body(self) -> None:
        # Chapter 1 — Introduction (from proposal pages 6-10, skip proposal covers/TOC)
        self.mark("Chapter # 1 Introduction", 1)
        self.add_pages(lambda b: rl_chapter_page(b, "Chapter # 1\nIntroduction"))
        self.add_pdf(src("DMS_Proposal__2___3__8dc4.pdf"), skip=5)  # pages 6-10 content

        # Chapter 2 — Analysis / SRS
        self.mark("Chapter # 2 Analysis", 1)
        self.add_pages(lambda b: rl_chapter_page(b, "CHAPTER #02\nAnalysis"))
        self.add_pages(lambda b: rl_title_page(b, "Software Requirement Specification", DATES["srs"], proposed=True))
        self.add_pdf(src("functional_requiement_of_deen_association_5ad6.pdf"), skip=1)  # skip SRS cover

        # External interface (separate artifact, same chapter date as SRS)
        self.mark("2.8 External Interface Requirements", 2)
        self.add_pages(
            lambda b: rl_title_page(b, "EXTERNAL INTERFACE REQUIREMENTS", DATES["srs"], proposed=True)
        )
        self.add_pdf(src("DAMS_External_Interface_Requirements__1__2b51.pdf"), skip=1)

        # Chapter 3 — Design Phase
        self.mark("Chapter # 3 Design Phase", 1)
        self.add_pages(lambda b: rl_chapter_page(b, "Design Phase"))
        self.add_pages(lambda b: rl_title_page(b, "Design Phase", DATES["design_phase"]))

        self.mark("3.1 Use Case Diagram", 2)
        self.add_pages(lambda b: rl_title_page(b, "USE CASE DIAGRAM", DATES["use_case"]))
        self.add_pages(lambda b: rl_section_header(b, "3.1 Use Case Diagram"))
        self.add_pdf(src("DMS_Duagran_3.drawio__2__1fae.pdf"))

        self.mark("3.2 Fully Dressed Use Cases", 2)
        self.add_pages(lambda b: rl_title_page(b, "Fully Dressed Use Cases", DATES["fully_dressed"], proposed=True))
        self.add_pages(lambda b: rl_section_header(b, "3.2 Fully Dressed Use Cases"))
        self.add_pdf(src("fully_dressed_use_cases_f94b.pdf"), skip=1)

        self.mark("3.3 Domain Model", 2)
        self.add_pages(lambda b: rl_title_page(b, "Domain Model", DATES["domain"]))
        self.add_pages(lambda b: rl_section_header(b, "3.3 Domain Model"))
        self.add_pdf(src("DAMS_Domain_Model__1__7a22.pdf"), skip=1)

        self.mark("3.4 Package Diagram", 2)
        self.add_pages(lambda b: rl_section_header(b, "3.4 Package Diagram"))
        self.add_pdf(src("DAMS_Package_Diagram_a5a9.pdf"))

        self.mark("3.5 System Sequence Diagram", 2)
        self.add_pages(lambda b: rl_title_page(b, "System Sequence Diagrams", DATES["ssd"]))
        self.add_pages(lambda b: rl_section_header(b, "3.5 System Sequence Diagram"))
        self.add_pdf(src("DAMS_System_Sequence_Diagrams__3__d2bd.pdf"), skip=1)

        self.mark("3.6 ER Diagram", 2)
        self.add_pages(lambda b: rl_section_header(b, "3.6 ER Diagram"))
        self.add_pdf(src("new_erd.drawio_ae49.pdf"))

        self.mark("3.7 Normalization", 2)
        self.add_pages(lambda b: rl_title_page(b, "Normalization", DATES["normalization"]))
        self.add_pages(lambda b: rl_section_header(b, "3.7 Normalization"))
        self.add_pdf(src("Normaizastion_for_DAMS_a929.pdf"), skip=1)

        self.mark("3.8 Relational Model", 2)
        self.add_pages(lambda b: rl_section_header(b, "3.8 Relational Model"))
        self.add_pdf(src("Relational_Model_of_DAMS_3a32.pdf"))

        # Chapter 4 — Construction
        self.mark("Chapter # 4 Construction", 1)
        self.add_pages(lambda b: rl_chapter_page(b, "Chapter # 4\nConstruction"))
        self.add_pages(lambda b: rl_title_page(b, "Class Diagram", DATES["class_diagram"]))

        self.mark("4.1 Class Diagram", 2)
        self.add_pages(lambda b: rl_section_header(b, "4.1 Class Diagram"))
        self.add_pdf(src("DAMS_Class_Diagram__3__e0d9.pdf"), skip=1)

        self.mark("4.2 Project Code", 2)
        self.add_pages(lambda b: rl_section_header(b, "4.2 Project Code"))
        self.add_pdf(src("DAMS_Project_Code_6dcf.pdf"))

        self.mark("4.3 Sample Queries", 2)
        self.add_pages(lambda b: rl_title_page(b, "Sample Queries", DATES["sample_queries"]))
        self.add_pdf(src("DAMS_Sample_Queries_911e.pdf"), skip=1)

        # Chapter 5 — Testing
        self.mark("Chapter # 5 Testing", 1)
        self.add_pages(lambda b: rl_chapter_page(b, "Chapter # 5\nTesting"))
        self.add_pages(lambda b: rl_title_page(b, "Test Case", DATES["test_cases"]))
        self.add_pdf(src("DAMS_Test_Cases_2130.pdf"))

        # Chapter 6 — User Manual
        self.mark("Chapter # 6 User Manual", 1)
        self.add_pages(lambda b: rl_chapter_page(b, "Chapter # 6\nUser Manual"))
        self.add_pages(lambda b: rl_title_page(b, "User Manual", DATES["user_manual"]))
        self.add_pdf(src("DAMS_User_Manual_0336.pdf"))

    def finalize_with_toc(self) -> fitz.Document:
        """Two-pass: build body, compute TOC with front matter offset, prepend front+TOC."""
        body = fitz.open()
        body.insert_pdf(self.doc)

        # Front matter without TOC
        front = fitz.open()
        front_builder = BookBuilder()
        front_builder.doc = front
        front_builder.build_front_matter()
        front_pages = page_count(front)

        toc_entries = [(s.title, s.toc_level, s.page + front_pages + 1) for s in self.sections]
        # +1 for TOC page itself — TOC starts at front_pages + 1, content shifts by toc_pages

        # Estimate TOC pages
        toc_buf = io.BytesIO()
        rl_toc_page(toc_buf, toc_entries)
        toc_doc = fitz.open(stream=toc_buf.getvalue(), filetype="pdf")
        toc_pages = page_count(toc_doc)
        toc_doc.close()

        # Recompute with TOC page count
        toc_entries = [(s.title, s.toc_level, s.page + front_pages + toc_pages + 1) for s in self.sections]

        final = fitz.open()
        final.insert_pdf(front)
        toc_buf2 = io.BytesIO()
        rl_toc_page(toc_buf2, toc_entries)
        final.insert_pdf(fitz.open(stream=toc_buf2.getvalue(), filetype="pdf"))
        final.insert_pdf(body)

        body.close()
        front.close()
        self.doc.close()
        return final


def main() -> None:
    builder = BookBuilder()
    builder.build_body()
    out = builder.finalize_with_toc()
    OUT_PDF.parent.mkdir(parents=True, exist_ok=True)
    ARTIFACT.parent.mkdir(parents=True, exist_ok=True)
    out.save(str(OUT_PDF))
    out.save(str(ARTIFACT))
    print(f"Created {OUT_PDF} ({page_count(out)} pages)")
    print(f"Copied to {ARTIFACT}")
    out.close()


if __name__ == "__main__":
    main()
