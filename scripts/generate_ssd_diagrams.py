"""Generate DAMS System Sequence Diagrams PDF matching thesis sample style."""

from __future__ import annotations

import textwrap
from dataclasses import dataclass, field
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont
from reportlab.lib.pagesizes import letter
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen import canvas

ROOT = Path(__file__).resolve().parents[1]
OUT_PDF = ROOT / "docs" / "DAMS_System_Sequence_Diagrams.pdf"
OUT_DIR = ROOT / "docs" / "ssd-diagrams"

FONT_REG = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
FONT_BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
FONT_ITALIC = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Oblique.ttf"

# Layout constants — bold, high-contrast styling
IMG_W = 2400
BASE_H = 560
MESSAGE_GAP = 60
MARGIN_TOP = 100
MARGIN_BOTTOM = 112
ACTOR_W = 120
PARTICIPANT_H = 46
PARTICIPANT_PAD_X = 16
PARTICIPANT_PAD_Y = 10
LIFELINE_TOP_OFFSET = 16
ACTIVATION_W = 18
ARROW_HEAD = 12
ARROW_HALF_H = 7
LINE_BOLD = 3
LINE_MED = 2
LABEL_FONT_SIZE = 17
TITLE_FONT_SIZE = 28
CAPTION_FONT_SIZE = 18
PARTICIPANT_FONT_SIZE = 17
ACTOR_FONT_SIZE = 17
MIN_PARTICIPANT_W = 148
LIFELINE_COLOR = "#111111"
BOUNDARY_COLOR = "#444444"


@dataclass
class Message:
    source: int  # -1 = actor
    target: int  # participant index
    label: str
    is_return: bool = False


@dataclass
class SSDDiagram:
    figure: str
    title: str
    caption: str
    actor: str
    participants: list[str]
    messages: list[Message] = field(default_factory=list)


def load_font(size: int, bold: bool = False, italic: bool = False) -> ImageFont.FreeTypeFont:
    if italic:
        return ImageFont.truetype(FONT_ITALIC, size)
    return ImageFont.truetype(FONT_BOLD if bold else FONT_REG, size)


def wrap_label(text: str, max_chars: int = 42) -> list[str]:
    wrapped: list[str] = []
    for part in text.split("\n"):
        wrapped.extend(textwrap.wrap(part, width=max_chars) or [""])
    return wrapped


def measure_participant_widths(
    draw: ImageDraw.ImageDraw, participants: list[str], font: ImageFont.FreeTypeFont
) -> list[int]:
    widths = []
    for name in participants:
        label = f":{name}"
        w = int(draw.textlength(label, font=font)) + PARTICIPANT_PAD_X * 2
        widths.append(max(w, MIN_PARTICIPANT_W))
    return widths


