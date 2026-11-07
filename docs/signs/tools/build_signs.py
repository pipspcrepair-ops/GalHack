#!/usr/bin/env python3
"""GalHack sign set generator. Original artwork, drawn entirely in this file.

Writes svg/ and svg-stencil/, renders them with Godot's built-in SVG rasteriser
(so every PNG is a true render of its SVG), then composes the review sheets
with PIL.  Run:  python build_signs.py          (add --stencil-check for a
scratch contact sheet of the stencils; pass a path after it to choose where)

Symbols are written in a 200-unit box centred on (0, 0), y down.  Every symbol
is a list of painted operations: ink shapes, and "cuts" that remove ink.
The framed tile paints cuts in the field colour; the stencil turns them into
real transparent holes with an SVG mask.
"""
import math
import os
import shutil
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
GODOT = os.environ.get(
    "GALHACK_GODOT",
    r"G:\GalHack\.tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe",
)
RENDER_TMP = Path(r"G:\GalHack\.tools\tmp\signs-render")

# --- palette and frame -------------------------------------------------------
RED = "#C8321E"      # frame and Danger field
PAPER = "#F4F1E8"    # keyline, pips, white symbols
INK = "#14171A"      # black symbols and the Void field
STENCIL = "#14171A"

#            field     symbol  covers (for the legend)
CATS = {
    "Void":    ("#14171A", PAPER, "air, pressure, gravity"),
    "Danger":  (RED,       PAPER, "hazards indoors"),
    "Land":    ("#9A5B1E", PAPER, "ground, weather, wild"),
    "Order":   ("#5A3E8E", PAPER, "old-world authority"),
    "Energy":  ("#FFC400", INK,   "power and radiation"),
    "Cold":    ("#1C75BC", PAPER, "cold and ice"),
    "Life":    ("#1E8449", PAPER, "medical and food"),
    "Systems": ("#5F6B73", PAPER, "machines and stores"),
    "Route":   (PAPER,     INK,   "ways to move"),
}

T = 256             # tile size
BAND = 18           # frame width at top and sides
SILL = 42           # frame width at the bottom: holds the hazard-level pips
KEY = 7             # keyline width
CHAMFER = 20        # the field is chamfered, the outer tile rounded
RADIUS = 30
PIP_W, PIP_H, PIP_GAP = 34, 16, 12
SYM_SCALE = 0.80    # symbol box -> tile units
SYM_CY = (BAND + T - SILL) / 2
STENCIL_SCALE = 1.08


# --- drawing primitives ------------------------------------------------------
def n(v):
    s = f"{v:.2f}".rstrip("0").rstrip(".")
    return "0" if s in ("-0", "") else s


def _col(cut):
    return "{CUT}" if cut else "{INK}"


def rect(x, y, w, h, r=0, cut=False):
    rx = f' rx="{n(r)}"' if r else ""
    return f'<rect x="{n(x)}" y="{n(y)}" width="{n(w)}" height="{n(h)}"{rx} fill="{_col(cut)}"/>'


def circ(x, y, r, cut=False):
    return f'<circle cx="{n(x)}" cy="{n(y)}" r="{n(r)}" fill="{_col(cut)}"/>'


def ring(x, y, r, w, cut=False):
    return f'<circle cx="{n(x)}" cy="{n(y)}" r="{n(r)}" fill="none" stroke="{_col(cut)}" stroke-width="{n(w)}"/>'


def poly(pts, cut=False):
    p = " ".join(f"{n(x)},{n(y)}" for x, y in pts)
    return f'<polygon points="{p}" fill="{_col(cut)}"/>'


def path(d, cut=False):
    return f'<path d="{d}" fill="{_col(cut)}"/>'


def stroke(d, w, cut=False, cap="butt", join="miter"):
    return (f'<path d="{d}" fill="none" stroke="{_col(cut)}" stroke-width="{n(w)}" '
            f'stroke-linecap="{cap}" stroke-linejoin="{join}"/>')


def line(pts, w, cut=False, cap="butt", join="miter"):
    d = "M" + " L".join(f"{n(x)} {n(y)}" for x, y in pts)
    return stroke(d, w, cut, cap, join)


def wave(y, x0, x1, amp, w, cut=False):
    """One full S-wave: the set's mark for moving air and heat."""
    l = x1 - x0
    d = f"M{n(x0)} {n(y)} Q{n(x0 + l / 4)} {n(y - amp * 2)} {n(x0 + l / 2)} {n(y)} T{n(x1)} {n(y)}"
    return stroke(d, w, cut)


def group(ops, translate=None, rotate=None, scale=None):
    t = []
    if translate:
        t.append(f"translate({n(translate[0])} {n(translate[1])})")
    if rotate:
        t.append(f"rotate({n(rotate)})")
    if scale:
        t.append(f"scale({n(scale)})")
    return f'<g transform="{" ".join(t)}">' + "".join(ops) + "</g>"


def flip(ops):
    """Swap ink and cut: draw a shape as a hole in whatever is under it."""
    return [o.replace("{INK}", "\0").replace("{CUT}", "{INK}").replace("\0", "{CUT}") for o in ops]


# --- shared motifs -----------------------------------------------------------
def person():
    """The set's figure is a block figure, like the world it lives in."""
    return [
        rect(-20, -100, 40, 40, 4),
        rect(-36, -52, 72, 84, 4),
        rect(-36, 38, 31, 62),
        rect(5, 38, 31, 62),
    ]


def crate_base(cut_slots=True):
    """Two blocks on the floor. The third slot on top names what is stored."""
    ops = [rect(-98, 4, 92, 92, 5), rect(6, 4, 92, 92, 5)]
    if cut_slots:
        ops += [rect(-76, 38, 48, 22, cut=True), rect(28, 38, 48, 22, cut=True)]
    return ops


def flake(r, w, tips=0.0, tip_w=0, cut=False):
    ops = []
    for a in (90, 30, -30):
        c, s = math.cos(math.radians(a)), math.sin(math.radians(a))
        ops.append(line([(-r * c, -r * s), (r * c, r * s)], w, cut))
    if tips:
        for a in range(0, 360, 60):
            d = r * tips
            b = d + r * 0.30
            ops.append(group([line([(-r * 0.30, -b), (0, -d), (r * 0.30, -b)], tip_w, cut)], rotate=a))
    return ops


def bolt(s=1.0, cut=False):
    pts = [(26, -102), (-58, 14), (-10, 14), (-34, 102), (60, -22), (12, -22), (48, -102)]
    return poly([(x * s, y * s) for x, y in pts], cut)


def heart(s=1.0, cut=False):
    d = ("M0 96 L-92 -6 C-126 -50 -84 -98 -46 -90 C-24 -86 -8 -70 0 -52 "
         "C8 -70 24 -86 46 -90 C84 -98 126 -50 92 -6 Z")
    return group([path(d, cut)], scale=s)


def trefoil():
    def sector(mid, half=31, ri=38, ro=102):
        a1, a2 = math.radians(mid - half), math.radians(mid + half)
        p = lambda r, a: (r * math.cos(a), r * math.sin(a))
        x1, y1 = p(ri, a1)
        x2, y2 = p(ro, a1)
        x3, y3 = p(ro, a2)
        x4, y4 = p(ri, a2)
        return path(f"M{n(x1)} {n(y1)} L{n(x2)} {n(y2)} A{ro} {ro} 0 0 1 {n(x3)} {n(y3)} "
                    f"L{n(x4)} {n(y4)} A{ri} {ri} 0 0 0 {n(x1)} {n(y1)} Z")
    return [circ(0, 0, 24), sector(-90), sector(30), sector(150)]


def floor():
    return rect(-100, 72, 200, 26)


