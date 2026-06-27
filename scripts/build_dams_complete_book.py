"""Assemble DAMS complete project book PDF following Zostel Inn (Doc_fixed) structure."""

from __future__ import annotations

import io
import re
from dataclasses import dataclass, field
from pathlib import Path

import fitz
from reportlab.lib.pagesizes import letter
from reportlab.lib.units import inch
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas

# Professional thesis fonts (Tinos ≈ Times New Roman, Arimo ≈ Calibri)
_FONT_DIR = "/usr/share/fonts/truetype/croscore"
pdfmetrics.registerFont(TTFont("TNR", f"{_FONT_DIR}/Tinos-Regular.ttf"))
pdfmetrics.registerFont(TTFont("TNR-B", f"{_FONT_DIR}/Tinos-Bold.ttf"))
pdfmetrics.registerFont(TTFont("TNR-I", f"{_FONT_DIR}/Tinos-Italic.ttf"))
pdfmetrics.registerFont(TTFont("Sans", f"{_FONT_DIR}/Arimo-Regular.ttf"))
pdfmetrics.registerFont(TTFont("Sans-B", f"{_FONT_DIR}/Arimo-Bold.ttf"))

# Layout: Oxford/Cambridge (12pt, 1.5 spacing, ≥1" margins) + Zostel front-matter positions
ML = 72  # left margin (1 inch)
MR = 72
MT = 72
MB = 72
BODY = 12
BODY_LEAD = 18  # 1.5 line spacing
SMALL = 11
HEADING = 16
TITLE = 18
CHAPTER = 48
FOR_SIZE = 22

ROOT = Path(__file__).resolve().parents[1]
UPLOAD = Path("/home/ubuntu/.cursor/projects/workspace/uploads")
OUT_PDF = ROOT / "docs" / "DAMS_Complete_Project_Document.pdf"
ARTIFACT = Path("/opt/cursor/artifacts/DAMS_Complete_Project_Document.pdf")

AUTHOR = "Muhammad Junaid Riaz"
SUPERVISOR = "Mr. Khawar Maqsood"
COORDINATOR = "Mr. Khawaja Tahir"
UNIVERSITY = "Federal Urdu University of Arts, Science & Technology, Islamabad"
SESSION = "SESSION [2022-2026]"
SYSTEM = "Deen Associate Management System"
CLIENT = "Deen Associate"

# Section dates from Zostel sample (Doc_fixed) — per section, not one global date
DATES = {
    "proposal": "15th October, 2025",
    "revision": "15th October, 2025",
    "srs": "27 Nov, 2025",
    "use_case": "27 Nov, 2025",
    "fully_dressed": "27 Nov, 2025",
    "design_phase": "27 Nov, 2025",
    "domain": "27 Nov, 2025",
    "package": "27 Nov, 2025",
    "ssd": "27 Nov, 2025",
    "er": "27 Nov, 2025",
    "normalization": "27 Nov, 2025",
    "relational": "27 Nov, 2025",
    "class_diagram": "27 Nov, 2025",
    "construction": "27 Nov, 2025",
    "project_code": "27 Nov, 2025",
    "sample_queries": "27 Nov, 2025",
    "test_cases": "25 Nov, 2025",
    "user_manual": "27 Nov, 2025",
}

SSD_TOC = [
    "User Registration",
    "User Login",
    "Browse Projects",
    "Submit Booking Request",
    "Approve Booking Request",
    "Reject Booking Request",
    "Create Walk-in Booking",
    "Record Booking Amount Payment",
    "Generate Installment Plan",
    "Record Installment Payment",
    "View Payment Receipt",
    "Client My Projects",
    "Cancel Booking",
    "Mark Employee Attendance",
    "Generate Employee Salary",
    "Finance Dashboard",
    "Add Manual Revenue",
    "Add Expense",
]

CH1_SUBSECTIONS = [
    (0, "Introduction", 2),
    (0, "1.1 Introduction:", 3),
    (0, "1.2 Problem Statement:", 3),
    (0, "1.3 Proposed System:", 3),
    (0, "1.4 Benefits of Proposed System:", 3),
    (1, "1.5 Scope:", 3),
    (1, "1.6 Survey Analysis:", 3),
    (2, "1.7 Modules & Sub-Modules Description:", 3),
    (3, "1.8 Primary Actors", 3),
    (4, "1.9 Tools & Techniques", 3),
    (4, "1.9.1 Front End:", 4),
    (4, "1.9.2 Back End:", 4),
    (4, "1.9.3 Deployment:", 4),
    (5, "1.10 Techniques:", 3),
    (5, "1.10.1 Process Model Approach Used:", 4),
    (5, "1.10.2 System Design Approach Used:", 4),
    (5, "1.11 Limitations:", 3),
    (5, "1.12 References:", 3),
]

