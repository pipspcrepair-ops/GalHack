#!/usr/bin/env python3
"""GalHack faction sigils. Original artwork, drawn here. Run: python build_sigils.py

One bold symbol per faction in DESIGN.md section 17, shaped by that faction's
look in section 6's table. A clean version in the faction's colours, and a
one-colour stencil for hulls, doors and for spraying over older signs.

Each sigil is a list of paint steps in order:
  ("ink", colour, op)    solid in both versions
  ("cut", op)            removes everything beneath, in both versions
  ("inlay", colour, op)  a colour in the clean version, a hole in the stencil
"""
import math
import random
import shutil
import zlib

from PIL import Image, ImageDraw

from build_signs import ROOT, RENDER_TMP, STENCIL, T, circ, font, group, line, path, poly, rect, render, stroke

OUT = ROOT / "sigils"
BG = "#E9E6DC"
SCALE = 1.05


def spline(pts, steps=12):
    """Catmull-Rom through pts."""
    p = [pts[0]] + list(pts) + [pts[-1]]
    out = []
    for i in range(1, len(p) - 2):
        for k in range(steps):
            t = k / steps
            a = [0.5 * (2 * p[i][c] + (p[i + 1][c] - p[i - 1][c]) * t
                        + (2 * p[i - 1][c] - 5 * p[i][c] + 4 * p[i + 1][c] - p[i + 2][c]) * t * t
                        + (3 * p[i][c] - p[i - 1][c] - 3 * p[i + 1][c] + p[i + 2][c]) * t ** 3) for c in (0, 1)]
            out.append(tuple(a))
    out.append(pts[-1])
    return out


def ribbon(pts, w0, w1, curve=1.0):
    """A filled tapering limb through pts, as one polygon."""
    c = spline(pts)
    left, right = [], []
    for i, (x, y) in enumerate(c):
        a, b = c[max(0, i - 1)], c[min(len(c) - 1, i + 1)]
        dx, dy = b[0] - a[0], b[1] - a[1]
        d = math.hypot(dx, dy) or 1
        t = i / (len(c) - 1)
        h = (w0 + (w1 - w0) * t ** curve) / 2
        left.append((x - dy / d * h, y + dx / d * h))
        right.append((x + dy / d * h, y - dx / d * h))
    return poly(left + right[::-1]), c


def s_vharn():
    # Dominion: spread wing-cases, one long and one short, nothing balanced.
    SHELL, GLOW = "#1C1A2B", "#2FE0B8"
    a, _ = ribbon([(-14, 52), (34, -6), (72, -58), (116, -110)], 66, 2, 1.0)
    b, _ = ribbon([(-18, 50), (-58, 8), (-88, -22), (-112, -66)], 52, 2, 1.0)
    c, _ = ribbon([(-14, 44), (-8, 74), (-4, 112)], 44, 2, 1.0)
    return [("ink", SHELL, a), ("ink", SHELL, b), ("ink", SHELL, c), ("inlay", GLOW, circ(-14, 34, 19))]


def s_kessari():
    # Clans: a lattice of identical modules, mirrored both ways, one status light.
    METAL, LIGHT = "#55636C", "#FFB000"
    grid = ["10101", "01110", "11L11", "01110", "10101"]
    pitch, k = 44, 35
    ops = []
    for a, b, c2, d in ((-2, -2, -1, -1), (2, -2, 1, -1), (-2, 2, -1, 1), (2, 2, 1, 1)):
        ops.append(("ink", METAL, line([(a * pitch, b * pitch), (c2 * pitch, d * pitch)], 18)))
    for r, row in enumerate(grid):
        for q, cell in enumerate(row):
            x, y = (q - 2) * pitch, (r - 2) * pitch
            if cell == "1":
                ops.append(("ink", METAL, rect(x - k / 2, y - k / 2, k, k, 3)))
            elif cell == "L":
                ops.append(("inlay", LIGHT, rect(x - k / 2, y - k / 2, k, k, 3)))
    return ops


def s_meridian():
    # Combine: one massive square with its corner taken, and a planet in the corner.
    BLUE, RED_ = "#1D55B8", "#D8281E"
    return [("ink", BLUE, rect(-108, -108, 216, 216)), ("cut", circ(108, -108, 112)), ("ink", RED_, circ(66, -66, 50))]


