"""Generate DAMS External Interface Requirements PDF in sample document style."""

import shutil
from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_JUSTIFY, TA_LEFT
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import inch
from reportlab.platypus import (
    ListFlowable,
    ListItem,
    PageBreak,
    Paragraph,
    SimpleDocTemplate,
    Spacer,
    Table,
    TableStyle,
)

ROOT = Path(__file__).resolve().parents[1]
OUT_PATHS = [
    ROOT / "docs" / "DAMS_External_Interface_Requirements.pdf",
    Path("/opt/cursor/artifacts/DAMS_External_Interface_Requirements.pdf"),
]

DOC_DATE = "27th November 2025"
AUTHOR = "Muhammad Junaid Riaz"


def bullet_list(items: list[str], style: ParagraphStyle) -> ListFlowable:
    return ListFlowable(
        [ListItem(Paragraph(item, style), leftIndent=12) for item in items],
        bulletType="bullet",
        start="•",
        leftIndent=24,
    )


def make_table_cell(text: str, bold: bool = False, size: int = 9) -> Paragraph:
    style = ParagraphStyle(
        "TableCell",
        fontName="Times-Bold" if bold else "Times-Roman",
        fontSize=size,
        leading=size + 2,
        alignment=TA_LEFT,
        wordWrap="CJK",
    )
    return Paragraph(text.replace("&", "&amp;"), style)


def build_wrapped_table(rows: list[list[str]], col_widths: list[float], header: bool = True) -> Table:
    data = []
    for r_idx, row in enumerate(rows):
        data.append([make_table_cell(cell, bold=(header and r_idx == 0)) for cell in row])
    table = Table(data, colWidths=col_widths, repeatRows=1 if header else 0)
    table.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, 0), colors.lightgrey),
                ("FONTNAME", (0, 0), (-1, 0), "Times-Bold"),
                ("GRID", (0, 0), (-1, -1), 0.5, colors.black),
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
                ("ALIGN", (0, 0), (-1, -1), "LEFT"),
                ("LEFTPADDING", (0, 0), (-1, -1), 6),
                ("RIGHTPADDING", (0, 0), (-1, -1), 6),
                ("TOPPADDING", (0, 0), (-1, -1), 6),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
                ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, colors.whitesmoke]),
            ]
        )
    )
    return table


