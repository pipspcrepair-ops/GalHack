#!/usr/bin/env python3
"""GalHack traveller marks: the unofficial family. Original artwork, drawn here.

These are what survivors scratch on walls, so nothing in this file uses a ruled
line.  Each stroke is turned into a wobbling, uneven, tapering filled shape, the
way a stick or a lump of chalk behaves.  Single colour on transparent.

Marks are written on a 256 canvas, y down.  The wobble is seeded from the mark's
id, so a rebuild gives the same drawing.  Run:  python build_marks.py
"""
import math
import random
import shutil
import sys
import zlib
from pathlib import Path

from PIL import Image, ImageDraw

from build_signs import ROOT, RENDER_TMP, font, render

OUT = ROOT / "marks"
CHALK = "#EFE9DA"
WALL = "#3A3D40"
W = 25  # stroke width: a thick stick, 6 px wide at 64 px


class Hand:
    """Draws like a person in a hurry."""

    def __init__(self, sid):
        self.r = random.Random(zlib.crc32(sid.encode()))
        self.shapes = []

    def _noise(self, amount, wavelength):
        p1, p2 = self.r.uniform(0, 6.28), self.r.uniform(0, 6.28)
        f1, f2 = 6.28 / wavelength, 6.28 / (wavelength * 0.43)
        return lambda t: amount * (0.7 * math.sin(p1 + f1 * t) + 0.3 * math.sin(p2 + f2 * t))

    def stroke(self, pts, w=W, wobble=3.0, lift=True):
        """A freehand stroke through pts. The start bites in, the end lifts away."""
        # resample
        samples = []
        for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
            d = math.hypot(x1 - x0, y1 - y0)
            for i in range(max(1, int(d / 7))):
                t = i / max(1, int(d / 7))
                samples.append((x0 + (x1 - x0) * t, y0 + (y1 - y0) * t))
        samples.append(pts[-1])
        drift = self._noise(wobble, 90)
        width = self._noise(0.13, 60)
        left, right = [], []
        length = len(samples)
        for i, (x, y) in enumerate(samples):
            a = samples[max(0, i - 1)]
            b = samples[min(length - 1, i + 1)]
            dx, dy = b[0] - a[0], b[1] - a[1]
            d = math.hypot(dx, dy) or 1
            nx, ny = -dy / d, dx / d
            t = i / max(1, length - 1)
            taper = min(1, 0.72 + t * 6)            # blunt bite at the start
            if lift:
                taper *= min(1, 0.42 + (1 - t) * 4)  # thins as the hand lifts
            h = w / 2 * (1 + width(i * 7)) * taper
            cx, cy = x + nx * drift(i * 7), y + ny * drift(i * 7)
            left.append((cx + nx * (h + self.r.uniform(-1.1, 1.1)), cy + ny * (h + self.r.uniform(-1.1, 1.1))))
            right.append((cx - nx * (h + self.r.uniform(-1.1, 1.1)), cy - ny * (h + self.r.uniform(-1.1, 1.1))))
        ring = left + right[::-1]
        self.shapes.append("M" + " L".join(f"{x:.1f} {y:.1f}" for x, y in ring) + " Z")

    def line(self, x0, y0, x1, y1, w=W, wobble=3.0, overshoot=5):
        """A straight-ish stroke that slightly overruns, as strokes on a wall do."""
        d = math.hypot(x1 - x0, y1 - y0) or 1
        ux, uy = (x1 - x0) / d, (y1 - y0) / d
        e = self.r.uniform(0, overshoot)
        self.stroke([(x0, y0), (x1 + ux * e, y1 + uy * e)], w, wobble)

    def arc(self, cx, cy, rx, ry, a0, a1, w=W, wobble=3.0, lift=True):
        """Part of a hand-drawn ellipse, degrees, 0 = east, clockwise on screen."""
        steps = max(8, int(abs(a1 - a0) / 8))
        pts = [(cx + rx * math.cos(math.radians(a0 + (a1 - a0) * i / steps)),
                cy + ry * math.sin(math.radians(a0 + (a1 - a0) * i / steps))) for i in range(steps + 1)]
        self.stroke(pts, w, wobble, lift)

    def loop(self, cx, cy, rx, ry=None, w=W, start=-100):
        """A closed-ish loop: the ends overlap and do not quite meet."""
        self.arc(cx, cy, rx, ry or rx, start, start + 368, w)

    def worm(self, x, y, ang, length, amp=14, waves=1.25, w=20, phase=0.0, tip=0):
        """A worm: one even, blunt, sine-bent stroke, not flicked off like a line. tip = radius of a round head."""
        a = math.radians(ang)
        ux, uy = math.cos(a), math.sin(a)
        nx, ny = -uy, ux
        pts = []
        for i in range(26):
            t = i / 25
            off = amp * math.sin(6.283 * waves * t + phase)
            pts.append((x + ux * length * t + nx * off, y + uy * length * t + ny * off))
        self.stroke(pts, w, 1.2, lift=False)
        if tip:
            self.dot(pts[-1][0], pts[-1][1], tip)

    def dot(self, x, y, r=15):
        pts = []
        for i in range(10):
            a = i / 10 * 6.283
            k = r * self.r.uniform(0.85, 1.15)
            pts.append((x + k * math.cos(a), y + k * math.sin(a)))
        self.shapes.append("M" + " L".join(f"{px:.1f} {py:.1f}" for px, py in pts) + " Z")

    def svg(self, title):
        body = "".join(f'<path d="{d}"/>' for d in self.shapes)
        return ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256">'
                f'<title>{title}</title><g fill="{CHALK}">{body}</g></svg>\n')


