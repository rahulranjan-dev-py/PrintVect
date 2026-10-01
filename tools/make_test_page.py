#!/usr/bin/env python3
"""Builds docs/samples/PrintVect-test-page.xps: a one-page A4 XPS document used to test printing
through a PrintVect host without needing the Microsoft XPS Document Writer on the sending PC.

The page embeds a small subset of DejaVu Sans (Bitstream Vera licence, free to embed and
redistribute). Needs: pip install fonttools, and a DejaVuSans.ttf on the machine.

    python3 tools/make_test_page.py
"""
import datetime
import io
import os
import sys
import zipfile
from xml.sax.saxutils import escape

from fontTools import subset
from fontTools.ttLib import TTFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "docs", "samples", "PrintVect-test-page.xps")
OUT_SHAPES = os.path.join(ROOT, "docs", "samples", "PrintVect-test-shapes.xps")
FONT_CANDIDATES = [
    "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
    "/usr/share/fonts/TTF/DejaVuSans.ttf",
    "C:/Windows/Fonts/DejaVuSans.ttf",
]
FONT_PART = "/Resources/Fonts/PrintVect.ttf"

XPS_NS = "http://schemas.microsoft.com/xps/2005/06"
PAGE_W = 793.7   # A4 in 1/96 inch units
PAGE_H = 1122.5
BLUE = "#1F5FA8"
GREY = "#C8D3E0"
BLACK = "#000000"
WHITE = "#FFFFFF"

today = datetime.date.today().isoformat()
TEXT = [
    # (font size, x, y baseline, colour, text)
    (40, 86, 132, WHITE, "PrintVect"),
    (28, 72, 232, BLACK, "Test page"),
    (14, 72, 280, BLACK, "If you can read this, the PrintVect host printed a job that arrived over the office network."),
    (14, 72, 304, BLACK, "Sent with pvct-send (milestone M1). Paper size: A4, one page, one copy."),
    (12, 72, 360, BLACK, "The blue frame is 10 mm inside the paper edge. A missing side means the printer margins are larger than that."),
    (12, 72, 384, BLACK, "The five grey boxes below should be evenly spaced and the same size."),
    (12, 72, 480, BLACK, "Generated on " + today + " by tools/make_test_page.py. Font: DejaVu Sans (subset)."),
    (12, 72, 1040, BLACK, "PrintVect - printer sharing for offices with mixed Windows 7 / 8 / 10 / 11 PCs."),
]


def find_font():
    for candidate in FONT_CANDIDATES:
        if os.path.exists(candidate):
            return candidate
    sys.exit("No DejaVuSans.ttf found; install fonts-dejavu-core or edit FONT_CANDIDATES.")


def subset_font(path, chars):
    font = TTFont(path)
    options = subset.Options()
    options.notdef_outline = True
    options.name_IDs = [1, 2, 3, 4, 6]
    options.hinting = False
    subsetter = subset.Subsetter(options)
    subsetter.populate(text=chars)
    subsetter.subset(font)
    buffer = io.BytesIO()
    font.save(buffer)
    return buffer.getvalue()


