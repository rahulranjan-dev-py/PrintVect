#!/usr/bin/env python3
"""Builds docs/design/social-preview.png, the 1280 x 640 picture GitHub, chat apps and social sites show when
someone shares a link to this repository (upload it under Settings, General, Social preview). Colours from the
icon (tools/make_icon.py), fonts from src/PrintVect.App/Fonts. Drawn at 2x and downsampled.

    pip install pillow && python3 tools/make_social_preview.py
"""
import os

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FONTS = os.path.join(ROOT, "src", "PrintVect.App", "Fonts", "NotoSans")
ICON = os.path.join(ROOT, "docs", "design", "printvect-icon-256.png")
OUT = os.path.join(ROOT, "docs", "design", "social-preview.png")

W, H = 1280, 640
S = 2  # oversample

BACKGROUND = "#12203D"
TITLE = "#FFFFFF"
TAGLINE = "#A9C4F5"
BODY = "#D6E2F7"
CHIP = "#1F3B73"
CHIP_TEXT = "#EEF3FC"
GREEN = "#34D399"

TAG_LINES = ["Share a printer with every Windows", "PC and laptop, at home or at work"]
BODY_LINE = "No printer drivers and no passwords on the PCs that print."
CHIPS = ["Windows 10 and 11", "Stays on your network", "Free, open source (MIT)"]


def font(weight, size):
    return ImageFont.truetype(os.path.join(FONTS, "NotoSans-%s.ttf" % weight), size * S)


def main():
    img = Image.new("RGB", (W * S, H * S), BACKGROUND)
    draw = ImageDraw.Draw(img)

    icon_size = 248
    icon = Image.open(ICON).convert("RGBA").resize((icon_size * S, icon_size * S), Image.LANCZOS)
    left = 96
    img.paste(icon, (left * S, 120 * S), icon)

    x = (left + icon_size + 64) * S
    y = 104 * S
    draw.text((x, y), "PrintVect", font=font("Bold", 92), fill=TITLE)
    y += 132 * S
    for line in TAG_LINES:
        draw.text((x, y), line, font=font("SemiBold", 40), fill=TAGLINE)
        y += 56 * S
    y += 16 * S
    draw.text((x, y), BODY_LINE, font=font("Regular", 27), fill=BODY)

    chip_font = font("Medium", 26)
    cx, cy, ch = left * S, 486 * S, 58 * S
    for text in CHIPS:
        tw = draw.textlength(text, font=chip_font)
        cw = int(tw + 76 * S)
        draw.rounded_rectangle((cx, cy, cx + cw, cy + ch), radius=ch // 2, fill=CHIP)
        dot = 12 * S
        draw.ellipse((cx + 24 * S, cy + (ch - dot) // 2, cx + 24 * S + dot, cy + (ch + dot) // 2), fill=GREEN)
        draw.text((cx + 50 * S, cy + ch // 2), text, font=chip_font, fill=CHIP_TEXT, anchor="lm")
        cx += cw + 20 * S

    img.resize((W, H), Image.LANCZOS).save(OUT, optimize=True)
    print("wrote", OUT)


if __name__ == "__main__":
    main()