SRS_SUBSECTIONS = [
    (1, "2.1 Introduction", 3),
    (2, "2.2 Scope", 3),
    (3, "2.3 Definitions, Acronyms and Abbreviations", 3),
    (3, "2.4 Overview", 3),
    (4, "2.5 Functional Requirements", 3),
    (4, "2.6.1 Authentication Management", 3),
    (4, "2.6.1.1 Sign Up", 4),
    (4, "2.6.1.2 Login", 4),
    (4, "2.6.1.3 Change Password", 4),
    (5, "2.6.1.4 Forget Password", 4),
    (5, "2.6.2 Staff Management", 3),
    (5, "2.6.2.1 Add Staff", 4),
    (5, "2.6.2.2 Edit Staff", 4),
    (5, "2.6.2.3 View Staff", 4),
    (5, "2.6.2.4 Search Staff", 4),
    (6, "2.6.2.5 Assign Staff to Project", 4),
    (6, "2.6.3 Project Management", 3),
    (6, "2.6.3.1 Add Project", 4),
    (6, "2.6.3.2 Edit Project", 4),
    (7, "2.6.3.3 View Project", 4),
    (7, "2.6.3.4 Search Project", 4),
    (7, "2.6.4 Client Management", 3),
    (7, "2.6.4.1 Add Client", 4),
    (8, "2.6.4.2 Edit Client", 4),
    (8, "2.6.4.3 View Client", 4),
    (9, "2.6.4.4 Search Client", 4),
    (9, "2.6.5 Booking Management", 3),
    (9, "2.6.5.1 Add Booking", 4),
    (9, "2.6.5.2 View Booking", 4),
    (10, "2.6.5.3 Search Booking", 4),
    (10, "2.6.5.4 Cancel Booking", 4),
    (10, "2.6.5.5 Edit Booking", 4),
    (10, "2.6.6 Construction Progress", 3),
    (10, "2.6.6.1 Add Progress", 4),
    (11, "2.6.6.2 Edit Progress", 4),
    (11, "2.6.6.3 View Progress", 4),
    (11, "2.6.6.4 Upload Media", 4),
    (11, "2.6.7 Notification System", 3),
    (11, "2.6.7.1 Send Notification", 4),
    (11, "2.6.7.2 View Notifications", 4),
    (12, "2.6.7.3 Manage Announcements", 4),
    (12, "2.6.8 Expense Management", 3),
    (12, "2.6.8.1 Add Expense", 4),
    (12, "2.6.8.2 Edit Expense", 4),
    (12, "2.6.8.3 Delete Expense", 4),
    (13, "2.6.8.4 View Expenses", 4),
    (13, "2.6.8.5 View Profit & Loss Report", 4),
    (13, "2.6.9 Income Management", 3),
    (13, "2.6.9.1 Record Income", 4),
    (13, "2.6.9.2 Edit Income", 4),
    (14, "2.6.9.3 View Income Records", 4),
    (14, "2.6.9.4 Filter Income", 4),
    (14, "2.6.9.5 Income Summary Report", 4),
    (14, "2.6.10 System Interface", 3),
    (14, "2.6.10.1 Dashboard Overview", 4),
    (14, "2.6.10.2 Access All Modules", 4),
    (15, "2.6.10.3 Manage Users, Roles & Permissions", 4),
    (15, "2.7 Non-Functional Requirements", 3),
    (15, "2.7.1 Security", 4),
    (15, "2.7.2 Usability", 4),
    (15, "2.7.3 Availability", 4),
    (16, "2.7.4 Performance", 4),
    (16, "2.7.5 Accuracy", 4),
]

NORM_SUBSECTIONS = [
    (1, "3.7.1 Table Role", 4),
    (1, "3.7.2 Table User", 4),
    (2, "3.7.3 Table Project", 4),
    (2, "3.7.4 Table Notification", 4),
    (3, "3.7.5 Table Expense", 4),
    (3, "3.7.6 Table Unit", 4),
    (3, "3.7.7 Table Booking", 4),
    (4, "3.7.8 Table Income", 4),
    (4, "3.7.9 Table Staff", 4),
    (4, "3.7.10 Table Progress", 4),
    (5, "3.7.11 Table Progress Media", 4),
]

USER_MANUAL_SUBSECTIONS = [
    (0, "6.1 Introduction", 3),
    (0, "6.1.1 User Roles", 3),
    (0, "6.1.1.1 Client (Customer)", 4),
    (0, "6.1.1.2 Administrator", 4),
    (1, "6.2 Getting Started", 3),
    (1, "6.2.1 Opening the Application", 4),
    (1, "6.2.2 Registration and Login", 4),
    (3, "6.2.3 Theme and Profile Controls", 4),
    (3, "6.3 Client User Guide", 3),
    (11, "6.4 Administrator User Guide", 3),
    (12, "6.4.1 Booking Requests and Confirmed Bookings", 4),
    (15, "6.4.2 Customers Module", 4),
    (15, "6.4.3 Employees Module", 4),
    (17, "6.4.4 Finance Module", 4),
    (19, "6.4.5 Project and Media Management", 4),
    (19, "6.5 Quick Reference — Who Can Access What", 3),
    (19, "6.6 Tips and Troubleshooting", 3),
]