# --- shared grammar ----------------------------------------------------------
def thumb(h, x=128, y=222):
    """The GOOD stroke: one heavy bar under the mark. Someone vouches for this."""
    h.line(x - 62, y, x + 62, y - 3, W, 2)


def strike(h):
    """The BAD stroke: a hard slash from high right to low left, gone over twice."""
    h.line(216, 34, 44, 216, W + 2, 2.5)
    h.line(222, 46, 56, 222, 15, 3)


def face(h):
    h.loop(128, 124, 88, 90, W)


# --- the marks ---------------------------------------------------------------
def m_safe_camp(h):
    # A low ring of stones with the fire still going. Bar under: someone vouches.
    h.arc(128, 150, 92, 36, 10, 170, W)
    h.arc(128, 150, 92, 36, 170, 370, W)
    h.line(100, 138, 74, 64, W)
    h.line(128, 134, 130, 24, W + 3)
    h.line(156, 138, 186, 64, W)
    thumb(h, y=226)


def _drop(h):
    pts = [(128, 24), (104, 66)]
    pts += [(128 + 62 * math.cos(math.radians(a)), 122 + 58 * math.sin(math.radians(a)))
            for a in range(205, -26, -15)]
    h.stroke(pts + [(152, 66), (130, 22)], W, 2.0, lift=False)


def m_clean_water(h):
    _drop(h)
    thumb(h)


def m_bad_water(h):
    _drop(h)
    strike(h)


def m_food(h):
    # A bowl with the spoon left standing in it.
    h.arc(120, 112, 86, 86, 170, 10, W + 2)
    h.line(36, 112, 204, 108, W)
    h.line(150, 150, 214, 26, W)
    h.dot(216, 32, 19)


def m_shelter(h):
    # A roof, and somebody home under it.
    h.line(28, 150, 128, 42, W)
    h.line(128, 42, 228, 150, W)
    h.dot(128, 150, 26)
    h.line(52, 206, 204, 202, W)


def m_cache(h):
    # A little heap of stones on the ground, and a box under it.
    h.line(20, 96, 236, 92, W)
    h.dot(94, 62, 17)
    h.dot(134, 60, 17)
    h.dot(114, 26, 16)
    h.line(58, 138, 150, 136, W)
    h.line(150, 134, 152, 226, W)
    h.line(152, 226, 56, 228, W)
    h.line(58, 230, 58, 134, W)


def m_trap(h):
    # Ground that stops, and stakes in the hole.
    h.line(10, 88, 50, 88, W)
    h.line(50, 84, 54, 216, W)
    h.line(50, 214, 208, 214, W)
    h.line(208, 216, 208, 86, W)
    h.line(204, 88, 246, 86, W)
    for x, top in ((104, 96), (152, 96)):
        h.stroke([(x - 3, 208), (x + 2, top)], 28, 1.0)


def m_danger(h):
    # Two stakes driven in. One is a warning. Two means it.
    for x in (84, 172):
        h.stroke([(x + 4, 22), (x, 96), (x - 2, 158)], 34, 2)
        h.dot(x - 2, 208, 21)


def m_keep_moving(h):
    # An arrow somebody kept going over: do not stop here.
    h.line(24, 128, 200, 124, W)
    h.line(130, 56, 222, 124, W)
    h.line(130, 196, 222, 126, W)
    for x in (44, 84):
        h.line(x, 84, x + 26, 170, 20)