def fixed_page(with_text=True):
    parts = ['<FixedPage xmlns="%s" xml:lang="en-US" Width="%s" Height="%s">' % (XPS_NS, PAGE_W, PAGE_H)]
    # Frame 10 mm (37.8 units) inside the paper edge.
    m = 37.8
    parts.append('  <Path Data="M %.1f,%.1f L %.1f,%.1f L %.1f,%.1f L %.1f,%.1f Z" Stroke="%s" StrokeThickness="2" />'
                 % (m, m, PAGE_W - m, m, PAGE_W - m, PAGE_H - m, m, PAGE_H - m, BLUE))
    # Title box.
    parts.append('  <Path Data="M 72,84 L 330,84 L 330,150 L 72,150 Z" Fill="%s" />' % BLUE)
    # Five grey boxes.
    for i in range(5):
        x = 72 + i * 130
        parts.append('  <Path Data="M %d,400 L %d,400 L %d,450 L %d,450 Z" Fill="%s" />' % (x, x + 100, x + 100, x, GREY))
    # A thin rule.
    parts.append('  <Path Data="M 72,1010 L %.1f,1010" Stroke="%s" StrokeThickness="1" />' % (PAGE_W - 72, BLACK))
    if with_text:
        for size, x, y, colour, text in TEXT:
            parts.append('  <Glyphs FontUri="%s" FontRenderingEmSize="%s" OriginX="%s" OriginY="%s" Fill="%s" UnicodeString="%s" />'
                         % (FONT_PART, size, x, y, colour, escape(text, {'"': "&quot;"})))
    else:
        # A big cross and a circle-ish diamond so the page is recognisable without any text.
        parts.append('  <Path Data="M 72,520 L 721.7,900" Stroke="%s" StrokeThickness="6" />' % BLUE)
        parts.append('  <Path Data="M 721.7,520 L 72,900" Stroke="%s" StrokeThickness="6" />' % BLUE)
        parts.append('  <Path Data="M 396.85,560 L 480,710 L 396.85,860 L 313.7,710 Z" Fill="%s" />' % GREY)
    parts.append("</FixedPage>")
    return "\n".join(parts)


def main():
    chars = "".join(sorted(set("".join(t[4] for t in TEXT))))
    font_bytes = subset_font(find_font(), chars)
    write_package(OUT, font_bytes, with_text=True)
    write_package(OUT_SHAPES, None, with_text=False)
    return 0


def write_package(out, font_bytes, with_text):

    content_types = (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">\n'
        '  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />\n'
        '  <Default Extension="fdseq" ContentType="application/vnd.ms-package.xps-fixeddocumentsequence+xml" />\n'
        '  <Default Extension="fdoc" ContentType="application/vnd.ms-package.xps-fixeddocument+xml" />\n'
        '  <Default Extension="fpage" ContentType="application/vnd.ms-package.xps-fixedpage+xml" />\n'
        + ('  <Default Extension="ttf" ContentType="application/vnd.ms-opentype" />\n' if with_text else '')
        + '</Types>\n')
    root_rels = (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">\n'
        '  <Relationship Id="rId1" Type="http://schemas.microsoft.com/xps/2005/06/fixedrepresentation" Target="/FixedDocumentSequence.fdseq" />\n'
        '</Relationships>\n')
    fdseq = (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<FixedDocumentSequence xmlns="%s">\n'
        '  <DocumentReference Source="/Documents/1/FixedDocument.fdoc" />\n'
        '</FixedDocumentSequence>\n' % XPS_NS)
    fdoc = (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<FixedDocument xmlns="%s">\n'
        '  <PageContent Source="/Documents/1/Pages/1.fpage" />\n'
        '</FixedDocument>\n' % XPS_NS)
    page_rels = (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">\n'
        '  <Relationship Id="rId1" Type="http://schemas.microsoft.com/xps/2005/06/required-resource" Target="%s" />\n'
        '</Relationships>\n' % FONT_PART)
    page = '<?xml version="1.0" encoding="utf-8"?>\n' + fixed_page(with_text) + "\n"

    os.makedirs(os.path.dirname(out), exist_ok=True)
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("[Content_Types].xml", content_types)
        z.writestr("_rels/.rels", root_rels)
        z.writestr("FixedDocumentSequence.fdseq", fdseq)
        z.writestr("Documents/1/FixedDocument.fdoc", fdoc)
        z.writestr("Documents/1/Pages/1.fpage", page)
        if with_text:
            z.writestr("Documents/1/Pages/_rels/1.fpage.rels", page_rels)
            z.writestr(FONT_PART.lstrip("/"), font_bytes)
    print("wrote %s (%d bytes%s)" % (os.path.relpath(out, ROOT), os.path.getsize(out),
                                     ", font subset %d bytes" % len(font_bytes) if font_bytes else ", no text"))


if __name__ == "__main__":
    sys.exit(main())