# --- the 32 symbols ----------------------------------------------------------
def s_pressurised():
    # A sealed hull with moving air inside it.
    return [
        stroke("M-72 -86 H72 Q86 -86 86 -72 V72 Q86 86 72 86 H-72 Q-86 86 -86 72 V-72 Q-86 -86 -72 -86 Z", 28),
        wave(-26, -54, 54, 15, 24),
        wave(30, -54, 54, 15, 24),
    ]


def s_no_air():
    # The same air, struck out.
    return [
        wave(-58, -94, 94, 18, 28),
        wave(0, -94, 94, 18, 28),
        wave(58, -94, 94, 18, 28),
        line([(-66, 80), (66, -80)], 60, cut=True),
        line([(-74, 90), (74, -90)], 30),
    ]


def s_airlock():
    # Two doors and one block figure caught between them.
    return [rect(-100, -100, 32, 200), rect(68, -100, 32, 200), group(person(), scale=0.86)]


def s_suit():
    # A helmet seen head on, visor and collar.
    return [
        rect(-84, -100, 168, 156, 68),
        rect(-60, -54, 120, 60, 26, cut=True),
        rect(-68, 70, 136, 30, 6),
    ]


def s_gravity_on():
    # A block on the floor, with weight driving down on both sides of it.
    ops = [rect(-40, -24, 80, 88), floor()]
    for x in (-74, 74):
        ops += [rect(x - 13, -100, 26, 104), poly([(x - 28, 0), (x + 28, 0), (x, 58)])]
    return ops


def s_no_gravity():
    # The same blocks and floor, adrift.
    return [
        group([rect(-46, -46, 92, 92)], translate=(-26, -40), rotate=24),
        group([rect(-27, -27, 54, 54)], translate=(62, 22), rotate=-32),
        floor(),
    ]


def s_gravity_anomaly():
    # Space wound up around a point.
    pts = []
    turns, r0, r1 = 1.85, 6, 88
    for i in range(121):
        t = i / 120
        a = t * turns * 2 * math.pi - 0.6
        r = r0 + (r1 - r0) * t
        pts.append((r * math.cos(a), r * math.sin(a)))
    return [circ(0, 0, 20), line(pts, 26, join="round")]


def s_hazard():
    # A blunt tilted block with one mark on it.
    return [
        poly([(0, -110), (110, 0), (0, 110), (-110, 0)]),
        poly([(-20, -68), (20, -68), (11, 20), (-11, 20)], cut=True),
        rect(-16, 34, 32, 32, cut=True),
    ]


def s_fire():
    return [
        path("M6 -102 C18 -62 74 -34 74 32 C74 74 42 102 0 102 C-42 102 -74 74 -74 32 "
             "C-74 -2 -50 -16 -42 -54 C-26 -42 -18 -30 -14 -14 C-2 -34 12 -62 6 -102 Z"),
        path("M2 14 C10 36 34 44 34 64 C34 80 18 88 0 88 C-18 88 -34 80 -34 64 C-34 46 -8 38 2 14 Z", cut=True),
    ]


def s_toxic_air():
    # A gas mask: the only way to breathe what is in there.
    return [
        rect(-92, -100, 184, 122, 50),
        poly([(-80, 0), (80, 0), (46, 58), (-46, 58)]),
        circ(-42, -44, 29, cut=True),
        circ(42, -44, 29, cut=True),
        circ(0, 56, 54, cut=True),
        circ(0, 56, 44),
        rect(-26, 44, 52, 24, cut=True),
    ]


def s_biohazard():
    # A spiked cell, big enough to spot across a room.
    ops = [circ(0, 0, 58)]
    for a in range(0, 360, 45):
        ops.append(group([rect(-11, -96, 22, 50), rect(-23, -106, 46, 26, 6)], rotate=a + 22.5))
    ops += [circ(-16, -14, 15, cut=True), circ(20, 2, 11, cut=True), circ(-6, 26, 11, cut=True)]
    return ops


def s_deep_drop():
    # A crumbling stepped edge and the way down from it.
    return [
        poly([(-100, -66), (-14, -66), (-14, -26), (-40, -26), (-40, 30), (-66, 30), (-66, 100), (-100, 100)]),
        rect(26, -100, 40, 118),
        poly([(-6, 14), (98, 14), (46, 102)]),
    ]


def s_unstable():
    # A block cracked through, one half already slipping. Added for the ruins.
    return [
        poly([(-96, -90), (-4, -90), (-30, -30), (14, -14), (-22, 40), (6, 62), (-14, 78), (-96, 78)]),
        poly([(40, -74), (96, -74), (96, 100), (32, 100), (58, 70), (30, 40), (72, -14), (22, -28)]),
    ]


def s_restricted():
    # A closed gate, seen end on.
    return [circ(0, 0, 102), rect(-70, -24, 140, 48, cut=True)]


def s_armoury():
    # A blocky sidearm, side on.
    return [
        rect(-104, -74, 208, 62, 6),
        poly([(-96, -14), (-20, -14), (-4, 20), (-22, 20), (-32, 96), (-104, 96)]),
        stroke("M-12 -16 V26 H40 V-16", 16),
        rect(-104, -30, 208, 8, cut=True),
    ]


def s_radiation():
    return trefoil()


def s_high_radiation():
    # Worse than plain radiation, so the symbol sits inside a black disc.
    return [circ(0, 0, 108)] + flip([group(trefoil(), scale=0.82)])


def s_high_voltage():
    return [bolt(1.0)]


def s_generator():
    # Power held inside a heavy rotor ring, with terminals.
    return [ring(0, 0, 80, 28), rect(-104, -19, 26, 38), rect(78, -19, 26, 38), bolt(0.60)]


def s_extreme_cold():
    return flake(102, 26, 0.56, 22)


def s_cryo():
    # A sealed upright pod with the sleeper inside and frost on either side.
    return ([rect(-66, -104, 132, 208, 62)]
            + flip([group(person(), translate=(0, 6), scale=0.68)])
            + [group(flake(25, 12), translate=(-92, -58)), group(flake(25, 12), translate=(92, 58))])


def s_cold_store():
    return crate_base() + [group(flake(49, 21), translate=(0, -50))]


def s_medical():
    # A heart with its trace running through it.
    return [
        heart(1.0),
        line([(-118, 4), (-42, 4), (-20, -38), (12, 44), (32, 4), (118, 4)], 20, cut=True),
    ]


def s_healing_booth():
    # A figure stood in an open booth, with a heart in its chest.
    return [
        stroke("M-82 90 V-22 A82 82 0 0 1 82 -22 V90", 26),
        rect(-104, 76, 208, 26),
        group(person(), translate=(0, 2), scale=0.70),
        group([heart(0.22, cut=True)], translate=(0, -9)),
    ]


def s_food_store():
    return crate_base() + [
        circ(-22, -44, 39),
        circ(22, -44, 39),
        poly([(-2, -88), (2, -102), (34, -108), (20, -86)]),
    ]


def s_storage():
    return crate_base() + [rect(-46, -96, 92, 92, 5), rect(-24, -62, 48, 22, cut=True)]


def s_terminal():
    # A screen waiting at a prompt.
    return [
        rect(-102, -92, 204, 142, 12),
        line([(-68, -60), (-26, -22), (-68, 16)], 26, cut=True),
        rect(-6, -4, 60, 26, cut=True),
        rect(-22, 50, 44, 26),
        rect(-68, 74, 136, 24),
    ]


def s_command():
    # The helm: whoever holds this has the ship.
    ops = [ring(0, 0, 72, 26), circ(0, 0, 25)]
    for a in (0, 90):
        ops.append(group([rect(-10, -70, 20, 140)], rotate=a + 45))
    for a in range(0, 360, 45):
        ops.append(group([rect(-14, -108, 28, 34, 4)], rotate=a))
    return ops


def s_workshop():
    # A gear around a square hole: the fitting that makes blocks.
    ops = [circ(0, 0, 76)]
    for a in range(0, 360, 45):
        ops.append(group([poly([(-24, -66), (24, -66), (16, -104), (-16, -104)])], rotate=a))
    ops.append(rect(-30, -30, 60, 60, cut=True))
    return ops


