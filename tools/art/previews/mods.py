#!/usr/bin/env python3
"""Workshop preview images (1280x720) for all published mods of juliusberger00 in one shared style.

  uv run --with resvg-py --with pillow python3 tools/art/previews/mods.py [out_dir] [name ...]

Writes <out_dir>/<ModFolder>.png (default: ../_Release/Vorschaubilder/2026-11-23) and contact_sheet.png.
Style: a lit scene in the H&H house style, a warm title band at the bottom, a round bold title, the same small
"by juliusberger00" mark everywhere.  Class frame (KONZEPT 14.3): game mod = thin gold frame + diamond,
content mod = gold hairline, technical mod = steel frame + gear and slightly muted colours.  Only Hearth & Harbor
carries the "Main Mod" ribbon (its own preview, About/Preview.png, is not regenerated here).
"""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from kit import *  # noqa: E402,F401,F403

ROOT = Path(__file__).resolve().parents[3]
DEFAULT_OUT = ROOT.parent / "_Release" / "Vorschaubilder" / "2026-11-23"


def ds(fn, wc=1, hc=1, ppc=130):
    return deco(fn, wc, hc, ppc)


def bed(im, x, y, ppc=130, **kw):
    put(im, deco(lambda c: bed2(c, **kw), 2, 1, ppc), x, y)
    return x + 47 * ppc / 128, y + 64 * ppc / 128      # head centre


