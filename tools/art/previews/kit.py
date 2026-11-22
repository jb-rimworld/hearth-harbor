"""Shared kit for the Workshop preview images of all of Julius' mods.

Re-uses the H&H art pipeline (Canvas primitives, house style, sprite helpers from tools/art/previews.py) and adds:
backgrounds, small drawn props (pawns, beds, robots ...), the common title band, the class frames and the byline.
Everything is deterministic (fixed seeds).
"""
import sys, math, random, importlib.util
from pathlib import Path

HERE = Path(__file__).resolve().parent
ART = HERE.parent
sys.path.insert(0, str(ART))
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageEnhance  # noqa: E402

_spec = importlib.util.spec_from_file_location("hh_base_previews", ART / "previews.py")
base = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(base)

from hhart.style import *  # noqa: E402,F401,F403
from hhart.parts import *  # noqa: E402,F401,F403
from hhart.registry import render_png  # noqa: E402

W, H = 1280, 720
OUTL = (28, 20, 17)
CREAM = (255, 246, 224)
GOLD = (224, 180, 74)
STEEL = (138, 147, 160)

sprite, deco, glow, put, shadow, vignette, planks = base.sprite, base.deco, base.glow, base.put, base.shadow, base.vignette, base.planks


def font(sz):
    for p in ("/System/Library/Fonts/Supplemental/Arial Rounded Bold.ttf", "/System/Library/Fonts/Supplemental/Arial Bold.ttf", "/System/Library/Fonts/Helvetica.ttc"):
        try:
            return ImageFont.truetype(p, sz)
        except Exception:
            pass
    return ImageFont.load_default()