def s_arrow():
    # Head and shaft are separate blocks. Rotate to point any way.
    return [rect(-102, -34, 94, 68), poly([(8, -92), (104, 0), (8, 92)])]


def s_ladder():
    rungs = [rect(-40, y, 80, 22) for y in (-84, -36, 12, 60)]
    return [group([rect(-62, -106, 26, 212), rect(36, -106, 26, 212)] + rungs, rotate=11)]


def s_exit():
    # Out through the frame.
    return [
        line([(22, -88), (-82, -88), (-82, 88), (22, 88)], 32),
        rect(-44, -24, 92, 48),
        poly([(44, -60), (106, 0), (44, 60)]),
    ]


def rock(x, y, s, rot=0):
    pts = [(-1, -.45), (-.3, -1), (.65, -.8), (1, -.1), (.6, .9), (-.55, .95)]
    return group([poly([(px * s, py * s) for px, py in pts])], translate=(x, y), rotate=rot)


def slash():
    return [line([(-66, 80), (66, -80)], 60, cut=True), line([(-74, 90), (74, -90)], 30)]


def s_extreme_heat():
    # Heat lifting off a surface: the air mark stood on end.
    ops = [rect(-100, 72, 200, 28)]
    for x in (-58, 0, 58):
        ops.append(group([wave(0, -104, 54, 14, 28)], translate=(x, 0), rotate=90))
    return ops


def s_contamination():
    # An old drum, tipped and leaking.
    return [
        group([rect(-54, -88, 108, 150, 8), rect(-60, -46, 120, 16, cut=True), rect(-60, 8, 120, 16, cut=True)],
              translate=(-14, -8), rotate=14),
        rect(-92, 72, 172, 28, 14),
        path("M58 8 C72 30 80 40 80 54 C80 66 70 74 58 74 C46 74 36 66 36 54 C36 40 44 30 58 8 Z"),
    ]


def s_sealed():
    # A door shut with a strap and a lock, by someone with the right to do it.
    return [
        rect(-72, -104, 144, 208, 8),
        rect(-104, 2, 208, 50, cut=True),
        rect(-104, 12, 208, 30),
        rect(-58, -14, 116, 94, 10, cut=True),
        rect(-46, -2, 92, 72, 8),
        stroke("M-30 -6 V-34 A30 30 0 0 1 30 -34 V-6", 20, cut=True),
        rect(-11, 16, 22, 34, cut=True),
    ]


def s_do_not_open():
    # A shut container with its lid seam, struck out.
    return [
        rect(-92, -62, 184, 150, 8),
        rect(-100, -26, 200, 14, cut=True),
    ] + slash()


def s_decontamination():
    # A figure under a spray bar.
    ops = [rect(-84, -104, 168, 30, 8), group(person(), translate=(0, 32), scale=0.68)]
    for x in (-76, 76):
        ops += [rect(x - 9, -60, 18, 52, 9), rect(x - 9, 8, 18, 52, 9)]
    return ops


def s_evacuation():
    # A figure at a run, and where to run to.
    return [
        group([rect(-22, -22, 44, 44, 4)], translate=(-34, -82), rotate=12),
        line([(-48, -44), (-66, 22)], 40),
        line([(-52, -36), (-16, -16), (10, -32)], 26),
        line([(-46, -34), (-92, -22), (-102, 10)], 26),
        line([(-60, 14), (-16, 40), (-22, 100)], 30),
        line([(-72, 14), (-82, 60), (-122, 90)], 30),
        rect(26, 18, 40, 44),
        poly([(62, -24), (112, 40), (62, 104)]),
    ]


def s_rockfall():
    return [
        poly([(-104, -100), (-6, -100), (-38, -46), (-60, -40), (-66, 30), (-104, 100)]),
        rock(36, -60, 34, 10),
        rock(62, 22, 28, 50),
        rock(10, 62, 36, 100),
    ]


def s_sinkhole():
    # The ground stepping away into nothing.
    return [
        poly([(-104, -30), (-70, -30), (-70, 4), (-44, 4), (-44, 38), (-20, 38), (-20, 70),
              (20, 70), (20, 38), (44, 38), (44, 4), (70, 4), (70, -30), (104, -30),
              (104, 100), (-104, 100)]),
        rect(-14, -104, 28, 42),
        poly([(-36, -66), (36, -66), (0, -12)]),
    ]


def s_flood():
    # A roofline with the water already over the door.
    return [
        poly([(0, -104), (94, -24), (68, -24), (68, 30), (-68, 30), (-68, -24), (-94, -24)]),
        wave(32, -100, 100, 14, 52, cut=True),
        wave(32, -100, 100, 14, 26),
        wave(80, -100, 100, 14, 26),
    ]


def s_deep_water():
    # A surface, a bottom, and too much between them.
    return [
        wave(-84, -100, 100, 13, 28),
        rect(-14, -22, 28, 54),
        poly([(-40, -18), (40, -18), (0, -60)]),
        poly([(-40, 28), (40, 28), (0, 70)]),
        rect(-100, 78, 200, 24),
    ]


def s_thin_ice():
    # A sheet that has already let go.
    return [
        poly([(-104, -66), (-22, -42), (-38, -14), (-6, -4), (-14, 14), (-104, -10)]),
        poly([(104, -66), (24, -40), (34, -18), (12, -2), (22, 14), (104, -10)]),
        wave(46, -100, 100, 13, 24),
        wave(88, -100, 100, 13, 24),
    ]


def s_avalanche():
    # A slope with the snow coming down it in lumps.
    return [
        poly([(-104, -100), (-76, -100), (104, 100), (-104, 100)]),
        circ(-14, -66, 22),
        circ(50, -34, 32),
        circ(84, 42, 22),
    ]


def s_lava():
    # A cone with a molten vein running out of its crater.
    return [
        poly([(-106, 102), (-34, -22), (34, -22), (106, 102)]),
        line([(4, -28), (-14, 12), (14, 44), (-6, 108)], 24, cut=True),
        rect(-15, -96, 30, 30, 4),
        group([rect(-13, -13, 26, 26, 4)], translate=(-58, -62), rotate=25),
        group([rect(-13, -13, 26, 26, 4)], translate=(58, -62), rotate=-25),
    ]


def _drop(x, y, k=1.0):
    d = "M0 -30 C9 -14 19 -5 19 8 C19 20 10 28 0 28 C-10 28 -19 20 -19 8 C-19 -5 -9 -14 0 -30 Z"
    return group([path(d)], translate=(x, y), scale=k)


def s_geyser():
    # A vent throwing up a column of water, and the water coming back down.
    ops = [
        rect(-104, 74, 208, 26),
        poly([(-74, 76), (-42, 44), (-20, 44), (-14, 76), (14, 76), (20, 44), (42, 44), (74, 76)]),
        path("M-15 78 C-14 30 -30 0 -40 -64 L40 -64 C30 0 14 30 15 78 Z"),
        circ(-23, -68, 25),
        circ(23, -68, 25),
        circ(0, -84, 25),
    ]
    for sg in (-1, 1):
        ops += [_drop(sg * 80, -34, 0.95), _drop(sg * 72, 32, 0.8)]
    return ops


def s_quicksand():
    # A block figure already chest deep, arms flung up.
    return [
        rect(-25, -62, 50, 50, 4),
        rect(-44, -2, 88, 56, 4),
        line([(-30, 16), (-84, -100)], 28),
        line([(30, 16), (84, -100)], 28),
        wave(50, -104, 104, 11, 46, cut=True),
        wave(50, -104, 104, 11, 28),
        wave(92, -74, 74, 10, 24),
    ]


