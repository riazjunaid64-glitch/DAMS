"""Create labeled placeholder screenshots for admin-only manual figures."""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "docs" / "user-manual-screenshots"

PLACEHOLDERS = [
    (
        "07-project-overview.png",
        "Project Detail — Overview",
        "Shared (Guest / Client / Admin)",
        [
            "Overview, Units, and Media tabs",
            "Project description, location, and status",
            "Delivery timeline with current stage",
        ],
    ),
    (
        "08-project-units.png",
        "Project Detail — Units",
        "Shared (Guest / Client / Admin)",
        [
            "Search and filter units by type, status, floor",
            "Unit cards with image, status badge, floor, and area",
        ],
    ),
    (
        "09-project-media.png",
        "Project Detail — Media",
        "Shared (Guest / Client / Admin)",
        [
            "Gallery of uploaded project images",
            "File name, size, and dimensions shown on each card",
        ],
    ),
    (
        "10-my-projects.png",
        "My Projects",
        "Client only",
        [
            "List of confirmed bookings for the logged-in customer",
            "Booking reference, unit, payment progress, and status",
        ],
    ),
    (
        "11-booking-detail-admin.png",
        "Confirmed Booking Detail",
        "Admin only",
        [
            "Summary cards: sale price, booking amount, installment pool",
            "Regenerate Installment Plan form",
            "Print Application Form and Cancel Booking actions",
        ],
    ),
    (
        "12-installment-schedule.png",
        "Installment Schedule",
        "Admin only",
        [
            "Table of due dates, amounts, paid/remaining balances",
            "Record Payment action on each installment row",
        ],
    ),
    (
        "13-record-payment-modal.png",
        "Record Installment Payment",
        "Admin only",
        [
            "Amount, payment method, reference, date, and notes",
            "Records payment and generates receipt automatically",
        ],
    ),
    (
        "14-application-form.png",
        "Application Form (Print / PDF)",
        "Admin (print) · Client (view own booking)",
        [
            "Official booking application with customer and unit details",
            "Print / Save as PDF from browser",
        ],
    ),
    (
        "15-payment-receipt.png",
        "Payment Receipt",
        "Admin (record) · Client (view own payments)",
        [
            "Receipt number, amount in words, payment type and mode",
            "Linked to installment or booking amount payment",
        ],
    ),
    (
        "16-employees-team.png",
        "Employees — Team",
        "Admin only",
        [
            "Search and filter employees",
            "Add Employee and View Details actions",
        ],
    ),
    (
        "17-employees-attendance.png",
        "Employees — Attendance",
        "Admin only",
        [
            "Mark present/absent by date for all active employees",
            "Save individual rows or Save All",
        ],
    ),
    (
        "18-employees-salaries.png",
        "Employees — Salaries",
        "Admin only",
        [
            "Monthly salary sheet with paid checkbox",
            "View Receipt after salary payment",
        ],
    ),
    (
        "19-finance-revenue.png",
        "Finance Dashboard — Revenue",
        "Admin only",
        [
            "Summary cards: revenue, expenses, profit, outstanding",
            "Revenue table with payment and manual revenue sources",
        ],
    ),
    (
        "20-add-manual-revenue.png",
        "Add Manual Revenue",
        "Admin only",
        [
            "Project, revenue type, amount, date, reference, description",
        ],
    ),
    (
        "21-finance-expenses.png",
        "Finance Dashboard — Expenses",
        "Admin only",
        [
            "Expense categories such as Salary and Material",
            "Edit and Delete actions on manual expense rows",
        ],
    ),
]


def load_font(size: int, bold: bool = False):
    candidates = [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" if bold else "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf" if bold else "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
    ]
    for path in candidates:
        try:
            return ImageFont.truetype(path, size)
        except OSError:
            continue
    return ImageFont.load_default()


def draw_placeholder(filename: str, title: str, role: str, bullets: list[str]) -> None:
    width, height = 1280, 720
    img = Image.new("RGB", (width, height), "#0b0b12")
    draw = ImageDraw.Draw(img)
    title_font = load_font(34, bold=True)
    role_font = load_font(22, bold=True)
    body_font = load_font(20)
    small_font = load_font(16)

    draw.rounded_rectangle((40, 40, width - 40, height - 40), radius=24, outline="#6366f1", width=2, fill="#12121c")
    draw.text((72, 72), "DeenAssociate — DAMS", fill="#a5b4fc", font=small_font)
    draw.text((72, 110), title, fill="#ffffff", font=title_font)

    badge_color = "#312e81" if "Admin" in role and "Client" not in role else "#1e3a5f"
    badge_text = role
    badge_box = draw.textbbox((72, 170), badge_text, font=role_font)
    pad_x, pad_y = 14, 8
    draw.rounded_rectangle(
        (
            badge_box[0] - pad_x,
            badge_box[1] - pad_y,
            badge_box[2] + pad_x,
            badge_box[3] + pad_y,
        ),
        radius=12,
        fill=badge_color,
    )
    draw.text((72, 170), badge_text, fill="#e0e7ff", font=role_font)

    y = 240
    draw.text((72, y), "Key elements on this screen:", fill="#cbd5e1", font=body_font)
    y += 42
    for bullet in bullets:
        draw.text((92, y), f"• {bullet}", fill="#94a3b8", font=body_font)
        y += 34

    draw.text(
        (72, height - 90),
        "Replace this placeholder with your live screenshot if needed.",
        fill="#64748b",
        font=small_font,
    )
    img.save(OUT / filename, format="PNG")


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for item in PLACEHOLDERS:
        draw_placeholder(*item)


if __name__ == "__main__":
    main()
