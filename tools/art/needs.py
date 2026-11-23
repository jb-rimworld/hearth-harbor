#!/usr/bin/env python3
"""Audit: every texture path the mod references vs. what the art generator registers and what is on disk.

  uv run --with resvg-py --with pillow python3 tools/art/needs.py          # print problems only (exit 1 if any)
  ... needs.py --all                                                      # print every referenced path

Checks per referenced path: (1) registered in tools/art/hhart (real art, not a placeholder),
(2) file(s) exist with the pixel size the def's drawSize/size implies (64 px per cell for Things, UI icons 128).
Sources of references: Common/Defs (graphicData texPath, iconPath, uiIconPath), ContentFinder<Texture2D>.Get("literal") in Source.
"""
import re, sys
import xml.etree.ElementTree as ET
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from PIL import Image
import hhart.all  # noqa: F401
from hhart.registry import REG

R = Path(__file__).resolve().parents[2]
TEX = R / "Common" / "Textures"


def pv(s, default=(1, 1)):
    if not s:
        return default
    s = s.strip().strip("()")
    p = [float(x) for x in s.split(",")]
    return (p[0], p[1]) if len(p) > 1 else (p[0], p[0])


def load_defs():
    named, out = {}, []
    for f in sorted((R / "Common" / "Defs").rglob("*.xml")):
        for d in ET.parse(f).getroot():
            if d.get("Name"):
                named[d.get("Name")] = d
            out.append(d)
    return named, out


def inherited(d, named, tag):
    while d is not None:
        v = d.findtext(tag)
        if v:
            return v
        d = named.get(d.get("ParentName"))
    return None


def main():
    named, defs = load_defs()
    need = {}  # path -> dict(kind, w, h, src)
    for d in defs:
        g = d.find("graphicData")
        if g is not None:
            tp = (g.findtext("texPath") or "").strip()
            if tp.startswith(("Things/HH/", "UI/HH/")):
                kind = (g.findtext("graphicClass") or "Graphic_Single").strip()
                ds = g.findtext("drawSize")
                size = inherited(d, named, "size")
                w, h = pv(ds, pv(size)) if ds else pv(size)
                need[tp] = dict(kind=kind, w=w, h=h, src=d.findtext("defName"))
        for tag in ("portraitLarge", "portraitTiny"):   # StorytellerDef (1.1.0)
            tp = (d.findtext(tag) or "").strip()
            if tp.startswith("UI/HH/"):
                need.setdefault(tp, dict(kind="icon", w=1, h=1, src=d.findtext("defName")))
        for tag in ("iconPath", "uiIconPath"):
            for e in d.iter(tag):
                tp = (e.text or "").strip()
                if tp.startswith(("Things/HH/", "UI/HH/")):
                    need.setdefault(tp, dict(kind="icon", w=1, h=1, src=d.findtext("defName")))
    for f in (R / "Source").rglob("*.cs"):
        t = f.read_text(encoding="utf-8", errors="ignore")
        for m in re.finditer(r'ContentFinder<Texture2D>\.Get\("((?:Things|UI)/HH/[^"]+)"', t):
            need.setdefault(m.group(1), dict(kind="icon", w=1, h=1, src=f.name))
    reg = {e["path"]: e for e in REG}
    problems, rows = [], []
    for tp, n in sorted(need.items()):
        multi = n["kind"] == "Graphic_Multi"
        e = reg.get(tp)
        files = ["%s_%s.png" % (tp, v) for v in ("south", "east", "north")] if multi else ["%s.png" % tp]
        msg = []
        if e is None:
            msg.append("NOT REGISTERED (placeholder or missing art)")
        else:
            if multi != (e["kind"] == "multi"):
                msg.append("kind mismatch: def %s, registry %s" % (n["kind"], e["kind"]))
            ew, eh = e["cells"]
            if not tp.startswith("UI/") and (abs(ew - n["w"]) > .01 or abs(eh - n["h"]) > .01) and n["kind"] != "Graphic_Single":
                msg.append("size mismatch: def %sx%s, registry %sx%s" % (n["w"], n["h"], ew, eh))
        for fn in files:
            p = TEX / fn
            if not p.exists():
                msg.append("missing file " + fn)
                continue
            if e is not None:
                w, h = Image.open(p).size
                cw, ch = e["cells"]
                if multi and fn.endswith("_east.png"):
                    cw, ch = ch, cw
                if (w, h) != (int(round(cw * e["ppc"])), int(round(ch * e["ppc"]))):
                    msg.append("pixel size %dx%d of %s differs from registry" % (w, h, fn))
        rows.append((tp, n, msg))
        if msg:
            problems.append((tp, n, msg))
    print("%d referenced texture paths, %d registered generator entries" % (len(need), len(REG)))
    for tp, n, msg in (rows if "--all" in sys.argv else problems):
        print(" %-45s %-14s %-5sx%-5s %s  %s" % (tp, n["kind"], n["w"], n["h"], n["src"], "; ".join(msg)))
    unused = [p for p in reg if p not in need]
    if unused:
        print("registered but not referenced:", ", ".join(unused))
    sys.exit(1 if problems else 0)


if __name__ == "__main__":
    main()
