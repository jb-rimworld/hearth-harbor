#!/usr/bin/env python3
"""SUPERSEDED (24.11.2026): the Workshop previews About/Preview.png and About/Silverline_Preview.png are now the illustrated
thumbnails of the JB house style (see 22_RimWorld/_Planung_Marketing_2026-11-24/thumbs/compose.py). Do not run this script
for a release; it would overwrite them with the old sprite collage.

Steam Workshop preview images composed from our own sprites (KONZEPT 14.3, N05).

  uv run --with resvg-py --with pillow python3 tools/art/previews.py
Writes About/Preview.png (Hearth & Harbor, ribbon "Main Mod", no other text) and About/Silverline_Preview.png (the standalone bank, no text).
Sprites are rendered straight from the generator functions at 120 px per cell, so they are crisp vector renders, not scaled bitmaps.
"""
import sys, math, random
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from PIL import Image, ImageDraw, ImageFont, ImageFilter
import hhart.all  # noqa: F401
from hhart.registry import REG, render_png
from hhart.style import *
from hhart.parts import *

ROOT = Path(__file__).resolve().parents[2]
PPC = 120
W, H = 1280, 720
BYPATH = {e["path"]: e for e in REG}


def sprite(path, view="south", ppc=PPC):
    e = BYPATH[path]
    v = "single" if e["kind"] == "single" else view
    cv = e["fn"]("south" if v == "single" else v, e["cells"], e["masked"])
    return render_png(cv.svg(), int(round(cv.wc * ppc)), int(round(cv.hc * ppc)))


def deco(fn, wc, hc, ppc=PPC):
    from hhart.style import Canvas
    c = Canvas(wc, hc)
    fn(c)
    return render_png(c.svg(), int(round(wc * ppc)), int(round(hc * ppc)))


def shadow(im, x, y, canvas, strength=70, blur=6, dy=4):
    a = im.split()[3].point(lambda v: strength if v > 200 else 0)
    sh = Image.new("RGBA", im.size, (20, 10, 6, 0))
    sh.putalpha(a)
    pad = blur * 3
    big = Image.new("RGBA", (im.width + 2 * pad, im.height + 2 * pad), (0, 0, 0, 0))
    big.paste(sh, (pad, pad))
    big = big.filter(ImageFilter.GaussianBlur(blur))
    canvas.alpha_composite(big, (max(0, int(x - pad)), max(0, int(y - pad + dy))))


def put(canvas, im, x, y, sh=True):
    if sh:
        shadow(im, x, y, canvas)
    canvas.alpha_composite(im, (int(x), int(y)))


def glow(canvas, x, y, r, col=(255, 200, 110), a=110):
    g = Image.new("RGBA", (r * 2, r * 2), (0, 0, 0, 0))
    px = g.load()
    for j in range(r * 2):
        for i in range(r * 2):
            d = math.hypot(i - r, j - r) / r
            if d < 1:
                px[i, j] = col + (int(a * (1 - d) ** 2),)
    canvas.alpha_composite(g, (int(x - r), int(y - r)))


def font(sz):
    for p in ("/System/Library/Fonts/Supplemental/Arial Bold.ttf", "/System/Library/Fonts/Helvetica.ttc"):
        try:
            return ImageFont.truetype(p, sz)
        except Exception:
            pass
    return ImageFont.load_default()


def vignette(im, strength=150):
    v = Image.new("L", im.size, 0)
    d = ImageDraw.Draw(v)
    d.rectangle([0, 0, im.width, im.height], fill=strength)
    d.ellipse([-im.width * .15, -im.height * .25, im.width * 1.15, im.height * 1.25], fill=0)
    v = v.filter(ImageFilter.GaussianBlur(90))
    dark_ = Image.new("RGBA", im.size, (22, 12, 8, 255))
    dark_.putalpha(v)
    im.alpha_composite(dark_)


