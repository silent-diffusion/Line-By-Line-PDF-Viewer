"""Writes samples/sample-invoice.pdf: a two-page invoice with fictional line items for testing.

Uses only the standard library (hand-written PDF with the built-in Helvetica fonts).
Run:  python samples/make_sample_invoice.py
"""
from pathlib import Path

ITEMS = [
    ("PA-2011", "Copy paper, 8.5 x 11, 20 lb (case)", 10, 42.50),
    ("TN-0458", "Toner cartridge, black, high-yield", 4, 89.99),
    ("LB-4600", "Shipping labels, 4 x 6 (roll)", 6, 18.25),
    ("TP-0200", "Packing tape, 2 in, clear", 24, 3.40),
    ("BX-1212", "Corrugated box, 12 x 12 x 8", 200, 1.15),
    ("SF-1800", "Stretch film, 18 in x 1500 ft", 8, 24.75),
    ("GL-NL04", "Nitrile gloves, large (box of 100)", 12, 11.20),
    ("LB-PL10", "Pallet labels, pack of 50", 3, 15.00),
    ("MK-DE12", "Dry-erase markers, assorted (12)", 5, 7.80),
    ("CL-FL01", "Floor cleaner, 1 gal", 6, 13.95),
    ("PN-BL50", "Ballpoint pens, blue (box of 50)", 4, 9.60),
    ("ST-2200", "Stapler, heavy duty", 2, 34.99),
    ("SP-0500", "Staples, standard (5000)", 10, 2.15),
    ("FL-LT25", "File folders, letter (box of 100)", 3, 16.40),
    ("BN-0300", "3-ring binder, 3 in", 12, 6.85),
    ("TR-BLK1", "Trash bags, 33 gal (100)", 5, 21.30),
    ("PT-RL12", "Paper towels, 12 rolls", 8, 18.90),
    ("SN-HD08", "Hand sanitizer, 8 oz", 24, 2.75),
    ("BT-AA24", "Batteries, AA (24 pack)", 6, 15.49),
    ("EX-CRD6", "Extension cord, 6 ft", 4, 8.25),
    ("CL-WP40", "Disinfecting wipes (40 ct)", 18, 4.60),
    ("CF-GRD2", "Coffee, ground, 2 lb", 6, 17.80),
    ("CP-1612", "Paper cups, 12 oz (50)", 10, 5.95),
    ("ST-NT36", "Sticky notes, 3 x 3 (12 pads)", 5, 8.40),
    ("EN-1010", "Envelopes, #10 (500)", 2, 26.70),
    ("SH-MBL1", "Shelf bins, medium, blue", 20, 3.95),
    ("LB-THM4", "Thermal labels, 4 x 6 (4 rolls)", 3, 44.00),
    ("CT-BLD1", "Box cutter, retractable", 6, 4.85),
]
TAX_RATE = 0.07
PAGE_W, PAGE_H = 612, 792
ROW_H = 20
ROWS_PAGE_1 = 18


def esc(text: str) -> str:
    return text.replace("\\", "\\\\").replace("(", "\\(").replace(")", "\\)")


class Page:
    def __init__(self):
        self.ops = []

    def text(self, x, y, s, font="F1", size=10, gray=0.0):
        # y is measured from the top of the page, like the app does
        self.ops.append(f"{gray:.2f} g BT /{font} {size} Tf {x:.2f} {PAGE_H - y:.2f} Td ({esc(s)}) Tj ET")

    def text_right(self, right_x, y, s, font="F1", size=10, gray=0.0):
        char_w = 0.6 if font == "F3" else 0.52  # rough widths are fine for a test fixture
        self.text(right_x - len(s) * size * char_w, y, s, font, size, gray)

    def line(self, x1, y, x2, width=0.5, gray=0.85):
        self.ops.append(f"{gray:.2f} G {width} w {x1} {PAGE_H - y:.2f} m {x2} {PAGE_H - y:.2f} l S")

    def stream(self) -> bytes:
        return "\n".join(self.ops).encode("latin-1")


def table_header(p: Page, y: float):
    p.text(54, y, "ITEM", "F2", 8, 0.4)
    p.text(130, y, "DESCRIPTION", "F2", 8, 0.4)
    p.text_right(400, y, "QTY", "F2", 8, 0.4)
    p.text_right(476, y, "UNIT", "F2", 8, 0.4)
    p.text_right(558, y, "AMOUNT", "F2", 8, 0.4)
    p.line(54, y + 6, 558, 1.2, 0.1)


def item_row(p: Page, y: float, item):
    sku, desc, qty, unit = item
    p.text(54, y, sku, "F3", 9, 0.35)
    p.text(130, y, desc, "F1", 10)
    p.text_right(400, y, str(qty), "F3", 10)
    p.text_right(476, y, f"{unit:,.2f}", "F3", 10)
    p.text_right(558, y, f"{qty * unit:,.2f}", "F3", 10)
    p.line(54, y + 7, 558)