def m_hostile(h):
    # A head that is all teeth, brows driven down.
    face(h)
    h.line(66, 74, 112, 104, 21)
    h.line(190, 74, 144, 104, 21)
    h.stroke([(66, 152), (90, 128), (108, 168), (128, 130), (148, 168), (166, 128), (190, 152)], 19, 1.5)


def m_friendly(h):
    # A hand held up open, with nothing in it.
    h.loop(126, 138, 54, 52, W)
    h.line(88, 100, 68, 34, W - 2)
    h.line(114, 88, 108, 16, W - 2)
    h.line(142, 88, 150, 18, W - 2)
    h.line(166, 102, 190, 40, W - 2)
    h.line(74, 152, 26, 112, W - 2)
    thumb(h, y=228)


def m_sick(h):
    # A head that has come out in spots, and has stopped looking back at you.
    face(h)
    h.line(78, 96, 110, 96, 19)
    h.line(146, 96, 178, 96, 19)
    h.line(92, 166, 164, 164, 19)
    for x, y in ((100, 132), (154, 134), (128, 62)):
        h.dot(x, y, 12)


def m_trader(h):
    # Yours goes that way, mine comes this way.
    h.line(30, 78, 216, 76, W)
    h.line(166, 28, 226, 78, W)
    h.line(166, 126, 226, 80, W)
    h.line(226, 178, 40, 180, W)
    h.line(90, 130, 30, 178, W)
    h.line(90, 228, 30, 180, W)


def m_nest(h):
    # Eggs in a hollowed-out place, not touching.
    h.loop(72, 118, 29, 38, 20)
    h.loop(184, 118, 29, 38, 20)
    h.loop(128, 58, 29, 38, 20)
    h.arc(128, 116, 116, 104, 170, 10, W + 2)


def m_dead_end(h):
    # You walk, you stop.
    h.line(24, 130, 164, 128, W)
    h.line(186, 34, 190, 226, W + 8)
    h.line(232, 60, 234, 200, 20)


def m_way_out(h):
    # The same wall as a dead end, but this one has a gap in it.
    h.line(116, 18, 118, 88, W + 8)
    h.line(116, 168, 118, 238, W + 8)
    h.line(24, 128, 214, 126, W)
    h.line(166, 72, 230, 126, W)
    h.line(168, 182, 230, 128, W)


