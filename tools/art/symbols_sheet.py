#!/usr/bin/env python3
"""Contact sheet of the slot symbols on a cream reel card at 128 / 64 / 48 px -> tools/art/out/symbols_sheet.png"""
from pathlib import Path
from PIL import Image
D = Path(__file__).resolve().parents[2] / "Common/Textures/UI/HH/Casino/Symbols"
ids = ["cherry", "lemon", "plum", "bell", "bar", "seven", "muffalo"]
cream = (239, 227, 194)
im = Image.new("RGB", (len(ids) * 150 + 20, 128 + 64 + 48 + 80), cream)
for i, s in enumerate(ids):
    t = Image.open(D / (s + ".png")).convert("RGBA")
    x = 20 + i * 150
    im.paste(t, (x, 10), t)
    for y, px in ((150, 64), (230, 48)):
        r = t.resize((px, px), Image.LANCZOS)
        im.paste(r, (x, y), r)
out = Path(__file__).resolve().parent / "out" / "symbols_sheet.png"
im.save(out)
print(out)
