#!/usr/bin/env python3
"""Builds the static Noto Sans weights PrintVect uses (Regular 400, Medium 500, SemiBold 600, Bold 700) from
the variable font of github.com/google/fonts, ofl/notosans/NotoSans[wdth,wght].ttf (SIL OFL 1.1, no Reserved
Font Name, so the instances keep the name Noto Sans).

Windows' GDI+ (System.Drawing) only sees the default instance of a variable font, so the app loads fixed-weight
files instead. Each one is cut down to the Latin letters, punctuation, currency signs (the rupee sign too) and
arrows the screens use: the full font carries Greek and Cyrillic as well and would add 2 MB to the setup.
Windows draws any other character (a Hindi document name, say) with its own fonts, as before.

    pip install fonttools
    python3 tools/make_fonts.py [path to NotoSans[wdth,wght].ttf]

Without a path the variable font is downloaded from the address below; its SHA-256 is checked either way.
"""
import hashlib
import os
import sys
import tempfile
import urllib.request

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "src", "PrintVect.App", "Fonts", "NotoSans")
SOURCE_URL = "https://raw.githubusercontent.com/google/fonts/main/ofl/notosans/NotoSans%5Bwdth%2Cwght%5D.ttf"
SOURCE_SHA256 = "bfb7bb691513f12e734dc346c03a03f784912432d7e3fa8e56efcf906fe86b3d"
WEIGHTS = {"Regular": 400, "Medium": 500, "SemiBold": 600, "Bold": 700}
UNICODES = [
    (0x0020, 0x007E), (0x00A0, 0x024F), (0x02B0, 0x036F), (0x1E00, 0x1EFF), (0x2000, 0x20CF),
    (0x2100, 0x22FF), (0x25A0, 0x25FF), (0xFB00, 0xFB06), (0xFEFF, 0xFEFF), (0xFFFD, 0xFFFD),
]


def source_path(argv):
    if len(argv) > 1:
        return argv[1]
    path = os.path.join(tempfile.mkdtemp(prefix="notosans-"), "NotoSans-wdth-wght.ttf")
    print("Downloading " + SOURCE_URL)
    urllib.request.urlretrieve(SOURCE_URL, path)
    return path


def main(argv):
    source = source_path(argv)
    with open(source, "rb") as f:
        digest = hashlib.sha256(f.read()).hexdigest()
    if digest != SOURCE_SHA256:
        print("WARNING: %s has SHA-256 %s, not the one these files were made from (%s)." % (source, digest, SOURCE_SHA256))
    os.makedirs(OUT, exist_ok=True)
    unicodes = [u for start, end in UNICODES for u in range(start, end + 1)]
    for style, weight in WEIGHTS.items():
        # updateFontNames uses the STAT table: Regular and Bold stay in family "Noto Sans",
        # Medium and SemiBold become their own families "Noto Sans Medium" / "Noto Sans SemiBold" (GDI naming).
        static = instancer.instantiateVariableFont(TTFont(source), {"wdth": 100, "wght": weight}, updateFontNames=True)
        options = subset.Options()
        options.layout_features = ["*"]
        options.name_IDs = ["*"]
        options.name_languages = ["*"]
        options.notdef_outline = True
        subsetter = subset.Subsetter(options)
        subsetter.populate(unicodes=unicodes)
        subsetter.subset(static)
        out = os.path.join(OUT, "NotoSans-%s.ttf" % style)
        static.save(out)
        name = static["name"]
        print("%s: family %r, style %r, %d glyphs, %d bytes" % (os.path.basename(out), name.getDebugName(1),
              name.getDebugName(2), len(static.getGlyphOrder()), os.path.getsize(out)))


if __name__ == "__main__":
    main(sys.argv)
