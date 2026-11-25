#!/usr/bin/env python3
"""Freeholders reroll: four new sigil ideas. Run: python build_freeholders_attempts.py

Writes sigils/attempts-freeholders/ and sigils-freeholders.png, and shows each
attempt beside the five chosen sigils. All original to GalHack.
"""
import shutil

from PIL import Image, ImageDraw

from build_signs import ROOT, RENDER_TMP, font, group, path, poly, rect, render, stroke
from build_sigils import OUT as SIGILS, build_svg

OUT = SIGILS / "attempts-freeholders"
OLIVE, OCHRE, GOLD = "#66743F", "#C69A42", "#FFC400"
CHOSEN = ("vharn", "kessari", "meridian", "ledger", "reavers")


def lantern():
    # Somebody left the light on. That is all a safe place is.
    return [("ink", OLIVE, stroke("M-44 -62 A44 42 0 0 1 44 -62", 22)),
            ("ink", OLIVE, poly([(-56, -72), (56, -72), (72, -46), (-72, -46)])),
            ("ink", OLIVE, rect(-70, -42, 140, 112, 30)),
            ("inlay", GOLD, rect(-46, -22, 92, 72, 22)),
            ("ink", OLIVE, rect(-82, 72, 164, 38, 6))]


def anvil():
    # Nobody is coming to fix it for you.
    return [("ink", OLIVE, path("M-118 -78 H108 V-30 C72 -28 52 -14 44 8 V42 H-38 V8 C-42 -14 -58 -26 -88 -36 "
                                "C-102 -46 -112 -62 -118 -78 Z")),
            ("ink", OCHRE, poly([(-44, 54), (50, 54), (84, 78), (84, 110), (-78, 110), (-78, 78)]))]


def claim_stake():
    # A post, a rag and a plot of ground: this bit is mine.
    return [("ink", OLIVE, rect(-74, -112, 30, 200)),
            ("ink", OCHRE, poly([(-38, -108), (112, -96), (92, -62), (116, -30), (-38, -18)])),
            ("ink", OLIVE, poly([(-116, 112), (-60, 66), (42, 66), (98, 112)]))]


def hat():
    # The only faction you would know by its people.
    return [("ink", OLIVE, path("M-66 42 C-70 -30 -54 -86 -20 -90 C-4 -92 -2 -78 0 -78 C4 -78 8 -92 24 -90 "
                                "C58 -86 70 -30 66 66 H-66 Z")),
            ("ink", OCHRE, poly([(-67, 2), (67, 2), (66, 36), (-66, 36)])),
            ("ink", OLIVE, path("M-120 34 Q0 76 120 34 Q116 84 0 88 Q-116 84 -120 34 Z"))]


ATTEMPTS = [("f_lantern", "Lantern", lantern), ("f_anvil", "Anvil", anvil),
            ("f_claim", "Claim stake", claim_stake), ("f_hat", "Wide-brim hat", hat)]


def build():
    shutil.rmtree(OUT, ignore_errors=True)
    for sub in ("svg", "svg-stencil", "png"):
        (OUT / sub).mkdir(parents=True)
    for sid, _label, fn in ATTEMPTS:
        steps = fn()
        (OUT / "svg" / f"{sid}.svg").write_text(build_svg("Sigil attempt", steps, False), encoding="utf-8")
        (OUT / "svg-stencil" / f"{sid}.svg").write_text(build_svg("Sigil attempt stencil", steps, True), encoding="utf-8")
    a, b = RENDER_TMP / "fh-a", RENDER_TMP / "fh-b"
    for d in (a, b):
        shutil.rmtree(d, ignore_errors=True)
    render(OUT / "svg", a, 4.0)
    render(OUT / "svg-stencil", b, 4.0)
    clean = {sid: Image.open(a / f"{sid}.png").convert("RGBA") for sid, *_ in ATTEMPTS}
    sten = {sid: Image.open(b / f"{sid}.png").convert("RGBA") for sid, *_ in ATTEMPTS}
    for k, im in clean.items():
        im.resize((256, 256), Image.LANCZOS).save(OUT / "png" / f"{k}.png")
    chosen = {k: Image.open(SIGILS / "png" / f"{k}.png").convert("RGBA") for k in CHOSEN}

    tile, cell, left = 340, 462, 40
    sheet = Image.new("RGB", (2000, 840), "#E9E6DC")
    d = ImageDraw.Draw(sheet)
    ink = "#14171A"
    d.text((left, 40), "GalHack Freeholders sigil: reroll", font=font(58, True), fill=ink)
    d.text((left, 112), "Four new ideas. Under each: 64 and 32 pixels on hull grey, stencil, then in line with the five chosen sigils.",
           font=font(22), fill="#555555")
    grey = "#8E9397"
    for i, (sid, label, _fn) in enumerate(ATTEMPTS):
        x = left + i * (cell + 28)
        im = clean[sid].resize((tile, tile), Image.LANCZOS)
        sheet.paste(im, (x + (cell - tile) // 2, 160), im)
        d.text((x + cell / 2, 508), label, font=font(24, True), fill=ink, anchor="ma")
        d.rectangle((x, 544, x + cell, 810), fill=grey)
        s64 = clean[sid].resize((64, 64), Image.BOX)
        st = sten[sid].resize((64, 64), Image.BOX)
        s32 = clean[sid].resize((32, 32), Image.BOX)
        sheet.paste(s64, (x + 110, 556), s64)
        sheet.paste(s32, (x + 200, 572), s32)
        sheet.paste(st, (x + 258, 556), st)
        row = [chosen[k] for k in CHOSEN] + [clean[sid]]
        for j, c in enumerate(row):
            big = c.resize((64, 64), Image.BOX)
            sheet.paste(big, (x + 14 + j * 73, 640), big)
            small = c.resize((32, 32), Image.BOX)
            sheet.paste(small, (x + 30 + j * 73, 724), small)
    sheet.save(ROOT / "sigils-freeholders.png", optimize=True)
    print(len(ATTEMPTS), "Freeholders attempts built")


if __name__ == "__main__":
    build()
