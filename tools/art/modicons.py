#!/usr/bin/env python3
"""Renders the mod icons shown in RimWorld's mod list and mod settings (F34, 1.1.0) from the same marks as the in-game icons:
About/ModIcon.png (Hearth & Harbor: hearth flame over a harbour anchor) and About/Silverline_ModIcon.png (copied by tools/package.py
as Silverline/About/ModIcon.png). 256 px, transparent background.
  uv run --with resvg-py --with pillow python3 tools/art/modicons.py"""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import hhart.all  # noqa: F401
from hhart.registry import REG, render_png

R = Path(__file__).resolve().parents[2]
OUT = {"UI/HH/Core/ModMark": R / "About" / "ModIcon.png", "UI/HH/Bank/SilverlineMark": R / "About" / "Silverline_ModIcon.png"}
for e in REG:
    if e["path"] in OUT:
        cv = e["fn"]("single", e["cells"], False)
        render_png(cv.svg(), 256, 256).save(OUT[e["path"]], optimize=True)
        print("wrote", OUT[e["path"]].relative_to(R))
