#!/usr/bin/env python3
"""Creates simple placeholder PNGs for every texPath/iconPath referenced in Common/Defs that has no texture yet.
Run: uv run --with pillow python3 tools/placeholder_textures.py
Placeholders are flat, muted shapes with initials and a dashed border; files are listed in tools/out/placeholders.txt
so the art pipeline knows what still needs real art. Never overwrites existing PNGs."""
import re, sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
R = Path(__file__).resolve().parent.parent
TEX = R / "Common" / "Textures"
defs = list((R / "Common" / "Defs").rglob("*.xml"))
need = {}
for f in defs:
    t = f.read_text(encoding="utf-8", errors="ignore")
    # graphicData blocks: texPath + graphicClass
    for m in re.finditer(r"<graphicData>(.*?)</graphicData>", t, re.S):
        g = m.group(1)
        tp = re.search(r"<texPath>([^<]+)</texPath>", g)
        gc = re.search(r"<graphicClass>([^<]+)</graphicClass>", g)
        if tp: need[tp.group(1).strip()] = (gc.group(1).strip() if gc else "Graphic_Single")
    for tag in ("iconPath", "uiIconPath", "texPath"):
        for m in re.finditer(rf"<{tag}>([^<]+)</{tag}>", t):
            need.setdefault(m.group(1).strip(), "Graphic_Single")
# also C# ContentFinder<Texture2D>.Get("...") literals
for f in (R / "Source").rglob("*.cs"):
    for m in re.finditer(r'ContentFinder<Texture2D>\.Get\("([^"]+)"', f.read_text(encoding="utf-8", errors="ignore")):
        need.setdefault(m.group(1), "Graphic_Single")
made = []
pal = [(140, 98, 62), (63, 110, 104), (122, 74, 64), (88, 96, 120), (110, 120, 70), (150, 120, 60)]
def draw(path: Path, label: str, size=128, seed=0):
    path.parent.mkdir(parents=True, exist_ok=True)
    im = Image.new("RGBA", (size, size), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    c = pal[seed % len(pal)]
    d.rounded_rectangle([8, 8, size - 9, size - 9], radius=14, fill=c + (255,), outline=(28, 20, 17, 255), width=4)
    d.rounded_rectangle([18, 18, size - 19, size - 19], radius=10, outline=(240, 225, 190, 140), width=2)
    ini = "".join(w[0] for w in re.split(r"[_\W]+", label) if w)[:3].upper() or "?"
    try: font = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial Bold.ttf", 34)
    except Exception: font = ImageFont.load_default()
    w = d.textlength(ini, font=font); d.text(((size - w) / 2, size / 2 - 20), ini, font=font, fill=(250, 240, 220, 255))
    im.save(path)
for tp, gc in sorted(need.items()):
    if tp.startswith(("Things/Building/", "Things/Item/", "UI/Commands/", "UI/Icons/", "Things/Pawn/", "Things/Mote")) and not tp.startswith(("Things/HH", "UI/HH")):
        continue  # vanilla textures
    # a prefix built by string concatenation in C# ("UI/HH/Bank/" + name) is a folder, not a texture: nothing to make
    if tp.endswith("/") or not tp.split("/")[-1].strip():
        continue
    name = tp.split("/")[-1]; seed = sum(map(ord, tp))
    if gc == "Graphic_Multi":
        files = [TEX / f"{tp}_{d}.png" for d in ("north", "east", "south")]
    elif gc in ("Graphic_Random", "Graphic_StackCount", "Graphic_Appearances"):
        files = [TEX / tp / f"{name}_a.png"]
    else:
        files = [TEX / f"{tp}.png"]
    for p in files:
        if not p.exists():
            draw(p, name, 128, seed); made.append(str(p.relative_to(R)))
out = R / "tools" / "out"; out.mkdir(parents=True, exist_ok=True)
prev = set((out / "placeholders.txt").read_text().split()) if (out / "placeholders.txt").exists() else set()
(out / "placeholders.txt").write_text("\n".join(sorted(prev | set(made))) + "\n")
print(f"{len(need)} texture paths referenced, {len(made)} placeholders created")