def _seg_dist(px, py, ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    t = max(0, min(1, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
    return math.hypot(px - ax - t * dx, py - ay - t * dy)


def _gap(x, y):
    """The part of the wall nobody would touch. Never drawn, only avoided."""
    parts = [
        (104, 44, 106, 50, 13),     # too small, and not quite over the shoulders
        (108, 76, 112, 150, 25),
        (86, 70, 66, 194, 9),       # too long
        (130, 72, 152, 206, 9),     # longer
        (100, 150, 92, 250, 13),
        (120, 150, 128, 250, 13),
        (204, 158, 204, 160, 7),    # again, further off
        (204, 176, 206, 216, 12),
    ]
    return any(_seg_dist(x, y, a, b, c, d) < r for a, b, c, d, r in parts)


def _hatch(h, angle, spacing, w, length_jitter):
    """Fast parallel scratches that always stop short of the gap."""
    a = math.radians(angle)
    ux, uy = math.cos(a), math.sin(a)
    nx, ny = -uy, ux
    offset = -190.0
    while offset < 190:
        offset += spacing * h.r.uniform(0.8, 1.2)
        run = []
        reach = 128 + h.r.uniform(-length_jitter, 0)
        t = -reach + h.r.uniform(0, length_jitter)
        while t <= reach:
            x = 128 + nx * offset + ux * t
            y = 128 + ny * offset + uy * t
            if 8 < x < 248 and 8 < y < 248 and not _gap(x, y):
                run.append((x, y))
            elif run:
                if len(run) > 1:
                    h.stroke([run[0], run[-1]], w, 2.0)
                run = []
            t += 4
        if len(run) > 1:
            h.stroke([run[0], run[-1]], w, 2.0)


def m_warding(h):
    # Scratched fast, and gone over again. Whoever made this drew everything
    # except the part that matters, and would not let the stick go near it.
    _hatch(h, 74, 14, 13, 30)
    _hatch(h, 112, 27, 10, 70)


def _barbs(h, x, y, hx, hy, length=58, w=W):
    """Two strokes back from an arrow tip at (x, y) heading along (hx, hy)."""
    for a in (32, -32):
        c, sn = math.cos(math.radians(a)), math.sin(math.radians(a))
        bx, by = -hx * c + hy * sn, -hx * sn - hy * c
        h.line(x + bx * length, y + by * length, x, y, w)


def m_salvage(h):
    # A cog with a bite out of it, and the piece beside it.
    h.arc(120, 124, 72, 72, 130, 400, W)
    for a in (160, 215, 270, 325, 375):
        c, sn = math.cos(math.radians(a)), math.sin(math.radians(a))
        h.line(120 + 72 * c, 124 + 72 * sn, 120 + 108 * c, 124 + 108 * sn, W)
    h.dot(120, 124, 19)
    h.line(186, 200, 226, 236, W)


def m_wreck(h):
    # A hull standing nose down in the ground, tail spread in the air.
    h.line(18, 214, 238, 210, W)
    h.line(186, 56, 88, 208, W + 8)
    h.line(186, 58, 150, 22, W)
    h.line(186, 58, 238, 54, W)
    h.line(50, 176, 28, 146, 19)
    h.line(138, 182, 166, 156, 19)


def m_fuel(h):
    # A can with a clipped corner and a spout. The dot means it is not empty.
    h.line(112, 80, 196, 78, W)
    h.line(114, 80, 62, 130, W)
    h.line(196, 76, 198, 228, W)
    h.line(198, 228, 60, 230, W)
    h.line(62, 232, 62, 126, W)
    h.line(92, 96, 42, 40, W - 3)
    h.line(150, 52, 186, 52, 20)
    h.dot(130, 164, 26)


def m_road_blocked(h):
    # A road going away from you, and something jagged thrown across it.
    h.line(64, 16, 54, 240, W)
    h.line(192, 16, 202, 240, W)
    h.stroke([(14, 150), (58, 92), (88, 156), (128, 94), (164, 158), (198, 96), (244, 150)], W + 5, 1.5)


def m_go_around(h):
    # A thing in the way, and the long way past it.
    h.dot(100, 130, 42)
    h.line(26, 222, 222, 220, W)
    h.line(222, 224, 222, 44, W)
    h.line(226, 46, 62, 44, W)
    h.line(112, 8, 52, 44, W)
    h.line(112, 82, 52, 46, W)


def m_danger_night(h):
    # Fine under the moon's back, bad under its face: the stake says when.
    h.arc(112, 128, 92, 96, 62, 298, W + 2)
    h.arc(164, 128, 76, 82, 120, 240, W - 4)
    h.stroke([(214, 34), (212, 100), (210, 158)], 32, 2)
    h.dot(210, 206, 21)


def m_sleep_here(h):
    # Someone lying down under a cover, and a place to do it.
    h.dot(62, 118, 29)
    h.arc(158, 150, 70, 62, 180, 360, W)
    h.line(22, 160, 234, 156, W)
    thumb(h, y=216)


def m_machines(h):
    # The people marks have round heads. This one has a square one.
    h.line(60, 72, 196, 70, W)
    h.line(196, 68, 198, 214, W)
    h.line(198, 214, 58, 216, W)
    h.line(60, 218, 60, 68, W)
    h.dot(102, 122, 18)
    h.dot(156, 122, 18)
    h.line(100, 172, 158, 170, 20)
    h.line(128, 60, 126, 14, 22)


def m_raider_ground(h):
    # A boundary post with what is left of the last visitors hung from it.
    # This one is about the ground, not the people: you are already on it.
    h.line(128, 236, 128, 26, W + 8)
    h.line(36, 42, 220, 38, W)
    for x, length in ((62, 112), (96, 78), (162, 96), (196, 124)):
        h.stroke([(x, 48), (x + 3, length)], 17, 2.0)
        h.dot(x + 3, length + 12, 15)
    h.line(26, 236, 230, 232, W)


def m_warding_2(h):
    # The open hand from the people marks, boarded down. Count the fingers.
    # The first mark is about nothing being there. This one is about something
    # that is there, and is not staying where it was put.
    cx, cy = 128, 152
    h.loop(cx, cy, 44, 42, 22)
    for deg, kinks in ((-152, (10, -16, 22)), (-125, (-14, 18, -20)), (-102, (12, -20, 16, -18)),
                       (-80, (-12, 22, -16)), (-56, (16, -22, 20)), (-30, (-14, 20, -24))):
        a = math.radians(deg)
        x, y = cx + 42 * math.cos(a), cy + 42 * math.sin(a)
        pts = [(x, y)]
        for kink in kinks:
            b = math.radians(deg + kink)
            step = 104 / len(kinks)
            x, y = x + step * math.cos(b), y + step * math.sin(b)
            pts.append((x, y))
        h.stroke(pts, 17, 1.5)
    h.line(14, 232, 244, 186, 28, 2.5)
    h.line(24, 254, 246, 216, 22, 2.5)


# --- draft 4: Husks and Hollows ---------------------------------------------------
# The final Husk and Hollow marks are not in MARKS, so marks-sheet.png is untouched.
def m_husk_hanging(h):
    # The old tendril mark turned over, with its ground taken away: a round body and
    # the tendrils hanging from it. A regional way of drawing "Husks here".
    cx, cy = 128, 88
    h.loop(cx, cy, 44, 42, 22)
    for deg, kinks in ((152, (-10, 16, -22)), (125, (14, -18, 20)), (102, (-12, 20, -16, 18)),
                       (80, (12, -22, 16)), (56, (-16, 22, -20)), (30, (14, -20, 24))):
        a = math.radians(deg)
        x, y = cx + 42 * math.cos(a), cy + 42 * math.sin(a)
        pts = [(x, y)]
        for kink in kinks:
            b = math.radians(deg + kink)
            step = 104 / len(kinks)
            x, y = x + step * math.cos(b), y + step * math.sin(b)
            pts.append((x, y))
        h.stroke(pts, 17, 1.5)


def _shroom(h, x, top, rim, rx, stem_w, foot):
    """A mushroom as one closed outline, so a slash through it cannot hide it: up the stem,
    out along the rim, over the dome and back down the other side."""
    dome = [(x + rx * math.cos(math.radians(a)), rim - (rim - top) * math.sin(math.radians(a))) for a in range(180, -1, -12)]
    pts = [(x - stem_w / 2, foot), (x - stem_w / 2 + 2, rim + 8), (x - rx, rim + 6)] + dome[1:]
    pts += [(x + rx - 2, rim + 6), (x + stem_w / 2 + 2, rim + 8), (x + stem_w / 2, foot), (x - stem_w / 2 + 4, foot + 2)]
    h.stroke(pts, W, 2.0, lift=False)


def m_hollow_a(h):
    # Fungus here, do not touch: a cap on its stem, spores coming down, struck out.
    _shroom(h, 120, 40, 120, 92, 40, 206)
    for x, y in ((186, 170), (214, 204), (170, 232)):
        h.dot(x, y, 14)
    strike(h)


def m_hollow_b(h):
    # A spotted cap and nothing else, with spores falling from under it.
    dome = [(128 + 100 * math.cos(math.radians(a)), 150 - 96 * math.sin(math.radians(a))) for a in range(180, -1, -12)]
    h.stroke(dome + [(124, 164), (30, 152)], W, 2.0, lift=False)
    for x, y, r in ((82, 118, 13), (136, 92, 12), (178, 128, 13)):
        h.dot(x, y, r)
    for x, y in ((62, 204), (120, 232), (186, 204)):
        h.dot(x, y, 14)
    strike(h)


def m_hollow_c(h):
    # A small cap in a cloud of its own spores.
    _shroom(h, 128, 82, 134, 58, 28, 196)
    for x, y in ((46, 112), (34, 168), (214, 100), (224, 160), (62, 222), (130, 232), (198, 222), (84, 38), (172, 34)):
        h.dot(x, y, 12)
    strike(h)


def _head(h, cx=128, cy=104, rx=60, ry=64):
    h.loop(cx, cy, rx, ry, W)
    h.dot(cx - 22, cy - 10, 11)
    h.dot(cx + 22, cy - 10, 11)


def m_husk_a(h):
    # A head with its mouth open and a worm hanging out of it, two more rising from the ears.
    _head(h, cy=112)
    h.dot(128, 142, 15)
    h.worm(128, 148, 90, 96, amp=15, waves=1.25, tip=12)
    h.worm(72, 106, -150, 66, amp=9, waves=1.0, w=18, phase=0.6, tip=10)
    h.worm(184, 106, -30, 66, amp=9, waves=1.0, w=18, phase=0.6, tip=10)


def m_husk_b(h):
    # The same head with worms standing up out of the eyes, and one from the mouth.
    _head(h, cy=132, rx=56, ry=60)
    h.dot(128, 160, 14)
    h.worm(106, 114, -108, 100, amp=12, waves=1.2, w=19, tip=11)
    h.worm(150, 114, -72, 100, amp=12, waves=1.2, w=19, phase=2.2, tip=11)
    h.worm(128, 166, 90, 66, amp=11, waves=1.0, w=19, tip=11)


def m_husk_c(h):
    # A head with the top of it open and three worms reaching up out of the opening.
    h.arc(128, 166, 62, 66, 300, 600, W)
    h.dot(106, 164, 11)
    h.dot(150, 164, 11)
    h.line(104, 202, 152, 202, 17)
    h.worm(106, 110, -112, 84, amp=12, waves=1.1, w=21, tip=13)
    h.worm(128, 104, -90, 88, amp=14, waves=1.4, w=21, phase=1.4, tip=13)
    h.worm(150, 110, -66, 80, amp=12, waves=1.0, w=21, phase=3.0, tip=13)


def m_husk_d(h):
    # An eye, and the thing that has come out of it.
    h.arc(128, 150, 92, 68, 200, 340, W)
    h.arc(128, 106, 92, 68, 20, 160, W)
    h.dot(120, 128, 21)
    h.worm(124, 132, 62, 118, amp=15, waves=1.1, w=20, tip=13)


def m_wrong_a(h):
    # A room that does not fit its own walls: the sides lean, the corners overrun, and a
    # second room sits inside it, turned, with one corner out through the wall.
    h.line(38, 58, 204, 48, W)
    h.line(208, 52, 214, 202, W)
    h.line(214, 206, 52, 222, W)
    h.line(56, 224, 40, 58, W)
    h.line(84, 112, 238, 96, 19)
    h.line(236, 98, 196, 182, 19)
    h.line(198, 182, 70, 168, 19)
    h.line(72, 168, 86, 108, 19)


def m_wrong_b(h):
    # A needle that will not settle: the same needle drawn at three bearings.
    h.arc(128, 128, 92, 92, 10, 350, W)
    h.dot(128, 128, 15)
    h.line(128, 128, 178, 60, 21)
    h.line(178, 60, 146, 72, 19)
    h.line(178, 60, 172, 92, 19)
    h.line(128, 128, 196, 150, 17)
    h.line(128, 128, 76, 178, 17)


def m_wrong_c(h):
    # One stake, two shadows pointing opposite ways, and a light for each.
    h.stroke([(128, 200), (128, 80)], 32, 2)
    h.dot(128, 46, 20)
    h.line(128, 232, 22, 226, 19)
    h.line(128, 232, 222, 238, 19)
    h.dot(44, 62, 14)
    h.dot(212, 86, 14)


def _legs(h, legs, y0, y1, w=17):
    """Four legs mid-stride: (x at the belly, how far the foot lands from it)."""
    for x, drift in legs:
        h.line(x, y0, x + drift, y1, w)


def _toadstool(h, x, y, height, rx, w=17):
    """A cap on a stem growing straight up out of (x, y): a stem, and a dome over the top of it."""
    h.line(x, y, x, y - height, w, 1.5)
    h.arc(x, y - height + 4, rx, rx * 0.78, 180, 360, w, 1.5)


# Hollows only ever take wildlife, so the Hollow mark is the animal wearing it. There is no
# slash: the caps on the beast are the warning.
def m_hollow_beast_a(h):
    # A beast side on, toadstools growing out of its back.
    h.loop(104, 152, 66, 28, 22)
    _legs(h, ((52, -10), (80, 2), (128, -2), (154, 12)), 174, 232)
    h.line(40, 140, 14, 106, 16)                              # tail
    h.stroke([(162, 138), (190, 106), (234, 114)], 26, 2)     # neck and snout
    h.line(194, 100, 190, 76, 13)                             # ear
    _toadstool(h, 66, 128, 44, 24)
    _toadstool(h, 108, 124, 70, 30)
    _toadstool(h, 148, 130, 36, 20)


def m_hollow_beast_b(h):
    # A long-necked grazer with caps up its back and one on its neck.
    h.loop(86, 162, 58, 26, 22)
    _legs(h, ((50, -8), (74, 4), (106, -2), (126, 10)), 182, 234)
    h.line(34, 152, 12, 122, 16)
    h.stroke([(132, 148), (166, 106), (176, 68)], 24, 2)
    h.stroke([(172, 70), (208, 62), (234, 78)], 22, 2)
    _toadstool(h, 62, 140, 40, 22)
    _toadstool(h, 100, 138, 58, 26)
    _toadstool(h, 152, 120, 30, 17)


def m_hollow_beast_c(h):
    # A low, heavy beast with a row of caps along its spine.
    h.loop(108, 172, 82, 30, 24)
    _legs(h, ((52, -8), (80, 2), (140, -2), (166, 10)), 192, 236)
    h.stroke([(30, 160), (10, 140), (16, 118)], 16, 1)
    h.stroke([(184, 166), (224, 162), (240, 176)], 30, 2)    # a heavy head
    h.line(196, 150, 200, 130, 13)
    _toadstool(h, 62, 150, 38, 20)
    _toadstool(h, 108, 142, 60, 27)
    _toadstool(h, 154, 150, 34, 18)


def m_hollow_beast_d(h):
    # A horned beast whose horns have gone to caps, and one more on its back.
    h.loop(98, 166, 62, 28, 22)
    _legs(h, ((54, -10), (80, 2), (120, -2), (144, 12)), 188, 236)
    h.line(40, 156, 16, 124, 16)
    h.stroke([(154, 150), (178, 118), (212, 118)], 26, 2)
    _toadstool(h, 176, 108, 50, 15, 14)
    _toadstool(h, 212, 110, 34, 13, 14)
    _toadstool(h, 90, 142, 56, 26)


# id, label on the final sheet, drawing, title in the file, seed. The seed is the id the wobble was first
# drawn with, kept so each mark looks exactly as it did when it was picked.
FINAL = [
    ("wrong_room", "Something wrong here: a room that does not fit", m_wrong_a, "Warding mark", "wrong_a"),
    ("wrong_needle", "Something wrong here: a needle that will not settle", m_wrong_b, "Warding mark", "wrong_b"),
    ("wrong_shadows", "Something wrong here: two shadows", m_wrong_c, "Warding mark", "wrong_c"),
    ("husk_mouth_ears", "Husks here: head, mouth and ears", m_husk_a, "Husks here", "husk_a"),
    ("husk_eyes", "Husks here: worms from the eyes", m_husk_b, "Husks here", "husk_b"),
    ("husk_open_head", "Husks here: worms from an open head", m_husk_c, "Husks here", "husk_c"),
    ("husk_eye", "Husks here: an eye and its worm", m_husk_d, "Husks here", "husk_d"),
    ("husk_hanging", "Husks here: tendrils hanging", m_husk_hanging, "Husks here", "husk_hanging"),
    ("hollow_beast_back", "Hollow: a beast, caps on its back", m_hollow_beast_a, "Hollow here", "hollow_beast_a"),
    ("hollow_beast_long", "Hollow: a long-necked beast", m_hollow_beast_b, "Hollow here", "hollow_beast_b"),
    ("hollow_beast_low", "Hollow: a low beast, a row of caps", m_hollow_beast_c, "Hollow here", "hollow_beast_c"),
    ("hollow_beast_horned", "Hollow: horns gone to caps", m_hollow_beast_d, "Hollow here", "hollow_beast_d"),
]

# Drafts that were not picked, kept in attempts/husks-hollows/marks, out of the live set.
ATTEMPTS = [
    ("hollow_a", m_hollow_a, "Hollow here"),
    ("hollow_b", m_hollow_b, "Hollow here"),
    ("hollow_c", m_hollow_c, "Hollow here"),
]
ATTEMPTS_DIR = ROOT / "attempts" / "husks-hollows" / "marks"


def build_husks_hollows():
    """Write the final Husk and Hollow marks beside the approved marks, and the drafts that were not picked
    into attempts/husks-hollows/marks. Touches nothing else. Returns {final id: 1024 px master}."""
    src, out = RENDER_TMP / "hh-mark-src", RENDER_TMP / "hh-mark-masters"
    for p in (src, out):
        shutil.rmtree(p, ignore_errors=True)
    src.mkdir(parents=True)
    OUT.mkdir(exist_ok=True)
    ATTEMPTS_DIR.mkdir(parents=True, exist_ok=True)
    rows = [(sid, fn, title, seed, True) for sid, _label, fn, title, seed in FINAL]
    rows += [(sid, fn, title, sid, False) for sid, fn, title in ATTEMPTS]
    for sid, fn, title, seed, final in rows:
        h = Hand(seed)
        fn(h)
        svg = h.svg(title)
        (src / f"{sid}.svg").write_text(svg, encoding="utf-8")
        ((OUT if final else ATTEMPTS_DIR) / f"{sid}.svg").write_text(svg, encoding="utf-8")
    render(src, out, 4.0)
    tiles = {}
    for sid, _fn, _title, _seed, final in rows:
        im = Image.open(out / f"{sid}.png").convert("RGBA")
        im.resize((256, 256), Image.LANCZOS).save((OUT if final else ATTEMPTS_DIR) / f"{sid}.png")
        if final:
            tiles[sid] = im
    print(len(FINAL), "Husk and Hollow marks built,", len(ATTEMPTS), "attempts kept")
    return tiles


#      id              label on the review sheet
MARKS = [
    ("safe_camp",   "Safe camp",                  m_safe_camp),
    ("clean_water", "Clean water",                m_clean_water),
    ("bad_water",   "Bad water",                  m_bad_water),
    ("food",        "Food here",                  m_food),
    ("shelter",     "Shelter",                    m_shelter),
    ("cache",       "Cache hidden here",          m_cache),
    ("friendly",    "Friendly people",            m_friendly),
    ("trader",      "Trader",                     m_trader),
    ("way_out",     "Way out",                    m_way_out),
    ("keep_moving", "Keep moving",                m_keep_moving),
    ("dead_end",    "Dead end",                   m_dead_end),
    ("trap",        "Trap",                       m_trap),
    ("danger",      "Danger",                     m_danger),
    ("hostile",     "Hostile people",             m_hostile),
    ("nest",        "Creatures nest here",        m_nest),
    ("sick",        "Sick here",                  m_sick),
    ("warding",     "Something wrong here",      m_warding),
    ("warding_2",   "Husks here",                 m_warding_2),
    ("salvage",     "Salvage here",               m_salvage),
    ("wreck",       "Wreck here",                 m_wreck),
    ("fuel",        "Fuel here",                  m_fuel),
    ("sleep_here",  "Sleep here",                 m_sleep_here),
    ("go_around",   "Go around",                  m_go_around),
    ("road_blocked", "Road blocked",              m_road_blocked),
    ("danger_night", "Danger at night",           m_danger_night),
    ("machines",    "Machines here",              m_machines),
    ("raider_ground", "Raider ground",            m_raider_ground),
]


def build():
    shutil.rmtree(OUT, ignore_errors=True)
    OUT.mkdir()
    for sid, _label, fn in MARKS:
        h = Hand(sid)
        fn(h)
        title = "Warding mark" if sid.startswith("warding") else _label
        (OUT / f"{sid}.svg").write_text(h.svg(title), encoding="utf-8")
    masters = RENDER_TMP / "marks"
    shutil.rmtree(masters, ignore_errors=True)
    render(OUT, masters, 4.0)
    tiles = {sid: Image.open(masters / f"{sid}.png").convert("RGBA") for sid, *_ in MARKS}
    for sid, im in tiles.items():
        im.resize((256, 256), Image.LANCZOS).save(OUT / f"{sid}.png")

    cols, tile, gap_x, gap_y, label_h = 7, 224, 36, 22, 40
    margin = (2000 - (cols * tile + (cols - 1) * gap_x)) // 2
    head = 170
    rows = math.ceil(len(MARKS) / cols)
    strip_y = head + rows * (tile + label_h + gap_y) + 24
    sheet = Image.new("RGB", (2000, strip_y + 270), WALL)
    d = ImageDraw.Draw(sheet)
    d.text((margin, 46), "GalHack traveller marks: draft 3", font=font(60, True), fill=CHALK)
    d.text((margin, 118), "Bar under = someone vouches for this.  Slash through = bad.  Unframed, rough, "
           "one colour: survivors made these, not the old world.", font=font(22), fill="#B9B5AA")
    for i, (sid, label, _fn) in enumerate(MARKS):
        x = margin + (i % cols) * (tile + gap_x)
        y = head + (i // cols) * (tile + label_h + gap_y)
        im = tiles[sid].resize((tile, tile), Image.LANCZOS)
        sheet.paste(im, (x, y), im)
        d.text((x + tile / 2, y + tile + 8), label, font=font(19), fill=CHALK, anchor="ma")
    d.line((margin, strip_y, 2000 - margin, strip_y), fill="#6A6D70", width=2)
    d.text((margin, strip_y + 16), "The same marks at 64 pixels", font=font(21), fill="#B9B5AA")
    per_row = 14
    step = (2000 - 2 * margin) // per_row
    for i, (sid, *_rest) in enumerate(MARKS):
        im = tiles[sid].resize((64, 64), Image.BOX)
        sheet.paste(im, (margin + (i % per_row) * step, strip_y + 58 + (i // per_row) * 84), im)
    sheet.save(ROOT / "marks-sheet.png", optimize=True)
    print(len(MARKS), "marks built")
    build_husks_hollows()                # a full rebuild clears marks/, so put the Husk and Hollow marks back


if __name__ == "__main__":
    if "--husks-hollows" in sys.argv:    # only the Husk and Hollow marks; no approved file is touched
        build_husks_hollows()
    else:
        build()