def hx(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def putf(im, img, cx, by, sh=True):
    """place an image with its horizontal centre at cx and its bottom edge at by (feet on the floor)"""
    put(im, img, cx - img.width / 2, by - img.height, sh=sh)


def textc(im, xy, s, sz, fill=CREAM, stroke=2, anchor="mm", outline=OUTL):
    d = ImageDraw.Draw(im)
    d.text(xy, s, font=font(sz), fill=fill, anchor=anchor, stroke_width=stroke, stroke_fill=outline)


# ------------------------------------------------------------------ backgrounds
def wall_floor(wall, wain, base_floor, wall_h=300, seam=60, floor="planks", floor_b=None, tile=80, seed=3, trim=(62, 40, 26)):
    im = Image.new("RGBA", (W, H), (0, 0, 0, 255))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W, wall_h], fill=wall)
    sd = tuple(max(0, v - 12) for v in wall)
    for x in range(0, W, seam):
        d.line([x, 0, x, wall_h], fill=sd, width=3)
    d.rectangle([0, wall_h - 64, W, wall_h], fill=wain)
    d.line([0, wall_h - 64, W, wall_h - 64], fill=tuple(max(0, v - 50) for v in wain), width=4)
    if floor == "planks":
        fl = planks(W, H, wall_h, base=base_floor, seed=seed)
        im.alpha_composite(fl.crop((0, wall_h, W, H)), (0, wall_h))
    else:
        fb = floor_b or tuple(max(0, v - 28) for v in base_floor)
        for r in range((H - wall_h) // tile + 2):
            for k in range(W // tile + 1):
                col = base_floor if (r + k) % 2 == 0 else fb
                d.rectangle([k * tile, wall_h + r * tile, k * tile + tile, wall_h + r * tile + tile], fill=col)
                d.rectangle([k * tile, wall_h + r * tile, k * tile + tile, wall_h + r * tile + tile], outline=tuple(max(0, v - 18) for v in col), width=1)
    d.rectangle([0, wall_h, W, wall_h + 10], fill=trim)
    return im


def finish(im, accent, title, tagline, cls, desat=1.0, vig=140, band_top=548):
    """vignette, title band, byline, class frame -> RGB image"""
    vignette(im, vig)
    if desat != 1.0:
        im = ImageEnhance.Color(im.convert("RGB")).enhance(desat).convert("RGBA")
    # scrim: dark warm gradient rising from the bottom, holds the title
    sc = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    px = sc.load()
    for y in range(band_top, H):
        t = (y - band_top) / (H - band_top)
        a = int(235 * min(1.0, t * 1.5) ** 1.1)
        for x in range(W):
            px[x, y] = (24, 15, 11, a)
    im.alpha_composite(sc)
    d = ImageDraw.Draw(im)
    # title, shrunk to fit
    sz = 74
    while font(sz).getlength(title) > 1000 and sz > 40:
        sz -= 2
    d.text((60, 636), title, font=font(sz), fill=CREAM, anchor="ls", stroke_width=5, stroke_fill=OUTL)
    ac = hx(accent) if isinstance(accent, str) else accent
    d.rounded_rectangle([60, 650, 60 + 96, 656], radius=3, fill=ac, outline=OUTL, width=1)
    if tagline:
        d.text((60, 692), tagline, font=font(28), fill=(238, 226, 200), anchor="ls", stroke_width=3, stroke_fill=OUTL)
    # byline mark, bottom right, same on every image
    bx = W - 52
    d.text((bx, 692), "by juliusberger00", font=font(24), fill=(232, 220, 192), anchor="rs", stroke_width=3, stroke_fill=OUTL)
    r = font(24).getlength("by juliusberger00")
    d.ellipse([bx - r - 26, 676, bx - r - 12, 690], fill=ac, outline=OUTL, width=2)
    frame(im, cls, ac)
    return im.convert("RGB")


def gear_badge(im, x, y, r=17, col=STEEL):
    d = ImageDraw.Draw(im)
    for i in range(8):
        a = i * math.pi / 4
        cx, cy = x + math.cos(a) * r, y + math.sin(a) * r
        d.ellipse([cx - 5, cy - 5, cx + 5, cy + 5], fill=col, outline=OUTL, width=2)
    d.ellipse([x - r + 3, y - r + 3, x + r - 3, y + r - 3], fill=col, outline=OUTL, width=3)
    d.ellipse([x - 6, y - 6, x + 6, y + 6], fill=(54, 58, 66), outline=OUTL, width=2)


def frame(im, cls, ac):
    d = ImageDraw.Draw(im)
    if cls == "game":      # thin gold frame + small diamond: "you play with this"
        d.rectangle([0, 0, W - 1, H - 1], outline=GOLD, width=5)
        d.rectangle([5, 5, W - 6, H - 6], outline=OUTL, width=2)
        d.polygon([(34, 14), (50, 30), (34, 46), (18, 30)], fill=GOLD, outline=OUTL)
    elif cls == "content":  # gold hairline
        d.rectangle([8, 8, W - 9, H - 9], outline=GOLD, width=2)
    else:                   # tech: steel frame + gear: "this repairs or connects something"
        d.rectangle([0, 0, W - 1, H - 1], outline=STEEL, width=6)
        d.rectangle([6, 6, W - 7, H - 7], outline=OUTL, width=2)
        gear_badge(im, 44, 44)


# ------------------------------------------------------------------ props (Canvas drawings)
SKIN = {"a": "#efc9a2", "b": "#d9a074", "c": "#b57a4f", "d": "#8a5a38"}


def pawn(c, skin="a", hair="#5a3a22", shirt="#5e8fc2", pants="#3b4560", style="short", mask=False, eyes=OUT, mouth=True, shadow_=True, cape=None):
    sk = SKIN.get(skin, skin)
    if shadow_:
        c.raw('<ellipse cx="64" cy="118" rx="30" ry="6" fill="#1c1411" fill-opacity=".28"/>')
    if style == "long":
        c.rect(38, 22, 52, 52, hair, r=16, sw=3, hl=False, sh=False)
    if cape:
        c.rect(34, 56, 60, 52, cape, r=10, sw=3)
    c.rect(46, 88, 14, 26, pants, r=5, sw=3)
    c.rect(68, 88, 14, 26, pants, r=5, sw=3)
    c.rect(44, 108, 17, 9, dark(pants, .3), r=4, sw=2.6, hl=False)
    c.rect(67, 108, 17, 9, dark(pants, .3), r=4, sw=2.6, hl=False)
    c.rect(26, 60, 14, 34, light(shirt, .06), r=6, sw=3)
    c.rect(88, 60, 14, 34, light(shirt, .06), r=6, sw=3)
    c.circle(33, 96, 7, sk, sw=2.6, hl=False)
    c.circle(95, 96, 7, sk, sw=2.6, hl=False)
    c.rect(38, 56, 52, 40, shirt, r=12, sw=3.4)
    c.circle(64, 36, 24, sk, sw=3.4)
    if style in ("short", "long", "bun"):
        c.path("M39 38 Q38 8 64 9 Q90 8 89 38 Q82 24 64 24 Q46 24 39 38 Z", hair, sw=3.2)
    if style == "bun":
        c.circle(64, 8, 9, hair, sw=3)
    if style == "cap":
        c.path("M38 34 Q40 8 64 8 Q88 8 90 34 Z", hair, sw=3.2)
        c.rect(36, 28, 56, 9, dark(hair, .15), r=4, sw=2.8, hl=False)
    c.circle(54, 39, 3.2, eyes, sw=0, hl=False, sh=False)
    c.circle(74, 39, 3.2, eyes, sw=0, hl=False, sh=False)
    if mask:
        c.rect(46, 42, 36, 18, "#e8f1ee", r=7, sw=3, hl=True)
        c.line(46, 49, 40, 44, OUT, w=2.2)
        c.line(82, 49, 88, 44, OUT, w=2.2)
    elif mouth:
        c.path("M56 50 Q64 56 72 50", None, sw=2.6)


def bed2(c, frame_col="#7a5638", sheet="#efe3c2", blanket="#d9803f", skin="a", hair="#5a3a22", rails=False, patient=True, glow_=None):
    """2x1 bed seen from above (head at the left). 256x128 canvas."""
    c.rect(6, 12, 244, 104, frame_col, r=10, sw=3.8)
    c.rect(18, 24, 220, 80, sheet, r=8, sw=3)
    c.rect(24, 32, 46, 64, "#f6f0dc", r=10, sw=2.8)
    if patient:
        sk = SKIN.get(skin, skin)
        c.circle(47, 64, 20, sk, sw=3.2)
        c.path("M28 62 Q30 42 47 42 Q64 42 66 62 Q58 52 47 52 Q36 52 28 62 Z", hair, sw=2.8)
        c.path("M40 68 Q42 70 44 68", None, sw=2.2)
        c.path("M52 68 Q54 70 56 68", None, sw=2.2)
    c.rect(86, 24, 152, 80, blanket, r=8, sw=3.2)
    for x in (126, 166, 206):
        c.line(x, 30, x, 98, dark(blanket, .22), w=2.4)
    c.rect(86, 24, 12, 80, dark(blanket, .12), r=4, sw=0, hl=False, sh=False)
    if rails:
        c.rect(78, 8, 164, 9, "#c9ccd1", r=4, sw=2.6)
        c.rect(78, 110, 164, 9, "#c9ccd1", r=4, sw=2.6)
        c.rect(238, 8, 9, 114, "#c9ccd1", r=4, sw=2.6)


def zzz(c, col="#cfe9ff"):
    for (x, y, s) in ((20, 70, 16), (52, 42, 22), (92, 12, 28)):
        c.pline([(x, y), (x + s, y), (x, y + s), (x + s, y + s)], OUT, w=7.5)
        c.pline([(x, y), (x + s, y), (x, y + s), (x + s, y + s)], col, w=3.6)


def heart(c, x, y, s, fill="#e4566d", sw=3):
    d = "M%s %s C%s %s %s %s %s %s C%s %s %s %s %s %s C%s %s %s %s %s %s Z" % (
        f2(x), f2(y + s * .9), f2(x - s * 1.3), f2(y + s * .1), f2(x - s * .9), f2(y - s * .9), f2(x), f2(y - s * .25),
        f2(x + s * .9), f2(y - s * .9), f2(x + s * 1.3), f2(y + s * .1), f2(x), f2(y + s * .9),
        f2(x), f2(y + s * .9), f2(x), f2(y + s * .9), f2(x), f2(y + s * .9))
    c.path(d, fill, sw=sw)
    c.raw('<ellipse cx="%s" cy="%s" rx="%s" ry="%s" fill="#fff" fill-opacity=".55" transform="rotate(-30 %s %s)"/>' % (f2(x - s * .45), f2(y - s * .25), f2(s * .14), f2(s * .24), f2(x - s * .45), f2(y - s * .25)))


def star(c, x, y, r, fill="#e0b44a", sw=3):
    pts = []
    for i in range(10):
        a = -math.pi / 2 + i * math.pi / 5
        rr = r if i % 2 == 0 else r * .46
        pts.append((x + math.cos(a) * rr, y + math.sin(a) * rr))
    c.poly(pts, fill, sw=sw)


def plus(c, x, y, s, fill="#5fd08a", sw=3):
    t = s * .36
    c.poly([(x - t, y - s), (x + t, y - s), (x + t, y - t), (x + s, y - t), (x + s, y + t), (x + t, y + t), (x + t, y + s), (x - t, y + s), (x - t, y + t), (x - s, y + t), (x - s, y - t), (x - t, y - t)], fill, sw=sw)


def sparkle(c, x, y, s, col="#fff3c0"):
    c.poly([(x, y - s), (x + s * .25, y - s * .25), (x + s, y), (x + s * .25, y + s * .25), (x, y + s), (x - s * .25, y + s * .25), (x - s, y), (x - s * .25, y - s * .25)], col, sw=0)


def monitor(c, screen="#10231e", line="#5fe08e", fast=False):
    c.rect(26, 12, 76, 62, "#2b2f38", r=9, sw=3.8)
    c.rect(34, 20, 60, 44, screen, r=5, sw=2.6, hl=False, sh=False)
    pts = [(36, 44), (46, 44), (51, 28), (57, 58), (63, 40), (68, 44), (92, 44)] if not fast else [(36, 44), (42, 44), (46, 28), (51, 58), (55, 44), (59, 44), (63, 28), (68, 58), (72, 44), (76, 44), (80, 28), (85, 58), (89, 44)]
    c.pline(pts, line, w=3.6)
    c.rect(56, 74, 16, 22, "#4a4d55", r=4, sw=3, hl=False)
    c.rect(32, 94, 64, 14, "#2b2f38", r=6, sw=3.4)
    c.dot(94, 70, 3, "#e06a5a")


def iv_stand(c, fluid="#7fd3cf"):
    c.line(64, 18, 64, 108, OUT, w=9)
    c.line(64, 18, 64, 108, "#b9b2a3", w=4.4)
    c.line(36, 22, 92, 22, OUT, w=8)
    c.line(36, 22, 92, 22, "#b9b2a3", w=3.6)
    c.rect(28, 20, 26, 46, "#eaf3f2", r=9, sw=3)
    c.rect(32, 40, 18, 22, fluid, r=6, sw=0, hl=False, sh=False)
    c.line(41, 66, 41, 86, OUT, w=3)
    c.pline([(41, 86), (58, 98), (80, 96)], OUT, w=3)
    c.pline([(40, 108), (64, 108), (88, 108)], OUT, w=8)
    c.pline([(40, 108), (64, 108), (88, 108)], "#4a4d55", w=3.6)
    for x in (40, 88):
        c.circle(x, 114, 6, "#2b2f38", sw=2.6, hl=False)


def gas_machine(c):
    c.rect(24, 12, 80, 100, "#c6d2d0", r=10, sw=3.8)
    c.rect(24, 12, 80, 18, "#e1ece9", r=8, sw=2.6, hl=False, sh=False)
    c.circle(50, 52, 19, "#f4f1e4", sw=3.2)
    c.line(50, 52, 61, 41, "#c8372f", w=3.4)
    c.circle(50, 52, 3, OUT, sw=0, hl=False, sh=False)
    c.rect(72, 38, 22, 28, "#1f2a2e", r=5, sw=2.8, hl=False, sh=False)
    c.rect(76, 44, 14, 6, "#5fe0b5", r=2, sw=0, hl=False, sh=False)
    c.rect(76, 54, 14, 6, "#3f8f8a", r=2, sw=0, hl=False, sh=False)
    c.rect(36, 78, 18, 30, "#5e8fc2", r=7, sw=3)
    c.rect(60, 78, 18, 30, "#3fa66b", r=7, sw=3)
    c.rect(84, 78, 14, 30, "#d9803f", r=6, sw=3)
    c.rect(24, 106, 80, 12, "#4a4d55", r=5, sw=3.2)


def med_sign(c, col="#e0463c"):
    c.rect(8, 8, 112, 112, "#f2efe2", r=16, sw=4.2)
    plus(c, 64, 64, 36, col, sw=3.6)


def bottle2(c, x, y, w, h, col, glow_=None):
    bottle(c, x, y, w, h, col, None)


def flask(c, liquid="#a44cf0", glass="#2b3140"):
    c.path("M52 12 H76 V50 L108 104 Q114 118 100 118 H28 Q14 118 20 104 L52 50 Z", glass, sw=4.2)
    c.path("M44 84 H84 L104 104 Q108 112 98 112 H30 Q20 112 24 104 Z", liquid, sw=0)
    c.line(44, 84, 84, 84, light(liquid, .35), w=3)
    c.rect(48, 6, 32, 14, "#7a5638", r=5, sw=3.4)
    c.path("M64 54 Q80 72 70 88 Q64 96 58 88 Q48 72 64 54 Z", "#f6e8ff", sw=2.8)
    c.circle(50, 100, 5, light(liquid, .4), sw=0, hl=False, sh=False)
    c.circle(78, 96, 4, light(liquid, .4), sw=0, hl=False, sh=False)
    c.line(34, 100, 44, 76, "#ffffff", w=2.4, extra='stroke-opacity=".35"')


def vial(c, col, tall=1.0):
    c.rect(48, 22, 32, 96, "#cfd8e6", r=9, sw=3.2, hl=False)
    c.rect(52, 62, 24, 52, col, r=7, sw=0, hl=False, sh=False)
    c.line(52, 62, 76, 62, light(col, .4), w=2.4)
    c.rect(52, 10, 24, 16, "#7a5638", r=4, sw=3)


def robot(c, body="#9fb4c8", acc="#e0b44a", eye="#7ff0ff", helmet=True, tool=None):
    c.raw('<ellipse cx="64" cy="119" rx="32" ry="6" fill="#1c1411" fill-opacity=".28"/>')
    c.line(64, 12, 64, 26, OUT, w=6)
    c.circle(64, 10, 6, "#e06a5a", sw=3, hl=False)
    c.rect(42, 92, 16, 24, "#4a4d55", r=5, sw=3)
    c.rect(70, 92, 16, 24, "#4a4d55", r=5, sw=3)
    c.rect(34, 58, 60, 40, body, r=12, sw=3.6)
    c.rect(52, 68, 24, 14, "#2b2f38", r=4, sw=2.4, hl=False)
    c.dot(58, 75, 2.6, acc)
    c.dot(70, 75, 2.6, "#5fe08e")
    c.rect(22, 62, 14, 28, body, r=6, sw=3)
    c.rect(92, 62, 14, 28, body, r=6, sw=3)
    c.rect(36, 24, 56, 38, light(body, .12), r=12, sw=3.6)
    c.rect(44, 33, 40, 20, "#1b2a33", r=8, sw=2.8, hl=False, sh=False)
    c.circle(55, 43, 5, eye, sw=0, hl=False, sh=False)
    c.circle(73, 43, 5, eye, sw=0, hl=False, sh=False)
    if helmet:
        c.path("M34 30 Q34 10 64 10 Q94 10 94 30 Z", acc, sw=3.4)
        c.rect(30, 28, 68, 7, dark(acc, .15), r=3, sw=2.6, hl=False)
    if tool == "hammer":
        c.line(104, 92, 118, 56, OUT, w=8)
        c.line(104, 92, 118, 56, "#97704a", w=4)
        c.rect(104, 44, 22, 16, "#b9b2a3", r=4, sw=3)


def hammer(c):
    c.line(34, 108, 82, 34, OUT, w=13)
    c.line(34, 108, 82, 34, "#97704a", w=7)
    c.rect(56, 14, 50, 28, "#b9b2a3", r=7, sw=3.6)
    c.rect(98, 18, 10, 20, "#8d8678", r=3, sw=2.4, hl=False, sh=False)


def steel_stack(c, n=3, col="#b9b2a3"):
    for i in range(n):
        y = 100 - i * 26
        c.rect(18 + (i % 2) * 6, y, 92, 24, col, r=5, sw=3.2)
        c.line(30 + (i % 2) * 6, y + 12, 96 + (i % 2) * 6, y + 12, dark(col, .2), w=2)


def gold_bar(c, x, y, w=40, h=20):
    c.poly([(x + 8, y), (x + w - 8, y), (x + w, y + h), (x, y + h)], "#e0b44a", sw=3)
    c.line(x + 12, y + 5, x + w - 14, y + 5, "#fff3c0", w=2.4)


def hay(c):
    c.rect(10, 28, 108, 86, "#d8b350", r=12, sw=3.8)
    for x in (34, 64, 94):
        c.line(x, 28, x, 114, "#a8832e", w=3)
    c.line(14, 60, 114, 60, "#a8832e", w=3)
    for (x, y, a) in ((22, 50, -20), (50, 40, 25), (80, 48, -15), (104, 38, 20)):
        c.line(x, y, x + 12, y - 12, "#e8cd6c", w=3, extra='transform="rotate(%d %d %d)"' % (a, x, y))


def lightning(c, x, y, s, fill="#f7d66a"):
    c.poly([(x + s * .2, y - s), (x - s * .6, y + s * .1), (x - s * .05, y + s * .1), (x - s * .35, y + s), (x + s * .6, y - s * .2), (x + s * .05, y - s * .2)], fill, sw=3)


def bubble(im, x, y, w, h, fill=(246, 240, 224), tail="left", r=18):
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([x, y, x + w, y + h], radius=r, fill=fill, outline=OUTL, width=4)
    if tail == "left":
        d.polygon([(x + 26, y + h - 2), (x + 8, y + h + 24), (x + 52, y + h - 2)], fill=fill, outline=OUTL)
        d.polygon([(x + 28, y + h - 5), (x + 12, y + h + 18), (x + 50, y + h - 5)], fill=fill)
    elif tail == "right":
        d.polygon([(x + w - 52, y + h - 2), (x + w - 8, y + h + 24), (x + w - 26, y + h - 2)], fill=fill, outline=OUTL)
        d.polygon([(x + w - 50, y + h - 5), (x + w - 12, y + h + 18), (x + w - 28, y + h - 5)], fill=fill)


def panel(im, x, y, w, h, fill=(38, 40, 46), line=(120, 126, 138), r=16):
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([x + 4, y + 6, x + w + 4, y + h + 6], radius=r, fill=(14, 10, 8, 120))
    d.rounded_rectangle([x, y, x + w, y + h], radius=r, fill=fill, outline=OUTL, width=5)
    d.rounded_rectangle([x + 9, y + 9, x + w - 9, y + h - 9], radius=r - 6, outline=line, width=2)
