#!/usr/bin/env python3
"""Sigil attempts: three new ideas per faction, for review beside the current one.

Run after build_sigils.py:  python build_sigil_attempts.py
Writes sigils/attempts/ and sigils-attempts.png. Touches nothing else.
The sheet is a record of that round: its Current column showed the sigils of the
time, so rebuilding now would show the final picks there instead.
Each attempt is one strong shape in one or two colours, meant to be told apart
from far away on a hull. All original to GalHack.
"""
import math
import shutil

from PIL import Image, ImageDraw

from build_signs import ROOT, RENDER_TMP, circ, font, group, line, path, poly, rect, render, stroke
from build_sigils import OUT as SIGILS, ribbon, build_svg

OUT = SIGILS / "attempts"
SHELL, GLOW = "#1C1A2B", "#2FE0B8"
STEEL, AMBER = "#46525A", "#FFB000"
BLUE, RED_, YEL = "#1D55B8", "#D8281E", "#FFC400"
TEAL, ORANGE, GOLD = "#0E6B6E", "#F0781A", "#FFC400"
RUST, DARK = "#A5431B", "#241C18"
OLIVE, OCHRE = "#66743F", "#C69A42"


# --- Vharn Dominion: alien, segmented, insect-like, sweeping and asymmetric -----
def v_shell():
    # A segmented shell, seen from above, leaning.
    body = "M0 -112 C60 -72 84 -10 70 50 C60 90 30 108 0 114 C-30 108 -54 88 -60 50 C-70 -6 -44 -76 0 -112 Z"
    tilt = lambda op: group([op], rotate=18)
    steps = [("ink", SHELL, tilt(path(body)))]
    for y, d in ((-42, 16), (6, 22), (56, 22)):
        steps.append(("cut", tilt(stroke(f"M-90 {y} Q0 {y + d * 2} 90 {y}", 13))))
    return steps + [("inlay", GLOW, tilt(poly([(-9, -86), (5, -86), (9, -48), (-9, -46)])))]


def v_talons():
    # Three unequal blades sweeping the same way, biggest first.
    a, _ = ribbon([(-100, 104), (-82, 10), (-10, -62), (104, -104)], 62, 2, 1.1)
    b, _ = ribbon([(-30, 106), (-10, 42), (46, -6), (112, -32)], 48, 2, 1.1)
    c, _ = ribbon([(44, 108), (58, 72), (86, 48), (114, 38)], 36, 2, 1.1)
    return [("ink", SHELL, a), ("ink", SHELL, b), ("ink", SHELL, c),
            ("inlay", GLOW, poly([(-78, 62), (-62, 4), (-34, -24), (-58, 26)]))]


def v_wings():
    # Spread wing-cases: one long, one short, and nothing balanced about it.
    a, _ = ribbon([(-14, 52), (34, -6), (72, -58), (116, -110)], 66, 2, 1.0)
    b, _ = ribbon([(-18, 50), (-58, 8), (-88, -22), (-112, -66)], 52, 2, 1.0)
    c, _ = ribbon([(-14, 44), (-8, 74), (-4, 112)], 44, 2, 1.0)
    return [("ink", SHELL, a), ("ink", SHELL, b), ("ink", SHELL, c), ("inlay", GLOW, circ(-14, 34, 19))]


# --- Kessari Clans: machine, modular, perfectly symmetrical, one status light ---
def k_ring():
    # Eight identical modules holding a single light.
    steps = []
    for i in range(8):
        steps.append(("ink", STEEL, group([rect(-25, -112, 50, 50, 3)], rotate=i * 45)))
    return steps + [("ink", AMBER, circ(0, 0, 40))]


def k_diamond():
    # A three-by-three lattice stood on its point, lit in the middle.
    steps = []
    for r in (-1, 0, 1):
        for q in (-1, 0, 1):
            kind = ("inlay", AMBER) if (r, q) == (0, 0) else ("ink", STEEL)
            steps.append(kind + (group([rect(q * 60 - 26, r * 60 - 26, 52, 52, 3)], rotate=45),))
    return steps


def k_nut():
    # The simplest machine part there is, with a light where the bolt goes.
    pts = [(112 * math.cos(math.radians(a)), 112 * math.sin(math.radians(a))) for a in range(0, 360, 60)]
    return [("ink", STEEL, poly(pts)), ("cut", circ(0, 0, 58)), ("ink", AMBER, circ(0, 0, 34))]


# --- Meridian Combine: clean massive blocks, long spines, bright primaries ------
def m_halves():
    # A world cut on its meridian and slid: two halves, offset along the line.
    return [("ink", BLUE, path("M-7 -112 A90 90 0 0 0 -7 68 Z")),
            ("ink", YEL, path("M7 -68 A90 90 0 0 1 7 112 Z"))]


