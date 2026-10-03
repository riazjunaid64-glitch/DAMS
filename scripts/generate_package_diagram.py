"""Generate DAMS package diagram PDF matching the sample layout."""

from __future__ import annotations

import math
import os
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont
from reportlab.lib.pagesizes import letter
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen import canvas

ROOT = Path(__file__).resolve().parents[1]
OUT_PDF = ROOT / "docs" / "DAMS_Package_Diagram.pdf"
OUT_PNG = ROOT / "docs" / "DAMS_Package_Diagram.png"

IMG_W, IMG_H = 1427, 1066
MARGIN = 40

FONT_REG = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
FONT_BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"


def load_font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(FONT_BOLD if bold else FONT_REG, size)


def rounded_rect(
    draw: ImageDraw.ImageDraw,
    box: tuple[int, int, int, int],
    radius: int,
    outline: str = "black",
    width: int = 2,
    fill: str = "white",
) -> None:
    draw.rounded_rectangle(box, radius=radius, outline=outline, width=width, fill=fill)


def draw_arrow(
    draw: ImageDraw.ImageDraw,
    x: int,
    y1: int,
    y2: int,
    width: int = 2,
) -> None:
    draw.line((x, y1, x, y2), fill="black", width=width)
    tip = 10
    draw.polygon(
        [(x, y2), (x - tip, y2 - tip), (x + tip, y2 - tip)],
        fill="black",
        outline="black",
    )


def draw_centered_text(
    draw: ImageDraw.ImageDraw,
    box: tuple[int, int, int, int],
    lines: list[str],
    font: ImageFont.FreeTypeFont,
    fill: str = "black",
    line_gap: int = 4,
) -> None:
    x0, y0, x1, y1 = box
    heights = [font.size + line_gap for _ in lines]
    total_h = sum(heights) - line_gap
    y = y0 + (y1 - y0 - total_h) / 2
    for line, h in zip(lines, heights):
        w = draw.textlength(line, font=font)
        draw.text((x0 + (x1 - x0 - w) / 2, y), line, fill=fill, font=font)
        y += h


def create_diagram_image() -> Image.Image:
    img = Image.new("RGB", (IMG_W, IMG_H), "white")
    draw = ImageDraw.Draw(img)

    title_font = load_font(24, bold=True)
    layer_font = load_font(22, bold=True)
    body_font = load_font(20)
    module_font = load_font(18)

    cx = IMG_W // 2

    # Presentation layer
    pres_w, pres_h = 980, 88
    pres_x = cx - pres_w // 2
    pres_y = 70
    rounded_rect(draw, (pres_x, pres_y, pres_x + pres_w, pres_y + pres_h), 16)
    draw_centered_text(
        draw,
        (pres_x, pres_y + 10, pres_x + pres_w, pres_y + 46),
        ["Presentation layer"],
        layer_font,
    )
    draw_centered_text(
        draw,
        (pres_x, pres_y + 40, pres_x + pres_w, pres_y + pres_h),
        ["Login, signup, dashboard, booking & payment forms"],
        body_font,
    )

    draw_arrow(draw, cx, pres_y + pres_h + 4, pres_y + pres_h + 44)

    # Business logic layer
    biz_w, biz_h = 1180, 560
    biz_x = cx - biz_w // 2
    biz_y = pres_y + pres_h + 44
    rounded_rect(draw, (biz_x, biz_y, biz_x + biz_w, biz_y + biz_h), 16)
    draw.text(
        (cx - draw.textlength("Business logic layer", font=layer_font) / 2, biz_y + 14),
        "Business logic layer",
        fill="black",
        font=layer_font,
    )

    modules = [
        ["Security mgmt", "Staff mgmt", "Project mgmt", "Client mgmt"],
        ["Booking mgmt", "Construction prog.", "Notification", "Expense mgmt"],
        ["Income mgmt", "System interface", "Installment mgmt", "Finance mgmt"],
        ["Media mgmt", "Reports & hist."],
    ]

    box_w, box_h = 250, 78
    gap_x, gap_y = 28, 24
    grid_w = 4 * box_w + 3 * gap_x
    start_x = cx - grid_w // 2
    start_y = biz_y + 58

    for row_idx, row in enumerate(modules):
        cols = len(row)
        row_w = cols * box_w + (cols - 1) * gap_x
        row_x = cx - row_w // 2
        y = start_y + row_idx * (box_h + gap_y)
        for col_idx, label in enumerate(row):
            x = row_x + col_idx * (box_w + gap_x)
            rounded_rect(draw, (x, y, x + box_w, y + box_h), 12)
            if len(label) > 18 and " " in label:
                parts = label.split(" ", 1)
                draw_centered_text(draw, (x, y, x + box_w, y + box_h), parts, module_font)
            else:
                draw_centered_text(draw, (x, y, x + box_w, y + box_h), [label], module_font)

    draw_arrow(draw, cx, biz_y + biz_h + 4, biz_y + biz_h + 44)

    # Data access layer
    dal_w, dal_h = 980, 88
    dal_x = cx - dal_w // 2
    dal_y = biz_y + biz_h + 44
    rounded_rect(draw, (dal_x, dal_y, dal_x + dal_w, dal_y + dal_h), 16)
    draw_centered_text(
        draw,
        (dal_x, dal_y + 10, dal_x + dal_w, dal_y + 46),
        ["Data access layer"],
        layer_font,
    )
    draw_centered_text(
        draw,
        (dal_x, dal_y + 40, dal_x + dal_w, dal_y + dal_h),
        ["SQL Server database"],
        body_font,
    )

    return img


def create_pdf(img: Image.Image) -> None:
    OUT_PDF.parent.mkdir(parents=True, exist_ok=True)
    w, h = letter
    c = canvas.Canvas(str(OUT_PDF), pagesize=letter)

    c.setFont("Times-Bold", 14)
    c.drawString(72, h - 72, "3.4 Package Diagram")

    img_w = 470
    img_h = img_w * img.height / img.width
    c.drawImage(ImageReader(img), (w - img_w) / 2, h - 92 - img_h, width=img_w, height=img_h)

    c.setFont("Helvetica", 12)
    c.drawCentredString(w / 2, 50, "87")

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