@dataclass
class TocEntry:
    title: str
    level: int
    body_page: int  # 1-based page within body document


@dataclass
class BookBuilder:
    doc: fitz.Document = field(default_factory=fitz.open)
    entries: list[TocEntry] = field(default_factory=list)

    def current_page(self) -> int:
        return len(self.doc) + 1

    def mark(self, title: str, level: int = 1) -> None:
        self.entries.append(TocEntry(title=title, level=level, body_page=self.current_page()))

    def mark_at(self, title: str, level: int, body_page: int) -> None:
        self.entries.append(TocEntry(title=title, level=level, body_page=body_page))

    def mark_offsets(self, start: int, items: list[tuple[int, str, int]]) -> None:
        for offset, title, level in items:
            self.mark_at(title, level, start + offset)

    def add_bytes(self, pdf_bytes: bytes) -> None:
        insert_bytes(self.doc, pdf_bytes)


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


def _wrap(c: canvas.Canvas, text: str, font: str, size: float, width: float) -> list[str]:
    c.setFont(font, size)
    words = text.split()
    lines: list[str] = []
    line = ""
    for word in words:
        test = (line + " " + word).strip()
        if c.stringWidth(test, font, size) <= width:
            line = test
        else:
            if line:
                lines.append(line)
            line = word
    if line:
        lines.append(line)
    return lines


def _draw_wrapped(
    c: canvas.Canvas,
    x: float,
    y: float,
    text: str,
    font: str,
    size: float,
    width: float,
    leading: float,
) -> float:
    for ln in _wrap(c, text, font, size, width):
        c.setFont(font, size)
        c.drawString(x, y, ln)
        y -= leading
    return y


def rl_blank_page(buf: io.BytesIO) -> None:
    c = canvas.Canvas(buf, pagesize=letter)
    c.showPage()
    c.save()


def rl_cover_main(buf: io.BytesIO) -> None:
    """Zostel-style title page — top-aligned, not vertically centred."""
    w, h = letter
    c = canvas.Canvas(buf, pagesize=letter)
    y = h - 100
    c.setFont("TNR-B", 20)
    c.drawCentredString(w / 2, y, SYSTEM)
    y -= 48
    c.setFont("TNR-I", 14)
    c.drawCentredString(w / 2, y, "Developed By")
    y -= 28
    c.setFont("TNR-B", 14)
    c.drawCentredString(w / 2, y, AUTHOR)
    y -= 52
    c.setFont("TNR", 17)
    c.drawCentredString(w / 2, y, "BS (CS) in Software Systems")
    y -= 58
    c.setFont("TNR-B", 14)
    c.drawCentredString(w / 2, y, "Supervised By")
    y -= 32
    c.setFont("TNR-B", 16)
    c.drawCentredString(w / 2, y, SUPERVISOR)
    y -= 38
    c.setFont("TNR", 16)
    c.drawCentredString(w / 2, y, "Project Coordinator")
    y -= 32
    c.setFont("TNR-B", 16)
    c.drawCentredString(w / 2, y, COORDINATOR)
    y -= 38
    c.setFont("TNR", 16)
    c.drawCentredString(w / 2, y, "Department of Computer Science")
    y -= 22
    c.drawCentredString(w / 2, y, UNIVERSITY)
    y -= 22
    c.drawCentredString(w / 2, y, SESSION)
    c.showPage()
    c.save()


def rl_cover_presentation(buf: io.BytesIO) -> None:
    w, h = letter
    c = canvas.Canvas(buf, pagesize=letter)
    y = h - 118
    c.setFont("TNR-B", 14)
    c.drawCentredString(w / 2, y, "A Project Presented to")
    y -= 44
    c.setFont("TNR", 14)
    c.drawCentredString(w / 2, y, "Federal Urdu University of Arts, Science & Technology")
    y -= 44
    c.drawCentredString(w / 2, y, "In Partial Fulfillment of the Requirement for the Degree")
    y -= 58
    c.setFont("TNR-B", 14)
    c.drawCentredString(w / 2, y, "Bachelor of Computer Science")
    y -= 32
    c.setFont("TNR", 16)
    c.drawCentredString(w / 2, y, "(Software Systems)")
    y -= 48
    c.drawCentredString(w / 2, y, "By")
    y -= 48
    c.setFont("TNR-B", 16)
    c.drawCentredString(w / 2, y, AUTHOR)
    y -= 44
    c.setFont("TNR", 16)
    c.drawCentredString(w / 2, y, DATES["proposal"])
    y -= 52
    c.setFont("TNR-B", 16)
    c.drawCentredString(w / 2, y, f"({UNIVERSITY})")
    c.showPage()
    c.save()