def m_steps():
    # A stepped mass as big as a colony, with one long red spine rising from it.
    return [("ink", BLUE, poly([(-112, 110), (-112, 58), (-74, 58), (-74, 8), (-40, 8), (-40, -42), (40, -42),
                                (40, 8), (74, 8), (74, 58), (112, 58), (112, 110)])),
            ("ink", RED_, rect(-15, -114, 30, 64))]


def m_corner():
    # One massive square with its corner taken, and a planet in the corner.
    return [("ink", BLUE, rect(-108, -108, 216, 216)), ("cut", circ(108, -108, 112)), ("ink", RED_, circ(66, -66, 50))]


def _arc(r, a0, a1):
    n = 40
    return [(r * math.cos(math.radians(a0 + (a1 - a0) * i / n)), r * math.sin(math.radians(a0 + (a1 - a0) * i / n)))
            for i in range(n + 1)]


def l_coin():
    # A coin split on a step: one side given, one side got.
    r = 110
    top, bot = -math.degrees(math.acos(26 / r)), 180 - math.degrees(math.acos(26 / r))
    step = [(-26, 0), (26, 0)]
    left = poly(_arc(r, 360 + top, bot) + step)
    right = poly(_arc(r, top, bot) + step)
    return [("ink", TEAL, left), ("ink", ORANGE, right),
            ("cut", line([(26, -120), (26, 0), (-26, 0), (-26, 120)], 14))]


def l_stack():
    # Three pods stacked the way a freighter stacks them: not quite square.
    return [("ink", ORANGE, rect(-112, -104, 158, 62, 31)), ("ink", TEAL, rect(-46, -32, 158, 62, 31)),
            ("ink", GOLD, rect(-112, 40, 158, 62, 31))]


def l_placard():
    # A cargo placard, the one label every dock can read.
    k = 116
    return [("ink", TEAL, poly([(-7, -k + 7), (-7, -7), (-k + 7, -7)])),
            ("ink", ORANGE, poly([(7, -k + 7), (k - 7, -7), (7, -7)])),
            ("ink", TEAL, poly([(7, 7), (k - 7, 7), (7, k - 7)])),
            ("ink", ORANGE, poly([(-7, 7), (-7, k - 7), (-k + 7, 7)]))]


# --- Carrion Reavers: rust, ram spikes, warning stripes, kill marks --------------
def r_ram():
    # The front of the ship, and the last thing a hauler sees: broad, level, jagged behind.
    return [("ink", RUST, poly([(-112, -98), (116, 0), (-112, 98), (-86, 48), (-112, 22), (-84, -6), (-110, -44)])),
            ("ink", GOLD, poly([(-44, -68), (-10, -54), (-10, 54), (-44, 68)]))]


def r_beak():
    # One hooked beak, cut from plate. It only needs the one.
    return [("ink", DARK, poly([(-112, -36), (-52, -98), (40, -102), (100, -58), (116, 14), (96, 110), (70, 46),
                                (28, 24), (-32, 22), (-112, 14)])),
            ("inlay", GOLD, poly([(-48, -56), (2, -60), (-22, -18)]))]


def r_tally():
    # Every Reaver hull counts. Four kills and the one that closes the gate.
    steps = [("ink", RUST, rect(x, y, 32, h)) for x, y, h in ((-92, -96, 186), (-40, -108, 214), (12, -92, 190), (64, -104, 204))]
    return steps + [("cut", line([(-116, 82), (116, -70)], 56)), ("ink", GOLD, line([(-112, 80), (112, -68)], 34))]


# --- Freeholders: patched, ramshackle, hand-painted, farms and mines -------------
def f_house():
    # A roof that isn't quite true, and someone's light on.
    return [("ink", OLIVE, poly([(-8, -112), (106, -22), (97, 110), (-102, 104), (-106, -16)])),
            ("inlay", GOLD, poly([(-30, 4), (34, 0), (38, 64), (-28, 68)]))]


def f_shovel():
    # Everyone here owns one.
    return [("ink", OLIVE, group([rect(-48, -114, 96, 30, 4), rect(-20, -92, 40, 124)], rotate=14)),
            ("ink", OCHRE, group([path("M-62 24 H62 V66 C62 94 22 110 0 118 C-22 110 -62 94 -62 66 Z")], rotate=14))]


def f_ring():
    # A broken ring, bolted back together. That is the whole story.
    return [("ink", OLIVE, line(_arc(82, 5, 325), 42)),
            ("cut", group([rect(-64, -48, 128, 96)], translate=(70, -22), rotate=72)),
            ("ink", OCHRE, group([rect(-54, -38, 108, 76, 3)], translate=(70, -22), rotate=72)),
            ("cut", group([circ(-30, 0, 11), circ(30, 0, 11)], translate=(70, -22), rotate=72))]