def _arc(r, a0, a1):
    n = 40
    return [(r * math.cos(math.radians(a0 + (a1 - a0) * i / n)), r * math.sin(math.radians(a0 + (a1 - a0) * i / n)))
            for i in range(n + 1)]


def s_ledger():
    # Exchange: a coin split on a step, one side given, one side got.
    TEAL, ORANGE = "#0E6B6E", "#F0781A"
    r = 110
    top, bot = -math.degrees(math.acos(26 / r)), 180 - math.degrees(math.acos(26 / r))
    step = [(-26, 0), (26, 0)]
    return [("ink", TEAL, poly(_arc(r, 360 + top, bot) + step)), ("ink", ORANGE, poly(_arc(r, top, bot) + step)),
            ("cut", line([(26, -120), (26, 0), (-26, 0), (-26, 120)], 14))]


def s_reavers():
    # Reavers: every hull counts. Four kills and the one that closes the gate.
    RUST, GOLD = "#A5431B", "#FFC400"
    steps = [("ink", RUST, rect(x, y, 32, h)) for x, y, h in ((-92, -96, 186), (-40, -108, 214), (12, -92, 190), (64, -104, 204))]
    return steps + [("cut", line([(-116, 82), (116, -70)], 56)), ("ink", GOLD, line([(-112, 80), (112, -68)], 34))]


def s_freeholders():
    # Freeholders: a lantern. Somebody left the light on, and that is all a safe place is.
    OLIVE, GOLD = "#66743F", "#FFC400"
    return [("ink", OLIVE, stroke("M-44 -62 A44 42 0 0 1 44 -62", 22)),
            ("ink", OLIVE, poly([(-56, -72), (56, -72), (72, -46), (-72, -46)])),
            ("ink", OLIVE, rect(-70, -42, 140, 112, 30)),
            ("inlay", GOLD, rect(-46, -22, 92, 72, 22)),
            ("ink", OLIVE, rect(-82, 72, 164, 38, 6))]


def s_bloom():
    # Grown, not built. Nothing here repeats: limbs of unequal length leave an
    # off-centre gap, and there is no axis to line it up by.
    SICK, BRUISE = "#AEB934", "#4F2A58"
    r = random.Random(zlib.crc32(b"bloom"))
    cx, cy = -12, 8
    limbs = [
        (-160, 1.0, 66), (-112, 0.8, 84), (-58, 1.05, 98), (-4, 0.72, 74),
        (44, 1.1, 92), (104, 0.62, 62), (150, 0.92, 88),
    ]
    ops = []
    for deg, bend, reach in limbs:
        a = math.radians(deg)
        pts = [(cx + 24 * math.cos(a), cy + 24 * math.sin(a))]
        for t in (0.45, 0.75, 1.0):
            b = a + bend * t * t * 1.5
            d = 26 + reach * t
            pts.append((cx + d * math.cos(b), cy + d * math.sin(b)))
        shape, c = ribbon(pts, 40, 6, 0.8)
        ops.append(("ink", BRUISE, shape))
        tip = c[-1]
        ops.append(("ink", SICK, circ(tip[0], tip[1], r.uniform(9, 15))))
    ops.append(("ink", BRUISE, circ(cx, cy, 50)))
    ops.append(("inlay", SICK, circ(cx + 4, cy - 3, 38)))
    ops.append(("cut", circ(cx + 9, cy - 7, 25)))
    return ops


SIGILS = [
    ("vharn", "Vharn Dominion", s_vharn, "exit"),
    ("kessari", "Kessari Clans", s_kessari, "arrow"),
    ("meridian", "Meridian Combine", s_meridian, "ladder"),
    ("ledger", "Ledger Exchange", s_ledger, "stairs"),
    ("reavers", "Carrion Reavers", s_reavers, "restricted"),
    ("freeholders", "Freeholders", s_freeholders, "lift"),
    ("bloom", "Hollow Bloom", s_bloom, "hazard"),
]
# The Hollow Bloom keeps its files but stays off the review sheet for now.
ON_SHEET = [row for row in SIGILS if row[0] != "bloom"]