def s_storm():
    return [
        circ(-52, -36, 42),
        circ(8, -58, 50),
        circ(62, -34, 40),
        rect(-94, -30, 196, 42, 18),
        group([poly([(30, -40), (-44, 56), (-6, 56), (-24, 122), (52, 26), (14, 26), (40, -40)])],
              translate=(0, -6), scale=1.0),
    ]


def s_tornado():
    ops = []
    for y, w, x in ((-100, 200, 0), (-58, 156, 8), (-16, 112, -2), (26, 72, 10), (68, 36, 2)):
        ops.append(rect(x - w / 2, y, w, 32, 15))
    return ops


def s_high_wind():
    # A windsock standing straight out from its pole.
    ops = [rect(-104, -104, 24, 208)]
    segs = [(-76, -16, 58, 48), (-4, 46, 44, 34), (58, 104, 30, 20)]
    for x0, x1, h0, h1 in segs:
        ops.append(poly([(x0, -46 - h0), (x1, -46 - h1), (x1, -46 + h1), (x0, -46 + h0)]))
    return ops


def s_dust_storm():
    # A wall of dust rolling over a small house.
    return [
        circ(-52, -48, 52),
        circ(4, -6, 44),
        circ(-30, 40, 34),
        rect(-104, -48, 88, 124),
        poly([(66, -4), (104, 34), (92, 34), (92, 76), (40, 76), (40, 34), (28, 34)]),
        floor(),
    ]


def s_seismic():
    # A block shaking on broken ground.
    ops = [
        poly([(-104, 100), (-104, 58), (-62, 40), (-26, 72), (20, 44), (58, 72), (104, 48), (104, 100)]),
        rect(-30, -58, 60, 84),
    ]
    for sign in (-1, 1):
        ops.append(stroke(f"M{sign * 54} -72 Q{sign * 80} -26 {sign * 54} 20", 20))
        ops.append(stroke(f"M{sign * 84} -86 Q{sign * 122} -26 {sign * 84} 34", 20))
    return ops


def s_wildlife():
    # Jaws, closing.
    upper = [(-100, -92), (100, -92), (100, -34), (75, 14), (50, -34), (25, 14),
             (0, -34), (-25, 14), (-50, -34), (-75, 14), (-100, -34)]
    lower = [(-100, 96), (100, 96), (100, 2), (75, 50), (50, 2), (25, 50),
             (0, 2), (-25, 50), (-50, 2), (-75, 50), (-100, 2)]
    return [
        poly(upper),
        poly(lower),
        poly([(-76, -78), (-20, -62), (-26, -46), (-76, -56)], cut=True),
        poly([(76, -78), (20, -62), (26, -46), (76, -56)], cut=True),
    ]


def s_venom_plant():
    # One barbed leaf with a bead of venom falling from its tip.
    top = [(-96, 0), (-70, -30), (-62, -14), (-36, -46), (-26, -24), (4, -52), (12, -26), (44, -44), (46, -20), (108, 0)]
    leaf = top + [(x, -y) for x, y in reversed(top[1:-1])]
    return [
        group([poly(leaf), rect(-120, -8, 170, 16, cut=True), rect(-124, -9, 34, 18)],
              translate=(-14, -14), rotate=-32),
        path("M66 8 C78 32 94 44 94 64 C94 84 80 98 62 98 C44 98 30 84 30 64 C30 44 54 32 66 8 Z"),
    ]


def s_mines():
    # A boot coming down on ground with three charges ready to go off under it.
    ops = [
        poly([(-62, -106), (2, -106), (2, -62), (50, -48), (70, -30), (70, -14), (-62, -14)]),
        rect(-104, -2, 208, 102),
    ]
    for x in (-68, 0, 68):
        ops.append(circ(x, 96, 28, cut=True))
        for a in (-38, 0, 38):
            ops.append(group([poly([(-11, -24), (11, -24), (0, -80)], cut=True)], translate=(x, 96), rotate=a))
    return ops


def s_contaminated_ground():
    # A skull lying on bad earth.
    return [
        rect(-68, -104, 136, 106, 36),
        rect(-46, -6, 92, 40),
        rect(-44, -68, 34, 36, 8, cut=True),
        rect(10, -68, 34, 36, 8, cut=True),
        rect(-8, 2, 16, 32, cut=True),
        rect(-36, 2, 14, 32, cut=True),
        rect(22, 2, 14, 32, cut=True),
        rect(-104, 50, 208, 24),
        rect(-104, 84, 88, 18),
        rect(0, 84, 104, 18),
    ]


# --- draft 3: the geometric language -------------------------------------------
# New signs and restyled signs are drawn directly in field units (tile units,
# origin at the middle of the field) so shapes can run off the field's edges.
# Vocabulary: a square is a body or a thing, a disc is something alive, a
# triangle is force or direction, a bar is a wall, floor or boundary, and a
# shape that swaps colour at a line is half inside something.
FW, FH = 96, 86     # half the field
B = 112             # far enough to bleed off any edge


def _diamond(k):
    return poly([(0, -k), (k, 0), (0, k), (-k, 0)])


def _square(x, y, k, rot=0):
    return group([rect(-k / 2, -k / 2, k, k)], translate=(x, y), rotate=rot)


def _star(radii, rot=0):
    c = len(radii)
    return poly([(r * math.cos(math.radians(rot + i * 360 / c)), r * math.sin(math.radians(rot + i * 360 / c)))
                 for i, r in enumerate(radii)])


def a_life_support():
    # Something alive, with a line in and a line out.
    return [rect(-B, -50, 96, 28), rect(16, 22, 96, 28), circ(0, 0, 60)]


def a_water():
    return [poly([(0, -104), (62, -2), (-62, -2)]), circ(0, 20, 66)]


def a_fuel():
    # A drum filling the field top to bottom, banded.
    return [rect(-66, -B, 132, 2 * B), rect(-B, -36, 2 * B, 18, cut=True), rect(-B, 30, 2 * B, 18, cut=True),
            circ(28, -64, 13, cut=True)]


def a_maintenance():
    # One diagonal bar with a jaw on the end of it.
    return [line([(-B, B), (34, -34)], 46), circ(38, -38, 58),
            group([rect(-20, -74, 40, 58, cut=True)], translate=(38, -38), rotate=45)]


def a_galley():
    return [path("M-96 -12 H96 A96 96 0 0 1 -96 -12 Z"),
            rect(-68, -B, 24, 82), rect(-12, -B, 24, 82), rect(44, -B, 24, 82)]


def a_comms():
    # Rings thrown out from one corner.
    return [circ(-FW, FH, 62), ring(-FW, FH, 106, 28), ring(-FW, FH, 154, 28), ring(-FW, FH, 202, 28)]


def a_stairs():
    return [poly([(-B, B), (-B, 54), (-48, 54), (-48, 14), (0, 14), (0, -26), (48, -26), (48, -66), (B, -66), (B, B)])]


def a_lift():
    # A floor, with up on one side of it and down on the other.
    return [rect(-B, -12, 2 * B, 24), poly([(-66, -26), (66, -26), (0, -84)]), poly([(-66, 26), (66, 26), (0, 84)])]


def a_bulkhead():
    # Two masses that close the opening and lock on a stepped tongue.
    return [poly([(-B, -60), (-14, -60), (-14, -24), (30, -24), (30, 24), (-14, 24), (-14, 60), (-B, 60)]),
            poly([(B, -60), (4, -60), (4, -42), (48, -42), (48, 42), (4, 42), (4, 60), (B, 60)])]


def a_hangar():
    return [rect(-B, -B, 2 * B, 56), rect(-B, -B, 40, 2 * B), rect(72, -B, 40, 2 * B),
            rect(-50, 2, 100, 38), rect(-20, -24, 46, 26), circ(-30, 58, 21), circ(30, 58, 21)]


def a_landing_pad():
    # Everything solid except a ring: you put down on the middle of it.
    return [rect(-B, -B, 2 * B, 2 * B), circ(0, 0, 80, cut=True), circ(0, 0, 46)]


