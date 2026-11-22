"""Texture registry + renderer (resvg via resvg-py, Pillow for grain)."""
import io, random, zlib
from pathlib import Path
import resvg_py
from PIL import Image
from .style import Canvas

REG = []
# --- footprints (cells, south orientation). The integrator edits THESE if a def uses a different size; textures adapt.
SIZES = {}


def tex(path, kind="single", cells=(1, 1), masked=False, ppc=64, grain=2, plan=True, doc=""):
    """Register a texture. kind: 'single' | 'multi' (north/east/south). cells: footprint when facing south.
    The decorated function gets (view, cells, masked) and returns a Canvas whose cell size already matches the view
    (for 'multi' east the canvas is (h, w) cells, because RimWorld draws east textures with the size rotated)."""
    def deco(fn):
        REG.append(dict(path=path, kind=kind, cells=SIZES.get(path, cells), masked=masked, ppc=ppc, grain=grain, fn=fn, doc=doc or (fn.__doc__ or "")))
        return fn
    return deco


def canvas_for(e, view):
    w, h = e["cells"]
    if e["kind"] == "multi" and view == "east":
        w, h = h, w
    return Canvas(w, h, e["masked"])


def render_png(svg, w_px, h_px):
    b = resvg_py.svg_to_bytes(svg_string=svg, width=w_px, height=h_px)
    return Image.open(io.BytesIO(bytes(b))).convert("RGBA")


def add_grain(im, key, amount=2):
    """deterministic low-amplitude paper grain on opaque pixels (hand-painted look, never on alpha)"""
    if not amount:
        return im
    rnd = random.Random(zlib.crc32(key.encode()))
    px = im.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px[x, y]
            if a > 200:
                d = rnd.randint(-amount, amount)
                px[x, y] = (max(0, min(255, r + d)), max(0, min(255, g + d)), max(0, min(255, b + d)), a)
    return im


def outputs(e):
    """yield (relative png path, view, is_mask)"""
    p = e["path"]
    if e["kind"] == "multi":
        for v in ("south", "east", "north"):
            yield "%s_%s.png" % (p, v), v, False
            if e["masked"]:
                yield "%s_%sm.png" % (p, v), v, True
    else:
        yield "%s.png" % p, "single", False


def render_entry(e, root: Path, only_views=None):
    out = []
    for rel, view, ismask in outputs(e):
        cv = e["fn"](view, e["cells"], e["masked"])
        w_px, h_px = int(round(cv.wc * e["ppc"])), int(round(cv.hc * e["ppc"]))
        im = render_png(cv.svg(mask=ismask), w_px, h_px)
        if ismask:
            pass  # mask SVG already has opaque black silhouette + red/green stuff areas
        else:
            add_grain(im, rel, e["grain"])
        dst = root / rel
        dst.parent.mkdir(parents=True, exist_ok=True)
        im.save(dst, optimize=True)
        out.append((rel, view, ismask, im))
    return out