def rl_declaration(buf: io.BytesIO) -> None:
    w, h = letter
    c = canvas.Canvas(buf, pagesize=letter)
    c.setFont("Sans-B", HEADING)
    c.drawCentredString(w / 2, h - MT - 8, "Declaration")
    text = (
        "I hereby declare that this software, neither as a whole nor as a part, has been "
        "copied from any source. I have developed this software and the accompanied report "
        "entirely on the basis of my personal efforts. If any part of this project is proved "
        "to be copied from any source, I will stand by the consequences. No portion of the work "
        "presented has been submitted in support of any application for any other degree or "
        "qualification of this or any other university or institute of learning."
    )
    y = _draw_wrapped(c, ML, h - MT - 48, text, "Sans", 14, w - ML - MR, 25)
    y -= 36
    c.setFont("Sans-B", 14)
    c.drawCentredString(w / 2, y, AUTHOR)
    c.showPage()
    c.save()


def rl_dedication(buf: io.BytesIO) -> None:
    w, h = letter
    c = canvas.Canvas(buf, pagesize=letter)
    c.setFont("Sans", 28)
    lines = ["Dedicated to my beloved parents,", "Teachers", "And", "The fellows"]
    y = h - 200
    for ln in lines:
        c.drawCentredString(w / 2, y, ln)
        y -= 58
    c.showPage()
    c.save()


def rl_acknowledgement(buf: io.BytesIO) -> None:
    w, h = letter
    c = canvas.Canvas(buf, pagesize=letter)
    c.setFont("Sans-B", HEADING)
    c.drawCentredString(w / 2, h - MT - 8, "Acknowledgement")
    paras = [
        "Thanks to Almighty Allah for giving me knowledge, power and strength to accomplish this task. "
        "I learned a lot while doing this project and this will certainly help me in my forthcoming life.",
        f"I am thankful to my supervisor {SUPERVISOR} for his help and support in all phases of my project. "
        "His encouragement helped me during times of difficulty.",
        "I would also like to thank the project coordinator and all of my teachers and friends for their support.",
    ]
    y = h - MT - 48
    for para in paras:
        y = _draw_wrapped(c, ML, y, para, "Sans", 14, w - ML - MR, 25)
        y -= 8
    y -= 24
    c.setFont("Sans-B", 14)
    c.drawCentredString(w / 2, y, AUTHOR)
    c.showPage()
    c.save()


def rl_project_brief(buf: io.BytesIO) -> None:
    """Zostel-style two-column project brief."""
    w, h = letter
    c = canvas.Canvas(buf, pagesize=letter)
    c.setFont("Sans-B", 14)
    c.drawCentredString(w / 2, h - MT - 8, "Project in Brief")
    label_x = 54
    value_x = 187
    y = h - MT - 40

    def row(label: str, value: str, *, dy: float = 30) -> None:
        nonlocal y
        c.setFont("Sans", BODY)
        c.drawString(label_x, y, label)
        c.drawString(value_x, y, value)
        y -= dy

    row("Project Title", SYSTEM)
    row("Organization", UNIVERSITY, dy=30)
    row("Client", CLIENT, dy=30)
    y -= 20
    c.setFont("Sans", BODY)
    c.drawString(label_x, y, "Objectives")
    y -= 22
    objectives = [
        "Develop a digital platform to manage real-estate projects, units, bookings, and payments.",
        "Provide separate admin and client interfaces for transparent project and sales management.",
        "Automate installment schedules, employee management, and finance reporting.",
        "Replace manual record keeping with a centralized web application.",
    ]
    for obj in objectives:
        c.drawString(205, y, "\u2022")
        y = _draw_wrapped(c, 223, y, obj, "Sans", BODY, w - 223 - MR, BODY_LEAD)
        y -= 4
    y -= 10
    row("Undertaken By", AUTHOR, dy=30)
    row("Supervised By", SUPERVISOR, dy=30)
    row("Date Started", "15th October, 2025", dy=30)
    row("Date Completed", "27 Nov, 2025", dy=30)
    y -= 16
    c.drawString(label_x, y, "Technologies Used")
    y -= 22
    for tech in ["React.js", "TypeScript", ".NET 8 Web API", "Microsoft SQL Server", "JWT Authentication"]:
        c.drawString(205, y, "\u2022")
        c.drawString(223, y, tech)
        y -= BODY_LEAD
    row("System used", SYSTEM, dy=30)
    c.showPage()
    c.save()