def a_docking():
    # A round end and the round socket that takes it.
    return [rect(2, -B, 110, 2 * B), circ(-16, 0, 72, cut=True), rect(-B, -B, 96, 2 * B), circ(-16, 0, 54)]


def a_storm_shelter():
    # Heavy roof, solid thing under it, weather stopping at the roof.
    return [poly([(-B, 4), (0, -60), (B, 4), (B, 44), (0, -20), (-B, 44)]),
            rect(-40, 0, 80, 110),
            line([(-46, -96), (-64, -52)], 20), line([(30, -96), (14, -66)], 20), line([(92, -96), (74, -52)], 20)]


def a_trade_post():
    # Two things going opposite ways past each other.
    return [rect(-B, -64, 106, 38), poly([(-10, -84), (72, -45), (-10, -6)]),
            rect(6, 26, 106, 38), poly([(10, 6), (-72, 45), (10, 84)])]


def a_teleporter():
    # A body here, the same body starting to be there.
    ops = [rect(-90, -4, 84, 84)]
    for y in (-76, -55, -34, -13):
        ops.append(rect(6, y, 84, 14))
    return ops


def a_clone():
    # Two copies of one square. Where they overlap you can't tell them apart.
    return [rect(-86, -76, 112, 112), rect(-26, -36, 112, 112), rect(-26, -36, 52, 72, cut=True)]


def a_recycler():
    half = [line([(-68, 34), (-68, -52), (28, -52)], 30), poly([(24, -84), (78, -52), (24, -20)])]
    return half + [group(half, rotate=180)]


def a_explosives():
    # An uneven burst: nine points, none of them matching.
    return [_star([120, 58, 96, 62, 118, 54, 104, 60, 92, 56, 122, 62, 98, 54, 112, 60, 100, 58], rot=-90),
            rect(-22, -22, 44, 44, cut=True)]


def a_ammo_store():
    # One round, stood on its rim.
    return [poly([(-40, -22), (0, -86), (40, -22)]), rect(-40, -10, 80, 70), rect(-B, 68, 2 * B, 44)]


def a_moving_machinery():
    # A big wheel, a small one, and the slots that show them turning.
    return [circ(-32, -10, 68), circ(60, 50, 36),
            group([rect(-11, -66, 22, 44, cut=True)], translate=(-32, -10), rotate=35),
            group([rect(-9, -38, 18, 24, cut=True)], translate=(60, 50), rotate=-60)]


def a_falling_debris():
    return [rect(-B, -B, 2 * B, 58), _square(-52, -10, 54, 20), _square(52, -2, 46, -26), _square(-2, 56, 40, 12)]


def a_laser():
    # A beam, turned by a mirror, leaving by another edge.
    return [rect(-B, -13, 130, 26), rect(5, -B, 26, B), line([(-30, 48), (66, -48)], 30)]


def a_hull_breach():
    # A wall with a hole in it, and all of the room going through.
    return [rect(54, -B, 58, 80), rect(54, 32, 58, 80), poly([(-92, -62), (-92, 62), (92, 0)])]


def a_ore():
    # Something worth having, set in the ground.
    return [rect(-B, -34, 2 * B, 150),
            group([poly([(0, -38), (38, 0), (0, 38), (-38, 0)], cut=True)], translate=(-44, 22)),
            group([poly([(0, -28), (28, 0), (0, 28), (-28, 0)], cut=True)], translate=(44, 2)),
            group([poly([(0, -22), (22, 0), (0, 22), (-22, 0)], cut=True)], translate=(36, 58)),
            poly([(-32, -34), (0, -80), (32, -34)])]


def a_crash():
    # One long diagonal coming down into the ground line.
    return [
        rect(-B, 52, 2 * B, 60),
        poly([(-74, 52), (18, 52), (-12, 86), (-46, 86)], cut=True),
        line([(B, -B), (-26, 62)], 40),
        line([(26, -72), (92, -20)], 14, cut=True),
        line([(-12, -24), (54, 28)], 14, cut=True),
    ]


# Proposed replacements for approved signs. Kept apart until the owner decides.


def r_fire():
    # Flame as straight cuts.
    return [poly([(-74, B), (-74, 8), (-40, -38), (-26, 6), (8, -88), (36, -12), (58, -38), (74, 18), (74, B)]),
            poly([(-34, 90), (0, 8), (34, 90)], cut=True)]


def r_toxic_air():
    # Air bars with the hazard block sitting in them.
    ops = [rect(-B, y - 15, 2 * B, 30) for y in (-58, 0, 58)]
    return ops + [circ(0, 0, 70, cut=True), _diamond(58), rect(-9, -34, 18, 40, cut=True), rect(-9, 14, 18, 18, cut=True)]


def r_biohazard():
    # A living thing, spreading.
    return [circ(-34, 26, 56), circ(38, -22, 30), circ(74, -64, 17), circ(66, 34, 15), circ(-60, -62, 16)]


def r_contamination():
    # Something coming through from above, unevenly.
    return [rect(-B, -B, 2 * B, 74), rect(-76, -60, 44, 100, 22), rect(-18, -60, 44, 150, 22), rect(44, -60, 34, 66, 17)]


def r_sealed():
    # Two doors, with something closed over the join.
    return [rect(-74, -B, 66, 2 * B), rect(8, -B, 66, 2 * B),
            poly([(0, -64), (64, 0), (0, 64), (-64, 0)], cut=True),
            poly([(0, -46), (46, 0), (0, 46), (-46, 0)])]


def r_armoury():
    return [rect(-84, -64, 196, 46), poly([(-84, -18), (-26, -18), (-40, 86), (-96, 86)]),
            rect(-8, -18, 34, 30), rect(0, -18, 18, 18, cut=True)]


def r_decontamination():
    ops = [rect(-B, -B, 2 * B, 52), rect(-38, 0, 76, 110)]
    for x in (-72, 0, 72):
        ops.append(rect(x - 11, -46, 22, 34))
    for x in (-72, 72):
        ops += [rect(x - 11, 0, 22, 34), rect(x - 11, 46, 22, 34)]
    return ops


def r_evacuation():
    # A body and everything telling it to go.
    return [rect(-96, -38, 76, 76), poly([(-12, -78), (44, 0), (-12, 78)]), poly([(40, -78), (96, 0), (40, 78)])]


def r_suit():
    return [rect(-74, -84, 148, 124, 58), rect(-54, -46, 108, 40, 14, cut=True), rect(-B, 56, 2 * B, 60)]


def r_cryo():
    return [rect(-92, -66, 184, 132, 66)] + flake(52, 20, cut=True)


def r_healing_booth():
    # A tall sealed block with the trace running through it.
    return [rect(-58, -B, 116, 2 * B),
            line([(-42, 22), (-24, 22), (-10, -46), (12, 60), (26, 22), (42, 22)], 17, cut=True)]


def r_medical():
    # A trace, edge to edge.
    return [line([(-B, 16), (-46, 16), (-18, -70), (16, 78), (42, 16), (B, 16)], 26)]


def r_food_store():
    return [rect(-96, 22, 192, 90), rect(-42, 44, 84, 22, cut=True), circ(0, -34, 44)]


def r_contaminated():
    # A hazard block half sunk in the ground, changing colour where it goes in.
    return [rect(-B, 2, 2 * B, B), poly([(-70, 2), (0, -76), (70, 2)]), poly([(-70, 2), (0, 76), (70, 2)], cut=True)]


def r_dust_storm():
    ops = [rect(-B, y, B + x, 24) for y, x in ((-84, 54), (-48, 82), (-12, 28), (24, -6), (60, 30))]
    return ops + [rect(46, 10, 50, 100)]


def r_flood():
    return [wave(y, -130, 130, 15, 30) for y in (6, 52, 98)] + [poly([(-50, -28), (50, -28), (0, -88)])]