def build_svg(title, steps, stencil):
    mask, paint = [], []
    for step in steps:
        kind = step[0]
        op = step[-1]
        if kind == "cut" or (kind == "inlay" and stencil):
            mask.append(op.replace("{INK}", "#000"))
        else:
            mask.append(op.replace("{INK}", "#fff"))
        if not stencil and kind != "cut":
            paint.append(op.replace("{INK}", step[1]))
    tr = f"translate({T / 2} {T / 2}) scale({SCALE})"
    if stencil:
        body = f'<rect width="{T}" height="{T}" fill="{STENCIL}" mask="url(#s)"/>'
    else:
        body = f'<g mask="url(#s)"><g transform="{tr}">{"".join(paint)}</g></g>'
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {T} {T}" width="{T}" height="{T}"><title>{title}</title>'
            f'<mask id="s" maskUnits="userSpaceOnUse" x="0" y="0" width="{T}" height="{T}">'
            f'<g transform="{tr}">{"".join(mask)}</g></mask>{body}</svg>\n')


def build():
    for sub in ("svg", "svg-stencil", "png"):   # never the attempts folders beside them
        shutil.rmtree(OUT / sub, ignore_errors=True)
        (OUT / sub).mkdir(parents=True)
    for sid, label, fn, _sign in SIGILS:
        steps = fn()
        (OUT / "svg" / f"{sid}.svg").write_text(build_svg("Sigil", steps, False), encoding="utf-8")
        (OUT / "svg-stencil" / f"{sid}.svg").write_text(build_svg("Sigil stencil", steps, True), encoding="utf-8")
    clean, sten = RENDER_TMP / "sigils-clean", RENDER_TMP / "sigils-stencil"
    for d in (clean, sten):
        shutil.rmtree(d, ignore_errors=True)
    render(OUT / "svg", clean, 4.0)
    render(OUT / "svg-stencil", sten, 4.0)
    a = {sid: Image.open(clean / f"{sid}.png").convert("RGBA") for sid, *_ in SIGILS}
    b = {sid: Image.open(sten / f"{sid}.png").convert("RGBA") for sid, *_ in SIGILS}
    for sid, im in a.items():
        im.resize((256, 256), Image.LANCZOS).save(OUT / "png" / f"{sid}.png")

    cols, tile, gap = len(ON_SHEET), 270, 36
    margin = (2000 - (cols * tile + (cols - 1) * gap)) // 2
    y_clean, y_sten, y_small, y_over = 150, 510, 830, 960
    sheet = Image.new("RGB", (2000, y_over + tile + 40), BG)
    d = ImageDraw.Draw(sheet)
    d.text((margin, 46), "GalHack faction sigils: final picks", font=font(60, True), fill="#14171A")
    d.text((margin, y_sten - 30), "Stencil", font=font(21), fill="#555555")
    d.text((margin, y_small - 34), "At 64 pixels, clean and stencil", font=font(21), fill="#555555")
    d.text((margin, y_over - 34), "Stencilled over an old sign", font=font(21), fill="#555555")
    for i, (sid, label, _fn, sign) in enumerate(ON_SHEET):
        x = margin + i * (tile + gap)
        im = a[sid].resize((tile, tile), Image.LANCZOS)
        sheet.paste(im, (x, y_clean), im)
        d.text((x + tile / 2, y_clean + tile + 6), label, font=font(21, True), fill="#14171A", anchor="ma")
        im = b[sid].resize((tile, tile), Image.LANCZOS)
        sheet.paste(im, (x, y_sten), im)
        sm = a[sid].resize((64, 64), Image.BOX)
        sheet.paste(sm, (x + tile // 2 - 76, y_small), sm)
        sm = b[sid].resize((64, 64), Image.BOX)
        sheet.paste(sm, (x + tile // 2 + 12, y_small), sm)
        tile_im = Image.open(ROOT / "png" / f"{sign}.png").convert("RGBA").resize((tile, tile), Image.LANCZOS)
        over = b[sid].resize((tile, tile), Image.LANCZOS)
        spray = Image.new("RGBA", over.size, "#14171A")
        spray.putalpha(over.getchannel("A").point(lambda v: int(v * 0.88)))
        tile_im.alpha_composite(spray)
        sheet.paste(tile_im, (x, y_over), tile_im)
    sheet.save(ROOT / "sigils-sheet.png", optimize=True)
    print(len(SIGILS), "sigils built")


if __name__ == "__main__":
    build()