def rl_abstract(buf: io.BytesIO) -> None:
    w, h = letter
    c = canvas.Canvas(buf, pagesize=letter)
    c.setFont("TNR-B", TITLE)
    c.drawCentredString(w / 2, h - MT - 6, "ABSTRACT")
    c.setFont("TNR-B", TITLE)
    c.drawCentredString(w / 2, h - MT - 36, "DAMS")
    paras = [
        f"The {SYSTEM} (DAMS) is a web-based real estate management platform designed for "
        f"{CLIENT}. It supports project publishing, unit inventory, customer booking requests, "
        "confirmed sales, installment tracking, payment receipts, employee attendance and salary "
        "management, and finance dashboards.",
        "The system uses React for the frontend, .NET 8 Web API for the backend, and Microsoft "
        "SQL Server for data storage. JWT authentication and role-based access control protect admin "
        "and client operations.",
        "By replacing manual spreadsheets and paper forms with a centralized application, DAMS "
        "improves transparency for customers and efficiency for administrators.",
    ]
    y = h - MT - 78
    for para in paras:
        y = _draw_wrapped(c, ML, y, para, "TNR", SMALL, w - ML - MR, 19)
        y -= 6
    c.showPage()
    c.save()


def rl_revision_history(buf: io.BytesIO) -> None:
    """Zostel-style revision history table."""
    w, h = letter
    c = canvas.Canvas(buf, pagesize=letter)
    c.setFont("TNR-B", TITLE)
    c.drawCentredString(w / 2, h / 2 + 60, "Revision History")
    cols = [96, 181, 320, 456]
    y = h / 2 + 10
    headers = ["Version", "Description", "Author", "Date"]
    c.setFont("TNR-B", 14)
    for i, hdr in enumerate(headers):
        c.drawString(cols[i], y, hdr)
    y -= 36
    c.setFont("TNR", BODY)
    c.drawString(cols[0], y, "1.0")
    desc_lines = [
        "This document contains Project",
        "documentation of",
        "Deen Associate Management System",
    ]
    author_lines = [AUTHOR]
    date_lines = ["15th October, 2025"]
    dy = 15
    for i, ln in enumerate(desc_lines):
        c.drawString(cols[1], y - i * dy, ln)
    for i, ln in enumerate(author_lines):
        c.drawString(cols[2], y - i * dy, ln)
    for i, ln in enumerate(date_lines):
        c.drawString(cols[3], y - i * dy, ln)
    c.showPage()
    c.save()


def rl_for_page(buf: io.BytesIO, doc_title: str, date: str, *, proposed: bool = False) -> None:
    """Zostel/Oxford-style FOR page — document title upper-left, formal centred block below."""
    w, h = letter
    c = canvas.Canvas(buf, pagesize=letter)
    c.setFont("TNR-B", FOR_SIZE)
    c.drawString(ML, h - 90, doc_title)

    block: list[tuple[str, str, float]] = [
        ("FOR", "TNR-B", FOR_SIZE),
        (SYSTEM, "TNR-B", 18),
        ("Version 1.0", "TNR-B", FOR_SIZE),
        ("Prepared By" if not proposed else "Proposed By", "TNR-B", FOR_SIZE),
        (AUTHOR, "TNR-B", FOR_SIZE),
        (date, "TNR-B", FOR_SIZE),
    ]
    y = h - 210
    for text, font, size in block:
        c.setFont(font, size)
        c.drawCentredString(w / 2, y, text)
        y -= size + 16
    c.showPage()
    c.save()


def rl_chapter_page(buf: io.BytesIO, text: str) -> None:
    w, h = letter
    c = canvas.Canvas(buf, pagesize=letter)
    lines = [ln.strip() for ln in text.split("\n") if ln.strip()]
    c.setFont("TNR-B", CHAPTER)
    if len(lines) >= 2:
        c.drawCentredString(w / 2, h - 297, lines[0])
        c.drawCentredString(w / 2, h - 364, lines[1])
    elif lines:
        c.drawCentredString(w / 2, h - 330, lines[0])
    c.showPage()
    c.save()


def rl_section_header(buf: io.BytesIO, text: str) -> None:
    w, h = letter
    c = canvas.Canvas(buf, pagesize=letter)
    c.setFont("TNR-B", 14)
    c.drawCentredString(w / 2, h - MT, text)
    c.showPage()
    c.save()


def rl_ssd_index_page(buf: io.BytesIO, header: str, ssd_items: list[str]) -> None:
    w, h = letter
    c = canvas.Canvas(buf, pagesize=letter)
    c.setFont("TNR-B", 14)
    c.drawCentredString(w / 2, h - MT, header)
    c.setFont("TNR", BODY)
    y = h - MT - 28
    for i, name in enumerate(ssd_items, start=1):
        c.drawString(ML, y, f"SSD {i}: {name}")
        y -= BODY_LEAD
        if y < MB + 20:
            c.showPage()
            c.setFont("TNR", BODY)
            y = h - MT
    c.showPage()
    c.save()