def r_lava():
    return [poly([(-B, B), (-54, -44), (54, -44), (B, B)]), line([(2, -52), (-20, 2), (18, 40), (-10, B)], 26, cut=True),
            circ(0, -70, 16)]


def r_quicksand():
    # A body half in the ground, and which way it is going.
    return [rect(-B, 12, 2 * B, B), rect(-46, -36, 92, 48), rect(-46, 12, 92, 46, cut=True),
            poly([(-34, -86), (34, -86), (0, -46)])]


def r_rockfall():
    return [poly([(-B, -B), (46, -B), (-B, 72)]), _square(34, -28, 50, 22), _square(68, 48, 40, -18), _square(-4, 62, 44, 40)]


def r_storm():
    return [rect(-B, -B, 2 * B, 68), poly([(20, -40), (-54, 26), (-8, 26), (-40, 92), (56, -8), (14, -8), (46, -40)])]


def r_venom_plant():
    # A barbed line, and what comes off it.
    ops = [line([(-B, B), (B, -B)], 26)]
    for t, x, y in ((1, -46, 46), (1, 14, -14), (1, 62, -62)):
        ops.append(poly([(x - 20, y + 20), (x + 20, y - 20), (x - 34, y - 34)]))
    for t, x, y in ((1, -72, 72), (1, -12, 12)):
        ops.append(poly([(x - 20, y + 20), (x + 20, y - 20), (x + 34, y + 34)]))
    return ops + [poly([(56, -6), (34, 38), (78, 38)]), circ(56, 44, 25)]


def r_wildlife():
    # Teeth, and nothing else.
    ops = [rect(-B, -B, 2 * B, 46), rect(-B, 66, 2 * B, 46)]
    for i in range(4):
        x = -72 + i * 48
        ops.append(poly([(x - 21, -68), (x + 21, -68), (x, 6)]))
    for i in range(5):
        x = -96 + i * 48
        ops.append(poly([(x - 21, 68), (x + 21, 68), (x, -6)]))
    return ops


RESTYLE = {
    "fire": r_fire, "toxic_air": r_toxic_air, "biohazard": r_biohazard, "contamination": r_contamination,
    "sealed": r_sealed, "armoury": r_armoury, "cryo": r_cryo, "medical": r_medical,
    "healing_booth": r_healing_booth, "decontamination": r_decontamination, "food_store": r_food_store,
    "suit": r_suit, "evacuation": r_evacuation, "rockfall": r_rockfall, "flood": r_flood,
    "quicksand": r_quicksand, "lava": r_lava, "storm": r_storm, "dust_storm": r_dust_storm,
    "wildlife": r_wildlife, "venom_plant": r_venom_plant, "contaminated": r_contaminated,
}


#         id                meaning                    code       level  symbol
SIGNS = [
    ("pressurised",      "Pressurised area",       "Void",    0, s_pressurised),
    ("no_air",           "No air beyond",          "Void",    3, s_no_air),
    ("airlock",          "Airlock",                "Void",    0, s_airlock),
    ("bulkhead",         "Bulkhead door",          "Void",    0, a_bulkhead),
    ("suit",             "Suit required",          "Void",    2, s_suit),
    ("gravity_on",       "Artificial gravity on",  "Void",    0, s_gravity_on),
    ("no_gravity",       "No gravity",             "Void",    1, s_no_gravity),
    ("gravity_anomaly",  "Gravity anomaly ahead",  "Void",    2, s_gravity_anomaly),
    ("hull_breach",      "Hull breach",            "Void",    3, a_hull_breach),
    ("hazard",           "General hazard",         "Danger",  1, s_hazard),
    ("fire",             "Fire",                   "Danger",  2, s_fire),
    ("extreme_heat",     "Extreme heat",           "Danger",  2, s_extreme_heat),
    ("toxic_air",        "Toxic air",              "Danger",  2, s_toxic_air),
    ("biohazard",        "Biohazard",              "Danger",  2, s_biohazard),
    ("contamination",    "Contamination",          "Danger",  2, s_contamination),
    ("unstable",         "Unstable structure",     "Danger",  1, s_unstable),
    ("falling_debris",   "Falling debris",         "Danger",  2, a_falling_debris),
    ("moving_machinery", "Moving machinery",       "Danger",  1, a_moving_machinery),
    ("explosives",       "Explosives",             "Danger",  3, a_explosives),
    ("restricted",       "Restricted area",        "Order",   1, s_restricted),
    ("sealed",           "Sealed by order",        "Order",   2, s_sealed),
    ("do_not_open",      "Do not open",            "Order",   2, s_do_not_open),
    ("armoury",          "Armoury",                "Order",   0, s_armoury),
    ("ammo_store",       "Ammunition store",       "Order",   1, a_ammo_store),
    ("radiation",        "Radiation",              "Energy",  1, s_radiation),
    ("high_radiation",   "High radiation",         "Energy",  3, s_high_radiation),
    ("high_voltage",     "High voltage",           "Energy",  2, s_high_voltage),
    ("generator",        "Power generator",        "Energy",  0, s_generator),
    ("fuel",             "Fuel",                   "Energy",  1, a_fuel),
    ("laser",            "Laser",                  "Energy",  2, a_laser),
    ("extreme_cold",     "Extreme cold",           "Cold",    2, s_extreme_cold),
    ("thin_ice",         "Thin ice",               "Cold",    2, s_thin_ice),
    ("avalanche",        "Avalanche",              "Cold",    3, s_avalanche),
    ("cryo",             "Cryo chamber",           "Cold",    0, s_cryo),
    ("cold_store",       "Cold store",             "Cold",    0, s_cold_store),
    ("medical",          "Medical",                "Life",    0, s_medical),
    ("healing_booth",    "Healing booth",          "Life",    0, s_healing_booth),
    ("decontamination",  "Decontamination",        "Life",    0, s_decontamination),
    ("food_store",       "Food store",             "Life",    0, s_food_store),
    ("galley",           "Galley",                 "Life",    0, a_galley),
    ("water",            "Water",                  "Life",    0, a_water),
    ("life_support",     "Life support",           "Life",    0, a_life_support),
    ("clone",            "Clone chamber",          "Life",    0, a_clone),
    ("storage",          "General storage",        "Systems", 0, s_storage),
    ("terminal",         "Computer terminal",      "Systems", 0, s_terminal),
    ("command",          "Command / bridge",       "Systems", 0, s_command),
    ("workshop",         "Workshop / fabricator",  "Systems", 0, s_workshop),
    ("maintenance",      "Maintenance",            "Systems", 0, a_maintenance),
    ("comms",            "Comms",                  "Systems", 0, a_comms),
    ("recycler",         "Recycler",               "Systems", 0, a_recycler),
    ("trade_post",       "Trade post",             "Systems", 0, a_trade_post),
    ("arrow",            "Direction",              "Route",   0, s_arrow),
    ("ladder",           "Ladder",                 "Route",   0, s_ladder),
    ("stairs",           "Stairs",                 "Route",   0, a_stairs),
    ("lift",             "Lift",                   "Route",   0, a_lift),
    ("exit",             "Exit",                   "Route",   0, s_exit),
    ("evacuation",       "Evacuation route",       "Route",   0, s_evacuation),
    ("hangar",           "Hangar / vehicle bay",   "Route",   0, a_hangar),
    ("landing_pad",      "Landing pad",            "Route",   0, a_landing_pad),
    ("docking",          "Docking port",           "Route",   0, a_docking),
    ("teleporter",       "Teleporter",             "Route",   0, a_teleporter),
    ("deep_drop",        "Cliff edge / deep drop", "Land",    2, s_deep_drop),
    ("rockfall",         "Rockfall",               "Land",    2, s_rockfall),
    ("sinkhole",         "Sinkhole",               "Land",    2, s_sinkhole),
    ("seismic",          "Seismic zone",           "Land",    1, s_seismic),
    ("flood",            "Flood",                  "Land",    2, s_flood),
    ("deep_water",       "Deep water",             "Land",    1, s_deep_water),
    ("quicksand",        "Quicksand / bog",        "Land",    3, s_quicksand),
    ("lava",             "Lava",                   "Land",    3, s_lava),
    ("geyser",           "Geyser / vent",          "Land",    2, s_geyser),
    ("storm",            "Lightning storm",        "Land",    2, s_storm),
    ("tornado",          "Tornado",                "Land",    3, s_tornado),
    ("high_wind",        "High wind",              "Land",    1, s_high_wind),
    ("dust_storm",       "Dust storm",             "Land",    1, s_dust_storm),
    ("wildlife",         "Dangerous wildlife",     "Land",    2, s_wildlife),
    ("venom_plant",      "Venomous plants",        "Land",    2, s_venom_plant),
    ("mines",            "Buried mines",           "Land",    3, s_mines),
    ("contaminated",     "Contaminated ground",    "Land",    2, s_contaminated_ground),
    ("ore",              "Ore deposit / mine",     "Land",    0, a_ore),
    ("crash",            "Crash site",             "Land",    1, a_crash),
    ("storm_shelter",    "Storm shelter",          "Land",    0, a_storm_shelter),
]


