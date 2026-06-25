"""Generate detailed DAMS UML class diagram matching the sample format."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont
from reportlab.lib.pagesizes import letter
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen import canvas

ROOT = Path(__file__).resolve().parents[1]
OUT_PDF = ROOT / "docs" / "DAMS_Class_Diagram.pdf"
OUT_PNG = ROOT / "docs" / "DAMS_Class_Diagram.png"

IMG_W, IMG_H = 2100, 1700
BOX_W = 250
LINE_H = 20
HEADER_H = 30
PAD = 8

FONT_REG = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
FONT_BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
FONT_ITALIC = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Italic.ttf"


@dataclass
class UmlClass:
    name: str
    attributes: list[str]
    methods: list[str]
    center: tuple[int, int]


def load_font(size: int, bold: bool = False, italic: bool = False) -> ImageFont.FreeTypeFont:
    if bold:
        return ImageFont.truetype(FONT_BOLD, size)
    if italic:
        return ImageFont.truetype(FONT_ITALIC, size)
    return ImageFont.truetype(FONT_REG, size)


def class_height(uml: UmlClass) -> int:
    sections = 1 + (1 if uml.attributes else 0) + (1 if uml.methods else 0)
    attr_h = max(len(uml.attributes), 1) * LINE_H if uml.attributes else LINE_H
    meth_h = max(len(uml.methods), 1) * LINE_H if uml.methods else LINE_H
    if not uml.attributes and not uml.methods:
        return HEADER_H + attr_h + meth_h
    return HEADER_H + attr_h + meth_h


def draw_class(draw: ImageDraw.ImageDraw, uml: UmlClass) -> tuple[int, int, int, int]:
    cx, cy = uml.center
    h = class_height(uml)
    x0 = cx - BOX_W // 2
    y0 = cy
    x1 = x0 + BOX_W
    y1 = y0 + h

    draw.rectangle((x0, y0, x1, y1), outline="black", width=2, fill="white")

    name_font = load_font(18, bold=True)
    body_font = load_font(15)
    nw = draw.textlength(uml.name, font=name_font)
    draw.text((cx - nw / 2, y0 + 6), uml.name, fill="black", font=name_font)

    y = y0 + HEADER_H
    draw.line((x0, y, x1, y), fill="black", width=2)
    y += 4

    if uml.attributes:
        for attr in uml.attributes:
            draw.text((x0 + PAD, y), attr, fill="black", font=body_font)
            y += LINE_H
    else:
        y += LINE_H

    draw.line((x0, y, x1, y), fill="black", width=2)
    y += 4

    if uml.methods:
        for method in uml.methods:
            draw.text((x0 + PAD, y), method, fill="black", font=body_font)
            y += LINE_H
    else:
        y += LINE_H

    return (x0, y0, x1, y1)


def box_anchor(box: tuple[int, int, int, int], target: tuple[int, int, int, int]) -> tuple[int, int]:
    x0, y0, x1, y1 = box
    tx = (target[0] + target[2]) / 2
    ty = (target[1] + target[3]) / 2
    cx = (x0 + x1) / 2
    cy = (y0 + y1) / 2
    dx = tx - cx
    dy = ty - cy
    if abs(dx) > abs(dy):
        return (x1 if dx > 0 else x0, cy)
    return (cx, y1 if dy > 0 else y0)


def draw_link(
    draw: ImageDraw.ImageDraw,
    a: tuple[int, int, int, int],
    b: tuple[int, int, int, int],
    label_a: str = "",
    label_b: str = "",
    dotted: bool = False,
) -> None:
    p1 = box_anchor(a, b)
    p2 = box_anchor(b, a)
    if dotted:
        x0, y0 = p1
        x1, y1 = p2
        dist = ((x1 - x0) ** 2 + (y1 - y0) ** 2) ** 0.5
        dash = 10
        steps = max(int(dist / dash), 1)
        for i in range(0, steps, 2):
            t1 = i / steps
            t2 = min((i + 1) / steps, 1)
            draw.line(
                (x0 + (x1 - x0) * t1, y0 + (y1 - y0) * t1, x0 + (x1 - x0) * t2, y0 + (y1 - y0) * t2),
                fill="black",
                width=2,
            )
    else:
        draw.line((*p1, *p2), fill="black", width=2)

    label_font = load_font(14)
    if label_a:
        draw.text((p1[0] + 4, p1[1] - 16), label_a, fill="black", font=label_font)
    if label_b:
        draw.text((p2[0] - 28, p2[1] - 16), label_b, fill="black", font=label_font)


def build_classes() -> list[UmlClass]:
    return [
        UmlClass(
            "User",
            ["+int UserId", "+string Username", "+string Password", "+string Email", "+string Role"],
            ["+login()", "+logout()", "+changePassword()"],
            (1050, 60),
        ),
        UmlClass(
            "Customer",
            ["+int CustomerId", "+string FullName", "+string PhoneNumber", "+string ProfilePicture"],
            ["+viewProfile()", "+editProfile()"],
            (620, 60),
        ),
        UmlClass(
            "Booking",
            ["+int BookingId", "+Date BookingDate", "+Date DueDate", "+string Status"],
            ["+searchAvailability()", "+makeBooking()", "+confirmBooking()", "+cancelBooking()"],
            (620, 430),
        ),
        UmlClass(
            "BookingRequest",
            ["+int RequestId", "+string Description", "+string Status"],
            ["+submitRequest()", "+updateStatus()", "+collectFeedback()"],
            (1050, 430),
        ),
        UmlClass(
            "Project",
            ["+int ProjectId", "+string ProjectName", "+string Location", "+string Status"],
            ["+addProject()", "+updateProject()", "+viewProject()"],
            (1480, 60),
        ),
        UmlClass(
            "Employee",
            ["+int EmployeeId", "+string TaskDetails", "+string Status"],
            ["+viewTasks()", "+assignWorkOrder()", "+updateTask()"],
            (1780, 430),
        ),
        UmlClass(
            "Payment",
            ["+int PaymentId", "+float Amount", "+Date PaymentDate", "+string Method"],
            ["+processPayment()", "+generateReceipt()", "+viewHistory()"],
            (620, 820),
        ),
        UmlClass("Admin", [], [], (1050, 980)),
        UmlClass(
            "Unit",
            ["+int UnitId", "+string UnitType", "+float Price", "+string Status"],
            ["+addUnit()", "+updateUnit()", "+checkStatus()"],
            (620, 1240),
        ),
        UmlClass(
            "EmployeeSalary",
            ["+int SalaryId", "+float Amount", "+string Status"],
            ["+defineStructure()", "+generatePayroll()"],
            (1050, 1240),
        ),
        UmlClass(
            "Expense",
            ["+int ExpenseId", "+string Category", "+float Amount"],
            ["+addExpense()", "+updateExpense()", "+viewExpense()"],
            (1480, 1240),
        ),
    ]


def create_diagram_image() -> Image.Image:
    img = Image.new("RGB", (IMG_W, IMG_H), "white")
    draw = ImageDraw.Draw(img)

    classes = build_classes()
    boxes = {c.name: (c.center, class_height(c)) for c in classes}
    temp_boxes = {}
    for c in classes:
        cx, cy = c.center
        h = class_height(c)
        temp_boxes[c.name] = (cx - BOX_W // 2, cy, cx + BOX_W // 2, cy + h)

    links = [
        ("User", "Customer", "1", "1", False),
        ("User", "Booking", "1", "0..*", False),
        ("User", "BookingRequest", "1", "0..*", False),
        ("User", "Project", "1", "0..1", False),
        ("User", "Employee", "1", "0..1", False),
        ("Booking", "Payment", "1", "1", False),
        ("Admin", "Unit", "", "", False),
        ("Admin", "EmployeeSalary", "", "", False),
        ("Admin", "Expense", "", "", False),
        ("Admin", "Project", "", "", True),
        ("Admin", "Payment", "", "", True),
    ]

    for a, b, la, lb, dotted in links:
        draw_link(draw, temp_boxes[a], temp_boxes[b], la, lb, dotted)

    for c in classes:
        draw_class(draw, c)

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
    c.drawString(72, h - 48, "Class Diagram")

    img_w = 520
    img_h = img_w * img.height / img.width
    top = h - 70 - img_h
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
