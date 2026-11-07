#!/usr/bin/env python3
"""The mechanical pass from docs/world-layers.md to data/world-layers.txt (section 8.1 of that file).

  python layers_from_doc.py <path to world-layers.md>

Every fenced `layers` block goes across as it stands, one record to a line. The doc is the source: edit
it there and run this again, never the data file. Three things change on the way, all owner's decisions
in section 11: The Old Ground loses its old id, and two family ids take their new names. `oldtype` is a
record of where old world types went and no use to the game, so it stays behind.
"""
import pathlib
import re
import sys

RENAMES = {"barrow_downs": "old_ground", "yurt_camp": "felt_camp", "church_chapel": "shrine_hall"}
LEFT_BEHIND = {"oldtype"}

HEADER = """# World layers: every table of docs/world-layers.md, one record to a line, fields split by ' | '.
# Written by tools/layers_from_doc.py: edit the doc and run that, not this file.
# A world is whatever its layers add up to, and no generator names a value from here.
"""


def main():
    doc = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8")
    out = [HEADER]
    count = 0
    word = re.compile(r"\b(" + "|".join(RENAMES) + r")\b")
    for block in re.findall(r"```layers\n(.*?)```", doc, re.S):
        lines = [l.strip() for l in block.splitlines()]
        lines = [word.sub(lambda m: RENAMES[m.group(1)], l) for l in lines if l and not l.startswith("#") and l.split()[0] not in LEFT_BEHIND]
        if lines:
            out.append("\n".join(lines) + "\n")
            count += len(lines)
    target = pathlib.Path(__file__).resolve().parent.parent / "data" / "world-layers.txt"
    target.write_text("\n".join(out), encoding="utf-8", newline="\n")
    print(f"{count} records to {target}")


main()
