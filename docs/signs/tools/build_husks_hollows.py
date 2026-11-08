#!/usr/bin/env python3
"""GalHack Husk and Hollow final set. Run:  python build_husks_hollows.py

Builds only the Husk and Hollow pieces: the final signs (build_signs) and the final traveller marks
(build_marks), each written beside the approved files without touching them, with the drafts that were
not picked kept in attempts/husks-hollows/.  Then lays the final set out on husks-hollows-final.png,
each piece at 256 pixels with its 64 pixel version beside it, labelled with what it means.
Husks have no sigil, so none is drawn.
"""
from PIL import Image, ImageDraw

import build_marks
import build_signs
from build_signs import ROOT, font

CHALK, WALL = build_marks.CHALK, build_marks.WALL
LIGHT = "#ECE9DF"
INK = build_signs.INK

W = 2300
TILE, THUMB, GAP_IN, GAP_X, GAP_Y = 256, 64, 14, 30, 26
CELL = TILE + GAP_IN + THUMB
COLS = 6
MARGIN = (W - (COLS * CELL + (COLS - 1) * GAP_X)) // 2


def wrap(d, text, f, width):
    words, lines, cur = text.split(), [], ""
    for w in words:
        trial = (cur + " " + w).strip()
        if d.textlength(trial, font=f) <= width or not cur:
            cur = trial
        else:
            lines.append(cur)
            cur = w
    return lines + [cur]


def load(path):
    return Image.open(path).convert("RGBA")


def sections():
    marks = build_marks.build_husks_hollows()
    signs = build_signs.build_husks_hollows()
    mark_label = {sid: label for sid, label, *_ in build_marks.FINAL}
    sign_label = {c[0]: c[6] for c in build_signs.FINAL}

    def m(*ids):
        return [(mark_label[i], marks[i]) for i in ids]

    def s(*ids):
        return [(sign_label[i], signs[i]) for i in ids]

    return [
        ("Traveller marks: something wrong here", WALL, CHALK,
         "For places Husks have warped, and anomalies in general. Left unexplained in game.",
         [("Something wrong here: the scribble", load(build_marks.OUT / "warding.png"))]
         + m("wrong_room", "wrong_needle", "wrong_shadows")),
        ("Traveller marks: Husks here", WALL, CHALK,
         "Six regional variants of one mark: survivors in different places draw it differently.",
         m("husk_mouth_ears", "husk_eyes", "husk_open_head", "husk_eye", "husk_hanging")
         + [("Husks here: tendrils from a mound", load(build_marks.OUT / "warding_2.png"))]),
        ("Traveller marks: Hollow", WALL, CHALK,
         "Hollows only take wildlife, so the mark is the beast wearing it. Four regional variants, no slash.",
         m("hollow_beast_back", "hollow_beast_long", "hollow_beast_low", "hollow_beast_horned")),
        ("Signs: Hollow", LIGHT, INK,
         "The hazard is an infected animal. The boot print is the sign for fungus spreading on boots.",
         s("hollow_hazard", "hollow_spread_boots")),
        ("Signs: Husk hazard and group", LIGHT, INK,
         "Quarantine zones, screening points and infested facilities, in two colourways. "
         "The group sign is for Husks among people.",
         s("husk_hazard", "husk_hazard_paper", "husk_group")),
        ("Signs: Husk screening", LIGHT, INK,
         "Scanner gates at facility entrances. The scan line swaps colour where it crosses the figure. "
         "Worms at the head mean the gate has found something.",
         s("husk_screening_gate", "husk_screening_gate_pass", "husk_screening_gate_fail",
           "husk_screening_arch", "husk_screening_arch_fail")),
    ]


def build():
    secs = sections()
    probe = ImageDraw.Draw(Image.new("RGB", (10, 10)))
    f_label = font(19)
    heights = []
    for title, _bg, _fg, note, items in secs:
        rows = -(-len(items) // COLS)
        note_lines = wrap(probe, note, font(21), W - 2 * MARGIN)
        heights.append(34 + 56 + 28 * len(note_lines) + 14 + rows * (TILE + 62 + GAP_Y) + 24)
    head = 150
    sheet = Image.new("RGB", (W, head + sum(heights)), "#15171A")
    d = ImageDraw.Draw(sheet)
    d.text((MARGIN, 40), "GalHack Husks and Hollows: the final set", font=font(58, True), fill="#F2EFE6")
    d.text((MARGIN, 108), "Each piece at 256 px with its 64 px version beside it. Husks have no sigil.",
           font=font(22), fill="#B8B5AB")
    y = head
    for (title, bg, fg, note, items), h in zip(secs, heights):
        d.rectangle((0, y, W, y + h), fill=bg)
        sub = "#B9B5AA" if bg == WALL else "#555555"
        d.text((MARGIN, y + 26), title, font=font(32, True), fill=fg)
        ty = y + 76
        for line in wrap(d, note, font(21), W - 2 * MARGIN):
            d.text((MARGIN, ty), line, font=font(21), fill=sub)
            ty += 28
        ty += 14
        for i, (label, im) in enumerate(items):
            x = MARGIN + (i % COLS) * (CELL + GAP_X)
            iy = ty + (i // COLS) * (TILE + 62 + GAP_Y)
            big = im.resize((TILE, TILE), Image.LANCZOS)
            sheet.paste(big, (x, iy), big)
            small = im.resize((THUMB, THUMB), Image.BOX)
            sheet.paste(small, (x + TILE + GAP_IN, iy), small)
            ly = iy + TILE + 8
            for line in wrap(d, label, f_label, CELL):
                d.text((x, ly), line, font=f_label, fill=fg)
                ly += 24
        y += h
    sheet.save(ROOT / "husks-hollows-final.png", optimize=True)
    print("sheet", sheet.size)


if __name__ == "__main__":
    build()
