#!/usr/bin/env python3
"""Builds the static Manrope weights PrintVect uses (Regular 400, SemiBold 600, Bold 700) from the
variable font in src/PrintVect.App/Fonts/Manrope/Manrope-VariableFont_wght.ttf.

Windows' GDI+ (System.Drawing) only sees the default instance of a variable font, which for Manrope
is ExtraLight 200, so the app loads these fixed-weight files instead. Manrope's licence (SIL OFL 1.1)
names no Reserved Font Name, so the instances keep the name Manrope. The variable font stays in the
repository as the source and is not copied to the program folder.

    pip install fonttools && python3 tools/make_fonts.py
"""
import os

from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DIR = os.path.join(ROOT, "src", "PrintVect.App", "Fonts", "Manrope")
SOURCE = os.path.join(DIR, "Manrope-VariableFont_wght.ttf")
WEIGHTS = {"Regular": 400, "SemiBold": 600, "Bold": 700}


def main():
    for style, weight in WEIGHTS.items():
        font = TTFont(SOURCE)
        # updateFontNames uses the STAT table: Regular and Bold stay in family "Manrope",
        # SemiBold becomes its own family "Manrope SemiBold" (the usual GDI naming).
        static = instancer.instantiateVariableFont(font, {"wght": weight}, updateFontNames=True)
        out = os.path.join(DIR, "Manrope-%s.ttf" % style)
        static.save(out)
        name = static["name"]
        print("%s: family %r, style %r, %d bytes" % (os.path.basename(out), name.getDebugName(1),
                                                    name.getDebugName(2), os.path.getsize(out)))


if __name__ == "__main__":
    main()
