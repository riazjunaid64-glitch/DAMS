"""Generate DAMS Domain Model (UML / Larman style): attributes + multiplicity, no operations."""

from __future__ import annotations

import math
from dataclasses import dataclass, field
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont
from reportlab.lib.pagesizes import letter
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen import canvas

ROOT = Path(__file__).resolve().parents[1]
OUT_PDF = ROOT / "docs" / "DAMS_Domain_Model.pdf"
OUT_PNG = ROOT / "docs" / "DAMS_Domain_Model.png"

IMG_W = 3200
LINE_H = 20
HEADER_H = 34
PAD = 12
ROW_GAP = 175
COL_X = (520, 1600, 2680)

FONT_REG = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
FONT_BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"

AUTHOR = "Muhammad Junaid Riaz"
DOC_DATE = "27th November, 2025"
SYSTEM = "Deen Associate Management System"


@dataclass
class ConceptClass:
    name: str
    attributes: list[str]
    col: int = 0
    width: int = field(default=0, init=False)
    height: int = field(default=0, init=False)
    x: int = field(default=0, init=False)
    y: int = field(default=0, init=False)


@dataclass(frozen=True)
class Association:
    a: str
    b: str
    mult_a: str
    mult_b: str
    label: str = ""


def load_font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(FONT_BOLD if bold else FONT_REG, size)


def measure_classes(classes: list[ConceptClass], draw: ImageDraw.ImageDraw) -> None:
    name_font = load_font(16, bold=True)
    body_font = load_font(12)
    min_w = 220
    for cls in classes:
        lines = [cls.name, *cls.attributes]
        max_w = max(
            int(draw.textlength(line, font=name_font if line == cls.name else body_font))
            for line in lines
        )
        cls.width = max(max_w + PAD * 2 + 16, min_w)
        rows = len(cls.attributes) if cls.attributes else 1
        cls.height = HEADER_H + rows * LINE_H + 18


def place_row(by_name: dict[str, ConceptClass], names: list[str], y: int) -> int:
    row = [by_name[n] for n in names]
    max_h = max(c.height for c in row)
    for cls in row:
        cls.x = COL_X[cls.col] - cls.width // 2
        cls.y = y
    return y + max_h + ROW_GAP


def box_rect(cls: ConceptClass) -> tuple[int, int, int, int]:
    return cls.x, cls.y, cls.x + cls.width, cls.y + cls.height


def draw_class(draw: ImageDraw.ImageDraw, cls: ConceptClass) -> tuple[int, int, int, int]:
    x0, y0 = cls.x, cls.y
    x1, y1 = x0 + cls.width, y0 + cls.height
    draw.rectangle((x0, y0, x1, y1), outline="black", width=2, fill="white")

    name_font = load_font(16, bold=True)
    body_font = load_font(12)
    cx = x0 + cls.width / 2
    nw = draw.textlength(cls.name, font=name_font)
    draw.text((cx - nw / 2, y0 + 7), cls.name, fill="black", font=name_font)

    div_y = y0 + HEADER_H
    draw.line((x0, div_y, x1, div_y), fill="black", width=2)

    y = div_y + 6
    if cls.attributes:
        for attr in cls.attributes:
            draw.text((x0 + PAD, y), attr, fill="black", font=body_font)
            y += LINE_H
    return (x0, y0, x1, y1)


def box_anchor(box: tuple[int, int, int, int], target: tuple[int, int, int, int]) -> tuple[int, int]:
    x0, y0, x1, y1 = box
    tx = (target[0] + target[2]) / 2
    ty = (target[1] + target[3]) / 2
    cx = (x0 + x1) / 2
    cy = (y0 + y1) / 2
    dx, dy = tx - cx, ty - cy
    if abs(dx) > abs(dy):
        return (int(x1 if dx > 0 else x0), int(cy))
    return (int(cx), int(y1 if dy > 0 else y0))


def point_in_box(px: float, py: float, box: tuple[int, int, int, int], margin: int = 6) -> bool:
    x0, y0, x1, y1 = box
    return x0 - margin <= px <= x1 + margin and y0 - margin <= py <= y1 + margin