def rl_toc_pages(entries: list[tuple[str, int, int]]) -> bytes:
    """Zostel-style TOC: Sans for chapter lines, TNR for numbered subsections."""
    w, h = letter
    buf = io.BytesIO()
    c = canvas.Canvas(buf, pagesize=letter)
    c.setFont("TNR-B", TITLE)
    c.drawCentredString(w / 2, h - MT - 6, "Table of Contents")
    y = h - MT - 48
    right = w - MR

    for title, level, page in entries:
        indent = ML + (level - 1) * 16
        size = BODY if level <= 2 else 11
        page_str = str(page)

        numbered = bool(re.match(r"^(\d+\.|SSD |UC-|Table )", title))
        chapter_line = level == 1 or title in {
            "Introduction",
            "USE CASE DIAGRAM",
            "Fully Dressed Use Cases",
            "Design Phase",
            "Class Diagram",
            "Test Case",
            "User Manual",
        }

        if level == 1 or (level == 2 and chapter_line and not numbered):
            title_font = "Sans"
        elif numbered:
            title_font = "TNR"
        else:
            title_font = "Sans"

        c.setFont(title_font, size)
        page_w = c.stringWidth(page_str, "Sans", size)
        avail = right - indent - page_w - 8
        display_title = title
        while display_title and c.stringWidth(display_title + "...", title_font, size) > avail:
            display_title = display_title[:-1]
        if display_title != title:
            display_title += "..."

        title_w = c.stringWidth(display_title, title_font, size)
        c.setFont(title_font, size)
        c.drawString(indent, y, display_title)

        dot_start = indent + title_w + 4
        dot_end = right - page_w - 4
        c.setFont("Sans", size)
        dot_w = max(c.stringWidth(".", "Sans", size), 1)
        dots = "." * max(int((dot_end - dot_start) / dot_w), 2)
        c.drawString(dot_start, y, dots)
        c.drawRightString(right, y, page_str)

        y -= 22 if level == 1 else (20 if level == 2 else 18)
        if y < MB + 16:
            c.showPage()
            y = h - MT

    c.showPage()
    c.save()
    buf.seek(0)
    return buf.read()


def insert_bytes(doc: fitz.Document, pdf_bytes: bytes) -> None:
    doc.insert_pdf(fitz.open(stream=pdf_bytes, filetype="pdf"))


def insert_file(doc: fitz.Document, path: Path, skip: int = 0, stop_before: int | None = None) -> int:
    s = fitz.open(str(path))
    end = (stop_before - 1) if stop_before is not None else len(s) - 1
    end = min(end, len(s) - 1)
    if end >= skip:
        doc.insert_pdf(s, from_page=skip, to_page=end)
        n = end - skip + 1
    else:
        n = 0
    s.close()
    return n


def extract_use_cases(path: Path) -> list[str]:
    doc = fitz.open(str(path))
    text = "\n".join(doc[i].get_text() for i in range(len(doc)))
    doc.close()
    cases: list[str] = []
    for m in re.finditer(r"Use case Name:\s*\n\s*(UC-\d+)\s*\n\s*(.+?)(?:\n|$)", text):
        cases.append(f"{m.group(1)} {m.group(2).strip()}")
    return cases


def build_front_matter() -> fitz.Document:
    front = fitz.open()
    b = BookBuilder(doc=front)

    b.add_bytes(pdf_page_bytes(rl_cover_main))
    b.add_bytes(pdf_page_bytes(rl_cover_presentation))
    b.add_bytes(pdf_page_bytes(rl_blank_page))  # Zostel page 3 is blank
    b.add_bytes(pdf_page_bytes(rl_declaration))
    b.add_bytes(pdf_page_bytes(rl_dedication))
    b.add_bytes(pdf_page_bytes(rl_acknowledgement))
    b.add_bytes(pdf_page_bytes(rl_project_brief))
    b.add_bytes(pdf_page_bytes(rl_abstract))
    b.add_bytes(pdf_page_bytes(rl_revision_history))
    return front


