"""Export the issue-to-PR report: python scripts/export-report.py.

Requires ReportLab (python -m pip install reportlab). Run from any directory.
Only the Markdown constructs used by this report are supported deliberately.
"""

import html
import posixpath
import re
from pathlib import Path
from urllib.parse import urlsplit

from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.platypus import (
    Flowable, KeepTogether, PageBreak, Paragraph, Preformatted,
    SimpleDocTemplate, Spacer, Table, TableStyle,
)


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "docs/report/issue-to-pr-report.md"
OUTPUT = SOURCE.with_suffix(".pdf")
EVIDENCE_REVISION = "f026376"
REPOSITORY = "https://github.com/UAI-VUT-Brno/0VP-26-27Z-StudentsGroup-Team-1"


def pdf_text(text):
    return (text.replace("→", "->").replace("↔", "<->")
            .replace("–", "-").replace("—", "-").replace("·", "|"))


def link_target(target):
    if urlsplit(target).scheme:
        return target
    path, separator, fragment = target.partition("#")
    local_path = (SOURCE.parent / path).resolve()
    if not local_path.exists():
        raise ValueError(f"Broken report link: {target}")
    relative = local_path.relative_to(ROOT).as_posix()
    return (f"{REPOSITORY}/blob/{EVIDENCE_REVISION}/"
            f"{posixpath.normpath(relative)}" + (separator + fragment))


def inline(text):
    # Tokenize before escaping so XML metacharacters remain literal in prose.
    pattern = r"\[([^\]]+)\]\(([^)]+)\)|`([^`]+)`|\*\*([^*]+)\*\*"
    result, end = [], 0
    for match in re.finditer(pattern, text):
        result.append(html.escape(pdf_text(text[end:match.start()])))
        label, target, code, bold = match.groups()
        if target is not None:
            result.append(f'<a href="{html.escape(link_target(target), quote=True)}" '
                          f'color="#175b91">{html.escape(pdf_text(label))}</a>')
        elif code is not None:
            result.append(f'<font face="Courier" size="8">'
                          f'{html.escape(pdf_text(code))}</font>')
        else:
            result.append(f'<b>{html.escape(pdf_text(bold))}</b>')
        end = match.end()
    result.append(html.escape(pdf_text(text[end:])))
    return "".join(result)


class PageBoundary(Flowable):
    """Invisible marker for checking that each source section fits one page."""

    def __init__(self, expected_page):
        super().__init__()
        self.expected_page = expected_page
        self.width = self.height = 0

    def draw(self):
        if self.canv.getPageNumber() != self.expected_page:
            raise ValueError(f"Report overflowed before source page {self.expected_page}")


def export():
    styles = getSampleStyleSheet()
    styles.add(ParagraphStyle("ReportBody", fontName="Helvetica", fontSize=9.3,
                              leading=12.6, spaceAfter=7, alignment=TA_LEFT))
    styles.add(ParagraphStyle("ReportCell", parent=styles["ReportBody"],
                              fontSize=8, leading=10.5, spaceAfter=0))
    styles.add(ParagraphStyle("ReportCode", fontName="Courier", fontSize=8,
                              leading=10, spaceAfter=9))
    styles["Title"].fontSize = 20
    styles["Title"].leading = 23
    styles["Title"].alignment = TA_LEFT
    styles["Heading2"].fontSize = 13
    styles["Heading2"].leading = 17
    styles["Heading2"].spaceBefore = 5
    styles["Heading2"].spaceAfter = 10
    width = A4[0] - 88
    lines = SOURCE.read_text(encoding="utf-8-sig").splitlines()
    story = [PageBoundary(1)]
    page, index = 1, 0
    while index < len(lines):
        line = lines[index]
        if not line.strip():
            index += 1
            continue
        if line == "<!-- pagebreak -->":
            page += 1
            story.extend([PageBreak(), PageBoundary(page)])
            index += 1
        elif line.startswith("# "):
            story.append(Paragraph(inline(line[2:]), styles["Title"]))
            index += 1
        elif line.startswith("## "):
            story.append(Paragraph(inline(line[3:]), styles["Heading2"]))
            index += 1
        elif line.startswith("```"):
            code = []
            index += 1
            while index < len(lines) and not lines[index].startswith("```"):
                code.append(pdf_text(lines[index]))
                index += 1
            story.append(KeepTogether([Preformatted("\n".join(code), styles["ReportCode"])]))
            index += 1
        elif line.startswith("|"):
            rows = []
            while index < len(lines) and lines[index].startswith("|"):
                cells = [cell.strip() for cell in lines[index].strip("|").split("|")]
                if not all(re.fullmatch(r":?-+:?", cell) for cell in cells):
                    rows.append(cells)
                index += 1
            count = len(rows[0])
            fractions = {2: [0.23, 0.77], 6: [0.16, 0.19, 0.18, 0.12, 0.16, 0.19],
                         5: [0.24, 0.19, 0.19, 0.19, 0.19]}[count]
            rendered = [[Paragraph(inline(cell), styles["ReportCell"]) for cell in row]
                        for row in rows]
            table = Table(rendered, colWidths=[width * part for part in fractions],
                          repeatRows=1, hAlign="LEFT")
            table.setStyle(TableStyle([
                ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#e6edf4")),
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
                ("LINEBELOW", (0, 0), (-1, 0), 0.6, colors.HexColor("#7890a5")),
                ("LINEBELOW", (0, 1), (-1, -1), 0.3, colors.HexColor("#d2dce5")),
                ("LEFTPADDING", (0, 0), (-1, -1), 6),
                ("RIGHTPADDING", (0, 0), (-1, -1), 6),
                ("TOPPADDING", (0, 0), (-1, -1), 6),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
            ]))
            story.extend([KeepTogether([table]), Spacer(1, 8)])
        else:
            paragraph = []
            while index < len(lines) and lines[index].strip():
                paragraph.append(lines[index])
                index += 1
            text = " ".join(paragraph)
            if text.startswith("- "):
                story.append(Paragraph(inline(text[2:]), styles["ReportBody"], bulletText="-"))
            else:
                story.append(Paragraph(inline(text), styles["ReportBody"]))
    story.append(PageBoundary(page))
    if page != 6:
        raise ValueError("Expected six source pages")

    def decorate(canvas, doc):
        canvas.saveState()
        canvas.setStrokeColor(colors.HexColor("#d2dce5"))
        canvas.line(44, 39, A4[0] - 44, 39)
        canvas.setFont("Helvetica", 8)
        canvas.setFillColor(colors.HexColor("#526476"))
        canvas.drawString(44, 26, "0VP Team 1 | Technical and benchmark report | 23 November 2026")
        canvas.drawRightString(A4[0] - 44, 26, f"{doc.page} / 6")
        canvas.restoreState()

    doc = SimpleDocTemplate(str(OUTPUT), pagesize=A4, rightMargin=44, leftMargin=44,
                            topMargin=38, bottomMargin=51,
                            title="From GitHub issue to verified pull request",
                            author="0VP Team 1")
    doc.build(story, onFirstPage=decorate, onLaterPages=decorate)
    print(f"Exported six pages: {OUTPUT}")


if __name__ == "__main__":
    export()