def planks(w, h, y0, seed=3, base=(150, 112, 78), cell=120):
    rnd = random.Random(seed)
    im = Image.new("RGBA", (w, h), base + (255,))
    d = ImageDraw.Draw(im)
    rows = (h - y0) // (cell // 3) + 1
    for r in range(rows):
        y = y0 + r * cell // 3
        off = -rnd.randint(0, cell)
        x = off
        tone = rnd.randint(-8, 8)
        while x < w:
            ln = rnd.randint(cell, cell * 2)
            col = tuple(max(0, min(255, b + tone + rnd.randint(-7, 7))) for b in base)
            d.rectangle([x, y, x + ln, y + cell // 3], fill=col)
            d.line([x, y, x + ln, y], fill=(96, 68, 46), width=2)
            d.line([x + ln, y, x + ln, y + cell // 3], fill=(96, 68, 46), width=2)
            x += ln
    return im


# ======================================================================= decorations (Canvas drawings)
def d_window(c):
    c.raw('<defs><linearGradient id="sk" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#e9a65a"/><stop offset=".6" stop-color="#f6d48a"/><stop offset="1" stop-color="#f6d48a"/></linearGradient></defs>')
    c.rect(8, 8, 240, 240, P["wood_d"], r=10, sw=4.2)
    c.raw('<rect x="22" y="22" width="212" height="212" fill="url(#sk)" stroke="#1c1411" stroke-width="3"/>')
    c.circle(150, 150, 30, "#fff3c0", sw=0, hl=False, sh=False)
    c.raw('<rect x="22" y="150" width="212" height="84" fill="#3d5f78"/>')
    for i, y in enumerate((162, 180, 200, 220)):
        c.raw('<path d="M22 %d q16 -6 32 0 t32 0 t32 0 t32 0 t32 0 t32 0 t32 0" fill="none" stroke="#8fb4c4" stroke-opacity="%s" stroke-width="3"/>' % (y, f2(.7 - i * .12)))
    c.raw('<path d="M70 150 L100 110 L124 150 Z" fill="#5b3d37"/>')
    c.poly([(190, 90), (204, 90), (208, 152), (186, 152)], P["paper"], sw=2.6, hl=False)
    c.poly([(190, 112), (204, 112), (205, 124), (189, 124)], P["red_l"], sw=0, hl=False)
    c.rect(186, 80, 22, 10, P["dsteel"], r=2, sw=2.4, hl=False)
    c.poly([(98, 190), (150, 190), (140, 204), (108, 204)], P["red"], sw=2.6, hl=False)
    c.line(124, 190, 124, 158, OUT, w=4)
    c.poly([(128, 160), (128, 186), (150, 186)], P["cream"], sw=2.2, hl=False)
    c.rect(22, 126, 212, 8, P["wood_d"], r=0, sw=0, hl=False, sh=False)
    c.rect(122, 22, 8, 212, P["wood_d"], r=0, sw=0, hl=False, sh=False)
    c.raw('<rect x="22" y="22" width="212" height="212" fill="none" stroke="#1c1411" stroke-width="3.4"/>')


def d_hearth(c):
    c.rect(6, 6, 244, 244, "#7a4636", r=8, sw=4.2, hl=False, sh=False)
    for r in range(1, 9):
        c.line(8, 6 + r * 30, 248, 6 + r * 30, "#4d2c22", w=2.2)
        for k in range(0, 260, 52):
            xx = 8 + k + (26 if r % 2 else 0)
            if xx < 246:
                c.line(xx, 6 + (r - 1) * 30, xx, 6 + r * 30, "#4d2c22", w=2.2)
    c.rect(14, 28, 228, 20, P["wood_d"], r=5, sw=3.6)
    d = "M52 236 V140 Q52 78 128 78 Q204 78 204 140 V236 Z"
    c.path(d, "#1b110c", sw=4.6)
    c.raw('<circle cx="128" cy="190" r="70" fill="#f2a03a" opacity=".18"/>')
    flame(c, 128, 196, 1.2)
    flame(c, 96, 210, .75)
    flame(c, 162, 210, .75)
    c.rect(66, 214, 124, 14, P["wood"], r=6, sw=3.2)


def d_shelf(c):
    c.rect(6, 70, 244, 14, P["wood_d"], r=4, sw=3.4)
    cols = ("#3f8f8a", "#c8372f", "#d9803f", "#5e8fc2", "#8e5cc8", "#4f7f5a", "#e0b44a")
    for i, col in enumerate(cols):
        x = 22 + i * 32
        bottle(c, x, 14, 20, 56, col, "#efe3c2" if i % 2 else None)
    c.rect(6, 112, 244, 12, P["wood_d"], r=4, sw=3.4)
    for i in range(5):
        x = 22 + i * 44
        c.rect(x, 90, 26, 22, P["amber"], r=5, sw=2.6)
        c.circle(x + 13, 88, 9, P["paper"], sw=2.4, hl=False, sh=False)


def d_plant(c):
    c.poly([(36, 78), (92, 78), (84, 118), (44, 118)], P["brick"], sw=3.6, hl=False)
    c.rect(30, 70, 68, 14, P["brick_d"], r=4, sw=3.2)
    for (x, y, rx, ry, a) in ((44, 44, 11, 28, -25), (64, 30, 11, 32, 0), (84, 44, 11, 28, 25), (54, 56, 10, 22, -10), (76, 56, 10, 22, 10)):
        c.raw('<ellipse cx="%s" cy="%s" rx="%s" ry="%s" fill="%s" stroke="#1c1411" stroke-width="3" transform="rotate(%s %s %s)"/>' % (x, y, rx, ry, P["leaf"], a, x, y + ry))


def d_stool(c):
    c.circle(64, 58, 28, P["red_l"], sw=3.6)
    c.circle(64, 58, 18, light(P["red_l"], .15), sw=0, hl=False, sh=False)
    c.rect(58, 78, 12, 34, P["iron"], r=3, sw=3, hl=False, sh=False)
    c.rect(38, 108, 52, 10, P["dsteel"], r=4, sw=3, hl=False)


def d_bed(c):
    f = slab(c, "south", P["wood"], apron=14, m=None)
    c.rect(*f.r(0, 0, 14, f.h), P["wood_d"], r=4, sw=3)
    c.rect(*f.r(14, 3, f.w - 17, f.h - 8), P["cream"], r=6, sw=2.8)
    c.rect(*f.r(20, 10, 40, f.h - 22), P["paper"], r=8, sw=2.6)
    c.rect(*f.r(f.w * .38, 3, f.w * .62 - 3, f.h - 8), P["amber"], r=6, sw=2.8)
    for x in (.5, .64, .78):
        c.line(*f.pt(f.w * x, 10), *f.pt(f.w * x, f.h - 12), dark(P["amber"], .3), w=2.4)


def d_rug(c, col):
    c.rect(6, 6, c.W - 12, c.H - 12, col, r=14, sw=3.6, hl=False, sh=False)
    c.rect(18, 18, c.W - 36, c.H - 36, dark(col, .16), r=8, sw=2.6, hl=False, sh=False)
    c.rect(32, 32, c.W - 64, c.H - 64, col, r=6, sw=0, hl=False, sh=False)


def d_post(c):
    c.rect(58, 20, 12, 84, P["steel"], r=5, sw=3.2)
    c.circle(64, 18, 12, P["gold"], sw=3)
    c.rect(38, 100, 52, 14, P["dsteel"], r=6, sw=3.2)


def d_coins(c):
    coin_stack(c, 40, 100, 20, 5)
    coin_stack(c, 82, 106, 20, 3, P["silver"])
    coin_stack(c, 60, 78, 18, 2)


# ======================================================================= Hearth & Harbor
def hearth_harbor():
    im = Image.new("RGBA", (W, H), (0, 0, 0, 255))
    wall_h = 330
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W, wall_h], fill=(98, 66, 44))
    for x in range(0, W, 60):
        d.line([x, 0, x, wall_h], fill=(86, 57, 38), width=3)
    d.rectangle([0, wall_h - 64, W, wall_h], fill=(120, 80, 52))
    d.line([0, wall_h - 64, W, wall_h - 64], fill=(70, 44, 28), width=4)
    fl = planks(W, H, wall_h, base=(160, 118, 82))
    im.alpha_composite(fl.crop((0, wall_h, W, H)), (0, wall_h))
    d.rectangle([0, wall_h, W, wall_h + 10], fill=(62, 40, 26))
    # wall pieces
    put(im, deco(d_window, 2, 2), 150, 84)
    put(im, deco(d_hearth, 2, 2), 400, 70)
    put(im, deco(d_shelf, 2, 2), 770, 90, sh=False)
    glow(im, 520, 250, 160, (255, 170, 80), 120)
    # hanging sign with the main tab emblem
    sign = Image.new("RGBA", (190, 170), (0, 0, 0, 0))
    sd = ImageDraw.Draw(sign)
    sd.line([(40, 0), (40, 34)], fill=(28, 20, 17), width=5)
    sd.line([(150, 0), (150, 34)], fill=(28, 20, 17), width=5)
    sd.rounded_rectangle([6, 30, 184, 164], radius=16, fill=(122, 86, 56), outline=(28, 20, 17), width=5)
    sd.rounded_rectangle([16, 40, 174, 154], radius=10, fill=(233, 212, 160), outline=(28, 20, 17), width=3)
    sign.alpha_composite(sprite("UI/HH/UI/MainTab", ppc=100), (45, 46))
    put(im, sign, 1070, 20)
    # floor furniture (back to front)
    put(im, sprite("Things/HH/Casino/SlotMachine"), 980, 190)
    put(im, sprite("Things/HH/Casino/SlotMachine"), 1100, 190)
    put(im, deco(d_plant, 1, 1), 1160, 296)
    put(im, deco(lambda c: d_rug(c, "#7e3b36"), 3, 2), 300, 400)
    put(im, deco(d_bed, 2, 1), 330, 360)
    put(im, deco(d_bed, 2, 1), 330, 520)
    put(im, sprite("Things/HH/Inn/FrontDesk"), 50, 380)
    put(im, sprite("Things/HH/Inn/TravelBag", ppc=100), 60, 520)
    put(im, sprite("Things/HH/Inn/Wallet", ppc=80), 160, 530)
    put(im, sprite("Things/HH/Venues/ShopCounter"), 740, 300)
    for x in (745, 840, 935):
        put(im, deco(d_stool, 1, 1, ppc=84), x, 405)
    put(im, sprite("Things/HH/Casino/PokerTable", ppc=100), 660, 505)
    put(im, sprite("Things/HH/Casino/Chips", ppc=110), 1030, 560)
    glow(im, 1100, 300, 130, (255, 190, 110), 70)
    vignette(im, 150)
    # ribbon "Main Mod" (the only text)
    rb = Image.new("RGBA", (300, 78), (0, 0, 0, 0))
    rd = ImageDraw.Draw(rb)
    rd.polygon([(0, 8), (300, 8), (276, 39), (300, 70), (0, 70)], fill=(217, 128, 63), outline=(28, 20, 17))
    rd.line([(0, 12), (290, 12)], fill=(247, 196, 130), width=3)
    rd.line([(0, 66), (290, 66)], fill=(150, 80, 36), width=3)
    f = font(40)
    tw = rd.textlength("Main Mod", font=f)
    rd.text((20, 39), "Main Mod", font=f, fill=(255, 246, 224), anchor="lm", stroke_width=3, stroke_fill=(28, 20, 17))
    im.alpha_composite(rb, (0, 4))
    return im.convert("RGB")


# ======================================================================= Silverline
def silverline():
    im = Image.new("RGBA", (W, H), (0, 0, 0, 255))
    d = ImageDraw.Draw(im)
    wall_h = 320
    d.rectangle([0, 0, W, wall_h], fill=(44, 78, 66))
    for x in range(0, W, 120):
        d.rectangle([x + 4, 28, x + 112, wall_h - 70], outline=(70, 112, 96), width=3)
    d.rectangle([0, wall_h - 66, W, wall_h], fill=(34, 60, 52))
    d.line([0, wall_h - 66, W, wall_h - 66], fill=(185, 188, 192), width=5)
    # checker stone floor
    tile = 80
    for r in range((H - wall_h) // tile + 2):
        for k in range(W // tile + 1):
            col = (222, 215, 198) if (r + k) % 2 == 0 else (176, 170, 156)
            d.rectangle([k * tile, wall_h + r * tile, k * tile + tile, wall_h + r * tile + tile], fill=col)
    d.rectangle([0, wall_h, W, wall_h + 8], fill=(40, 34, 30))
    # central shield plaque
    pl = Image.new("RGBA", (260, 260), (0, 0, 0, 0))
    pd = ImageDraw.Draw(pl)
    pd.rounded_rectangle([4, 4, 256, 256], radius=26, fill=(27, 48, 41), outline=(28, 20, 17), width=6)
    pd.rounded_rectangle([18, 18, 242, 242], radius=16, outline=(185, 188, 192), width=4)
    pl.alpha_composite(sprite("UI/HH/Bank/Logo_SL_Mutual", ppc=190), (35, 35))
    put(im, pl, 510, 24)
    # other logos and the clerk on the wall
    xs = [(790, "UI/HH/Bank/Logo_SL_CompoundInterest"), (900, "UI/HH/Bank/Logo_SL_ImperialTreasury"), (790 + 0, None)]
    put(im, sprite("UI/HH/Bank/Logo_SL_CompoundInterest", ppc=90), 810, 70)
    put(im, sprite("UI/HH/Bank/Logo_SL_ImperialTreasury", ppc=90), 920, 70)
    put(im, sprite("UI/HH/Bank/Logo_SL_OrbitalExchange", ppc=90), 1030, 70)
    put(im, sprite("UI/HH/Bank/Logo_SL_BlackLedger", ppc=90), 1140, 70)
    fr = Image.new("RGBA", (170, 170), (0, 0, 0, 0))
    fd = ImageDraw.Draw(fr)
    fd.rounded_rectangle([3, 3, 167, 167], radius=12, fill=(185, 188, 192), outline=(28, 20, 17), width=5)
    fr.alpha_composite(sprite("UI/HH/Bank/Ottoline", ppc=125), (10, 10))
    put(im, fr, 150, 50)
    put(im, sprite("UI/HH/Insurance/ClawbackLogo", ppc=70), 340, 110, sh=True)
    # machines standing at the wall
    put(im, sprite("Things/HH/Bank/ATM"), 980, 190)
    put(im, sprite("Things/HH/Bank/ATM"), 1100, 190)
    put(im, sprite("Things/HH/Bank/NightSafe"), 860, 190)
    put(im, deco(d_plant, 1, 1), 30, 220)
    put(im, deco(d_plant, 1, 1), 1160, 296)
    # rug and queue posts
    put(im, deco(lambda c: d_rug(c, "#2f6b57"), 4, 2), 450, 440)
    for x in (430, 700, 970):
        put(im, deco(d_post, 1, 1, ppc=100), x, 540)
    rope = ImageDraw.Draw(im)
    rope.line([(465, 560), (738, 560)], fill=(168, 52, 44), width=7)
    rope.line([(738, 560), (1008, 560)], fill=(168, 52, 44), width=7)
    # teller counters
    put(im, sprite("Things/HH/Bank/BankCounter"), 60, 340)
    put(im, sprite("Things/HH/Bank/BankCounter", view="north"), 60, 570, sh=True)
    put(im, deco(d_coins, 1, 1), 560, 360)
    put(im, deco(d_coins, 1, 1), 700, 380)
    glow(im, 640, 140, 260, (255, 240, 200), 70)
    vignette(im, 130)
    return im.convert("RGB")


def save(im, path, limit=1_000_000):
    path.parent.mkdir(parents=True, exist_ok=True)
    im.save(path, optimize=True)
    if path.stat().st_size > limit:  # fall back to a 256-colour palette
        im.quantize(256, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.FLOYDSTEINBERG).save(path, optimize=True)
    print(path, im.size, path.stat().st_size // 1024, "KB")


if __name__ == "__main__":
    save(hearth_harbor(), ROOT / "About" / "Preview.png")
    save(silverline(), ROOT / "About" / "Silverline_Preview.png")