# --- SVG output --------------------------------------------------------------
def chamfered(x0, y0, x1, y1, c, fill):
    pts = [(x0 + c, y0), (x1 - c, y0), (x1, y0 + c), (x1, y1 - c),
           (x1 - c, y1), (x0 + c, y1), (x0, y1 - c), (x0, y0 + c)]
    return f'<polygon points="{" ".join(f"{n(x)},{n(y)}" for x, y in pts)}" fill="{fill}"/>'


def pips(level):
    if not level:
        return ""
    total = level * PIP_W + (level - 1) * PIP_GAP
    x = (T - total) / 2
    y = T - SILL / 2 - PIP_H / 2
    return "".join(f'<rect x="{n(x + i * (PIP_W + PIP_GAP))}" y="{n(y)}" width="{PIP_W}" height="{PIP_H}" '
                   f'rx="2" fill="{PAPER}"/>' for i in range(level))


def svg_open(title):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {T} {T}" width="{T}" height="{T}">'
            f"<title>{title}</title>")


def framed_svg(meaning, code, level, ops):
    field, ink, _ = CATS[code]
    body = "".join(ops).replace("{INK}", ink).replace("{CUT}", field)
    return "".join([
        svg_open(f"{meaning} ({code})"),
        f'<rect width="{T}" height="{T}" rx="{RADIUS}" fill="{RED}"/>',
        chamfered(BAND, BAND, T - BAND, T - SILL, CHAMFER, PAPER),
        chamfered(BAND + KEY, BAND + KEY, T - BAND - KEY, T - SILL - KEY, CHAMFER - 3, field),
        f'<g transform="translate({T / 2} {n(SYM_CY)}) scale({SYM_SCALE})">{body}</g>',
        pips(level),
        "</svg>\n",
    ])


def stencil_svg(meaning, code, ops):
    body = "".join(ops).replace("{INK}", "#fff").replace("{CUT}", "#000")
    tr = f"translate({T / 2} {T / 2}) scale({STENCIL_SCALE})"
    return "".join([
        svg_open(f"{meaning} stencil"),
        f'<mask id="s" maskUnits="userSpaceOnUse" x="0" y="0" width="{T}" height="{T}">',
        f'<g transform="{tr}">{body}</g></mask>',
        f'<rect width="{T}" height="{T}" fill="{STENCIL}" mask="url(#s)"/>',
        "</svg>\n",
    ])


FRAME2_BAND, FRAME2_SILL, FRAME2_KEY = 24, 44, 8


def framed2_svg(meaning, code, level, ops):
    """The firmer frame: heavier band, stronger keyline, plain round-cornered field."""
    field, ink, _ = CATS[code]
    x0, y0 = FRAME2_BAND + FRAME2_KEY, FRAME2_BAND + FRAME2_KEY
    fw, fh = T - 2 * x0, T - FRAME2_SILL - FRAME2_KEY - y0
    kx, ky = FRAME2_BAND, FRAME2_BAND
    kw, kh = T - 2 * kx, T - FRAME2_SILL - ky
    cx, cy = T / 2, y0 + fh / 2
    body = "".join(ops).replace("{INK}", ink).replace("{CUT}", field)
    return "".join([
        svg_open(f"{meaning} ({code})"),
        f'<clipPath id="f"><rect x="{x0}" y="{y0}" width="{fw}" height="{fh}" rx="9"/></clipPath>',
        f'<rect width="{T}" height="{T}" rx="{RADIUS}" fill="{RED}"/>',
        f'<rect x="{kx}" y="{ky}" width="{kw}" height="{kh}" rx="15" fill="{PAPER}"/>',
        f'<rect x="{x0}" y="{y0}" width="{fw}" height="{fh}" rx="9" fill="{field}"/>',
        f'<g clip-path="url(#f)"><g transform="translate({n(cx)} {n(cy)})">{body}</g></g>',
        pips(level),
        "</svg>\n",
    ])


def stencil2_svg(meaning, ops):
    k = 1.24
    body = "".join(ops).replace("{INK}", "#fff").replace("{CUT}", "#000")
    mx, my = FW * k, FH * k
    edge = "".join(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="#000"/>' for x, y, w, h in (
        (0, 0, T, n(T / 2 - my)), (0, n(T / 2 + my), T, n(T / 2 - my)),
        (0, 0, n(T / 2 - mx), T), (n(T / 2 + mx), 0, n(T / 2 - mx), T)))
    return "".join([
        svg_open(f"{meaning} stencil"),
        f'<mask id="s" maskUnits="userSpaceOnUse" x="0" y="0" width="{T}" height="{T}">',
        f'<g transform="translate({T / 2} {T / 2}) scale({k})">{body}</g>{edge}</mask>',
        f'<rect width="{T}" height="{T}" fill="{STENCIL}" mask="url(#s)"/>',
        "</svg>\n",
    ])


# --- rendering ---------------------------------------------------------------
GD = """extends SceneTree
func _init():
\tvar args = OS.get_cmdline_user_args()
\tvar src = args[0]
\tvar out = args[1]
\tvar scale = float(args[2])
\tvar d = DirAccess.open(src)
\tfor f in d.get_files():
\t\tif not f.ends_with(".svg"):
\t\t\tcontinue
\t\tvar img = Image.new()
\t\tvar err = img.load_svg_from_string(FileAccess.get_file_as_string(src.path_join(f)), scale)
\t\tif err != OK:
\t\t\tpush_error("failed " + f)
\t\t\tcontinue
\t\timg.save_png(out.path_join(f.get_basename() + ".png"))
\tquit()
"""


def render(src: Path, out: Path, scale: float):
    RENDER_TMP.mkdir(parents=True, exist_ok=True)
    (RENDER_TMP / "project.godot").write_text('config_version=5\n[application]\nconfig/name="signs"\n')
    (RENDER_TMP / "render.gd").write_text(GD)
    out.mkdir(parents=True, exist_ok=True)
    subprocess.run([GODOT, "--headless", "--path", str(RENDER_TMP), "--script", "render.gd", "--",
                    src.as_posix(), out.as_posix(), str(scale)], check=True, capture_output=True, timeout=300)


def font(size, bold=False):
    return ImageFont.truetype(r"C:\Windows\Fonts\arialbd.ttf" if bold else r"C:\Windows\Fonts\arial.ttf", size)


