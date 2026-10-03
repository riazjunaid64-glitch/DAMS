"""Generate detailed DAMS UML class diagram — no overlapping labels or boxes."""

from __future__ import annotations

import math
from dataclasses import dataclass, field
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont
from reportlab.lib.pagesizes import letter
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen import canvas

ROOT = Path(__file__).resolve().parents[1]
OUT_PDF = ROOT / "docs" / "DAMS_Class_Diagram.pdf"
OUT_PNG = ROOT / "docs" / "DAMS_Class_Diagram.png"

IMG_W = 3000
LINE_H = 21
HEADER_H = 36
PAD = 12
ROW_GAP = 170
COL_X = (500, 1500, 2500)
ADMIN_ROW_GAP = 100

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
    name_font = load_font(16, bold=True)
    body_font = load_font(12)
    min_w = 300

    for uml in classes:
        lines = [uml.name, *uml.attributes, *uml.methods]
        max_w = max(
            int(draw.textlength(line, font=name_font if line == uml.name else body_font))
            for line in lines
        )
        uml.width = max(max_w + PAD * 2 + 20, min_w)
        attr_rows = len(uml.attributes) if uml.attributes else 1
        meth_rows = len(uml.methods) if uml.methods else 1
        uml.height = HEADER_H + attr_rows * LINE_H + meth_rows * LINE_H + 22


def place_row(by_name: dict[str, UmlClass], names: list[str], y: int) -> int:
    row = [by_name[n] for n in names]
    max_h = max(c.height for c in row)
    for uml in row:
        uml.x = COL_X[uml.col] - uml.width // 2
        uml.y = y
    return y + max_h + ROW_GAP


def box_rect(uml: UmlClass) -> tuple[int, int, int, int]:
    return uml.x, uml.y, uml.x + uml.width, uml.y + uml.height


def point_in_box(px: float, py: float, box: tuple[int, int, int, int], margin: int = 8) -> bool:
    x0, y0, x1, y1 = box
    return x0 - margin <= px <= x1 + margin and y0 - margin <= py <= y1 + margin


def draw_class(draw: ImageDraw.ImageDraw, uml: UmlClass) -> tuple[int, int, int, int]:
    x0, y0 = uml.x, uml.y
    x1, y1 = x0 + uml.width, y0 + uml.height

    draw.rectangle((x0, y0, x1, y1), outline="black", width=2, fill="white")

    name_font = load_font(16, bold=True)
    body_font = load_font(12)
    cx = x0 + uml.width / 2
    nw = draw.textlength(uml.name, font=name_font)
    draw.text((cx - nw / 2, y0 + 8), uml.name, fill="black", font=name_font)

    attr_top = y0 + HEADER_H
    draw.line((x0, attr_top, x1, attr_top), fill="black", width=2)

    y = attr_top + 7
    if uml.attributes:
        for attr in uml.attributes:
            draw.text((x0 + PAD, y), attr, fill="black", font=body_font)
            y += LINE_H
    else:
        y += LINE_H

    meth_divider = attr_top + max(len(uml.attributes), 1) * LINE_H + 8
    draw.line((x0, meth_divider, x1, meth_divider), fill="black", width=2)

    y = meth_divider + 7
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