def draw_label_bg(
    draw: ImageDraw.ImageDraw,
    x: float,
    y: float,
    text: str,
    font: ImageFont.FreeTypeFont,
    boxes: dict[str, tuple[int, int, int, int]],
) -> None:
    if not text:
        return
    tw = draw.textlength(text, font=font)
    th = font.size + 6
    lx, ly = x - tw / 2, y - th / 2
    for ox, oy in [(0, -16), (0, 16), (-20, 0), (20, 0), (-14, -14), (14, 14)]:
        cx, cy = lx + tw / 2 + ox, ly + th / 2 + oy
        if not any(point_in_box(cx, cy, b) for b in boxes.values()):
            lx += ox
            ly += oy
            break
    draw.rectangle((lx - 4, ly - 3, lx + tw + 4, ly + th + 3), fill="white", outline="white")
    draw.text((lx, ly), text, fill="black", font=font)


def draw_association(
    draw: ImageDraw.ImageDraw,
    a: tuple[int, int, int, int],
    b: tuple[int, int, int, int],
    mult_a: str,
    mult_b: str,
    label: str,
    boxes: dict[str, tuple[int, int, int, int]],
    *,
    draw_line: bool = True,
    draw_labels: bool = True,
) -> None:
    p1 = box_anchor(a, b)
    p2 = box_anchor(b, a)
    label_font = load_font(11, bold=True)
    mult_font = load_font(12, bold=True)
    name_font = load_font(11)

    if draw_line:
        draw.line((*p1, *p2), fill="black", width=2)

    if not draw_labels:
        return

    dx, dy = p2[0] - p1[0], p2[1] - p1[1]
    length = math.hypot(dx, dy) or 1.0
    nx, ny = -dy / length, dx / length

    for i, (t, mult) in enumerate(((0.12, mult_a), (0.88, mult_b))):
        if not mult:
            continue
        side = 1 if i == 0 else -1
        mx = p1[0] + dx * t + nx * 24 * side
        my = p1[1] + dy * t + ny * 24 * side
        draw_label_bg(draw, mx, my, mult, mult_font, boxes)

    if label:
        mx = p1[0] + dx * 0.5 + nx * 14
        my = p1[1] + dy * 0.5 + ny * 14
        draw_label_bg(draw, mx, my, label, name_font, boxes)


def build_classes() -> list[ConceptClass]:
    return [
        ConceptClass("User", ["fullName", "email", "role"], col=0),
        ConceptClass("Customer", ["fullName", "phone", "cnic", "email"], col=0),
        ConceptClass("BookingRequest", ["fullName", "phone", "status"], col=1),
        ConceptClass("Project", ["projectName", "location", "status"], col=2),
        ConceptClass("Unit", ["unitNumber", "price", "status"], col=2),
        ConceptClass("Booking", ["bookingReference", "status", "agreedSalePrice"], col=1),
        ConceptClass("Payment", ["amount", "type", "receiptNumber"], col=0),
        ConceptClass("Installment", ["sequenceNumber", "amount", "dueDate"], col=1),
        ConceptClass("Employee", ["fullName", "department", "salary"], col=1),
        ConceptClass("EmployeeSalary", ["amount", "payMonth", "payYear"], col=1),
        ConceptClass("Expense", ["category", "amount", "date"], col=0),
        ConceptClass("ManualRevenue", ["revenueType", "amount", "date"], col=2),
    ]


def build_associations() -> list[Association]:
    return [
        Association("User", "Customer", "1", "0..1", "has profile"),
        Association("User", "BookingRequest", "1", "0..*", "submits"),
        Association("Customer", "Booking", "1", "0..*", "places"),
        Association("BookingRequest", "Booking", "0..1", "0..1", "approves to"),
        Association("BookingRequest", "Unit", "0..*", "1", "requests"),
        Association("Booking", "Unit", "0..*", "1", "reserves"),
        Association("Booking", "Payment", "1", "0..*", "receives"),
        Association("Booking", "Installment", "1", "0..*", "has"),
        Association("Installment", "Payment", "1", "0..*", "paid by"),
        Association("Project", "Unit", "1", "0..*", "contains"),
        Association("Project", "Expense", "1", "0..*", "incurs"),
        Association("Project", "ManualRevenue", "1", "0..*", "records"),
        Association("Employee", "EmployeeSalary", "1", "0..*", "receives"),
        Association("EmployeeSalary", "Expense", "1", "1", "generates"),
    ]