def sheets(masters: Path):
    tiles = {sid: Image.open(masters / f"{sid}.png").convert("RGBA") for sid, *_ in SIGNS}
    png = ROOT / "png"
    png.mkdir(exist_ok=True)
    for sid, im in tiles.items():
        im.resize((256, 256), Image.LANCZOS).save(png / f"{sid}.png")

    # review sheet
    cols, tile, gap_x, gap_y, label_h = 9, 190, 22, 28, 52
    margin = (2000 - (cols * tile + (cols - 1) * gap_x)) // 2
    head = 160
    rows = math.ceil(len(SIGNS) / cols)
    legend_y = head + rows * (tile + label_h + gap_y) + 10
    sheet = Image.new("RGB", (2000, legend_y + 170), "white")
    d = ImageDraw.Draw(sheet)
    d.text((margin, 56), "GalHack sign set: draft 3", font=font(60, True), fill=INK)
    for i, (sid, meaning, code, _level, _fn) in enumerate(SIGNS):
        x = margin + (i % cols) * (tile + gap_x)
        y = head + (i // cols) * (tile + label_h + gap_y)
        im = tiles[sid].resize((tile, tile), Image.LANCZOS)
        sheet.paste(im, (x, y), im)
        cx = x + tile / 2
        d.text((cx, y + tile + 10), meaning, font=font(18), fill=INK, anchor="ma")
        d.text((cx, y + tile + 32), code, font=font(15), fill="#666666", anchor="ma")
    # legend
    d.line((margin, legend_y, 2000 - margin, legend_y), fill="#BBBBBB", width=2)
    col_w = (2000 - 2 * margin) // len(CATS)
    for i, (code, (field, _ink, covers)) in enumerate(CATS.items()):
        x = margin + i * col_w
        d.rounded_rectangle((x, legend_y + 26, x + 44, legend_y + 70), 8, fill=field, outline="#888888", width=2)
        d.text((x + 56, legend_y + 26), code, font=font(22, True), fill=INK)
        d.text((x + 56, legend_y + 54), covers, font=font(14), fill="#555555")
    d.text((margin, legend_y + 96), "Bottom bar: no pips = a place.  One pip = take care.  Two = dangerous.  "
           "Three = lethal without protection.", font=font(20), fill=INK)
    d.text((margin, legend_y + 126), "The 25 signs with the firmer frame are new in draft 3. "
           "Older signs stay as approved: see signs-restyle.png for proposed changes.", font=font(18), fill="#555555")
    sheet.save(ROOT / "sign-sheet.png", optimize=True)

    # legibility sheet: real 64 px tiles, box-filtered like a mipped texture
    tile, gap, margin = 64, 16, 24
    w = 2 * margin + cols * tile + (cols - 1) * gap
    h = 2 * margin + rows * tile + (rows - 1) * gap
    small = Image.new("RGB", (w, h), "#808080")
    for i, (sid, *_rest) in enumerate(SIGNS):
        im = tiles[sid].resize((tile, tile), Image.BOX)
        small.paste(im, (margin + (i % cols) * (tile + gap), margin + (i // cols) * (tile + gap)), im)
    small.save(ROOT / "sign-sheet-small.png", optimize=True)


def restyle(masters: Path):
    """Proposed replacements: written beside the originals, never over them."""
    out = ROOT / "restyle"
    shutil.rmtree(out, ignore_errors=True)
    for sub in ("svg", "svg-stencil", "png"):
        (out / sub).mkdir(parents=True)
    info = {sid: (meaning, code, level) for sid, meaning, code, level, _fn in SIGNS}
    ordered = [sid for sid, *_rest in SIGNS if sid in RESTYLE]
    for sid in ordered:
        meaning, code, level = info[sid]
        ops = RESTYLE[sid]()
        (out / "svg" / f"{sid}.svg").write_text(framed2_svg(meaning, code, level, ops), encoding="utf-8")
        (out / "svg-stencil" / f"{sid}.svg").write_text(stencil2_svg(meaning, ops), encoding="utf-8")
    after = RENDER_TMP / "restyle-masters"
    shutil.rmtree(after, ignore_errors=True)
    render(out / "svg", after, 4.0)
    for sid in ordered:
        Image.open(after / f"{sid}.png").convert("RGBA").resize((256, 256), Image.LANCZOS).save(out / "png" / f"{sid}.png")

    cols, tile, gap, cell_w, cell_h = 4, 196, 12, 468, 262
    margin = (2000 - cols * cell_w) // 2
    head = 170
    rows = math.ceil(len(ordered) / cols)
    strip_y = head + rows * cell_h + 20
    sheet = Image.new("RGB", (2000, strip_y + 150), "white")
    d = ImageDraw.Draw(sheet)
    d.text((margin + 20, 46), "GalHack sign restyle: draft 3", font=font(60, True), fill=INK)
    d.text((margin + 20, 120), "Each pair: approved sign on the left, proposed sign and firmer frame on the right. "
           "Nothing is replaced until the owner decides.", font=font(22), fill="#555555")
    for i, sid in enumerate(ordered):
        x = margin + (i % cols) * cell_w + (cell_w - 2 * tile - gap) // 2
        y = head + (i // cols) * cell_h
        for j, base in enumerate((masters, after)):
            im = Image.open(base / f"{sid}.png").convert("RGBA").resize((tile, tile), Image.LANCZOS)
            sheet.paste(im, (x + j * (tile + gap), y), im)
        d.text((x + tile + gap // 2, y + tile + 8), info[sid][0], font=font(19), fill=INK, anchor="ma")
    d.line((margin + 20, strip_y, 2000 - margin - 20, strip_y), fill="#BBBBBB", width=2)
    d.text((margin + 20, strip_y + 14), "Proposed signs at 64 pixels", font=font(20), fill="#555555")
    step = (2000 - 2 * margin - 40) // len(ordered)
    for i, sid in enumerate(ordered):
        im = Image.open(after / f"{sid}.png").convert("RGBA").resize((64, 64), Image.BOX)
        sheet.paste(im, (margin + 20 + i * step, strip_y + 54), im)
    sheet.save(ROOT / "signs-restyle.png", optimize=True)


def stencil_check(masters: Path, out: Path):
    cols, tile = 8, 150
    rows = math.ceil(len(SIGNS) / cols)
    sheet = Image.new("RGB", (cols * tile, rows * tile), "#D8C9A8")
    for i, (sid, *_rest) in enumerate(SIGNS):
        im = Image.open(masters / f"{sid}.png").convert("RGBA").resize((tile - 10, tile - 10), Image.LANCZOS)
        sheet.paste(im, ((i % cols) * tile + 5, (i // cols) * tile + 5), im)
    sheet.save(out)


def main():
    for folder in ("svg", "svg-stencil", "png"):
        shutil.rmtree(ROOT / folder, ignore_errors=True)
        (ROOT / folder).mkdir()
    for sid, meaning, code, level, fn in SIGNS:
        ops = fn()
        if fn.__name__.startswith("a_"):
            framed, stencil = framed2_svg(meaning, code, level, ops), stencil2_svg(meaning, ops)
        else:
            framed, stencil = framed_svg(meaning, code, level, ops), stencil_svg(meaning, code, ops)
        (ROOT / "svg" / f"{sid}.svg").write_text(framed, encoding="utf-8")
        (ROOT / "svg-stencil" / f"{sid}.svg").write_text(stencil, encoding="utf-8")

    masters = RENDER_TMP / "masters"
    stencils = RENDER_TMP / "stencils"
    for p in (masters, stencils):
        shutil.rmtree(p, ignore_errors=True)
    render(ROOT / "svg", masters, 4.0)
    sheets(masters)
    restyle(masters)
    if "--stencil-check" in sys.argv:
        render(ROOT / "svg-stencil", stencils, 2.0)
        idx = sys.argv.index("--stencil-check") + 1
        out = Path(sys.argv[idx]) if idx < len(sys.argv) else RENDER_TMP / "stencil-check.png"
        stencil_check(stencils, out)
    print(len(SIGNS), "signs built")


if __name__ == "__main__":
    main()