def draw_label_with_bg(
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

    # Nudge label away from any class box (try all four directions)
    nudges = [(0, -18), (0, 18), (-24, 0), (24, 0), (-18, -18), (18, 18)]
    for ox, oy in nudges:
        cx, cy = lx + tw / 2 + ox, ly + th / 2 + oy
        if not any(point_in_box(cx, cy, b, margin=6) for b in boxes.values()):
            lx += ox
            ly += oy
            break

    draw.rectangle((lx - 5, ly - 3, lx + tw + 5, ly + th + 3), fill="white", outline="white")
    draw.text((lx, ly), text, fill="black", font=font)


def draw_link(
    draw: ImageDraw.ImageDraw,
    a: tuple[int, int, int, int],
    b: tuple[int, int, int, int],
    label_a: str,
    label_b: str,
    boxes: dict[str, tuple[int, int, int, int]],
    dotted: bool = False,
    *,
    draw_line: bool = True,
    draw_labels: bool = True,
) -> None:
    p1 = box_anchor(a, b)
    p2 = box_anchor(b, a)
    label_font = load_font(12, bold=True)

    if draw_line:
        if dotted:
            x0, y0 = p1
            x1, y1 = p2
            dist = math.hypot(x1 - x0, y1 - y0)
            dash, gap = 10, 7
            steps = max(int(dist / (dash + gap)), 1)
            for i in range(0, steps, 2):
                t1 = i / steps
                t2 = min((i + 1) / steps, 1)
                draw.line(
                    (
                        x0 + (x1 - x0) * t1,
                        y0 + (y1 - y0) * t1,
                        x0 + (x1 - x0) * t2,
                        y0 + (y1 - y0) * t2,
                    ),
                    fill="black",
                    width=2,
                )
        else:
            draw.line((*p1, *p2), fill="black", width=2)

    if not draw_labels:
        return

    dx, dy = p2[0] - p1[0], p2[1] - p1[1]
    length = math.hypot(dx, dy) or 1.0
    nx, ny = -dy / length, dx / length

    if length < 280:
        positions = ((0.10, label_a), (0.90, label_b))
        offset_base = 34
    else:
        positions = ((0.14, label_a), (0.86, label_b))
        offset_base = 28

    for i, (t, label) in enumerate(positions):
        if not label:
            continue
        side = 1 if i == 0 else -1
        mx = p1[0] + dx * t + nx * offset_base * side
        my = p1[1] + dy * t + ny * offset_base * side
        draw_label_with_bg(draw, mx, my, label, label_font, boxes)


def build_classes() -> list[UmlClass]:
    return [
        UmlClass(
            "Customer",
            [
                "+int Id",
                "+string FullName",
                "+string Phone",
                "+string CNIC",
                "+string Email",
            ],
            ["+viewProfile()", "+editProfile()"],
            col=0,
        ),
        UmlClass(
            "User",
            [
                "+int UserId",
                "+int RoleId",
                "+string FullName",
                "+string Email",
                "+string Password",
            ],
            ["+login()", "+logout()", "+refreshToken()"],
            col=1,
        ),
        UmlClass(
            "Project",
            [
                "+int Id",
                "+string ProjectName",
                "+string Location",
                "+ProjectStatus Status",
            ],
            ["+addProject()", "+updateProject()", "+viewProject()"],
            col=2,
        ),
        UmlClass(
            "Booking",
            [
                "+int Id",
                "+string BookingReference",
                "+int CustomerId",
                "+int UnitId",
                "+BookingStatus Status",
                "+decimal AgreedSalePrice",
            ],
            ["+createBooking()", "+cancelBooking()", "+recordPayment()"],
            col=0,
        ),
        UmlClass(
            "BookingRequest",
            [
                "+int Id",
                "+int UnitId",
                "+string FullName",
                "+BookingRequestStatus Status",
            ],
            ["+submitRequest()", "+approve()", "+reject()"],
            col=1,
        ),
        UmlClass(
            "Unit",
            [
                "+int Id",
                "+int ProjectId",
                "+string UnitNumber",
                "+decimal Price",
                "+UnitStatus Status",
            ],
            ["+addUnit()", "+updateUnit()", "+checkStatus()"],
            col=2,
        ),
        UmlClass(
            "Payment",
            [
                "+int Id",
                "+int BookingId",
                "+PaymentType Type",
                "+decimal Amount",
                "+string ReceiptNumber",
            ],
            ["+processPayment()", "+generateReceipt()"],
            col=0,
        ),
        UmlClass(
            "Installment",
            [
                "+int Id",
                "+int BookingId",
                "+int SequenceNumber",
                "+decimal Amount",
                "+InstallmentStatus Status",
            ],
            ["+recordPayment()", "+getSchedule()"],
            col=1,
        ),
        UmlClass(
            "Employee",
            [
                "+int Id",
                "+string FullName",
                "+string Department",
                "+decimal Salary",
                "+EmployeeStatus Status",
            ],
            ["+assignTask()", "+recordAttendance()"],
            col=2,
        ),
        UmlClass(
            "EmployeeSalary",
            [
                "+int Id",
                "+int EmployeeId",
                "+decimal Amount",
                "+int PayMonth",
                "+int ExpenseId",
            ],
            ["+generateSalary()", "+viewReceipt()"],
            col=0,
        ),
        UmlClass(
            "Expense",
            [
                "+int Id",
                "+string Category",
                "+decimal Amount",
                "+int ProjectId",
            ],
            ["+addExpense()", "+updateExpense()"],
            col=1,
        ),
        UmlClass(
            "ManualRevenue",
            [
                "+int Id",
                "+string RevenueType",
                "+decimal Amount",
                "+int ProjectId",
            ],
            ["+addRevenue()", "+updateRevenue()"],
            col=2,
        ),
        UmlClass("Admin", ["«extends User»"], [], col=1),
    ]


def build_links() -> list[tuple[str, str, str, str, bool]]:
    return [
        ("User", "Customer", "1", "0..1", False),
        ("Customer", "Booking", "1", "0..*", False),
        ("Unit", "Booking", "1", "0..*", False),
        ("Project", "Unit", "1", "0..*", False),
        ("BookingRequest", "Booking", "0..1", "1", False),
        ("Booking", "Installment", "1", "0..*", False),
        ("Booking", "Payment", "1", "0..*", False),
        ("Installment", "Payment", "1", "0..*", False),
        ("Employee", "EmployeeSalary", "1", "0..*", False),
        ("EmployeeSalary", "Expense", "1", "1", False),
        ("Project", "Expense", "1", "0..*", False),
        ("Project", "ManualRevenue", "1", "0..*", False),
        ("Admin", "User", "", "", True),
        ("Admin", "Booking", "", "", True),
        ("Admin", "Employee", "", "", True),
        ("Admin", "Expense", "", "", True),
        ("Admin", "ManualRevenue", "", "", True),
    ]


def compute_canvas_height(classes: list[UmlClass]) -> int:
    max_bottom = max(c.y + c.height for c in classes)
    return max_bottom + 80


def validate_no_overlap(classes: list[UmlClass]) -> list[str]:
    issues: list[str] = []
    rects = [box_rect(c) for c in classes]
    for i, a in enumerate(rects):
        for j, b in enumerate(rects):
            if i >= j:
                continue
            x_overlap = a[0] < b[2] and b[0] < a[2]
            y_overlap = a[1] < b[3] and b[1] < a[3]
            if x_overlap and y_overlap:
                issues.append(f"Box overlap: {classes[i].name} and {classes[j].name}")
    return issues


def create_diagram_image() -> Image.Image:
    img = Image.new("RGB", (IMG_W, 2200), "white")
    draw = ImageDraw.Draw(img)

    classes = build_classes()
    measure_classes(classes, draw)
    by_name = {c.name: c for c in classes}

    y = 70
    y = place_row(by_name, ["Customer", "User", "Project"], y)
    y = place_row(by_name, ["Booking", "BookingRequest", "Unit"], y)
    y = place_row(by_name, ["Payment", "Installment", "Employee"], y)
    y = place_row(by_name, ["EmployeeSalary", "Expense", "ManualRevenue"], y)

    # Admin on its own row below the grid — avoids overlap with BookingRequest
    admin = by_name["Admin"]
    admin.width = max(admin.width, 240)
    admin.height = HEADER_H + LINE_H + 22
    admin.x = COL_X[1] - admin.width // 2
    admin.y = y + ADMIN_ROW_GAP

    canvas_h = compute_canvas_height(classes)
    if canvas_h > img.height:
        img = Image.new("RGB", (IMG_W, canvas_h), "white")
        draw = ImageDraw.Draw(img)

    boxes = {c.name: box_rect(c) for c in classes}

    # Draw all class boxes first
    for c in classes:
        boxes[c.name] = draw_class(draw, c)

    # Draw association lines first (solid, then dashed)
    links = build_links()
    for a, b, la, lb, dotted in links:
        if a in boxes and b in boxes and not dotted:
            draw_link(draw, boxes[a], boxes[b], la, lb, boxes, dotted=False, draw_line=True, draw_labels=False)

    for a, b, la, lb, dotted in links:
        if a in boxes and b in boxes and dotted:
            draw_link(draw, boxes[a], boxes[b], la, lb, boxes, dotted=True, draw_line=True, draw_labels=False)

    # Draw multiplicity labels on top of all lines
    for a, b, la, lb, dotted in links:
        if a in boxes and b in boxes and (la or lb):
            draw_link(draw, boxes[a], boxes[b], la, lb, boxes, dotted=False, draw_line=False, draw_labels=True)

    issues = validate_no_overlap(classes)
    if issues:
        print("Layout warnings:")
        for issue in issues:
            print(f"  - {issue}")

    return crop_to_content(img, classes, margin=60)


def crop_to_content(img: Image.Image, classes: list[UmlClass], margin: int = 60) -> Image.Image:
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
    c.drawCentredString(w / 2, h - 480, "27th November, 2025")


def create_pdf(img: Image.Image) -> None:
    OUT_PDF.parent.mkdir(parents=True, exist_ok=True)
    w, h = letter
    c = canvas.Canvas(str(OUT_PDF), pagesize=letter)

    create_cover_page(c)
    c.showPage()

    c.setFont("Times-Bold", 14)
    c.drawCentredString(w / 2, h - 48, "Class Diagram")

    page_margin = 36
    max_w = w - 2 * page_margin
    max_h = h - 100
    img_w = max_w
    img_h = img_w * img.height / img.width
    if img_h > max_h:
        img_h = max_h
        img_w = img_h * img.width / img.height

    top = (h - 60 - img_h) / 2 + 20
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