def validate_no_overlap(classes: list[ConceptClass]) -> list[str]:
    issues: list[str] = []
    rects = [box_rect(c) for c in classes]
    for i, a in enumerate(rects):
        for j, b in enumerate(rects):
            if i >= j:
                continue
            if a[0] < b[2] and b[0] < a[2] and a[1] < b[3] and b[1] < a[3]:
                issues.append(f"{classes[i].name} overlaps {classes[j].name}")
    return issues


def create_diagram_image() -> Image.Image:
    img = Image.new("RGB", (IMG_W, 2400), "white")
    draw = ImageDraw.Draw(img)

    classes = build_classes()
    measure_classes(classes, draw)
    by_name = {c.name: c for c in classes}

    y = 55
    y = place_row(by_name, ["User", "BookingRequest", "Project"], y)
    y = place_row(by_name, ["Customer", "Booking", "Unit"], y)
    y = place_row(by_name, ["Payment", "Installment", "ManualRevenue"], y)
    y = place_row(by_name, ["Expense", "Employee"], y)
    place_row(by_name, ["EmployeeSalary"], y)

    canvas_h = max(c.y + c.height for c in classes) + 80
    if canvas_h > img.height:
        img = Image.new("RGB", (IMG_W, canvas_h), "white")
        draw = ImageDraw.Draw(img)

    boxes = {c.name: draw_class(draw, c) for c in classes}

    links = build_associations()
    for link in links:
        if link.a in boxes and link.b in boxes:
            draw_association(
                draw, boxes[link.a], boxes[link.b], link.mult_a, link.mult_b, link.label,
                boxes, draw_line=True, draw_labels=False,
            )
    for link in links:
        if link.a in boxes and link.b in boxes:
            draw_association(
                draw, boxes[link.a], boxes[link.b], link.mult_a, link.mult_b, link.label,
                boxes, draw_line=False, draw_labels=True,
            )

    issues = validate_no_overlap(classes)
    if issues:
        print("Layout warnings:")
        for issue in issues:
            print(f"  - {issue}")

    margin = 60
    min_x = max(min(c.x for c in classes) - margin, 0)
    min_y = max(min(c.y for c in classes) - margin, 0)
    max_x = min(max(c.x + c.width for c in classes) + margin, img.width)
    max_y = min(max(c.y + c.height for c in classes) + margin, img.height)
    return img.crop((min_x, min_y, max_x, max_y))


def create_cover_page(c: canvas.Canvas) -> None:
    w, h = letter
    c.setFont("Times-Bold", 18)
    c.drawCentredString(w / 2, h - 180, "Domain Model")
    c.setFont("Times-Bold", 16)
    c.drawCentredString(w / 2, h - 220, "FOR")
    c.setFont("Times-Bold", 18)
    c.drawCentredString(w / 2, h - 260, SYSTEM)
    c.setFont("Times-Roman", 13)
    c.drawCentredString(w / 2, h - 320, "VERSION 1.0")
    c.drawCentredString(w / 2, h - 380, "Prepared By")
    c.setFont("Times-Bold", 14)
    c.drawCentredString(w / 2, h - 420, AUTHOR)
    c.setFont("Times-Roman", 13)
    c.drawCentredString(w / 2, h - 480, DOC_DATE)


def create_pdf(img: Image.Image) -> None:
    OUT_PDF.parent.mkdir(parents=True, exist_ok=True)
    w, h = letter
    c = canvas.Canvas(str(OUT_PDF), pagesize=letter)

    create_cover_page(c)
    c.showPage()

    c.setFont("Times-Bold", 14)
    c.drawCentredString(w / 2, h - 48, "3.3 Domain Model")
    c.setFont("Times-Roman", 10)
    note = (
        "UML domain model (Larman): conceptual classes with key attributes, "
        "associations with multiplicity and names. No operations or navigability arrows."
    )
    c.drawCentredString(w / 2, h - 68, note)

    page_margin = 36
    max_w = w - 2 * page_margin
    max_h = h - 100
    img_w = max_w
    img_h = img_w * img.height / img.width
    if img_h > max_h:
        img_h = max_h
        img_w = img_h * img.width / img.height

    top = (h - 82 - img_h) / 2 + 8
    c.drawImage(ImageReader(img), (w - img_w) / 2, top, width=img_w, height=img_h)
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