def build_pages():
    pages = []
    p1 = Page()
    p1.text(54, 72, "Northwind Supply Co.", "F2", 16)
    p1.text(54, 90, "4410 Commerce Pkwy, Suite 200", "F1", 10, 0.4)
    p1.text(54, 104, "Columbus, OH 43219", "F1", 10, 0.4)
    p1.text_right(558, 74, "INVOICE", "F2", 22)
    p1.text_right(558, 94, "INV-20418", "F3", 10)
    p1.text_right(558, 108, "Date: Oct 2, 2026   Due: Nov 1, 2026", "F1", 9, 0.4)
    p1.text(54, 150, "BILL TO", "F2", 8, 0.4)
    p1.text(54, 164, "Sample Company LLC", "F1", 10)
    p1.text(54, 178, "Accounts Payable", "F1", 10, 0.4)
    p1.text(260, 150, "PO NUMBER", "F2", 8, 0.4)
    p1.text(260, 164, "PO-7731", "F3", 10)
    p1.text(400, 150, "TERMS", "F2", 8, 0.4)
    p1.text(400, 164, "Net 30", "F1", 10)

    y = 230
    table_header(p1, y)
    y += 22
    for item in ITEMS[:ROWS_PAGE_1]:
        item_row(p1, y, item)
        y += ROW_H
    p1.text_right(558, 760, "Page 1 of 2", "F1", 8, 0.4)
    pages.append(p1)

    p2 = Page()
    p2.text(54, 60, "Northwind Supply Co.  ·  INV-20418 (continued)", "F1", 9, 0.4)
    y = 96
    table_header(p2, y)
    y += 22
    for item in ITEMS[ROWS_PAGE_1:]:
        item_row(p2, y, item)
        y += ROW_H

    subtotal = sum(q * u for _, _, q, u in ITEMS)
    tax = round(subtotal * TAX_RATE, 2)
    y += 16
    p2.text_right(476, y, "Subtotal", "F1", 10)
    p2.text_right(558, y, f"{subtotal:,.2f}", "F3", 10)
    y += 18
    p2.text_right(476, y, "Tax 7%", "F1", 10)
    p2.text_right(558, y, f"{tax:,.2f}", "F3", 10)
    y += 22
    p2.text_right(476, y, "Total due", "F2", 12)
    p2.text_right(558, y, f"${subtotal + tax:,.2f}", "F3", 12)
    p2.text(54, y + 60, "Remit to: Northwind Supply Co., PO Box 1180, Columbus, OH 43216", "F1", 9, 0.4)
    p2.text_right(558, 760, "Page 2 of 2", "F1", 8, 0.4)
    pages.append(p2)
    return pages


def write_pdf(path: Path, pages):
    objects = []  # index i -> object number i+1

    def add(body: bytes) -> int:
        objects.append(body)
        return len(objects)

    font1 = add(b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>")
    font2 = add(b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>")
    font3 = add(b"<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>")
    pages_num = len(objects) + 1 + 2 * len(pages) + 1  # reserved below
    page_nums = []
    for p in pages:
        data = p.stream()
        content = add(b"<< /Length %d >>\nstream\n" % len(data) + data + b"\nendstream")
        page_nums.append(add(
            (f"<< /Type /Page /Parent {pages_num} 0 R /MediaBox [0 0 {PAGE_W} {PAGE_H}] "
             f"/Resources << /Font << /F1 {font1} 0 R /F2 {font2} 0 R /F3 {font3} 0 R >> >> "
             f"/Contents {content} 0 R >>").encode()))
    kids = " ".join(f"{n} 0 R" for n in page_nums)
    catalog = add(f"<< /Type /Catalog /Pages {pages_num} 0 R >>".encode())
    assert add(f"<< /Type /Pages /Kids [{kids}] /Count {len(pages)} >>".encode()) == pages_num

    out = bytearray(b"%PDF-1.4\n%\xe2\xe3\xcf\xd3\n")
    offsets = []
    for i, body in enumerate(objects, start=1):
        offsets.append(len(out))
        out += f"{i} 0 obj\n".encode() + body + b"\nendobj\n"
    xref = len(out)
    out += f"xref\n0 {len(objects) + 1}\n0000000000 65535 f \n".encode()
    for off in offsets:
        out += f"{off:010d} 00000 n \n".encode()
    out += f"trailer\n<< /Size {len(objects) + 1} /Root {catalog} 0 R >>\nstartxref\n{xref}\n%%EOF\n".encode()
    path.write_bytes(out)


if __name__ == "__main__":
    target = Path(__file__).with_name("sample-invoice.pdf")
    write_pdf(target, build_pages())
    print(f"wrote {target}")
