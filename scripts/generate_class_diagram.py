"""Generate DAMS class diagram PDF matching the sample UML layout."""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont
from reportlab.lib.pagesizes import letter
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen import canvas

ROOT = Path(__file__).resolve().parents[1]
OUT_PDF = ROOT / "docs" / "DAMS_Class_Diagram.pdf"
OUT_PNG = ROOT / "docs" / "DAMS_Class_Diagram.png"

IMG_W, IMG_H = 1430, 1293
BOX_W, BOX_H = 210, 96
NAME_H = 34

FONT_REG = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
FONT_BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"


def load_font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(FONT_BOLD if bold else FONT_REG, size)


def draw_uml_class(
    draw: ImageDraw.ImageDraw,
    center: tuple[int, int],
    name: str,
) -> tuple[int, int, int, int]:
    cx, cy = center
    x0 = cx - BOX_W // 2
    y0 = cy - BOX_H // 2
    x1 = x0 + BOX_W
    y1 = y0 + BOX_H
    draw.rectangle((x0, y0, x1, y1), outline="black", width=2, fill="white")
    draw.line((x0, y0 + NAME_H, x1, y0 + NAME_H), fill="black", width=2)
    draw.line((x0, y0 + NAME_H + 30, x1, y0 + NAME_H + 30), fill="black", width=2)

    font = load_font(20, bold=True)
    tw = draw.textlength(name, font=font)
    draw.text((cx - tw / 2, y0 + 8), name, fill="black", font=font)
    return (x0, y0, x1, y1)


def box_center(box: tuple[int, int, int, int]) -> tuple[int, int]:
    x0, y0, x1, y1 = box
    return ((x0 + x1) // 2, (y0 + y1) // 2)


def connect(
    draw: ImageDraw.ImageDraw,
    a: tuple[int, int, int, int],
    b: tuple[int, int, int, int],
) -> None:
    ax, ay = box_center(a)
    bx, by = box_center(b)
    dx, dy = bx - ax, by - ay
    if dx == 0 and dy == 0:
        return
    length = (dx * dx + dy * dy) ** 0.5
    pad = min(BOX_W, BOX_H) / 2 - 4
    ax += int(dx / length * pad)
    ay += int(dy / length * pad)
    bx -= int(dx / length * pad)
    by -= int(dy / length * pad)
    draw.line((ax, ay, bx, by), fill="black", width=2)


def create_diagram_image() -> Image.Image:
    img = Image.new("RGB", (IMG_W, IMG_H), "white")
    draw = ImageDraw.Draw(img)

    positions = {
        "Customer": (360, 110),
        "Role": (580, 110),
        "User": (780, 110),
        "BookingRequest": (1120, 110),
        "Booking": (360, 320),
        "Project": (1120, 320),
        "Payment": (250, 530),
        "Installment": (470, 530),
        "Unit": (1120, 530),
        "Admin": (780, 740),
        "Employee": (360, 950),
        "Expense": (780, 950),
        "ManualRevenue": (1120, 950),
    }

    boxes = {name: (pos[0], pos[1]) for name, pos in positions.items()}

    links = [
        ("User", "Role"),
        ("User", "Customer"),
        ("User", "BookingRequest"),
        ("User", "Booking"),
        ("User", "Project"),
        ("Customer", "Booking"),
        ("Booking", "Payment"),
        ("Booking", "Installment"),
        ("Project", "Unit"),
        ("Booking", "Unit"),
        ("Admin", "Project"),
        ("Admin", "Unit"),
        ("Admin", "Employee"),
        ("Admin", "Expense"),
        ("Admin", "ManualRevenue"),
        ("User", "Admin"),
    ]

    temp_boxes = {
        name: (
            cx - BOX_W // 2,
            cy - BOX_H // 2,
            cx + BOX_W // 2,
            cy + BOX_H // 2,
        )
        for name, (cx, cy) in boxes.items()
    }
    for a, b in links:
        connect(draw, temp_boxes[a], temp_boxes[b])

    for name, pos in boxes.items():
        draw_uml_class(draw, pos, name)

    return img


def create_cover_page(c: canvas.Canvas) -> None:
    w, h = letter
    c.setFont("Times-Bold", 18)
    c.drawCentredString(w / 2, h - 180, "Class Diagram")
    c.setFont("Times-Bold", 16)
    c.drawCentredString(w / 2, h - 220, "FOR")
    c.setFont("Times-Bold", 18)
    c.drawCentredString(w / 2, h - 260, "Deen Associate Management System")
    c.setFont("Times-Roman", 13)
    c.drawCentredString(w / 2, h - 320, "VERSION 1.0")
    c.drawCentredString(w / 2, h - 380, "Prepared By")
    c.setFont("Times-Bold", 14)
    c.drawCentredString(w / 2, h - 420, "Muhammad Junaid Riaz")
    c.setFont("Times-Roman", 13)
    c.drawCentredString(w / 2, h - 480, "27th June, 2026")


def create_pdf(img: Image.Image) -> None:
    OUT_PDF.parent.mkdir(parents=True, exist_ok=True)
    w, h = letter
    c = canvas.Canvas(str(OUT_PDF), pagesize=letter)

    create_cover_page(c)
    c.showPage()

    c.setFont("Times-Bold", 14)
    c.drawString(72, h - 72, "Class Diagram")

    img_w = 500
    img_h = img_w * img.height / img.width
    c.drawImage(ImageReader(img), (w - img_w) / 2, h - 92 - img_h, width=img_w, height=img_h)
    c.showPage()
    c.save()


def main() -> None:
    img = create_diagram_image()
    OUT_PNG.parent.mkdir(parents=True, exist_ok=True)
    img.save(OUT_PNG)
    create_pdf(img)
    print(f"Created {OUT_PDF}")


if __name__ == "__main__":
    main()
