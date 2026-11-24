#!/usr/bin/env python3
"""The mechanical pass from docs/signs/sigils/png to data/sigils.txt: each faction's sigil as a grid of
painted cells, in its own colours, the size a wall carries it (one cell a metre).

  python sigils_from_png.py <docs/signs/sigils/png> [cells]

Each pixel block takes the colour that covers most of it, or none where the art is mostly clear. Edit the
art, not the data file.
"""
import pathlib
import sys
from PIL import Image

SIZE = int(sys.argv[2]) if len(sys.argv) > 2 else 12
FACTIONS = ["vharn", "kessari", "meridian", "ledger", "reavers", "freeholders"]
LETTERS = "abcdefgh"


def main():
    src = pathlib.Path(sys.argv[1])
    out = ["# Faction sigils as painted cells: written by tools/sigils_from_png.py from docs/signs/sigils/png.",
           "# sigil <faction>, a colour per letter, then rows top to bottom; '.' is unpainted.", ""]
    for f in FACTIONS:
        im = Image.open(src / f"{f}.png").convert("RGBA")
        w, h = im.size
        colours = [c for n, c in sorted(im.getcolors(1 << 20), reverse=True) if c[3] > 200 and n > w * h / 400]
        palette = []
        for c in colours:
            if all(sum(abs(a - b) for a, b in zip(c[:3], p)) > 40 for p in palette):
                palette.append(c[:3])
        palette = palette[:len(LETTERS)]
        rows = []
        for gy in range(SIZE):
            row = ""
            for gx in range(SIZE):
                votes = [0] * (len(palette) + 1)
                for y in range(gy * h // SIZE, (gy + 1) * h // SIZE):
                    for x in range(gx * w // SIZE, (gx + 1) * w // SIZE):
                        r, g, b, a = im.getpixel((x, y))
                        if a < 128:
                            votes[-1] += 1
                            continue
                        k = min(range(len(palette)), key=lambda i: sum(abs(a1 - b1) for a1, b1 in zip((r, g, b), palette[i])))
                        votes[k] += 1
                best = max(range(len(votes)), key=lambda i: votes[i])
                row += "." if best == len(palette) or votes[best] < 0.4 * sum(votes) else LETTERS[best]
            rows.append(row)
        out.append(f"sigil {f}")
        out.append("  colours " + " ".join(f"{LETTERS[i]}={c[0]:02x}{c[1]:02x}{c[2]:02x}" for i, c in enumerate(palette)))
        out += ["  |" + r + "|" for r in rows]
        out.append("")
    target = pathlib.Path(__file__).resolve().parent.parent / "data" / "sigils.txt"
    target.write_text("\n".join(out), encoding="utf-8", newline="\n")
    print(f"{len(FACTIONS)} sigils to {target}")


main()
