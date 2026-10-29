"""Kleine Hilfsbibliothek für die Lern-PDFs in docs/ (A4, DejaVu Sans, Home-Control-Stil).

Benutzung (siehe lernunterlage_api_schluessel.py):

    from lernpdf import Doc
    d = Doc("Titel", "Untertitel", footer="Home Control · Thema · Lernunterlage")
    d.h1("1  Überschrift"); d.p("Text mit <b>fett</b>."); d.save("docs/Lernunterlage-X.pdf")

Braucht: pip install reportlab  und die Schrift DejaVu Sans (unter Linux meist vorhanden).
"""
from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    BaseDocTemplate, Frame, KeepTogether, PageBreak, PageTemplate, Paragraph, Preformatted,
    Spacer, Table, TableStyle,
)

FONT_DIR = "/usr/share/fonts/truetype/dejavu/"
pdfmetrics.registerFont(TTFont("Sans", FONT_DIR + "DejaVuSans.ttf"))
pdfmetrics.registerFont(TTFont("Sans-Bold", FONT_DIR + "DejaVuSans-Bold.ttf"))
pdfmetrics.registerFont(TTFont("Sans-Oblique", FONT_DIR + "DejaVuSans-Oblique.ttf"))
pdfmetrics.registerFont(TTFont("Mono", FONT_DIR + "DejaVuSansMono.ttf"))
pdfmetrics.registerFontFamily("Sans", normal="Sans", bold="Sans-Bold", italic="Sans-Oblique", boldItalic="Sans-Bold")

DARK = colors.HexColor("#1b2433")
ACCENT = colors.HexColor("#0e7490")
SOFT = colors.HexColor("#e6f4f8")
GRID = colors.HexColor("#c9d3df")
HEAD = colors.HexColor("#eef2f7")
CODE_BG = colors.HexColor("#f3f5f8")
WARN_BG = colors.HexColor("#fff6dd")

BODY = ParagraphStyle("body", fontName="Sans", fontSize=9.5, leading=14, textColor=DARK, alignment=TA_LEFT, spaceAfter=6)
H1 = ParagraphStyle("h1", parent=BODY, fontName="Sans-Bold", fontSize=16, leading=20, spaceBefore=10, spaceAfter=8)
H2 = ParagraphStyle("h2", parent=BODY, fontName="Sans-Bold", fontSize=11.5, leading=15, textColor=ACCENT, spaceBefore=8, spaceAfter=4)
CELL = ParagraphStyle("cell", parent=BODY, fontSize=8.5, leading=11.5, spaceAfter=0)
CELL_B = ParagraphStyle("cellb", parent=CELL, fontName="Sans-Bold")
CODE = ParagraphStyle("code", fontName="Mono", fontSize=8, leading=10.5, textColor=DARK)
BULLET = ParagraphStyle("bullet", parent=BODY, leftIndent=14, bulletIndent=3, spaceAfter=3)


class Doc:
    def __init__(self, title, subtitle, footer):
        self.title, self.subtitle, self.footer = title, subtitle, footer
        self.story = []

    # ---------- Bausteine ----------
    def h1(self, text):
        self.story.append(Paragraph(text, H1))

    def h2(self, text):
        self.story.append(Paragraph(text, H2))

    def p(self, text):
        self.story.append(Paragraph(text, BODY))

    def bullets(self, items):
        for item in items:
            self.story.append(Paragraph(item, BULLET, bulletText="•"))

    def numbered(self, items):
        for i, item in enumerate(items, 1):
            self.story.append(Paragraph(item, BULLET, bulletText=f"{i}."))

    def code(self, text):
        box = Table([[Preformatted(text.strip("\n"), CODE)]], colWidths=[170 * mm])
        box.setStyle(TableStyle([
            ("BACKGROUND", (0, 0), (-1, -1), CODE_BG), ("BOX", (0, 0), (-1, -1), 0.5, GRID),
            ("LEFTPADDING", (0, 0), (-1, -1), 8), ("TOPPADDING", (0, 0), (-1, -1), 6),
            ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
        ]))
        self.story += [box, Spacer(1, 8)]

    def merksatz(self, text, warn=False):
        box = Table([[Paragraph(text, CELL)]], colWidths=[170 * mm])
        box.setStyle(TableStyle([
            ("BACKGROUND", (0, 0), (-1, -1), WARN_BG if warn else SOFT),
            ("LINEBEFORE", (0, 0), (0, -1), 3, colors.HexColor("#d97706") if warn else ACCENT),
            ("LEFTPADDING", (0, 0), (-1, -1), 10), ("TOPPADDING", (0, 0), (-1, -1), 7),
            ("BOTTOMPADDING", (0, 0), (-1, -1), 7),
        ]))
        self.story += [box, Spacer(1, 8)]

    def table(self, rows, widths):
        data = [[Paragraph(str(c), CELL_B if r == 0 else CELL) for c in row] for r, row in enumerate(rows)]
        t = Table(data, colWidths=[w * 170 * mm for w in widths], repeatRows=1)
        t.setStyle(TableStyle([
            ("BACKGROUND", (0, 0), (-1, 0), HEAD), ("GRID", (0, 0), (-1, -1), 0.4, GRID),
            ("VALIGN", (0, 0), (-1, -1), "TOP"), ("TOPPADDING", (0, 0), (-1, -1), 4),
            ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
        ]))
        self.story += [t, Spacer(1, 8)]

    def page_break(self):
        self.story.append(PageBreak())

    # ---------- Ausgabe ----------
    def save(self, path):
        def decorate(canvas, doc):
            canvas.saveState()
            canvas.setFont("Sans", 7.5)
            canvas.setFillColor(colors.HexColor("#6b778a"))
            canvas.drawString(20 * mm, 12 * mm, self.footer)
            canvas.drawRightString(A4[0] - 20 * mm, 12 * mm, f"Seite {doc.page}")
            canvas.setStrokeColor(GRID)
            canvas.line(20 * mm, 16 * mm, A4[0] - 20 * mm, 16 * mm)
            canvas.restoreState()

        doc = BaseDocTemplate(path, pagesize=A4, leftMargin=20 * mm, rightMargin=20 * mm,
                              topMargin=18 * mm, bottomMargin=22 * mm, title=self.title,
                              author="Home Control")
        doc.addPageTemplates([PageTemplate(
            id="main", frames=[Frame(doc.leftMargin, doc.bottomMargin, doc.width, doc.height, id="f")],
            onPage=decorate)])
        title = [
            Spacer(1, 4 * mm),
            Paragraph(self.title, ParagraphStyle("t", parent=H1, fontSize=22, leading=27, spaceBefore=0)),
            Paragraph(self.subtitle, ParagraphStyle("st", parent=BODY, fontSize=11, textColor=ACCENT, spaceAfter=14)),
        ]
        doc.build(title + self.story)
