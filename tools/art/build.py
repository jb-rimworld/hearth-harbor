#!/usr/bin/env python3
"""Render every registered Hearth & Harbor texture into Common/Textures and build the contact sheets.

  uv run --with resvg-py --with pillow python3 tools/art/build.py            # everything
  uv run --with resvg-py --with pillow python3 tools/art/build.py Kitchen    # only paths containing "Kitchen"
  ... build.py --sheet-only      # rebuild the contact sheets from the PNGs already on disk (no render)
  ... build.py --svg Slot        # also dump the SVG source next to tools/art/out/svg/
"""
import sys, random
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from PIL import Image, ImageDraw, ImageFont
from hhart import registry
from hhart.registry import REG, render_entry, canvas_for
import hhart.all  # noqa: F401  (register textures)

ROOT = Path(__file__).resolve().parents[2]
TEX = ROOT / "Common" / "Textures"
OUT = Path(__file__).resolve().parent / "out"


def floor_bg(w, h, cell=128, seed=7):
    """vanilla-like wooden plank floor so the sheet shows textures in context"""
    rnd = random.Random(seed)
    im = Image.new("RGB", (w, h), (150, 128, 96))
    d = ImageDraw.Draw(im)
    for row in range(0, h // (cell // 2) + 1):
        y = row * cell // 2
        base = rnd.randint(-8, 8)
        x = -rnd.randint(0, cell)
        while x < w:
            ln = rnd.randint(cell, cell * 2)
            c = (150 + base + rnd.randint(-7, 7), 126 + base + rnd.randint(-6, 6), 92 + base + rnd.randint(-5, 5))
            d.rectangle([x, y, x + ln, y + cell // 2], fill=c)
            d.line([x, y, x + ln, y], fill=(112, 88, 62), width=2)
            d.line([x + ln, y, x + ln, y + cell // 2], fill=(112, 88, 62), width=2)
            x += ln
    return im


def ui_bg(w, h):
    im = Image.new("RGB", (w, h), (52, 50, 48))
    d = ImageDraw.Draw(im)
    for x in range(0, w, 24):
        d.line([x, 0, x, h], fill=(56, 54, 52))
    return im


def font(sz):
    for p in ("/System/Library/Fonts/Supplemental/Arial.ttf", "/System/Library/Fonts/Helvetica.ttc"):
        try:
            return ImageFont.truetype(p, sz)
        except Exception:
            pass
    return ImageFont.load_default()


def sheet(entries, scale_cell, name, views=("south",), width=1900):
    """shelf-pack all entries. scale_cell = px per cell on the sheet."""
    items = []
    for e in entries:
        ui = e["path"].startswith("UI/")
        for v in (views if e["kind"] == "multi" else ("single",)):
            rel = "%s_%s.png" % (e["path"], v) if e["kind"] == "multi" else "%s.png" % e["path"]
            f = TEX / rel
            if not f.exists():
                continue
            im = Image.open(f).convert("RGBA")
            w, h = e["cells"]
            if e["kind"] == "multi" and v == "east":
                w, h = h, w
            tw, th = int(w * scale_cell), int(h * scale_cell)
            if ui and w * e["ppc"] >= 256 and e["cells"][0] >= 4:
                tw, th = int(w * scale_cell), int(h * scale_cell)
            im = im.resize((max(1, tw), max(1, th)), Image.LANCZOS)
            items.append((e["path"].split("/", 2)[-1] + ("" if v == "single" else " " + v[0]), im, ui, e["path"].split("/")[2] if len(e["path"].split("/")) > 2 else ""))
    pad, lab = 26, 30
    x = y = pad
    rowh = 0
    place = []
    for label, im, ui, mod in items:
        w, h = im.size
        w = max(w, int(ImageDraw.Draw(Image.new("RGB", (1, 1))).textlength(label, font=font(15))) + 4)
        if x + w + pad > width:
            x = pad; y += rowh + pad + lab; rowh = 0
        place.append((x, y, label, im, ui))
        x += w + pad; rowh = max(rowh, h)
    H = y + rowh + pad + lab
    sheet_im = floor_bg(width, H, cell=scale_cell)
    d = ImageDraw.Draw(sheet_im)
    f = font(15)
    for x, y, label, im, ui in place:
        if ui:
            pnl = Image.new("RGBA", (im.width + 12, im.height + 12), (52, 50, 48, 255))
            sheet_im.paste(pnl, (x - 6, y - 6))
        sheet_im.paste(im, (x, y), im)
        d.rectangle([x - 2, y + im.height + 4, x + max(im.width, 8 * len(label)) + 2, y + im.height + 22], fill=(24, 18, 14))
        d.text((x, y + im.height + 5), label, fill=(240, 228, 200), font=f)
    p = OUT / name
    sheet_im.save(p)
    return p


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    flags = [a for a in sys.argv[1:] if a.startswith("--")]
    OUT.mkdir(parents=True, exist_ok=True)
    sel = [e for e in REG if not args or any(a.lower() in e["path"].lower() for a in args)]
    if "--sheet-only" not in flags:
        svgdir = OUT / "svg"
        for e in sel:
            render_entry(e, TEX)
            if "--svg" in flags:
                svgdir.mkdir(exist_ok=True)
                for rel, v, ism in registry.outputs(e):
                    if not ism:
                        (svgdir / (rel.replace("/", "__")[:-4] + ".svg")).write_text(e["fn"](v, e["cells"], e["masked"]).svg())
        print("rendered %d textures" % len(sel))
    order = ["Inn", "Venues", "Kitchen", "Casino", "Bank", "Insurance", "UI", "City", "Underworld", "Wellness", "Dlc", "Finance"]
    def key(e):
        parts = e["path"].split("/")
        grp = parts[2]
        return (parts[0] == "UI", order.index(grp) if grp in order else 99, e["kind"] != "multi", e["path"])
    allr = sorted(REG, key=key)
    p1 = sheet(allr, 128, "contact_sheet.png")
    p2 = sheet([e for e in allr if e["kind"] == "multi"], 96, "contact_sheet_views.png", views=("south", "east", "north"))
    print(p1, p2)
    # per-module sheets: all views of Things, all UI icons (module = third path part)
    mods = []
    for e in allr:
        g = e["path"].split("/")[2]
        if g not in mods:
            mods.append(g)
    for g in mods:
        es = [e for e in allr if e["path"].split("/")[2] == g]
        sheet(es, 128, "module_%s.png" % g.lower(), views=("south", "east", "north"), width=1500)
    icons = [e for e in allr if e["path"].startswith("UI/") and e["cells"][0] < 4]
    small(icons, "readability.png")
    print("module sheets:", ", ".join(mods))


def small(entries, name):
    """UI icons at 32 and 64 px (and 128) on the dark UI background: the readability check"""
    f = font(11)
    cols = 12
    cell = 150
    rows = (len(entries) + cols - 1) // cols
    im = Image.new("RGB", (cols * cell, rows * 172 + 10), (52, 50, 48))
    d = ImageDraw.Draw(im)
    for i, e in enumerate(entries):
        src = Image.open(TEX / ("%s.png" % e["path"])).convert("RGBA")
        x, y = (i % cols) * cell + 8, (i // cols) * 172 + 8
        for sz, ox, oy in ((32, 0, 0), (64, 40, 0), (128 if e["cells"][0] == 1 else 0, 0, 72)):
            if sz == 0:
                continue
            t = src.resize((sz, int(sz * src.height / src.width)), Image.LANCZOS)
            if sz == 128:
                t = src.resize((64, 64), Image.LANCZOS)
                im.paste(t, (x + 80, y + 72), t)
            else:
                im.paste(t, (x + ox, y + oy), t)
        d.text((x, y + 142), e["path"].split("/", 2)[-1].replace("HH/", "")[:22], fill=(220, 210, 190), font=f)
    p = OUT / name
    im.save(p)
    return p


if __name__ == "__main__":
    main()