def build_pdf() -> Path:
    out = OUT_PATHS[0]
    out.parent.mkdir(parents=True, exist_ok=True)
    Path("/opt/cursor/artifacts").mkdir(parents=True, exist_ok=True)

    doc = SimpleDocTemplate(
        str(out),
        pagesize=letter,
        leftMargin=0.85 * inch,
        rightMargin=0.85 * inch,
        topMargin=0.85 * inch,
        bottomMargin=0.85 * inch,
    )

    styles = getSampleStyleSheet()
    cover_title = ParagraphStyle(
        "CoverTitle",
        parent=styles["Normal"],
        fontName="Times-Bold",
        fontSize=18,
        leading=24,
        alignment=TA_CENTER,
        spaceAfter=10,
    )
    cover_sub = ParagraphStyle(
        "CoverSub",
        parent=styles["Normal"],
        fontName="Times-Bold",
        fontSize=14,
        leading=18,
        alignment=TA_CENTER,
        spaceAfter=8,
    )
    cover_body = ParagraphStyle(
        "CoverBody",
        parent=styles["Normal"],
        fontName="Times-Roman",
        fontSize=12,
        leading=16,
        alignment=TA_CENTER,
        spaceAfter=6,
    )
    heading = ParagraphStyle(
        "Heading",
        parent=styles["Heading2"],
        fontName="Times-Bold",
        fontSize=13,
        leading=16,
        spaceBefore=10,
        spaceAfter=8,
    )
    subheading = ParagraphStyle(
        "SubHeading",
        parent=styles["Heading3"],
        fontName="Times-Bold",
        fontSize=12,
        leading=15,
        spaceBefore=8,
        spaceAfter=6,
    )
    body = ParagraphStyle(
        "Body",
        parent=styles["BodyText"],
        fontName="Times-Roman",
        fontSize=11,
        leading=15,
        alignment=TA_JUSTIFY,
        spaceAfter=6,
    )

    page_width = letter[0] - doc.leftMargin - doc.rightMargin

    revision_table = build_wrapped_table(
        [
            ["Version", "Description", "Author", "Date"],
            [
                "1.0",
                "This document contains External Interface Requirements of Deen Associate Management System",
                AUTHOR,
                DOC_DATE,
            ],
        ],
        [0.65 * inch, page_width - 2.95 * inch, 1.35 * inch, 0.95 * inch],
    )

    license_table = build_wrapped_table(
        [
            ["Software / Tool", "Type", "License Type", "Usage", "Remarks"],
            ["React.js", "Open Source", "MIT License", "Frontend Development", "Free to use"],
            [".NET 8 Web API", "Open Source", "MIT License", "Backend Development", "Secure and scalable API"],
            [
                "Microsoft SQL Server",
                "Commercial / Developer",
                "Microsoft Software License",
                "Database Storage",
                "Used for DAMS database",
            ],
            ["Tailwind CSS", "Open Source", "MIT License", "UI Styling", "For responsive design"],
            [
                "Visual Studio Code",
                "Open Source",
                "MIT License",
                "Code Editor",
                "Free development environment",
            ],
            ["Postman", "Freeware", "Free", "API Testing", "Backend testing"],
            [
                "Azure / Local Server",
                "Cloud / Local Hosting",
                "As per hosting plan",
                "Deployment Environment",
                "Production or development hosting",
            ],
        ],
        [1.2 * inch, 1.0 * inch, 1.15 * inch, 1.55 * inch, page_width - 4.9 * inch],
    )

    story = [
        Spacer(1, 1.4 * inch),
        Paragraph("EXTERNAL INTERFACE REQUIREMENTS", cover_title),
        Spacer(1, 0.15 * inch),
        Paragraph("FOR", cover_sub),
        Spacer(1, 0.1 * inch),
        Paragraph("Deen Associate Management System", cover_title),
        Spacer(1, 0.2 * inch),
        Paragraph("VERSION 1.0", cover_body),
        Spacer(1, 0.45 * inch),
        Paragraph("Proposed By", cover_body),
        Spacer(1, 0.1 * inch),
        Paragraph(AUTHOR, cover_sub),
        Spacer(1, 0.35 * inch),
        Paragraph(DOC_DATE, cover_body),
        PageBreak(),
        Paragraph("Revision History", heading),
        Spacer(1, 8),
        revision_table,
        PageBreak(),
        Paragraph("External Interface Requirements", heading),
        Paragraph(
            "This section describes the external interface requirements of the Deen Associate Management System "
            "(DAMS), including user interfaces, hardware interfaces, and licensing requirements.",
            body,
        ),
        Paragraph("User Interfaces", subheading),
        Paragraph(
            "The system provides clean and simple user interfaces with separate dashboards for administrators, "
            "super administrators, and clients. All modules such as Projects, Clients, Booking, Income, and "
            "Expenses are easily accessible from the main menu.",
            body,
        ),
        Spacer(1, 4),
        Paragraph("User Interface (Client)", subheading),
        bullet_list(
            [
                "Dashboard showing current bookings, project details, payment status, and notifications",
                "Project search and online booking request page",
                "Booking confirmation and booking history",
                "Payment receipts and pending dues alerts",
                "Profile management for updating personal information",
                "Notifications for booking, payment, or important notices",
                "Login, registration, and forgot password screens",
            ],
            body,
        ),
        Spacer(1, 4),
        Paragraph("Admin Interface", subheading),
        bullet_list(
            [
                "Dashboard displaying new booking requests, confirmed bookings, customers, and finance summary",
                "Project and unit management (add/update projects, units, status, and media)",
                "Customer management panel",
                "Booking request review and confirmed booking management",
                "Installment and payment management",
                "Employee management, attendance, tasks, and salary panel",
                "Expense and manual income management",
            ],
            body,
        ),
        Spacer(1, 4),
        Paragraph("Super Admin Interface", subheading),
        bullet_list(
            [
                "Full control over projects, units, staff, clients, bookings, payments, and finance",
                "Reporting and analytics for booking, payment, income, and expenses",
                "User role and profile management",
                "Role-based access management",
            ],
            body,
        ),
        Paragraph("Hardware Interfaces", subheading),
        Paragraph(
            "The system requires a standard computer or laptop with basic input devices like keyboard and mouse. "
            "A stable internet connection is required for accessing the online system.",
            body,
        ),
        bullet_list(
            [
                "Standard computer, laptop, or tablet with keyboard and mouse or touch input",
                "Minimum recommended configuration: Intel Core i5 or equivalent processor, 8 GB RAM, 256 GB SSD",
                "Stable internet connection for accessing the web-based system",
                "Supported web browsers such as Google Chrome, Microsoft Edge, or Mozilla Firefox",
                "Optional printer for printing booking application forms and payment receipts",
                "Local or cloud server for hosting the backend API and SQL Server database",
            ],
            body,
        ),
        Paragraph("Licensing Requirements", subheading),
        Paragraph(
            "The Deen Associate Management System will comply with software licenses used in development. "
            "Open source or free tools will be used to ensure legality and cost-effectiveness.",
            body,
        ),
        Spacer(1, 4),
        Paragraph("Licensing Details", subheading),
        bullet_list(
            [
                "Frontend: React.js MIT License (Open Source)",
                "Backend: .NET 8 Web API MIT License (Open Source)",
                "Database: Microsoft SQL Server (Commercial / Developer Edition)",
                "Authentication: JWT (JSON Web Token) (Open Standard)",
                "UI Styling: Tailwind CSS, Bootstrap Icons (Open Source)",
                "Development Tools: Visual Studio Code, Postman (Free to use)",
                "Hosting: Azure, local server, or compatible cloud hosting",
            ],
            body,
        ),
        Spacer(1, 8),
        Paragraph("Licensing Requirements Table", subheading),
        license_table,
    ]

    doc.build(story)

    for path in OUT_PATHS[1:]:
        shutil.copy2(out, path)

    print(f"Created {out}")
    return out


if __name__ == "__main__":
    build_pdf()
