#!/usr/bin/env python3
"""GalHack deck and zone markers. Original artwork, drawn here. Run: python build_zones.py

A big place shows where you are with no numerals or words. A band runs edge to
edge, so tiles join along a wall. Its colour and pattern name the wing. Marks
above it count decks up from the main deck, marks below count decks down.
A thin bar is one, a wide block is five. A bare band is the main deck.
"""
import math
import shutil

from PIL import Image, ImageDraw

from build_signs import PAPER, RENDER_TMP, ROOT, STENCIL, T, font, render

OUT = ROOT / "zones"
PLATE = "#23272C"
BAND_Y, BAND_H = 92, 72
BAR, BLOCK, GAP = 24, 64, 16
UP_Y, DOWN_Y, MARK_H = 14, 176, 66


def r(x, y, w, h, c):
    return f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="{c}"/>'


def p(pts, c):
    return '<polygon points="' + " ".join(f"{x},{y}" for x, y in pts) + f'" fill="{c}"/>'


# Each pattern repeats every 64 units, so a run of tiles joins without a seam.
# The pattern matters: on a stencil the colour is gone and only this is left.
def pat_solid(c):
    return ""


def pat_stripe(c):
    return r(0, BAND_Y + 28, T, 16, c)


def pat_check(c):
    return "".join(r(x + (32 if row else 0), BAND_Y + row * 36, 32, 36, c) for row in (0, 1) for x in range(0, T, 64))


def pat_chevron(c):
    top, mid, bot = BAND_Y, BAND_Y + BAND_H // 2, BAND_Y + BAND_H
    return "".join(p([(x, top), (x + 20, top), (x + 56, mid), (x + 20, bot), (x, bot), (x + 36, mid)], c)
                   for x in range(-64, T + 64, 64))


def pat_diagonal(c):
    top, bot = BAND_Y, BAND_Y + BAND_H
    return "".join(p([(x, bot), (x + 28, bot), (x + 28 + 72, top), (x + 72, top)], c) for x in range(-128, T + 64, 64))


def pat_notch(c):
    return "".join(r(x + 16, BAND_Y + BAND_H - 24, 32, 24, c) for x in range(0, T, 64))


#        id        colour     pattern
WINGS = {
    "cobalt":  ("#1F5FBF", pat_solid),
    "orange":  ("#F07A1A", pat_stripe),
    "yellow":  ("#FFC400", pat_check),
    "green":   ("#1E8449", pat_chevron),
    "magenta": ("#D63C8C", pat_diagonal),
    "white":   ("#F4F1E8", pat_notch),
}

#  wing, signed count
SAMPLES = [("cobalt", 1), ("orange", 1), ("yellow", 1), ("green", 1), ("magenta", 1), ("white", 1),
           ("cobalt", 0), ("cobalt", 2), ("cobalt", 3), ("cobalt", 4), ("cobalt", 5), ("cobalt", 7),
           ("cobalt", -1), ("cobalt", -6)]


