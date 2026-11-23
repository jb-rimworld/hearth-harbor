#!/usr/bin/env python3
"""Adds the shared title band (same as all of Julius' mods) to the Hearth & Harbor and Silverline preview scenes.
uv run --with pillow python3 tools/art/previews/titled_main.py  → About/Preview.png, About/Silverline_Preview.png"""
import importlib.util
from pathlib import Path
from PIL import Image
HERE = Path(__file__).resolve().parent
R = HERE.parents[2]
spec = importlib.util.spec_from_file_location("kit", HERE / "kit.py"); kit = importlib.util.module_from_spec(spec); spec.loader.exec_module(kit)
for scene, out, title, tag, acc in [
    ("HearthHarbor_scene.png", "Preview.png", "Hearth & Harbor", "Guests, Hotels & Hospitality", "#D9803F"),
    ("Silverline_scene.png", "Silverline_Preview.png", "Silverline", "Banking, Loans & Insurance", "#3FA66B"),
]:
    im = Image.open(HERE / "base" / scene).convert("RGBA")
    res = kit.finish(im, acc, title, tag, "content")
    res.save(R / "About" / out, optimize=True)
    print(out, (R / "About" / out).stat().st_size)