def compute_layout(
    actor: str,
    participants: list[str],
    message_count: int,
    p_widths: list[int],
) -> tuple[int, int, list[int], int, int]:
    """Return height, actor cx, participant center x coords, lifeline y start/end."""
    height = MARGIN_TOP + PARTICIPANT_H + LIFELINE_TOP_OFFSET + message_count * MESSAGE_GAP + MARGIN_BOTTOM + 64
    height = max(height, BASE_H)

    gap_count = max(len(participants) - 1, 1)
    inner_gap = 44 if len(participants) >= 4 else 52
    total_p_w = sum(p_widths)
    content_w = ACTOR_W + 56 + total_p_w + inner_gap * (len(participants) - 1)

    # Place content block centered horizontally on canvas
    block_left = (IMG_W - content_w) // 2
    actor_cx = block_left + ACTOR_W // 2

    xs: list[int] = []
    x = block_left + ACTOR_W + 56 + p_widths[0] // 2
    for i, pw in enumerate(p_widths):
        if i == 0:
            xs.append(x)
        else:
            prev = xs[-1]
            xs.append(prev + p_widths[i - 1] // 2 + inner_gap + pw // 2)

    lifeline_y0 = MARGIN_TOP + PARTICIPANT_H + LIFELINE_TOP_OFFSET
    lifeline_y1 = height - MARGIN_BOTTOM
    return height, actor_cx, xs, lifeline_y0, lifeline_y1


def draw_actor(
    draw: ImageDraw.ImageDraw,
    cx: int,
    y_base: int,
    name: str,
    font: ImageFont.FreeTypeFont,
) -> None:
    """Draw stick-figure actor above lifeline."""
    head_r = 16
    head_cy = y_base - 82
    draw.ellipse(
        (cx - head_r, head_cy - head_r, cx + head_r, head_cy + head_r),
        outline="black",
        width=LINE_BOLD,
    )
    body_top = head_cy + head_r
    body_bot = y_base - 40
    draw.line((cx, body_top, cx, body_bot), fill="black", width=LINE_BOLD)
    draw.line((cx - 20, body_top + 18, cx + 20, body_top + 18), fill="black", width=LINE_BOLD)
    draw.line((cx, body_bot, cx - 18, body_bot + 24), fill="black", width=LINE_BOLD)
    draw.line((cx, body_bot, cx + 18, body_bot + 24), fill="black", width=LINE_BOLD)

    nw = draw.textlength(name, font=font)
    draw.text((cx - nw / 2, y_base - 30), name, fill="black", font=font)


def draw_participant_box(
    draw: ImageDraw.ImageDraw,
    cx: int,
    y: int,
    width: int,
    name: str,
    font: ImageFont.FreeTypeFont,
) -> tuple[int, int, int, int]:
    x0 = cx - width // 2
    x1 = cx + width // 2
    y0 = y
    y1 = y + PARTICIPANT_H
    draw.rectangle((x0, y0, x1, y1), outline="black", width=LINE_BOLD, fill="white")
    label = f":{name}"
    lw = draw.textlength(label, font=font)
    draw.text((cx - lw / 2, y0 + PARTICIPANT_PAD_Y), label, fill="black", font=font)
    return x0, y0, x1, y1


def draw_lifeline(
    draw: ImageDraw.ImageDraw, x: int, y0: int, y1: int, dashed: bool = True
) -> None:
    if dashed:
        step, seg = 12, 8
        y = y0
        while y < y1:
            seg_end = min(y + seg, y1)
            draw.line((x, y, x, seg_end), fill=LIFELINE_COLOR, width=LINE_MED)
            y += step + seg
    else:
        draw.line((x, y0, x, y1), fill=LIFELINE_COLOR, width=LINE_MED)


def draw_activation(
    draw: ImageDraw.ImageDraw, x: int, y0: int, y1: int
) -> None:
    x0 = x - ACTIVATION_W // 2
    draw.rectangle((x0, y0, x0 + ACTIVATION_W, y1), fill="#DDDDDD", outline="black", width=LINE_MED)


def draw_horizontal_arrow(
    draw: ImageDraw.ImageDraw,
    x1: int,
    x2: int,
    y: int,
    dashed: bool = False,
    label: str = "",
    label_font: ImageFont.FreeTypeFont | None = None,
) -> None:
    left, right = (x1, x2) if x1 < x2 else (x2, x1)
    direction = 1 if x2 > x1 else -1
    tip_x = right if direction == 1 else left

    if dashed:
        step = 9
        x = left + 6
        while x < tip_x - direction * (ARROW_HEAD + 3):
            seg = min(x + step, tip_x - direction * (ARROW_HEAD + 3))
            draw.line((x, y, seg, y), fill="black", width=LINE_MED)
            x += step * 2
        if direction == 1:
            draw.line((tip_x - ARROW_HEAD, y - ARROW_HALF_H, tip_x, y), fill="black", width=LINE_MED)
            draw.line((tip_x - ARROW_HEAD, y + ARROW_HALF_H, tip_x, y), fill="black", width=LINE_MED)
        else:
            draw.line((tip_x + ARROW_HEAD, y - ARROW_HALF_H, tip_x, y), fill="black", width=LINE_MED)
            draw.line((tip_x + ARROW_HEAD, y + ARROW_HALF_H, tip_x, y), fill="black", width=LINE_MED)
    else:
        draw.line((left, y, tip_x - direction * ARROW_HEAD, y), fill="black", width=LINE_BOLD)
        if direction == 1:
            draw.polygon(
                [(tip_x, y), (tip_x - ARROW_HEAD, y - ARROW_HALF_H), (tip_x - ARROW_HEAD, y + ARROW_HALF_H)],
                fill="black",
            )
        else:
            draw.polygon(
                [(tip_x, y), (tip_x + ARROW_HEAD, y - ARROW_HALF_H), (tip_x + ARROW_HEAD, y + ARROW_HALF_H)],
                fill="black",
            )

    if label and label_font:
        max_chars = 32 if abs(x2 - x1) < 300 else 38
        lines = wrap_label(label, max_chars)
        line_h = label_font.size + 5
        total_h = len(lines) * line_h
        ly = y - total_h - 10
        mid = (x1 + x2) / 2
        for line in lines:
            lw = draw.textlength(line, font=label_font)
            draw.text((mid - lw / 2, ly), line, fill="black", font=label_font)
            ly += line_h


def render_ssd(diagram: SSDDiagram) -> Image.Image:
    tmp = Image.new("RGB", (IMG_W, 100), "white")
    tdraw = ImageDraw.Draw(tmp)
    p_font = load_font(PARTICIPANT_FONT_SIZE, bold=True)
    p_widths = measure_participant_widths(tdraw, diagram.participants, p_font)

    height, actor_cx, p_xs, ly0, ly1 = compute_layout(
        diagram.actor, diagram.participants, len(diagram.messages), p_widths
    )

    img = Image.new("RGB", (IMG_W, height), "white")
    draw = ImageDraw.Draw(img)

    title_font = load_font(TITLE_FONT_SIZE, bold=True)
    caption_font = load_font(CAPTION_FONT_SIZE, bold=True)
    actor_font = load_font(ACTOR_FONT_SIZE, bold=True)
    label_size = 15 if len(diagram.participants) >= 5 else (16 if len(diagram.participants) >= 4 else LABEL_FONT_SIZE)
    label_font = load_font(label_size, bold=True)
    p_font = load_font(PARTICIPANT_FONT_SIZE, bold=True)

    # Title — centered
    title = diagram.title
    tw = draw.textlength(title, font=title_font)
    draw.text(((IMG_W - tw) / 2, 28), title, fill="black", font=title_font)

    actor_y = MARGIN_TOP

    # System boundary — bold dashed box around participants
    boundary_x0 = p_xs[0] - p_widths[0] // 2 - 28
    boundary_x1 = p_xs[-1] + p_widths[-1] // 2 + 28
    boundary_y0 = MARGIN_TOP - 14
    boundary_y1 = ly1 + 14
    dash, gap = 10, 8
    for side in [
        (boundary_x0, boundary_y0, boundary_x1, boundary_y0),
        (boundary_x0, boundary_y1, boundary_x1, boundary_y1),
        (boundary_x0, boundary_y0, boundary_x0, boundary_y1),
        (boundary_x1, boundary_y0, boundary_x1, boundary_y1),
    ]:
        x_a, y_a, x_b, y_b = side
        if x_a == x_b:
            y = min(y_a, y_b)
            while y < max(y_a, y_b):
                draw.line((x_a, y, x_a, min(y + dash, max(y_a, y_b))), fill=BOUNDARY_COLOR, width=LINE_MED)
                y += dash + gap
        else:
            x = min(x_a, x_b)
            while x < max(x_a, x_b):
                draw.line((x, y_a, min(x + dash, max(x_a, x_b)), y_a), fill=BOUNDARY_COLOR, width=LINE_MED)
                x += dash + gap

    draw_actor(draw, actor_cx, actor_y + PARTICIPANT_H, diagram.actor, actor_font)

    # Participant boxes + lifelines
    for i, (name, cx, pw) in enumerate(zip(diagram.participants, p_xs, p_widths)):
        draw_participant_box(draw, cx, MARGIN_TOP, pw, name, p_font)
        draw_lifeline(draw, cx, MARGIN_TOP + PARTICIPANT_H + LIFELINE_TOP_OFFSET, ly1)

    actor_lifeline_x = actor_cx
    draw_lifeline(draw, actor_lifeline_x, MARGIN_TOP + PARTICIPANT_H + LIFELINE_TOP_OFFSET, ly1)

    def x_for(idx: int) -> int:
        if idx == -1:
            return actor_lifeline_x
        return p_xs[idx]

    # Track activation spans per participant
    activations: dict[int, list[tuple[int, int]]] = {i: [] for i in range(len(diagram.participants))}
    y = ly0 + 28

    for msg in diagram.messages:
        x1 = x_for(msg.source)
        x2 = x_for(msg.target)
        draw_horizontal_arrow(draw, x1, x2, y, dashed=msg.is_return, label=msg.label, label_font=label_font)

        if not msg.is_return:
            for idx in (msg.source, msg.target):
                if idx >= 0:
                    activations[idx].append((y - 4, y + 4))
        y += MESSAGE_GAP

    # Draw merged activation boxes
    for idx, spans in activations.items():
        if not spans:
            continue
        y0 = min(s[0] for s in spans) - 2
        y1 = max(s[1] for s in spans) + 2
        draw_activation(draw, p_xs[idx], y0, y1)

    # Caption below system boundary
    cap = f"{diagram.figure}: {diagram.caption}"
    lines = wrap_label(cap, 105)
    cy = boundary_y1 + 22
    for line in lines:
        lw = draw.textlength(line, font=caption_font)
        draw.text(((IMG_W - lw) / 2, cy), line, fill="black", font=caption_font)
        cy += caption_font.size + 5

    # Extend canvas if caption overflows
    if cy + 20 > height:
        extra = cy + 20 - height
        extended = Image.new("RGB", (IMG_W, height + extra), "white")
        extended.paste(img, (0, 0))
        img = extended

    return img


def validate_image(img: Image.Image, name: str) -> list[str]:
    """Basic layout sanity checks."""
    issues: list[str] = []
    w, h = img.size
    if w < 800 or h < 400:
        issues.append(f"{name}: image too small ({w}x{h})")
    # Ensure caption text exists in bottom 120px band
    bottom_band = img.crop((0, h - 120, w, h)).convert("L")
    if bottom_band.getextrema()[0] == 255 and bottom_band.getextrema()[1] == 255:
        issues.append(f"{name}: no caption detected in bottom area")
    return issues


def build_diagrams() -> list[SSDDiagram]:
    return [
        SSDDiagram(
            figure="Figure 3.1",
            title="System Sequence Diagram — User Registration",
            caption="User Registration (Guest creates Client account)",
            actor="Guest",
            participants=["AuthController", "AuthService", "Database"],
            messages=[
                Message(-1, 0, "1: register(fullName, email, password)"),
                Message(0, 1, "2: RegisterAsync(dto)"),
                Message(1, 2, "3: checkEmailUnique(email)"),
                Message(2, 1, "4: email available", is_return=True),
                Message(1, 2, "5: insertUser(hashedPassword, Role=Client)"),
                Message(2, 1, "6: user saved", is_return=True),
                Message(1, 0, "7: success", is_return=True),
                Message(0, -1, "8: HTTP 200 OK", is_return=True),
            ],
        ),
        SSDDiagram(
            figure="Figure 3.2",
            title="System Sequence Diagram — User Login",
            caption="User Login and profile load",
            actor="User",
            participants=["AuthController", "AuthService", "TokenService", "Database"],
            messages=[
                Message(-1, 0, "1: login(email, password)"),
                Message(0, 1, "2: Login(credentials)"),
                Message(1, 3, "3: findUserByEmail(email)"),
                Message(3, 1, "4: user record", is_return=True),
                Message(1, 2, "5: GenerateAccessToken(user)"),
                Message(2, 1, "6: JWT token", is_return=True),
                Message(1, 2, "7: GenerateRefreshToken()"),
                Message(2, 1, "8: refresh token", is_return=True),
                Message(1, 3, "9: saveRefreshToken(user)"),
                Message(3, 1, "10: saved", is_return=True),
                Message(1, 0, "11: tokens", is_return=True),
                Message(0, -1, "12: { accessToken, refreshToken }", is_return=True),
                Message(-1, 0, "13: getProfile()"),
                Message(0, -1, "14: { userId, email, role }", is_return=True),
            ],
        ),
        SSDDiagram(
            figure="Figure 3.3",
            title="System Sequence Diagram — Browse Projects",
            caption="Guest browses projects and units",
            actor="Guest",
            participants=["ProjectController", "UnitController", "Database"],
            messages=[
                Message(-1, 0, "1: viewProjects()"),
                Message(0, 2, "2: getAllProjects()"),
                Message(2, 0, "3: project list", is_return=True),
                Message(0, -1, "4: projects[]", is_return=True),
                Message(-1, 1, "5: viewUnits(projectId)"),
                Message(1, 2, "6: getUnitsByProjectId(id)"),
                Message(2, 1, "7: units[]", is_return=True),
                Message(1, -1, "8: unit cards", is_return=True),
            ],
        ),
        SSDDiagram(
            figure="Figure 3.4",
            title="System Sequence Diagram — Submit Booking Request",
            caption="Guest/Client submits booking request for a unit",
            actor="Client",
            participants=["BookingRequestController", "BookingRequestService", "Database"],
            messages=[
                Message(-1, 0, "1: submitRequest(unitId, details)"),
                Message(0, 1, "2: CreateBookingRequestAsync(dto)"),
                Message(1, 2, "3: validateUnitAvailable()"),
                Message(2, 1, "4: unit OK", is_return=True),
                Message(1, 2, "5: insert BookingRequest (Pending)"),
                Message(1, 2, "6: update Unit.Status = PendingReview"),
                Message(2, 1, "7: saved", is_return=True),
                Message(1, 0, "8: requestDto", is_return=True),
                Message(0, -1, "9: HTTP 201 Created", is_return=True),
            ],
        ),
        SSDDiagram(
            figure="Figure 3.5",
            title="System Sequence Diagram — Approve Booking Request",
            caption="Admin approves request and creates confirmed booking",
            actor="Admin",
            participants=["BookingRequestController", "BookingRequestService", "CustomerService", "BookingService", "Database"],
            messages=[
                Message(-1, 0, "1: approve(requestId)"),
                Message(0, 1, "2: ApproveBookingRequestAsync(id)"),
                Message(1, 2, "3: FindOrCreateCustomerAsync(details)"),
                Message(2, 4, "4: findByCNIC/Phone/Email or create"),
                Message(4, 2, "5: customerId", is_return=True),
                Message(2, 1, "6: customerId", is_return=True),
                Message(1, 3, "7: CreateBookingForApprovedRequestAsync()"),
                Message(3, 4, "8: insert Booking (AwaitingBookingAmount)"),
                Message(3, 4, "9: update Unit.Status = Reserved"),
                Message(4, 3, "10: booking saved", is_return=True),
                Message(3, 1, "11: bookingDto", is_return=True),
                Message(1, 0, "12: approved", is_return=True),
                Message(0, -1, "13: HTTP 200 OK", is_return=True),
            ],
        ),
        SSDDiagram(
            figure="Figure 3.6",
            title="System Sequence Diagram — Reject Booking Request",
            caption="Admin rejects booking request and releases unit",
            actor="Admin",
            participants=["BookingRequestController", "BookingRequestService", "Database"],
            messages=[
                Message(-1, 0, "1: reject(requestId, reason)"),
                Message(0, 1, "2: RejectBookingRequestAsync(id, reason)"),
                Message(1, 2, "3: update Status = Rejected"),
                Message(1, 2, "4: update Unit.Status = Available"),
                Message(2, 1, "5: saved", is_return=True),
                Message(1, 0, "6: rejected", is_return=True),
                Message(0, -1, "7: HTTP 200 OK", is_return=True),
            ],
        ),
        SSDDiagram(
            figure="Figure 3.7",
            title="System Sequence Diagram — Create Walk-in Booking",
            caption="Admin creates direct booking for walk-in customer",
            actor="Admin",
            participants=["BookingController", "BookingService", "CustomerService", "Database"],
            messages=[
                Message(-1, 0, "1: createBooking(dto)"),
                Message(0, 1, "2: CreateBookingAsync(dto)"),
                Message(1, 2, "3: FindOrCreateCustomerAsync()"),
                Message(2, 3, "4: resolve customer"),
                Message(3, 2, "5: customerId", is_return=True),
                Message(2, 1, "6: customerId", is_return=True),
                Message(1, 3, "7: insert Booking + Unit=Reserved"),
                Message(3, 1, "8: bookingDto", is_return=True),
                Message(1, 0, "9: result", is_return=True),
                Message(0, -1, "10: HTTP 201 Created", is_return=True),
            ],
        ),
        SSDDiagram(
            figure="Figure 3.8",
            title="System Sequence Diagram — Record Booking Amount Payment",
            caption="Admin records booking amount; status becomes PaymentPlanActive",
            actor="Admin",
            participants=["BookingController", "BookingService", "Database"],
            messages=[
                Message(-1, 0, "1: recordBookingPayment(id, amount)"),
                Message(0, 1, "2: RecordBookingAmountPaymentAsync()"),
                Message(1, 2, "3: validate amount <= remaining"),
                Message(2, 1, "4: OK", is_return=True),
                Message(1, 2, "5: insert Payment (BookingAmount, RCP-xxx)"),
                Message(1, 2, "6: increment BookingAmountReceived"),
                Message(1, 2, "7: if fully paid: Status=PaymentPlanActive\nUnit=OnPaymentPlan"),
                Message(2, 1, "8: saved", is_return=True),
                Message(1, 0, "9: bookingDto", is_return=True),
                Message(0, -1, "10: HTTP 200 OK", is_return=True),
            ],
        ),
        SSDDiagram(
            figure="Figure 3.9",
            title="System Sequence Diagram — Generate Installment Plan",
            caption="Admin generates installment schedule for booking",
            actor="Admin",
            participants=["BookingController", "InstallmentService", "Database"],
            messages=[
                Message(-1, 0, "1: generatePlan(id, terms)"),
                Message(0, 1, "2: GenerateScheduleAsync(dto)"),
                Message(1, 2, "3: calculate pool = salePrice - received - possession"),
                Message(1, 2, "4: delete old installments (if regenerate)"),
                Message(1, 2, "5: insert Installment rows (Regular + Possession)"),
                Message(1, 2, "6: update Booking plan metadata"),
                Message(2, 1, "7: saved", is_return=True),
                Message(1, 0, "8: scheduleDto", is_return=True),
                Message(0, -1, "9: HTTP 200 OK", is_return=True),
            ],
        ),
        SSDDiagram(
            figure="Figure 3.10",
            title="System Sequence Diagram — Record Installment Payment",
            caption="Admin records installment payment and generates receipt",
            actor="Admin",
            participants=["BookingController", "InstallmentService", "Database"],
            messages=[
                Message(-1, 0, "1: recordPayment(bookingId, installmentId)"),
                Message(0, 1, "2: RecordInstallmentPaymentAsync()"),
                Message(1, 2, "3: validate installment not fully paid"),
                Message(2, 1, "4: OK", is_return=True),
                Message(1, 2, "5: insert Payment (Installment, RCP-xxx)"),
                Message(1, 2, "6: update Installment Status (Paid/Partial)"),
                Message(2, 1, "7: saved", is_return=True),
                Message(1, 0, "8: scheduleDto", is_return=True),
                Message(0, -1, "9: HTTP 200 OK", is_return=True),
            ],
        ),
        SSDDiagram(
            figure="Figure 3.11",
            title="System Sequence Diagram — View Payment Receipt",
            caption="Admin or Client views printable payment receipt",
            actor="User",
            participants=["ReceiptPage", "BookingController", "BookingService", "Database"],
            messages=[
                Message(-1, 0, "1: openReceipt(bookingId, paymentId)"),
                Message(0, 1, "2: GET /payments/{id}/receipt"),
                Message(1, 2, "3: GetPaymentReceiptAsync()"),
                Message(2, 3, "4: join Payment, Booking, Customer, Unit"),
                Message(3, 2, "5: receipt data", is_return=True),
                Message(2, 1, "6: PaymentReceiptDto", is_return=True),
                Message(1, 0, "7: receiptDto", is_return=True),
                Message(0, -1, "8: render PaymentReceipt + print", is_return=True),
            ],
        ),
        SSDDiagram(
            figure="Figure 3.12",
            title="System Sequence Diagram — Client My Projects",
            caption="Client views own confirmed bookings by email match",
            actor="Client",
            participants=["MyProjectsController", "BookingService", "Database"],
            messages=[
                Message(-1, 0, "1: viewMyProjects()"),
                Message(0, 1, "2: GetBookingsByCustomerEmailAsync(email)"),
                Message(1, 2, "3: query Bookings where Customer.Email = JWT email"),
                Message(2, 1, "4: booking list", is_return=True),
                Message(1, 0, "5: myProjects[]", is_return=True),
                Message(0, -1, "6: HTTP 200 OK", is_return=True),
            ],
        ),
        SSDDiagram(
            figure="Figure 3.13",
            title="System Sequence Diagram — Cancel Booking",
            caption="Admin cancels booking and releases unit",
            actor="Admin",
            participants=["BookingController", "BookingService", "Database"],
            messages=[
                Message(-1, 0, "1: cancelBooking(id, reason)"),
                Message(0, 1, "2: CancelBookingAsync(id, reason)"),
                Message(1, 2, "3: update Booking.Status = Cancelled"),
                Message(1, 2, "4: update Unit.Status = Available"),
                Message(2, 1, "5: saved", is_return=True),
                Message(1, 0, "6: bookingDto", is_return=True),
                Message(0, -1, "7: HTTP 200 OK", is_return=True),
            ],
        ),
        SSDDiagram(
            figure="Figure 3.14",
            title="System Sequence Diagram — Mark Employee Attendance",
            caption="Admin records daily attendance for employees",
            actor="Admin",
            participants=["EmployeeController", "EmployeeService", "Database"],
            messages=[
                Message(-1, 0, "1: saveAttendance(employeeId, date, status)"),
                Message(0, 1, "2: RecordAttendanceAsync(dto)"),
                Message(1, 2, "3: upsert EmployeeAttendance by employee+date"),
                Message(2, 1, "4: saved", is_return=True),
                Message(1, 0, "5: attendanceDto", is_return=True),
                Message(0, -1, "6: HTTP 200 OK", is_return=True),
            ],
        ),
        SSDDiagram(
            figure="Figure 3.15",
            title="System Sequence Diagram — Generate Employee Salary",
            caption="Admin generates salary payment and linked expense",
            actor="Admin",
            participants=["EmployeeController", "EmployeeService", "Database"],
            messages=[
                Message(-1, 0, "1: paySalary(employeeId, amount)"),
                Message(0, 1, "2: GenerateSalaryAsync(dto)"),
                Message(1, 2, "3: validate no duplicate for month/year"),
                Message(2, 1, "4: OK", is_return=True),
                Message(1, 2, "5: insert Expense (Category=Salary)"),
                Message(1, 2, "6: insert EmployeeSalary (linked ExpenseId)"),
                Message(2, 1, "7: saved", is_return=True),
                Message(1, 0, "8: salaryDto", is_return=True),
                Message(0, -1, "9: HTTP 200 OK", is_return=True),
            ],
        ),
        SSDDiagram(
            figure="Figure 3.16",
            title="System Sequence Diagram — Finance Dashboard",
            caption="Admin loads financial summary and transaction lines",
            actor="Admin",
            participants=["FinanceController", "FinanceService", "Database"],
            messages=[
                Message(-1, 0, "1: loadDashboard(filters)"),
                Message(0, 1, "2: GetDashboardAsync(projectId, from, to)"),
                Message(1, 2, "3: aggregate Payments (automatic revenue)"),
                Message(1, 2, "4: query ManualRevenue + Expenses"),
                Message(1, 2, "5: compute outstanding + overdue"),
                Message(2, 1, "6: raw data", is_return=True),
                Message(1, 0, "7: FinanceDashboardDto", is_return=True),
                Message(0, -1, "8: summary cards + tables", is_return=True),
            ],
        ),
        SSDDiagram(
            figure="Figure 3.17",
            title="System Sequence Diagram — Add Manual Revenue",
            caption="Admin adds manual revenue entry to finance ledger",
            actor="Admin",
            participants=["FinanceController", "FinanceService", "Database"],
            messages=[
                Message(-1, 0, "1: addRevenue(dto)"),
                Message(0, 1, "2: CreateManualRevenueAsync(dto)"),
                Message(1, 2, "3: insert ManualRevenue"),
                Message(2, 1, "4: saved", is_return=True),
                Message(1, 0, "5: revenueDto", is_return=True),
                Message(0, -1, "6: HTTP 201 Created", is_return=True),
            ],
        ),
        SSDDiagram(
            figure="Figure 3.18",
            title="System Sequence Diagram — Add Expense",
            caption="Admin adds expense entry to finance ledger",
            actor="Admin",
            participants=["FinanceController", "FinanceService", "Database"],
            messages=[
                Message(-1, 0, "1: addExpense(dto)"),
                Message(0, 1, "2: CreateExpenseAsync(dto)"),
                Message(1, 2, "3: insert Expense"),
                Message(2, 1, "4: saved", is_return=True),
                Message(1, 0, "5: expenseDto", is_return=True),
                Message(0, -1, "6: HTTP 201 Created", is_return=True),
            ],
        ),
    ]


def create_pdf(images: list[tuple[SSDDiagram, Image.Image]]) -> None:
    OUT_PDF.parent.mkdir(parents=True, exist_ok=True)
    page_w, page_h = letter
    c = canvas.Canvas(str(OUT_PDF), pagesize=letter)

    # Cover section title page
    c.setFont("Times-Bold", 16)
    c.drawCentredString(page_w / 2, page_h - 72, "System Sequence Diagrams")
    c.setFont("Times-Roman", 12)
    c.drawCentredString(page_w / 2, page_h - 96, "Deen Associate Management System (DAMS)")
    c.showPage()

    page_margin = 40
    max_w = page_w - 2 * page_margin
    max_h = page_h - 2 * page_margin

    for _diagram, img in images:
        # Scale to fit page while keeping diagram centered
        img_w = max_w
        img_h = img_w * img.height / img.width
        if img_h > max_h:
            img_h = max_h
            img_w = img_h * img.width / img.height

        x_pos = (page_w - img_w) / 2
        y_pos = (page_h - img_h) / 2
        c.drawImage(ImageReader(img), x_pos, y_pos, width=img_w, height=img_h)
        c.showPage()

    c.save()


def main() -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    diagrams = build_diagrams()
    rendered: list[tuple[SSDDiagram, Image.Image]] = []
    all_issues: list[str] = []

    for i, d in enumerate(diagrams):
        img = render_ssd(d)
        slug = d.figure.replace(" ", "_").replace(".", "_").lower()
        png_path = OUT_DIR / f"{slug}.png"
        img.save(png_path)
        rendered.append((d, img))
        issues = validate_image(img, d.title)
        all_issues.extend(issues)

    create_pdf(rendered)

    if all_issues:
        print("Layout review warnings:")
        for issue in all_issues:
            print(f"  - {issue}")
    else:
        print(f"Layout review: all {len(diagrams)} diagrams passed checks.")

    print(f"Created {OUT_PDF} ({len(diagrams)} diagrams)")
    print(f"PNG files in {OUT_DIR}")


if __name__ == "__main__":
    main()