GROUPS = [
    ("Vharn Dominion", "vharn", [("v_shell", "Shell", v_shell), ("v_talons", "Talons", v_talons), ("v_wings", "Wing-cases", v_wings)]),
    ("Kessari Clans", "kessari", [("k_ring", "Ring of modules", k_ring), ("k_diamond", "Lattice", k_diamond), ("k_nut", "Nut", k_nut)]),
    ("Meridian Combine", "meridian", [("m_halves", "Slid halves", m_halves), ("m_steps", "Stepped block", m_steps), ("m_corner", "Taken corner", m_corner)]),
    ("Ledger Exchange", "ledger", [("l_coin", "Split coin", l_coin), ("l_stack", "Pod stack", l_stack), ("l_placard", "Cargo placard", l_placard)]),
    ("Carrion Reavers", "reavers", [("r_ram", "Ram", r_ram), ("r_beak", "Beak", r_beak), ("r_tally", "Kill tally", r_tally)]),
    ("Freeholders", "freeholders", [("f_house", "Lit house", f_house), ("f_shovel", "Shovel", f_shovel), ("f_ring", "Mended ring", f_ring)]),
]


def build():
    shutil.rmtree(OUT, ignore_errors=True)
    for sub in ("svg", "svg-stencil", "png"):
        (OUT / sub).mkdir(parents=True)
    for _name, _cur, items in GROUPS:
        for sid, _label, fn in items:
            steps = fn()
            (OUT / "svg" / f"{sid}.svg").write_text(build_svg("Sigil attempt", steps, False), encoding="utf-8")
            (OUT / "svg-stencil" / f"{sid}.svg").write_text(build_svg("Sigil attempt stencil", steps, True), encoding="utf-8")
    a, b = RENDER_TMP / "att-a", RENDER_TMP / "att-b"
    for d in (a, b):
        shutil.rmtree(d, ignore_errors=True)
    render(OUT / "svg", a, 4.0)
    render(OUT / "svg-stencil", b, 4.0)
    clean = {p.stem: Image.open(p).convert("RGBA") for p in a.glob("*.png")}
    sten = {p.stem: Image.open(p).convert("RGBA") for p in b.glob("*.png")}
    for k, im in clean.items():
        im.resize((256, 256), Image.LANCZOS).save(OUT / "png" / f"{k}.png")

    tile, row_h, head, left = 196, 262, 180, 56
    col = 360
    sheet = Image.new("RGB", (2000, head + len(GROUPS) * row_h + 30), "#E9E6DC")
    d = ImageDraw.Draw(sheet)
    ink = "#14171A"
    d.text((left, 44), "GalHack sigil attempts: draft 4", font=font(60, True), fill=ink)
    d.text((left, 118), "Far left: current sigil. Then three new ideas. Beside each: 64 pixels clean, 64 pixels stencil, "
           "and 32 pixels, on hull grey.", font=font(22), fill="#555555")
    def cell(x, y, big, small_a, small_b, label, grey):
        sheet.paste(big.resize((tile, tile), Image.LANCZOS), (x, y), big.resize((tile, tile), Image.LANCZOS))
        panel = (x + tile + 8, y, x + tile + 8 + 84, y + tile)
        d.rectangle(panel, fill="#8E9397")
        s1 = small_a.resize((64, 64), Image.BOX)
        s2 = small_b.resize((64, 64), Image.BOX)
        s3 = small_a.resize((32, 32), Image.BOX)
        sheet.paste(s1, (x + tile + 18, y + 6), s1)
        sheet.paste(s2, (x + tile + 18, y + 76), s2)
        sheet.paste(s3, (x + tile + 34, y + 152), s3)
        d.text((x + tile / 2, y + tile + 6), label, font=font(19), fill=grey, anchor="ma")
    for r, (name, cur, items) in enumerate(GROUPS):
        y = head + r * row_h
        d.line((left, y - 12, 2000 - left, y - 12), fill="#BBB8AE", width=2)
        d.text((left, y - 6), name, font=font(20, True), fill=ink)
        cur_clean = Image.open(SIGILS / "png" / f"{cur}.png").convert("RGBA")
        cur_sten = Image.open(RENDER_TMP / "att-current" / f"{cur}.png").convert("RGBA")
        cell(left, y + 22, cur_clean, cur_clean, cur_sten, "Current", "#777777")
        for i, (sid, label, _fn) in enumerate(items):
            cell(left + 340 + i * col + (i * 40), y + 22, clean[sid], clean[sid], sten[sid], label, ink)
    sheet.save(ROOT / "sigils-attempts.png", optimize=True)
    print(sum(len(g[2]) for g in GROUPS), "attempts built")


if __name__ == "__main__":
    shutil.rmtree(RENDER_TMP / "att-current", ignore_errors=True)
    render(SIGILS / "svg-stencil", RENDER_TMP / "att-current", 4.0)
    build()