def shelf_vials(im, x, y, w, cols, vs=64):
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([x, y + vs - 6, x + w, y + vs + 10], radius=5, fill=(88, 57, 31), outline=OUTL, width=4)
    n = int(w // (vs * .55))
    for i in range(n):
        v = ds(lambda c, col=cols[i % len(cols)]: vial(c, col), ppc=vs)
        put(im, v, x + 8 + i * (w - 16 - vs * .6) / max(1, n - 1) - vs * .18, y - 6, sh=False)


# ============================================================================ RimWard
def rimward():
    im = wall_floor((72, 118, 122), (54, 92, 96), (212, 226, 222), floor="tiles", floor_b=(186, 206, 202), trim=(40, 66, 70))
    put(im, ds(med_sign, ppc=110), 770, 60)
    for x in (120, 1000):
        put(im, ds(monitor, ppc=150), x, 70)
    put(im, ds(monitor, ppc=150), 500, 70)
    d0 = ImageDraw.Draw(im)
    d0.line([(640, 0), (640, 40)], fill=OUTL, width=8)
    d0.line([(640, 0), (640, 40)], fill=(185, 178, 163), width=4)
    d0.ellipse([590, 36, 690, 70], fill=(233, 228, 212), outline=OUTL, width=5)
    glow(im, 600, 400, 330, (255, 255, 235), 80)
    # beds with sleeping patients
    h1 = bed(im, 60, 380, frame_col="#b9b2a3", sheet="#f2f6f4", blanket="#7fd3cf", skin="b", hair="#2b2218", rails=True)
    h2 = bed(im, 470, 380, frame_col="#b9b2a3", sheet="#f2f6f4", blanket="#7fd3cf", skin="a", hair="#8a4a22", rails=True)
    h3 = bed(im, 990, 380, frame_col="#b9b2a3", sheet="#f2f6f4", blanket="#7fd3cf", skin="c", hair="#1c1411", rails=True)
    for (hx_, hy_) in (h1, h2, h3):
        put(im, ds(zzz, ppc=80), hx_ - 10, hy_ - 120, sh=False)
    # anesthesia: machine, hose to mask on the middle patient, doctor
    put(im, ds(gas_machine, ppc=170), 760, 330)
    put(im, ds(iv_stand, ppc=150), 880, 350)
    d = ImageDraw.Draw(im)
    p0, p1, p2, p3 = (792, 385), (700, 330), (640, 500), (h2[0] + 14, h2[1] + 6)
    pts = [((1 - t) ** 3 * p0[0] + 3 * (1 - t) ** 2 * t * p1[0] + 3 * (1 - t) * t * t * p2[0] + t ** 3 * p3[0],
            (1 - t) ** 3 * p0[1] + 3 * (1 - t) ** 2 * t * p1[1] + 3 * (1 - t) * t * t * p2[1] + t ** 3 * p3[1]) for t in [i / 30 for i in range(31)]]
    d.line(pts, fill=OUTL, width=11, joint="curve")
    d.line(pts, fill=(127, 211, 207), width=5, joint="curve")
    d.rounded_rectangle([p3[0] - 16, p3[1] - 4, p3[0] + 18, p3[1] + 20], radius=8, fill=(232, 241, 238), outline=OUTL, width=4)
    putf(im, ds(lambda c: pawn(c, skin="d", hair="#2b2218", shirt="#3f8f8a", pants="#2f5a5c", mask=True, style="cap"), ppc=165), 405, 540)
    return im


# ============================================================================ Hospital Beds Gen 2
def hbg2():
    im = wall_floor((122, 164, 196), (96, 138, 170), (206, 178, 142), floor="planks", seed=9, trim=(80, 60, 44))
    put(im, deco(base.d_window, 2, 2), 110, 60)
    put(im, ds(lambda c: monitor(c, fast=True), ppc=170), 880, 70)
    # speed badge: two chevrons on a green disc
    bd = Image.new("RGBA", (190, 190), (0, 0, 0, 0))
    bdd = ImageDraw.Draw(bd)
    bdd.ellipse([8, 8, 182, 182], fill=(63, 166, 107), outline=OUTL, width=6)
    bdd.ellipse([22, 22, 168, 168], outline=(160, 230, 186), width=3)
    for dx in (-26, 18):
        bdd.line([(80 + dx, 56), (118 + dx, 95), (80 + dx, 134)], fill=OUTL, width=26, joint="curve")
        bdd.line([(80 + dx, 56), (118 + dx, 95), (80 + dx, 134)], fill=(244, 252, 240), width=14, joint="curve")
    put(im, bd, 545, 40)
    glow(im, 640, 400, 380, (140, 255, 170), 60)
    for x in (80, 470):
        hx_, hy_ = bed(im, x, 372, frame_col="#c9ccd1", sheet="#f6f4ee", blanket="#5e8fc2", skin="a" if x < 200 else "c", hair="#7a4a22" if x < 200 else "#1c1411", rails=True)
        glow(im, hx_ + 60, hy_, 140, (110, 255, 160), 120)
        for k, (ox, oy, s) in enumerate(((10, -80, 18), (80, -110, 13), (-30, -130, 11), (130, -70, 12))):
            put(im, ds(lambda c: plus(c, 64, 64, 42, "#7fe0a0", sw=5), ppc=s * 2.6), hx_ + ox - 20, hy_ + oy, sh=False)
    putf(im, ds(lambda c: pawn(c, skin="b", hair="#5a3a22", shirt="#f2f4f2", pants="#5e8fc2", style="bun"), ppc=170), 850, 540)
    putf(im, deco(base.d_plant, 1, 1), 1120, 520, sh=True)
    return im


# ============================================================================ Luciferium Overhaul
def luciferium():
    im = wall_floor((44, 34, 66), (34, 26, 52), (92, 68, 84), floor="planks", seed=5, trim=(30, 20, 36))
    cols = [(166, 80, 240), (100, 140, 255), (240, 120, 210), (120, 220, 200)]
    cc = ["#a44cf0", "#5e8fff", "#e870d0", "#6fe0c8"]
    shelf_vials(im, 60, 120, 320, cc)
    shelf_vials(im, 60, 220, 320, list(reversed(cc)))
    glow(im, 640, 360, 360, (180, 100, 255), 130)
    putf(im, ds(flask, ppc=330), 640, 535)
    # tiny sparks around the flask
    for (x, y, s) in ((470, 250, 14), (810, 230, 18), (850, 360, 11), (430, 380, 12), (720, 160, 12)):
        put(im, ds(lambda c: sparkle(c, 64, 64, 56, "#f1d9ff"), ppc=s * 2.4), x - s, y - s, sh=False)
    # colonist with glowing eyes
    putf(im, ds(lambda c: pawn(c, skin="a", hair="#d8d2e6", shirt="#6a4aa8", pants="#2f2a48", style="long", eyes="#d68cff"), ppc=165), 300, 535)
    glow(im, 300, 420, 120, (190, 120, 255), 90)
    put(im, ds(lambda c: heart(c, 64, 64, 40, "#e4566d", sw=5), ppc=60), 345, 280)
    put(im, ds(lambda c: plus(c, 64, 64, 42, "#7fe0a0", sw=5), ppc=48), 258, 300, sh=False)
    # settings panel with presets
    panel(im, 905, 110, 320, 230)
    textc(im, (1065, 146), "Luciferium Overhaul", 22, fill=(236, 226, 255), stroke=0)
    names = ["Vanilla", "Better", "Crazy", "Cheat"]
    for i, nm in enumerate(names):
        bx, by = 926 + (i % 2) * 148, 176 + (i // 2) * 62
        on = nm == "Crazy"
        d = ImageDraw.Draw(im)
        d.rounded_rectangle([bx, by, bx + 134, by + 48], radius=12, fill=(150, 72, 232) if on else (62, 64, 74), outline=OUTL, width=4)
        textc(im, (bx + 67, by + 25), nm, 24, fill=CREAM, stroke=0)
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([926, 308, 1204, 318], radius=5, fill=(30, 28, 38), outline=OUTL, width=2)
    d.rounded_rectangle([926, 308, 1100, 318], radius=5, fill=(176, 100, 245))
    d.ellipse([1090, 303, 1110, 323], fill=(250, 240, 255), outline=OUTL, width=3)
    return im


# ============================================================================ Mud and Plasteel Floor
def mudplasteel():
    rnd = random.Random(11)
    im = Image.new("RGBA", (W, H), (118, 84, 56, 255))
    d = ImageDraw.Draw(im)
    # mud: darker splotches
    for _ in range(190):
        x, y = rnd.randint(0, 700), rnd.randint(0, H)
        r = rnd.randint(8, 34)
        t = rnd.randint(-16, 10)
        d.ellipse([x - r, y - r * .6, x + r, y + r * .6], fill=(108 + t, 76 + t, 50 + t))
    # puddles
    for (x, y, rx, ry) in ((150, 330, 88, 30), (470, 505, 100, 32), (250, 150, 70, 24), (520, 140, 60, 20)):
        d.ellipse([x - rx - 4, y - ry - 4, x + rx + 4, y + ry + 4], fill=(70, 48, 34))
        d.ellipse([x - rx, y - ry, x + rx, y + ry], fill=(98, 82, 66))
        d.ellipse([x - rx + 10, y - ry + 6, x + rx * .3, y - 2], fill=(140, 124, 104))
    # plasteel tiles on the right, wavy boundary
    edge = [(640 + int(26 * math.sin(y / 38.0)) + int(5 * math.sin(y / 13.0)), y) for y in range(0, H + 1, 4)]
    tiles = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    td = ImageDraw.Draw(tiles)
    ts = 96
    for r in range(H // ts + 1):
        for k in range(W // ts + 1):
            col = (200, 224, 236) if (r + k) % 2 == 0 else (184, 210, 224)
            x0, y0 = k * ts - 20, r * ts - 10
            td.rectangle([x0, y0, x0 + ts, y0 + ts], fill=col, outline=(120, 150, 168), width=3)
            td.line([x0 + 8, y0 + 8, x0 + ts - 8, y0 + 8], fill=(240, 250, 255), width=3)
            td.line([x0 + 8, y0 + 8, x0 + 8, y0 + ts - 8], fill=(240, 250, 255), width=3)
            td.line([x0 + 8, y0 + ts - 8, x0 + ts - 8, y0 + ts - 8], fill=(150, 180, 198), width=3)
            td.polygon([(x0 + 22, y0 + 60), (x0 + 52, y0 + 18), (x0 + 60, y0 + 18), (x0 + 30, y0 + 60)], fill=(226, 242, 250))
    m = Image.new("L", (W, H), 0)
    ImageDraw.Draw(m).polygon(edge + [(W, H), (W, 0)], fill=255)
    im.paste(tiles, (0, 0), Image.composite(tiles.split()[3], Image.new("L", (W, H), 0), m))
    d = ImageDraw.Draw(im)
    d.line(edge, fill=(54, 38, 28), width=7, joint="curve")
    d.line([(x + 5, y) for x, y in edge], fill=(150, 112, 78), width=3, joint="curve")
    # footprints in the mud
    for (x, y) in ((100, 440), (130, 410), (80, 400), (170, 470), (205, 440), (560, 270), (585, 305)):
        d.ellipse([x - 8, y - 12, x + 8, y + 12], fill=(86, 60, 40))
    # props
    putf(im, ds(hay, ppc=150), 130, 280)
    putf(im, ds(hay, ppc=120), 560, 240)
    # raider stuck in the mud
    putf(im, ds(lambda c: (pawn(c, skin="c", hair="#b0332b", shirt="#7a2f2a", pants="#2b2f38", style="cap"),
                           c.line(46, 31, 60, 36, OUT, w=3.4), c.line(82, 31, 68, 36, OUT, w=3.4)), ppc=170), 380, 500)
    d = ImageDraw.Draw(im)
    d.ellipse([280, 468, 480, 520], fill=(74, 52, 38))
    putf(im, ds(lambda c: (pawn(c, skin="c", hair="#b0332b", shirt="#7a2f2a", pants="#2b2f38", style="cap"),
                           c.line(46, 31, 60, 36, OUT, w=3.4), c.line(82, 31, 68, 36, OUT, w=3.4)), ppc=170).crop((0, 0, 170, 150)), 380, 490, sh=False)
    for i, off in enumerate((-16, 0, 16)):
        d.arc([270 + i * 4, 350 + i * 8, 330 + i * 4, 440 + i * 8], 120, 240, fill=(240, 220, 190), width=4)
    # colonist sprinting on the plasteel
    for i in range(4):
        d.line([(690, 400 + i * 24), (770 - i * 10, 400 + i * 24)], fill=(255, 255, 255), width=5)
    putf(im, ds(lambda c: pawn(c, skin="a", hair="#5a3a22", shirt="#5e8fc2", pants="#3b4560", style="short"), ppc=170), 900, 500)
    putf(im, ds(lambda c: pawn(c, skin="b", hair="#1c1411", shirt="#4faf7a", pants="#3b4560", style="long"), ppc=150), 1090, 410)
    # badges
    for (x, y, txt, col) in ((300, 230, "slowed", (200, 72, 60)), (920, 270, "+150% speed", (63, 166, 107))):
        d = ImageDraw.Draw(im)
        w = int(font(30).getlength(txt)) + 44
        d.rounded_rectangle([x - w // 2, y - 24, x + w // 2, y + 24], radius=24, fill=col, outline=OUTL, width=4)
        textc(im, (x, y + 1), txt, 30, stroke=0)
    return im


# ============================================================================ Pawn Compatibility Global Slider
def pawncompat():
    im = wall_floor((126, 88, 104), (104, 70, 88), (158, 116, 94), floor="planks", seed=14, trim=(64, 40, 36))
    put(im, deco(base.d_window, 2, 2), 80, 60)
    put(im, deco(lambda c: base.d_rug(c, "#7e3b50"), 3, 2, 92), 205, 372, sh=False)
    glow(im, 350, 300, 220, (255, 160, 200), 70)
    putf(im, ds(lambda c: pawn(c, skin="a", hair="#7a4a22", shirt="#5e8fc2", pants="#3b4560", style="short"), ppc=175), 290, 540)
    putf(im, ds(lambda c: pawn(c, skin="b", hair="#2b2218", shirt="#c8605a", pants="#4a3a50", style="long"), ppc=175), 440, 540)
    put(im, ds(lambda c: heart(c, 64, 70, 46, "#e4566d", sw=5), ppc=120), 306, 210)
    put(im, ds(lambda c: heart(c, 64, 70, 46, "#f08aa0", sw=5), ppc=60), 250, 250)
    put(im, ds(lambda c: heart(c, 64, 70, 46, "#f08aa0", sw=5), ppc=48), 440, 215)
    # the big global slider panel
    panel(im, 640, 120, 560, 290)
    textc(im, (680, 168), "Global compatibility", 34, fill=(238, 232, 244), stroke=0, anchor="lm")
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([690, 262, 1150, 282], radius=10, fill=(24, 22, 30), outline=OUTL, width=3)
    d.rounded_rectangle([690, 262, 985, 282], radius=10, fill=(236, 96, 134), outline=OUTL, width=3)
    d.ellipse([966, 246, 1006, 298], fill=(250, 244, 250), outline=OUTL, width=5)
    for i in range(7):
        x = 690 + i * (460 / 6)
        d.line([(x, 298), (x, 312)], fill=(150, 154, 166), width=3)
    put(im, ds(lambda c: heart(c, 64, 70, 40, "#7a7e8c", sw=4), ppc=36), 650, 246, sh=False)
    put(im, ds(lambda c: heart(c, 64, 70, 40, "#e4566d", sw=4), ppc=48), 1158, 240, sh=False)
    textc(im, (1150, 350), "x 1.5", 50, fill=(255, 232, 240), stroke=0, anchor="rm")
    textc(im, (690, 350), "Pairs click more often", 24, fill=(176, 180, 194), stroke=0, anchor="lm")
    return im


# ============================================================================ Set my Defaults (and more)
def setdefaults():
    im = wall_floor((150, 134, 108), (120, 104, 82), (172, 166, 154), floor="tiles", floor_b=(152, 146, 134), trim=(70, 56, 44))
    putf(im, sprite("Things/HH/Kitchen/PrepCounter"), 160, 304)
    putf(im, sprite("Things/HH/Kitchen/Oven", ppc=125), 350, 304)
    putf(im, sprite("Things/HH/Kitchen/StockPot", ppc=125), 480, 304)
    putf(im, sprite("Things/HH/Kitchen/WokStation", ppc=125), 590, 304)
    putf(im, sprite("Things/HH/Kitchen/Grill"), 190, 470)
    putf(im, sprite("Things/HH/Kitchen/DoughBench"), 470, 470)
    # the bill panel: defaults filled in once
    panel(im, 700, 70, 540, 380)
    textc(im, (730, 112), "Bills", 32, fill=(238, 236, 228), stroke=0, anchor="lm")
    d = ImageDraw.Draw(im)
    rows = [("Cook simple meal", "until 40"), ("Cook fine meal", "until 25"), ("Make pemmican", "until 150")]
    for i, (a_, b_) in enumerate(rows):
        y = 148 + i * 96
        d.rounded_rectangle([728, y, 1212, y + 80], radius=12, fill=(58, 60, 68), outline=OUTL, width=3)
        if i == 0:
            d.rounded_rectangle([728, y, 744, y + 80], radius=8, fill=(217, 128, 63))
        d.rounded_rectangle([760, y + 26, 790, y + 56], radius=6, fill=(30, 30, 36), outline=(150, 154, 166), width=2)
        d.line([(766, y + 42), (774, y + 51), (786, y + 33)], fill=(110, 226, 150), width=5, joint="curve")
        textc(im, (808, y + 40), a_, 27, fill=(238, 236, 228), stroke=0, anchor="lm")
        textc(im, (1196, y + 40), b_, 24, fill=(190, 196, 208), stroke=0, anchor="rm")
    d.rounded_rectangle([728, 418 - 4, 1212, 424], radius=4, fill=(58, 60, 68))
    # DEFAULT stamp
    st = Image.new("RGBA", (260, 110), (0, 0, 0, 0))
    sd = ImageDraw.Draw(st)
    sd.rounded_rectangle([6, 6, 254, 104], radius=18, outline=(217, 128, 63), width=8)
    sd.text((130, 56), "DEFAULT", font=font(46), fill=(217, 128, 63), anchor="mm")
    st = st.rotate(9, expand=True, resample=Image.BICUBIC)
    im.alpha_composite(st, (940, 6))
    return im


# ============================================================================ QualityBuilder x Misc. Robots
def bed_frame(c):
    c.rect(6, 12, 244, 104, "#7a5638", r=10, sw=3.8)
    c.raw('<rect x="22" y="28" width="212" height="72" rx="6" fill="#5e8fc2" fill-opacity=".30" stroke="#dce8f4" stroke-width="3" stroke-dasharray="12 8"/>')
    c.rect(18, 24, 44, 80, "#97704a", r=8, sw=2.8)
    c.line(80, 28, 80, 100, "#dce8f4", w=2, extra='stroke-dasharray="6 6"')


def qbrobots():
    im = wall_floor((70, 78, 90), (58, 64, 74), (96, 102, 112), floor="tiles", floor_b=(84, 90, 100), trim=(38, 40, 46))
    d = ImageDraw.Draw(im)
    # hazard stripe along the wall base
    for x in range(-40, W, 56):
        d.polygon([(x, 296), (x + 28, 296), (x + 56, 236), (x + 28, 236)], fill=(224, 180, 74))
    d.rectangle([0, 236, W, 240], fill=OUTL)
    d.rectangle([0, 294, W, 298], fill=OUTL)
    d.rounded_rectangle([470, 40, 760, 210], radius=10, fill=(44, 92, 150), outline=OUTL, width=6)
    for x in range(486, 750, 28):
        d.line([(x, 52), (x, 198)], fill=(70, 120, 180), width=1)
    for y in range(56, 200, 28):
        d.line([(480, y), (750, y)], fill=(70, 120, 180), width=1)
    d.rectangle([520, 110, 710, 170], outline=(220, 236, 250), width=3)
    d.line([(520, 140), (710, 140)], fill=(220, 236, 250), width=2)
    d.rectangle([536, 120, 590, 160], outline=(220, 236, 250), width=2)
    d.line([(600, 70), (690, 70)], fill=(220, 236, 250), width=3)
    d.line([(600, 86), (660, 86)], fill=(220, 236, 250), width=3)
    put(im, ds(steel_stack, ppc=120), 930, 190)
    put(im, ds(lambda c: steel_stack(c, 2), ppc=120), 1050, 200)
    # the frame being built and its quality stars
    put(im, ds(bed_frame, 2, 1, 160), 440, 380)
    glow(im, 570, 380, 200, (255, 230, 150), 70)
    stars = Image.new("RGBA", (300, 70), (0, 0, 0, 0))
    for i in range(5):
        stars.alpha_composite(ds(lambda c, k=i: star(c, 64, 66, 50, "#e0b44a" if k < 4 else "#5a5e68", sw=5), ppc=56), (i * 58, 0))
    put(im, stars, 495, 318)
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([520, 276, 680, 306], radius=15, fill=(24, 20, 17), outline=(224, 180, 74), width=3)
    textc(im, (600, 291), "Excellent", 22, fill=CREAM, stroke=0)
    # builder robots
    putf(im, ds(lambda c: robot(c, tool="hammer"), ppc=210), 330, 540)
    putf(im, ds(lambda c: robot(c, body="#c9b896", acc="#d9803f"), ppc=190), 860, 530)
    putf(im, ds(lambda c: steel_stack(c, 2), ppc=95), 1010, 530)
    return im


# ============================================================================ Definitely Not Dev Mode
def creator(c):
    c.raw('<ellipse cx="64" cy="120" rx="46" ry="7" fill="#1c1411" fill-opacity=".3"/>')
    c.path("M30 120 L38 56 H90 L98 120 Z", "#2b2f38", sw=4)
    c.rect(24, 108, 80, 12, "#4a4d55", r=5, sw=3.4)
    c.rect(46, 70, 36, 30, "#5a2f8a", r=6, sw=3, hl=True)
    c.circle(64, 85, 8, "#f7d66a", sw=2.6, hl=False)
    c.rect(34, 46, 60, 14, "#3b2a5a", r=6, sw=3.4)
    c.circle(64, 30, 20, "#c58bff", sw=4)
    c.circle(64, 30, 9, "#f6e8ff", sw=0, hl=False, sh=False)
    c.path("M44 34 Q28 28 34 6 Q40 20 50 22 Z", "#c8372f", sw=3.4)
    c.path("M84 34 Q100 28 94 6 Q88 20 78 22 Z", "#c8372f", sw=3.4)


def door_leaf(c):
    c.rect(20, 6, 88, 116, "#58391f", r=8, sw=4)
    c.rect(32, 18, 64, 92, "#7a5638", r=5, sw=2.8)
    lightning(c, 64, 64, 34)
    c.circle(92, 70, 5, "#e0b44a", sw=2.4, hl=False)


def notdev():
    im = wall_floor((38, 24, 58), (60, 38, 78), (66, 46, 88), floor="tiles", floor_b=(54, 36, 74), trim=(30, 18, 44))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 236, W, 241], fill=GOLD)
    d.rectangle([0, 241, W, 244], fill=OUTL)
    for x in range(0, W, 120):
        d.rectangle([x + 20, 40, x + 100, 200], outline=(86, 56, 112), width=3)
    glow(im, 640, 360, 380, (170, 90, 255), 130)
    putf(im, ds(creator, ppc=320), 640, 498)
    # spawned goods arcing out of the creator
    for (x, y) in ((450, 280), (500, 350), (790, 300), (840, 365)):
        put(im, ds(lambda c: gold_bar(c, 40, 50, 48, 24), ppc=120), x, y)
    for (x, y, k) in ((330, 370, 3), (890, 380, 2)):
        put(im, ds(lambda c, n=k: coin_stack(c, 64, 100, 28, n + 2), ppc=88), x, y)
    for (x, y) in ((260, 450), (960, 440)):
        put(im, ds(lambda c: steel_stack(c, 2), ppc=80), x, y)
    for (x, y, s) in ((560, 200, 14), (750, 220, 18), (700, 150, 10), (590, 130, 12)):
        put(im, ds(lambda c: sparkle(c, 64, 64, 56, "#f1d9ff"), ppc=s * 2.4), x - s, y - s, sh=False)
    # instant door with lightning and an everlasting wall with the infinity sign
    putf(im, ds(door_leaf, ppc=230), 160, 520)
    w = Image.new("RGBA", (190, 250), (0, 0, 0, 0))
    wd = ImageDraw.Draw(w)
    wd.rounded_rectangle([4, 4, 186, 246], radius=12, fill=(122, 112, 130), outline=OUTL, width=5)
    for yy in range(4, 246, 62):
        wd.line([(4, yy), (186, yy)], fill=(82, 74, 92), width=4)
    wd.line([(10, 10), (180, 10)], fill=(170, 160, 182), width=3)
    wd.rounded_rectangle([40, 82, 150, 156], radius=18, fill=(30, 20, 44), outline=GOLD, width=4)
    for (cx_) in (80, 110):
        wd.ellipse([cx_ - 22, 104, cx_ + 4 if cx_ == 80 else cx_ + 22, 134], outline=(240, 214, 120), width=6) if False else None
    wd.arc([48, 100, 96, 138], 0, 360, fill=(246, 214, 106), width=7)
    wd.arc([94, 100, 142, 138], 0, 360, fill=(246, 214, 106), width=7)
    putf(im, w, 1130, 540)
    # tier plaques
    for i, (t, x) in enumerate((("Mk I  x3", 400), ("Mk II  x10", 560), ("Mk III  x100", 740))):
        ww = int(font(26).getlength(t)) + 34
        d = ImageDraw.Draw(im)
        d.rounded_rectangle([x, 506, x + ww, 546], radius=10, fill=(30, 20, 44), outline=GOLD, width=3)
        textc(im, (x + ww / 2, 527), t, 26, fill=(246, 226, 168), stroke=0)
    return im


# ============================================================================ Rimling
def rimling_fig(c):
    c.raw('<ellipse cx="64" cy="119" rx="38" ry="6" fill="#1c1411" fill-opacity=".3"/>')
    c.line(64, 6, 64, 24, OUT, w=6)
    c.circle(64, 8, 7, "#ffd84a", sw=3.4, hl=True)
    c.rect(36, 96, 17, 22, "#7a5638", r=6, sw=3.4)
    c.rect(75, 96, 17, 22, "#7a5638", r=6, sw=3.4)
    c.circle(64, 64, 44, "#8d6a3c", sw=4.4)
    c.path("M26 62 Q64 20 102 62 Z", "#b08c52", sw=3.4)
    c.circle(64, 66, 22, "#173844", sw=3.6)
    c.circle(64, 66, 15, "#35e3ef", sw=0, hl=False, sh=False)
    c.circle(64, 66, 7, "#0f4a58", sw=0, hl=False, sh=False)
    c.circle(69, 60, 3.6, "#ffffff", sw=0, hl=False, sh=False)


def rimling():
    im = wall_floor((64, 78, 98), (50, 62, 80), (122, 100, 84), floor="planks", seed=21, trim=(52, 38, 30))
    put(im, deco(base.d_window, 2, 2), 80, 50)
    put(im, deco(base.d_hearth, 2, 2), 380, 60)
    glow(im, 640, 360, 300, (60, 230, 240), 120)
    putf(im, ds(rimling_fig, ppc=290), 640, 540)
    putf(im, ds(lambda c: pawn(c, skin="a", hair="#5a3a22", shirt="#c8605a", pants="#3b4560", style="short"), ppc=140), 150, 520)
    putf(im, ds(lambda c: pawn(c, skin="c", hair="#1c1411", shirt="#6a8f4a", pants="#3b4560", style="bun"), ppc=130), 310, 480)
    for (x, y, k) in ((460, 220, "gear"), (790, 200, "sparkle"), (760, 330, "check")):
        pass
    # chat window
    panel(im, 880, 90, 330, 330)
    d = ImageDraw.Draw(im)
    d.ellipse([900, 106, 924, 130], fill=(53, 227, 239), outline=OUTL, width=3)
    textc(im, (936, 118), "Rimling", 24, fill=(226, 238, 244), stroke=0, anchor="lm")
    d.rounded_rectangle([960, 152, 1192, 220], radius=14, fill=(94, 143, 194), outline=OUTL, width=3)
    for i, t in enumerate(("Defend the", "east wall?")):
        textc(im, (976, 172 + i * 28), t, 24, fill=(248, 250, 252), stroke=0, anchor="lm")
    d.rounded_rectangle([898, 242, 1130, 330], radius=14, fill=(34, 86, 96), outline=OUTL, width=3)
    for i, t in enumerate(("Placing walls and", "turrets. On it.")):
        textc(im, (914, 264 + i * 28), t, 24, fill=(214, 248, 250), stroke=0, anchor="lm")
    d.rounded_rectangle([898, 358, 1192, 396], radius=19, fill=(30, 30, 36), outline=(120, 126, 138), width=2)
    textc(im, (918, 377), "Ask anything...", 22, fill=(130, 136, 150), stroke=0, anchor="lm")
    return im


# ============================================================================ build
MODS = [
    # folder, title, tagline, accent, class, desat, builder
    ("RimWard", "RimWard", "Anesthesia Not Included", "#3FB8AF", "game", 1.0, rimward),
    ("HospitalBedsGen2", "Hospital Beds Gen 2", "Fast regeneration in bed", "#5fd08a", "content", 1.0, hbg2),
    ("LuciferiumOverhaul", "Luciferium Overhaul", "Presets from Vanilla to Cheat", "#a44cf0", "content", 1.0, luciferium),
    ("MudAndPlasteelFloor", "Mud and Plasteel Floor", "Slow raiders, fast colonists", "#6fb8d8", "content", 1.0, mudplasteel),
    ("PawnCompatibilityGlobalAdjuster", "Pawn Compatibility Global Slider", "One slider for every pair", "#e4566d", "tech", 0.82, pawncompat),
    ("BetterWorkbenchDefaults", "Set my Defaults", "Bill defaults, set up once", "#d9803f", "tech", 0.82, setdefaults),
    ("QualityBuilderRobots", "QualityBuilder × Misc. Robots", "Robots build with quality", "#e0b44a", "tech", 0.82, qbrobots),
    ("DefinitelyNotDevMode", "Definitely Not Dev Mode", "Devil's buildings, no dev mode needed", "#8e5cc8", "game", 1.0, notdev),
    ("Rimling", "Rimling", "Your AI companion", "#35e3ef", "game", 1.0, rimling),
]


def sheet(out_dir, files):
    tw, th = 640, 360
    cols = 3
    rows = (len(files) + cols - 1) // cols
    g = 14
    sh = Image.new("RGB", (cols * tw + (cols + 1) * g, rows * th + (rows + 1) * g), (26, 22, 20))
    for i, f in enumerate(files):
        im = Image.open(f).convert("RGB").resize((tw, th), Image.LANCZOS)
        sh.paste(im, (g + (i % cols) * (tw + g), g + (i // cols) * (th + g)))
    sh.save(out_dir / "contact_sheet.png", optimize=True)


def main():
    args = [a for a in sys.argv[1:]]
    out_dir = DEFAULT_OUT
    if args and ("/" in args[0] or args[0].startswith(".")):
        out_dir = Path(args.pop(0))
    out_dir.mkdir(parents=True, exist_ok=True)
    files = []
    for folder, title, tag, acc, cls, desat, fn in MODS:
        if args and folder not in args:
            files.append(out_dir / (folder + ".png"))
            continue
        im = finish(fn(), acc, title, tag, cls, desat=desat)
        p = out_dir / (folder + ".png")
        base.save(im, p)
        files.append(p)
    # the two H&H previews (already exist in About/) join the sheet as a reference, not rewritten
    refs = [ROOT / "About" / "Preview.png", ROOT / "About" / "Silverline_Preview.png"]
    sheet(out_dir, files + [r for r in refs if r.exists()])


if __name__ == "__main__":
    main()
