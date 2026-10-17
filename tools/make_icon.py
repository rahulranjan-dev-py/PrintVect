#!/usr/bin/env python3
"""Builds src/PrintVect.App/PrintVect.ico from the owner's icon (docs/design/printvect-icon.svg):
a 64-unit grid of rounded rectangles, drawn here with Pillow at 4x and downsampled, in the sizes
Windows uses (16, 20, 24, 32, 40, 48, 64, 128, 256). At 16 and 20 px the three small PCs would
blur into a smudge, so those sizes use a simplified glyph: body, slot and one green bar.

    pip install pillow && python3 tools/make_icon.py
"""
import os
import sys

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "src", "PrintVect.App", "PrintVect.ico")
PNG_DIR = os.path.join(ROOT, "docs", "design")

TILE = "#1F3B73"
TRAY = "#A9C4F5"
BODY = "#FFFFFF"
PC = "#34D399"
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
OVERSAMPLE = 4

# (x, y, w, h, radius, colour) on the 64-unit grid, as in the SVG.
FULL = [
    (0, 0, 64, 64, 14, TILE),
    (21, 9, 22, 12, 2, TRAY),
    (10, 18, 44, 20, 5, BODY),
    (19, 27, 26, 4, 2, TILE),
    (30.5, 38, 3, 7, 0, TRAY),
    (13, 44, 38, 3, 1.5, TRAY),
    (10, 47, 10, 8, 2, PC),
    (27, 47, 10, 8, 2, PC),
    (44, 47, 10, 8, 2, PC),
]
# Simplified glyph for 16 and 20 px: bigger shapes, one bar for the "LAN".
SMALL = [
    (0, 0, 64, 64, 14, TILE),
    (18, 8, 28, 12, 3, TRAY),
    (8, 17, 48, 22, 6, BODY),
    (18, 26, 28, 5, 2.5, TILE),
    (8, 45, 48, 10, 4, PC),
]


def render(size, shapes):
    big = size * OVERSAMPLE
    scale = big / 64.0
    image = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    for x, y, w, h, r, colour in shapes:
        box = [x * scale, y * scale, (x + w) * scale - 1, (y + h) * scale - 1]
        radius = r * scale
        if radius > 0:
            draw.rounded_rectangle(box, radius=radius, fill=colour)
        else:
            draw.rectangle(box, fill=colour)
    return image.resize((size, size), Image.LANCZOS)


def main():
    frames = [render(size, SMALL if size <= 20 else FULL) for size in SIZES]
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    # Pillow writes every frame given through append_images, each at its own size.
    frames[-1].save(OUT, format="ICO", sizes=[(s, s) for s in SIZES], append_images=frames[:-1])
    frames[-1].save(os.path.join(PNG_DIR, "printvect-icon-256.png"))
    render(16, SMALL).resize((64, 64), Image.NEAREST).save(os.path.join(PNG_DIR, "printvect-icon-16-preview.png"))
    print("wrote %s (%d bytes, sizes %s)" % (os.path.relpath(OUT, ROOT), os.path.getsize(OUT), ", ".join(str(s) for s in SIZES)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