def marks(count, ink):
    c = abs(count)
    widths = [BLOCK] * (c // 5) + [BAR] * (c % 5)
    if not widths:
        return ""
    total = sum(widths) + GAP * (len(widths) - 1)
    x, y, out = (T - total) // 2, UP_Y if count > 0 else DOWN_Y, ""
    for w in widths:
        out += r(x, y, w, MARK_H, ink)
        x += w + GAP
    return out


def sid(wing, count):
    return f"{wing}_{'main' if count == 0 else ('up' if count > 0 else 'down') + str(abs(count))}"


def label(wing, count):
    w = wing.capitalize()
    if count == 0:
        return f"{w} wing, main deck"
    return f"{w} wing, {abs(count)} {'up' if count > 0 else 'down'}"


def open_svg(title):
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {T} {T}" width="{T}" height="{T}"><title>{title}</title>'


def clean(wing, count):
    col, pat = WINGS[wing]
    return (open_svg("Zone marker") + r(0, 0, T, T, PLATE) + r(0, BAND_Y, T, BAND_H, col) + pat(PLATE)
            + marks(count, PAPER) + "</svg>\n")


def stencil(wing, count):
    _col, pat = WINGS[wing]
    return (open_svg("Zone marker stencil") + f'<mask id="s" maskUnits="userSpaceOnUse" x="0" y="0" width="{T}" height="{T}">'
            + r(0, BAND_Y, T, BAND_H, "#fff") + pat("#000") + marks(count, "#fff") + "</mask>"
            + f'<rect width="{T}" height="{T}" fill="{STENCIL}" mask="url(#s)"/></svg>\n')


def build():
    shutil.rmtree(OUT, ignore_errors=True)
    for sub in ("svg", "svg-stencil", "png"):
        (OUT / sub).mkdir(parents=True)
    for wing, count in SAMPLES:
        (OUT / "svg" / f"{sid(wing, count)}.svg").write_text(clean(wing, count), encoding="utf-8")
        (OUT / "svg-stencil" / f"{sid(wing, count)}.svg").write_text(stencil(wing, count), encoding="utf-8")
    # extras used only to show a corridor on the sheet
    tmp = RENDER_TMP / "zones-extra"
    shutil.rmtree(tmp, ignore_errors=True)
    tmp.mkdir(parents=True)
    for w in ("magenta", "green"):
        (tmp / f"{w}_2.svg").write_text(clean(w, 2), encoding="utf-8")
    a, b, c = RENDER_TMP / "zones-a", RENDER_TMP / "zones-b", RENDER_TMP / "zones-c"
    for d in (a, b, c):
        shutil.rmtree(d, ignore_errors=True)
    render(OUT / "svg", a, 4.0)
    render(OUT / "svg-stencil", b, 4.0)
    render(tmp, c, 4.0)
    tiles = {sid(w, n): Image.open(a / f"{sid(w, n)}.png").convert("RGBA") for w, n in SAMPLES}
    stens = {sid(w, n): Image.open(b / f"{sid(w, n)}.png").convert("RGBA") for w, n in SAMPLES}
    for k, im in tiles.items():
        im.resize((256, 256), Image.LANCZOS).save(OUT / "png" / f"{k}.png")

    cols, tile, gx, gy, lab = 7, 230, 30, 26, 38
    margin = (2000 - (cols * tile + (cols - 1) * gx)) // 2
    head = 214
    rows = math.ceil(len(SAMPLES) / cols)
    y2 = head + rows * (tile + lab + gy) + 14
    sheet = Image.new("RGB", (2000, y2 + 640), "white")
    d = ImageDraw.Draw(sheet)
    ink = "#14171A"
    d.text((margin, 46), "GalHack deck and zone markers: draft 3", font=font(60, True), fill=ink)
    d.text((margin, 122), "Colour and pattern = which wing.   Above the band = decks up.   Below the band = decks down.   "
           "Bare band = main deck.", font=font(23), fill="#444444")
    d.text((margin, 156), "Thin bar = 1.   Wide block = 5.   Add them up: block and two bars = 7.", font=font(23), fill="#444444")
    for i, (w, n) in enumerate(SAMPLES):
        x = margin + (i % cols) * (tile + gx)
        y = head + (i // cols) * (tile + lab + gy)
        im = tiles[sid(w, n)].resize((tile, tile), Image.LANCZOS)
        sheet.paste(im, (x, y), im)
        d.text((x + tile / 2, y + tile + 7), label(w, n), font=font(20), fill=ink, anchor="ma")
    d.text((margin, y2), "Along a wall: tiles join into one continuous band", font=font(21), fill="#555555")
    run = [tiles["cobalt_up2"]] * 4 + [Image.open(c / "magenta_2.png").convert("RGBA")] * 4
    for i, im in enumerate(run):
        small = im.resize((230, 230), Image.LANCZOS)
        sheet.paste(small, (margin + i * 230, y2 + 34), small)
    y3 = y2 + 290
    d.text((margin, y3), "Stencils, where the pattern alone names the wing", font=font(21), fill="#555555")
    for i, w in enumerate(("cobalt", "orange", "yellow", "green", "magenta", "white")):
        im = stens[sid(w, 1)].resize((150, 150), Image.LANCZOS)
        sheet.paste(im, (margin + i * 175, y3 + 34), im)
    y4 = y3 + 200
    d.text((margin, y4), "At 64 pixels", font=font(21), fill="#555555")
    for i, (w, n) in enumerate(SAMPLES):
        im = tiles[sid(w, n)].resize((64, 64), Image.BOX)
        sheet.paste(im, (margin + i * 84, y4 + 32), im)
    sheet.save(ROOT / "zones-sheet.png", optimize=True)
    print(len(SAMPLES), "zone markers built")


if __name__ == "__main__":
    build()
