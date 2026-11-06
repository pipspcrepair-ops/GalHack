#!/usr/bin/env python3
"""Refresh docs/DESIGN.md from an export of the living design doc.

Usage: python tools/sync-design-doc.py <export-result-file>

The file is the docs export tool's result as saved by the agent: a JSON array of
{type, text} parts, one of which holds {data: {bytes_b64, ...}, rev}. A plain JSON
object with those fields also works.
"""
import base64
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
TARGET = ROOT / "docs" / "DESIGN.md"
NOTE = (
    "> This is a published copy of GalHack's living design document, last refreshed from "
    "revision {rev}. The design doc is the authority on how the game should work; the code "
    "may lag behind it.\n\n"
)
BLOCKED = [r"[\w.+-]+@[\w-]+\.\w+", r"sk-[A-Za-z0-9]{10,}", r"(?i)api[_ ]?key\s*[:=]"]


def payload(path):
    raw = json.loads(pathlib.Path(path).read_text(encoding="utf-8"))
    parts = [raw] if isinstance(raw, dict) else [json.loads(p["text"]) for p in raw if p.get("text", "").lstrip().startswith("{")]
    for part in parts:
        data = part.get("data") if isinstance(part.get("data"), dict) else part
        if isinstance(data.get("bytes_b64"), str):
            return data["bytes_b64"], part.get("rev", "unknown")
    sys.exit("no bytes_b64 found in the export")


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    encoded, rev = payload(sys.argv[1])
    text = base64.b64decode(encoded).decode("utf-8")
    title, _, rest = text.partition("\n")
    rest = re.sub(r"\A\s*[^\n]*@Guest[^\n]*\n", "", rest)  # the export's date and author line
    for pattern in BLOCKED:
        hit = re.search(pattern, text)
        if hit:
            sys.exit(f"refusing to publish: {pattern} matched {hit.group(0)!r}")
    TARGET.parent.mkdir(exist_ok=True)
    TARGET.write_text(f"{title}\n\n{NOTE.format(rev=rev)}{rest.lstrip()}", encoding="utf-8", newline="\n")
    print(f"docs/DESIGN.md refreshed from revision {rev}: {len(text)} characters")


if __name__ == "__main__":
    main()