def build_body() -> tuple[fitz.Document, list[TocEntry]]:
    b = BookBuilder()

    # Chapter 1 — Introduction
    b.mark("Chapter # 1 Introduction", 1)
    b.add_bytes(pdf_page_bytes(lambda buf: rl_chapter_page(buf, "Chapter # 1\nIntroduction")))
    ch1_start = b.current_page()
    b.mark("Introduction", 2)
    insert_file(b.doc, src("DMS_Proposal__2___3__8dc4.pdf"), skip=5)
    b.mark_offsets(ch1_start, CH1_SUBSECTIONS)

    # Chapter 2 — Analysis / SRS
    b.mark("Chapter # 2 Analysis", 1)
    b.add_bytes(pdf_page_bytes(lambda buf: rl_chapter_page(buf, "CHAPTER #02\nAnalysis")))
    b.add_bytes(pdf_page_bytes(lambda buf: rl_for_page(buf, "Software Requirement Specification", DATES["srs"], proposed=True)))
    srs_start = b.current_page()
    insert_file(b.doc, src("functional_requiement_of_deen_association_5ad6.pdf"), skip=1, stop_before=17)
    b.mark_offsets(srs_start, SRS_SUBSECTIONS)

    b.mark("2.8 External Interface Requirements", 2)
    b.add_bytes(pdf_page_bytes(lambda buf: rl_section_header(buf, "2.8 External Interface Requirements")))
    eir_start = b.current_page()
    insert_file(b.doc, src("DAMS_External_Interface_Requirements__1__2b51.pdf"), skip=2)
    b.mark_at("2.8.1 User Interface (UI)", 3, eir_start)
    b.mark_at("2.8.2 Hardware Interfaces", 3, eir_start)
    b.mark_at("2.8.3 Software Interfaces", 3, eir_start)
    b.mark_at("2.8.4 Licensing Requirements", 3, eir_start)

    # Chapter 3 — Design Phase (Zostel order: Use Case → Fully Dressed → Design Phase FOR → Domain → Package → SSD → ER → Norm → Relational)
    b.mark("CHAPTER #03 Design Phase", 1)
    b.add_bytes(pdf_page_bytes(lambda buf: rl_chapter_page(buf, "CHAPTER #03\nDesign Phase")))

    b.mark("USE CASE DIAGRAM", 2)
    b.add_bytes(pdf_page_bytes(lambda buf: rl_for_page(buf, "USE CASE DIAGRAM", DATES["use_case"])))
    b.mark("3.1 Use Case Diagram", 2)
    b.add_bytes(pdf_page_bytes(lambda buf: rl_section_header(buf, "3.1 Use Case Diagram")))
    insert_file(b.doc, src("DMS_Duagran_3.drawio__2__1fae.pdf"))

    b.mark("Fully Dressed Use Cases", 2)
    b.add_bytes(pdf_page_bytes(lambda buf: rl_for_page(buf, "Fully Dressed Use Cases", DATES["fully_dressed"], proposed=True)))
    b.mark("3.2 Fully Dressed Use Cases", 2)
    b.add_bytes(pdf_page_bytes(lambda buf: rl_section_header(buf, "3.2 Fully Dressed Use Cases")))
    fd_start = b.current_page()
    insert_file(b.doc, src("fully_dressed_use_cases_f94b.pdf"), skip=2)
    for i, uc in enumerate(extract_use_cases(src("fully_dressed_use_cases_f94b.pdf"))):
        b.mark_at(uc, 3, fd_start + i)

    b.mark("Design Phase", 2)
    b.add_bytes(pdf_page_bytes(lambda buf: rl_for_page(buf, "Design Phase", DATES["design_phase"])))

    b.mark("3.3 Domain Model", 2)
    b.add_bytes(pdf_page_bytes(lambda buf: rl_section_header(buf, "3.3 Domain Model")))
    insert_file(b.doc, src("DAMS_Domain_Model__1__7a22.pdf"), skip=1)

    b.mark("3.4 Package Diagram", 2)
    b.add_bytes(pdf_page_bytes(lambda buf: rl_section_header(buf, "3.4 Package Diagram")))
    insert_file(b.doc, src("DAMS_Package_Diagram_a5a9.pdf"))

    b.mark("3.5 System Sequence Diagram", 2)
    ssd_start = b.current_page()
    b.add_bytes(pdf_page_bytes(lambda buf: rl_ssd_index_page(buf, "3.5 System Sequence Diagram", SSD_TOC)))
    for i, name in enumerate(SSD_TOC):
        b.mark_at(f"SSD {i + 1}: {name}", 3, ssd_start)
    insert_file(b.doc, src("DAMS_System_Sequence_Diagrams__3__d2bd.pdf"), skip=1)

    b.mark("3.6 ER Diagram", 2)
    b.add_bytes(pdf_page_bytes(lambda buf: rl_section_header(buf, "3.6 ER Diagram")))
    insert_file(b.doc, src("new_erd.drawio_ae49.pdf"))

    b.mark("3.7 Normalization", 2)
    b.add_bytes(pdf_page_bytes(lambda buf: rl_for_page(buf, "Normalization", DATES["normalization"])))
    b.add_bytes(pdf_page_bytes(lambda buf: rl_section_header(buf, "3.7 Normalization")))
    norm_start = b.current_page()
    insert_file(b.doc, src("Normaizastion_for_DAMS_a929.pdf"), skip=1)
    b.mark_offsets(norm_start, NORM_SUBSECTIONS)

    b.mark("3.8 Relational Model", 2)
    b.add_bytes(pdf_page_bytes(lambda buf: rl_section_header(buf, "3.8 Relational Model")))
    insert_file(b.doc, src("Relational_Model_of_DAMS_3a32.pdf"))

    # Chapter 4 — Construction
    b.mark("Chapter # 4 Construction", 1)
    b.add_bytes(pdf_page_bytes(lambda buf: rl_chapter_page(buf, "Chapter # 4\nConstruction")))
    b.mark("Class Diagram", 2)
    b.add_bytes(pdf_page_bytes(lambda buf: rl_for_page(buf, "Class Diagram", DATES["class_diagram"])))
    b.mark("4.1 Class Diagram", 2)
    b.add_bytes(pdf_page_bytes(lambda buf: rl_section_header(buf, "4.1 Class Diagram")))
    insert_file(b.doc, src("DAMS_Class_Diagram__3__e0d9.pdf"), skip=1)

    b.mark("4.2 Project Code", 2)
    b.add_bytes(pdf_page_bytes(lambda buf: rl_section_header(buf, "4.2 Project Code")))
    insert_file(b.doc, src("DAMS_Project_Code_6dcf.pdf"))

    b.mark("4.3 Sample Queries", 2)
    b.add_bytes(pdf_page_bytes(lambda buf: rl_for_page(buf, "Sample Queries", DATES["sample_queries"])))
    insert_file(b.doc, src("DAMS_Sample_Queries_911e.pdf"), skip=1)

    # Chapter 5 — Testing
    b.mark("Chapter # 5 Testing", 1)
    b.add_bytes(pdf_page_bytes(lambda buf: rl_chapter_page(buf, "Chapter # 5\nTesting")))
    b.mark("Test Case", 2)
    b.add_bytes(pdf_page_bytes(lambda buf: rl_for_page(buf, "Test Case", DATES["test_cases"])))
    insert_file(b.doc, src("DAMS_Test_Cases_2130.pdf"))

    # Chapter 6 — User Manual
    b.mark("Chapter # 6 User Manual", 1)
    b.add_bytes(pdf_page_bytes(lambda buf: rl_chapter_page(buf, "Chapter # 6\nUser Manual")))
    b.mark("User Manual", 2)
    b.add_bytes(pdf_page_bytes(lambda buf: rl_for_page(buf, "User Manual", DATES["user_manual"])))
    um_start = b.current_page()
    insert_file(b.doc, src("DAMS_User_Manual_0336.pdf"))
    b.mark_offsets(um_start, USER_MANUAL_SUBSECTIONS)

    return b.doc, b.entries


