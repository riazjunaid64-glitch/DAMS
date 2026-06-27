"""Generate DAMS Domain Model diagram PDF matching Zostel Inn sample style."""

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

IMG_W = 1600
IMG_H = 1100
BOX_H = 46
BOX_PAD_X = 22
MIN_BOX_W = 148
FONT_SIZE = 17
LINE_W = 2

FONT_BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"

AUTHOR = "Muhammad Junaid Riaz"
DOC_DATE = "27th November, 2025"
SYSTEM = "Deen Associate Management System"


@dataclass
class DomainEntity:
    name: str
    x: int
    y: int
    width: int = field(default=0, init=False)
    height: int = BOX_H

    def rect(self) -> tuple[int, int, int, int]:
        return self.x, self.y, self.x + self.width, self.y + self.height


@dataclass(frozen=True)
class DomainLink:
    a: str
    b: str


def load_font(size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(FONT_BOLD, size)


def measure_entities(entities: dict[str, DomainEntity], draw: ImageDraw.ImageDraw) -> None:
    font = load_font(FONT_SIZE)
    for ent in entities.values():
        tw = draw.textlength(ent.name, font=font)
        ent.width = max(int(tw) + BOX_PAD_X * 2, MIN_BOX_W)


def center_entity(ent: DomainEntity, cx: int) -> None:
    ent.x = cx - ent.width // 2


def box_anchor(
    box: tuple[int, int, int, int],
    target: tuple[int, int, int, int],
) -> tuple[int, int]:
    x0, y0, x1, y1 = box
    tx = (target[0] + target[2]) / 2
    ty = (target[1] + target[3]) / 2
    cx = (x0 + x1) / 2
    cy = (y0 + y1) / 2
    dx = tx - cx
    dy = ty - cy
    if abs(dx) > abs(dy):
        return (int(x1 if dx > 0 else x0), int(cy))
    return (int(cx), int(y1 if dy > 0 else y0))


def draw_entity(draw: ImageDraw.ImageDraw, ent: DomainEntity) -> tuple[int, int, int, int]:
    x0, y0, x1, y1 = ent.rect()
    draw.rectangle((x0, y0, x1, y1), fill="black", outline="black", width=LINE_W)
    font = load_font(FONT_SIZE)
    tw = draw.textlength(ent.name, font=font)
    draw.text((x0 + (ent.width - tw) / 2, y0 + (ent.height - FONT_SIZE) / 2 - 2), ent.name, fill="white", font=font)
    return (x0, y0, x1, y1)


def draw_link(
    draw: ImageDraw.ImageDraw,
    a: tuple[int, int, int, int],
    b: tuple[int, int, int, int],
) -> None:
    p1 = box_anchor(a, b)
    p2 = box_anchor(b, a)
    draw.line((*p1, *p2), fill="black", width=LINE_W)


def build_entities() -> dict[str, DomainEntity]:
    """Hand-tuned layout mirroring sample domain model hierarchy."""
    cx = IMG_W // 2
    entities = {
        "User": DomainEntity("User", 0, 70),
        "Customer": DomainEntity("Customer", 0, 190),
        "BookingRequest": DomainEntity("Booking Request", 0, 190),
        "Booking": DomainEntity("Booking", 0, 310),
        "Unit": DomainEntity("Unit", 0, 430),
        "Payment": DomainEntity("Payment", 0, 430),
        "Installment": DomainEntity("Installment", 0, 430),
        "Project": DomainEntity("Project", 0, 550),
        "Employee": DomainEntity("Employee", 0, 670),
        "EmployeeSalary": DomainEntity("Employee Salary", 0, 790),
        "Expense": DomainEntity("Expense", 0, 790),
        "ManualRevenue": DomainEntity("Manual Revenue", 0, 790),
    }

    center_entity(entities["User"], cx)
    center_entity(entities["Customer"], cx - 300)
    center_entity(entities["BookingRequest"], cx + 300)
    center_entity(entities["Booking"], cx)
    center_entity(entities["Unit"], cx - 300)
    center_entity(entities["Payment"], cx)
    center_entity(entities["Installment"], cx + 300)
    center_entity(entities["Project"], cx - 300)
    center_entity(entities["Employee"], cx)
    center_entity(entities["EmployeeSalary"], cx)
    center_entity(entities["Expense"], cx - 300)
    center_entity(entities["ManualRevenue"], cx + 300)
    return entities


def build_links() -> list[DomainLink]:
    return [
        DomainLink("User", "Customer"),
        DomainLink("User", "BookingRequest"),
        DomainLink("Customer", "Booking"),
        DomainLink("BookingRequest", "Booking"),
        DomainLink("BookingRequest", "Unit"),
        DomainLink("Booking", "Unit"),
        DomainLink("Booking", "Payment"),
        DomainLink("Booking", "Installment"),
        DomainLink("Payment", "Installment"),
        DomainLink("Unit", "Project"),
        DomainLink("Project", "Employee"),
        DomainLink("Project", "Expense"),
        DomainLink("Project", "ManualRevenue"),
        DomainLink("Employee", "EmployeeSalary"),
        DomainLink("EmployeeSalary", "Expense"),
    ]


def validate_no_overlap(entities: dict[str, DomainEntity]) -> list[str]:
    issues: list[str] = []
    rects = list(entities.items())
    for i, (na, a) in enumerate(rects):
        for nb, b in rects[i + 1 :]:
            ar, br = a.rect(), b.rect()
            if ar[0] < br[2] and br[0] < ar[2] and ar[1] < br[3] and br[1] < ar[3]:
                issues.append(f"{na} overlaps {nb}")
    return issues


def crop_to_content(img: Image.Image, entities: dict[str, DomainEntity], margin: int = 70) -> Image.Image:
    rects = [e.rect() for e in entities.values()]
    min_x = max(min(r[0] for r in rects) - margin, 0)
    min_y = max(min(r[1] for r in rects) - margin, 0)
    max_x = min(max(r[2] for r in rects) + margin, img.width)
    max_y = min(max(r[3] for r in rects) + margin, img.height)
    return img.crop((min_x, min_y, max_x, max_y))


def create_diagram_image() -> Image.Image:
    img = Image.new("RGB", (IMG_W, IMG_H), "white")
    draw = ImageDraw.Draw(img)

    entities = build_entities()
    measure_entities(entities, draw)

    # Re-center after width measurement
    cx = IMG_W // 2
    center_entity(entities["User"], cx)
    center_entity(entities["Customer"], cx - 300)
    center_entity(entities["BookingRequest"], cx + 300)
    center_entity(entities["Booking"], cx)
    center_entity(entities["Unit"], cx - 300)
    center_entity(entities["Payment"], cx)
    center_entity(entities["Installment"], cx + 300)
    center_entity(entities["Project"], cx - 300)
    center_entity(entities["Employee"], cx)
    center_entity(entities["EmployeeSalary"], cx)
    center_entity(entities["Expense"], cx - 300)
    center_entity(entities["ManualRevenue"], cx + 300)

    boxes: dict[str, tuple[int, int, int, int]] = {}
    for name, ent in entities.items():
        boxes[name] = draw_entity(draw, ent)

    for link in build_links():
        if link.a in boxes and link.b in boxes:
            draw_link(draw, boxes[link.a], boxes[link.b])

    issues = validate_no_overlap(entities)
    if issues:
        print("Layout warnings:")
        for issue in issues:
            print(f"  - {issue}")

    return crop_to_content(img, entities)


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

    page_margin = 48
    max_w = w - 2 * page_margin
    max_h = h - 110
    img_w = max_w
    img_h = img_w * img.height / img.width
    if img_h > max_h:
        img_h = max_h
        img_w = img_h * img.width / img.height

    top = (h - 70 - img_h) / 2 + 10
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
