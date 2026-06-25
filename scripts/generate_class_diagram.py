"""Generate detailed DAMS UML class diagram matching the sample format."""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont
from reportlab.lib.pagesizes import letter
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen import canvas

ROOT = Path(__file__).resolve().parents[1]
OUT_PDF = ROOT / "docs" / "DAMS_Class_Diagram.pdf"
OUT_PNG = ROOT / "docs" / "DAMS_Class_Diagram.png"

IMG_W, IMG_H = 2400, 1950
LINE_H = 22
HEADER_H = 32
PAD = 10
ROW_GAP = 95
COL_X = (430, 1200, 1970)

FONT_REG = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
FONT_BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"


@dataclass
class UmlClass:
    name: str
    attributes: list[str]
    methods: list[str]
    col: int = 0
    width: int = field(default=0, init=False)
    height: int = field(default=0, init=False)
    x: int = field(default=0, init=False)
    y: int = field(default=0, init=False)


def load_font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(FONT_BOLD if bold else FONT_REG, size)


def measure_classes(classes: list[UmlClass], draw: ImageDraw.ImageDraw) -> None:
    name_font = load_font(17, bold=True)
    body_font = load_font(13)
    min_w = 290

    for uml in classes:
        lines = [uml.name, *uml.attributes, *uml.methods]
        max_w = max(
            draw.textlength(line, font=name_font if line == uml.name else body_font)
            for line in lines
        )
        uml.width = max(int(max_w) + PAD * 2 + 16, min_w)
        attr_rows = max(len(uml.attributes), 1)
        meth_rows = max(len(uml.methods), 1)
        uml.height = HEADER_H + attr_rows * LINE_H + meth_rows * LINE_H + 18


def place_row(by_name: dict[str, UmlClass], names: list[str], y: int) -> int:
    row = [by_name[name] for name in names]
    max_h = max(c.height for c in row)
    for uml in row:
        uml.x = COL_X[uml.col] - uml.width // 2
        uml.y = y
    return y + max_h + ROW_GAP


def draw_class(draw: ImageDraw.ImageDraw, uml: UmlClass) -> tuple[int, int, int, int]:
    x0, y0 = uml.x, uml.y
    x1, y1 = x0 + uml.width, y0 + uml.height

    draw.rectangle((x0, y0, x1, y1), outline="black", width=2, fill="white")

    name_font = load_font(17, bold=True)
    body_font = load_font(13)
    cx = x0 + uml.width / 2
    nw = draw.textlength(uml.name, font=name_font)
    draw.text((cx - nw / 2, y0 + 7), uml.name, fill="black", font=name_font)

    attr_top = y0 + HEADER_H
    draw.line((x0, attr_top, x1, attr_top), fill="black", width=2)

    y = attr_top + 6
    if uml.attributes:
        for attr in uml.attributes:
            draw.text((x0 + PAD, y), attr, fill="black", font=body_font)
            y += LINE_H
    else:
        y += LINE_H

    meth_divider = attr_top + max(len(uml.attributes), 1) * LINE_H + 6
    draw.line((x0, meth_divider, x1, meth_divider), fill="black", width=2)

    y = meth_divider + 6
    if uml.methods:
        for method in uml.methods:
            draw.text((x0 + PAD, y), method, fill="black", font=body_font)
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
        return (int(x1 if dx > 0 else x0), int(cy))
    return (int(cx), int(y1 if dy > 0 else y0))


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

    label_font = load_font(13)
    if label_a:
        draw.text((p1[0] + 6, p1[1] - 18), label_a, fill="black", font=label_font)
    if label_b:
        draw.text((p2[0] - 34, p2[1] - 18), label_b, fill="black", font=label_font)


def build_classes() -> list[UmlClass]:
    return [
        UmlClass(
            "Customer",
            ["+int CustomerId", "+string FullName", "+string Phone", "+string CNIC"],
            ["+viewProfile()", "+editProfile()"],
            col=0,
        ),
        UmlClass(
            "User",
            ["+int UserId", "+string Username", "+string Password", "+string Email", "+string Role"],
            ["+login()", "+logout()", "+changePassword()"],
            col=1,
        ),
        UmlClass(
            "Project",
            ["+int ProjectId", "+string ProjectName", "+string Location", "+string Status"],
            ["+addProject()", "+updateProject()", "+viewProject()"],
            col=2,
        ),
        UmlClass(
            "Booking",
            ["+int BookingId", "+Date BookingDate", "+Date DueDate", "+string Status"],
            ["+searchAvail()", "+makeBooking()", "+confirmBooking()", "+cancelBooking()"],
            col=0,
        ),
        UmlClass(
            "BookingRequest",
            ["+int RequestId", "+string Description", "+string Status"],
            ["+submitRequest()", "+updateStatus()", "+collectFeedback()"],
            col=1,
        ),
        UmlClass(
            "Employee",
            ["+int EmployeeId", "+string TaskDetails", "+string Status"],
            ["+viewTasks()", "+assignWorkOrder()", "+updateTask()"],
            col=2,
        ),
        UmlClass(
            "Payment",
            ["+int PaymentId", "+float Amount", "+Date PaymentDate", "+string Method"],
            ["+processPayment()", "+generateReceipt()", "+viewHistory()"],
            col=0,
        ),
        UmlClass("Admin", [], [], col=1),
        UmlClass(
            "Unit",
            ["+int UnitId", "+string UnitType", "+float Price", "+string Status"],
            ["+addUnit()", "+updateUnit()", "+checkStatus()"],
            col=0,
        ),
        UmlClass(
            "EmployeeSalary",
            ["+int SalaryId", "+float Amount", "+string Status"],
            ["+defineStructure()", "+generatePayroll()"],
            col=1,
        ),
        UmlClass(
            "Expense",
            ["+int ExpenseId", "+string Category", "+float Amount"],
            ["+addExpense()", "+updateExpense()", "+viewExpense()"],
            col=2,
        ),
    ]


def create_diagram_image() -> Image.Image:
    img = Image.new("RGB", (IMG_W, IMG_H), "white")
    draw = ImageDraw.Draw(img)

    classes = build_classes()
    measure_classes(classes, draw)
    by_name = {c.name: c for c in classes}

    y = place_row(by_name, ["Customer", "User", "Project"], 50)
    y = place_row(by_name, ["Booking", "BookingRequest", "Employee"], y)
    y = place_row(by_name, ["Payment", "Admin"], y)
    place_row(by_name, ["Unit", "EmployeeSalary", "Expense"], y)

    boxes = {c.name: (c.x, c.y, c.x + c.width, c.y + c.height) for c in classes}

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
        draw_link(draw, boxes[a], boxes[b], la, lb, dotted)

    for c in classes:
        draw_class(draw, c)

    return crop_to_content(img, classes)


def crop_to_content(img: Image.Image, classes: list[UmlClass], margin: int = 50) -> Image.Image:
    min_x = max(min(c.x for c in classes) - margin, 0)
    min_y = max(min(c.y for c in classes) - margin, 0)
    max_x = min(max(c.x + c.width for c in classes) + margin, img.width)
    max_y = min(max(c.y + c.height for c in classes) + margin, img.height)
    return img.crop((min_x, min_y, max_x, max_y))


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

    img_w = 540
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