def display_page(body_page: int, front_pages: int, toc_pages: int) -> int:
    """Zostel-style displayed page number = physical page - 1."""
    return front_pages + toc_pages + body_page - 1


def compile_toc(entries: list[TocEntry], front_pages: int, toc_pages: int) -> list[tuple[str, int, int]]:
    seen: set[tuple[int, str]] = set()
    out: list[tuple[str, int, int]] = []
    for e in entries:
        key = (e.body_page, e.title)
        if key in seen:
            continue
        seen.add(key)
        out.append((e.title, e.level, display_page(e.body_page, front_pages, toc_pages)))
    return out


def add_page_numbers(doc: fitz.Document, toc_end_index: int) -> None:
    """Add bottom-centered page numbers on body pages (Zostel style)."""
    for i in range(toc_end_index, len(doc)):
        if i == toc_end_index:
            # Match Zostel: first chapter divider page has no footer number
            continue
        page = doc[i]
        # Remove embedded footer numbers from source PDFs before adding our own
        strip = fitz.Rect(0, page.rect.height - 90, page.rect.width, page.rect.height)
        page.add_redact_annot(strip, fill=(1, 1, 1))
        page.apply_redactions()
        num = str(i)
        fontsize = 12
        text_width = fitz.get_text_length(num, fontname="helv", fontsize=fontsize)
        x = (page.rect.width - text_width) / 2
        y = page.rect.height - 67
        page.insert_text((x, y), num, fontsize=fontsize, fontname="helv", color=(0, 0, 0))


def assemble_book() -> fitz.Document:
    front = build_front_matter()
    front_pages = len(front)
    body, entries = build_body()

    toc_pages = 1
    for _ in range(12):
        toc_list = compile_toc(entries, front_pages, toc_pages)
        toc_bytes = rl_toc_pages(toc_list)
        new_toc_pages = len(fitz.open(stream=toc_bytes, filetype="pdf"))
        if new_toc_pages == toc_pages:
            break
        toc_pages = new_toc_pages

    toc_list = compile_toc(entries, front_pages, toc_pages)
    toc_bytes = rl_toc_pages(toc_list)
    toc_doc = fitz.open(stream=toc_bytes, filetype="pdf")

    final = fitz.open()
    final.insert_pdf(front)
    final.insert_pdf(toc_doc)
    final.insert_pdf(body)

    toc_end_index = front_pages + len(toc_doc)
    add_page_numbers(final, toc_end_index)

    front.close()
    body.close()
    toc_doc.close()
    return final


def main() -> None:
    out = assemble_book()
    OUT_PDF.parent.mkdir(parents=True, exist_ok=True)
    ARTIFACT.parent.mkdir(parents=True, exist_ok=True)
    out.save(str(OUT_PDF))
    out.save(str(ARTIFACT))
    print(f"Created {OUT_PDF} ({len(out)} pages, TOC entries: see script)")
    print(f"Copied to {ARTIFACT}")
    out.close()


if __name__ == "__main__":
    main()
