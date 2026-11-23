"""Slot reel symbols for "The Lucky Thrumbo" window and the colony slot machines (1.1.0: things of the rim, F30).
One 128 px UI icon per SlotPaytable.SymbolNames entry, cheapest first: berries, corn, meal (packaged survival meal), component,
plasteel, core (AI persona core), thrumbo (the jackpot). Path UI/HH/Casino/Symbols/<id>.
Bold silhouettes, readable at 24-64 px on a cream reel. Light edge top left, shadow edge bottom right, no text."""
import math
from .style import *
from .parts import *
from .registry import tex
from .inn import _icon


def stroke(c, d, w, col, ow=5):
    c.raw('<path d="%s" fill="none" stroke="%s" stroke-width="%s" stroke-linecap="round" stroke-linejoin="round"/>' % (d, OUT, f2(w + ow)))
    c.raw('<path d="%s" fill="none" stroke="%s" stroke-width="%s" stroke-linecap="round" stroke-linejoin="round"/>' % (d, col, f2(w)))


def arcline(c, d, col, w=3):
    c.raw('<path d="%s" fill="none" stroke="%s" stroke-width="%s" stroke-linecap="round"/>' % (d, col, f2(w)))


def leaf(c, x, y, sx=1.0, rot=0, col=None, size=1.0):
    col = col or P["leaf"]
    c.raw('<g transform="translate(%s %s) rotate(%s) scale(%s %s)">' % (f2(x), f2(y), f2(rot), f2(sx * size), f2(size)))
    c.path("M0 0 Q14 -26 44 -22 Q40 6 0 0 Z", col, sw=4)
    c.raw('<path d="M5 -3 Q22 -10 38 -17" fill="none" stroke="%s" stroke-width="2.4" stroke-linecap="round"/></g>' % light(col, .3))


def berry(c, cx, cy, r, col):
    c.circle(cx, cy, r, col, sw=3.6)
    c.circle(cx - r * .36, cy - r * .38, r * .24, light(col, .55), sw=0, hl=False, sh=False)
    c.dot(cx + r * .15, cy + r * .2, max(1.2, r * .08), dark(col, .45))


@tex("UI/HH/Casino/Symbols/berries", "single", (1, 1), ppc=128, grain=0, doc="Slot symbol: a handful of berries (the cheapest symbol)")
def sym_berries(view, cells, masked):
    c = _icon(cells, masked)
    leaf(c, 60, 34, 1.0, -150, size=1.05)
    leaf(c, 70, 34, 1.0, -30, size=1.0)
    stroke(c, "M64 40 Q62 28 66 18", 4.5, P["wood"], 3)
    col, col2 = "#b0263a", "#8e2a6b"
    for (x, y, r, k) in ((40, 70, 17, col2), (70, 62, 18, col), (94, 76, 16, col2), (54, 94, 18, col), (84, 100, 17, col), (30, 98, 14, col2), (102, 102, 12, col)):
        berry(c, x, y, r, k)
    return c


@tex("UI/HH/Casino/Symbols/corn", "single", (1, 1), ppc=128, grain=0, doc="Slot symbol: an ear of corn in its husk")
def sym_corn(view, cells, masked):
    c = _icon(cells, masked)
    husk, husk_d = "#7aa04a", "#5d7f36"
    c.raw('<g transform="rotate(-28 64 64)">')
    # the cob
    cob = "#f0c43a"
    c.path("M64 8 Q84 10 86 44 Q88 84 72 116 Q64 124 56 116 Q40 84 42 44 Q44 10 64 8 Z", cob, sw=4.4)
    for row in range(9):
        y = 18 + row * 10.5
        half = 10 + 9 * math.sin(min(1.0, (row + 1) / 4.0) * math.pi / 2) - max(0, row - 6) * 3
        for k in (-1, 0, 1):
            x = 64 + k * half * 0.62
            c.ellipse(x, y, 4.2, 4.4, light(cob, .15) if k == -1 else cob, sw=1.6, hl=False, sh=False)
    arcline(c, "M52 22 Q48 50 52 92", "#fff0a8", 3.2)
    # husk leaves around the bottom
    c.path("M40 70 Q30 98 58 124 Q50 100 52 74 Z", husk, sw=3.6)
    c.path("M88 70 Q98 98 70 124 Q78 100 76 74 Z", husk_d, sw=3.6)
    arcline(c, "M40 78 Q38 98 52 116", light(husk, .3), 2.4)
    c.raw('</g>')
    return c


@tex("UI/HH/Casino/Symbols/meal", "single", (1, 1), ppc=128, grain=0, doc="Slot symbol: a packaged survival meal")
def sym_meal(view, cells, masked):
    c = _icon(cells, masked)
    foil = "#b8ad8e"
    # pouch with crimped top and bottom
    c.rect(18, 22, 92, 86, foil, r=10, sw=4.4)
    for y in (24, 98):
        for i in range(9):
            x = 24 + i * 10
            c.line(x, y + 2, x, y + 8, dark(foil, .3), w=2.2)
    # label band
    c.rect(24, 44, 80, 40, "#d9803f", r=5, sw=3, hl=True)
    # fork and spoon on the label
    stroke(c, "M52 52 V76", 3.4, "#f4ecd4", 2.4)
    stroke(c, "M47 52 V60 Q52 66 57 60 V52", 2.6, "#f4ecd4", 2.2)
    c.ellipse(76, 57, 6, 7.5, "#f4ecd4", sw=2.4, hl=False, sh=False)
    stroke(c, "M76 64 V76", 3.4, "#f4ecd4", 2.4)
    arcline(c, "M26 30 Q40 26 60 27", light(foil, .4), 3)
    return c


def gear_path(cx, cy, r_out, r_in, teeth, phase=0.0):
    pts = []
    for i in range(teeth * 2):
        a0 = (i / (teeth * 2.0)) * 2 * math.pi + phase
        a1 = ((i + 1) / (teeth * 2.0)) * 2 * math.pi + phase
        r = r_out if i % 2 == 0 else r_in
        pts.append((cx + math.cos(a0) * r, cy + math.sin(a0) * r))
        pts.append((cx + math.cos(a1) * r, cy + math.sin(a1) * r))
    return pts


@tex("UI/HH/Casino/Symbols/component", "single", (1, 1), ppc=128, grain=0, doc="Slot symbol: an industrial component (gear with a circuit plate)")
def sym_component(view, cells, masked):
    c = _icon(cells, masked)
    steel = P["steel"]
    # a small circuit plate behind
    c.rect(56, 62, 60, 50, "#4f7f5a", r=6, sw=3.6)
    for (x1, y1, x2, y2) in ((64, 74, 104, 74), (104, 74, 104, 100), (64, 90, 88, 90), (88, 90, 88, 104)):
        c.line(x1, y1, x2, y2, "#d9b94a", w=2.4)
    for (x, y) in ((104, 100), (88, 104), (64, 74)):
        c.circle(x, y, 3.2, "#e0b44a", sw=1.4, hl=False, sh=False)
    # the gear
    c.poly(gear_path(52, 54, 40, 31, 10), steel, sw=4.4)
    c.circle(52, 54, 22, light(steel, .1), sw=3)
    c.circle(52, 54, 10, P["dsteel"], sw=3, hl=False)
    c.circle(52, 54, 4, P["amber"], sw=1.6, hl=False, sh=False)
    arcline(c, "M30 40 Q38 30 50 28", light(steel, .5), 3)
    return c


@tex("UI/HH/Casino/Symbols/plasteel", "single", (1, 1), ppc=128, grain=0, doc="Slot symbol: three plasteel ingots")
def sym_plasteel(view, cells, masked):
    c = _icon(cells, masked)
    ps = "#a9d3dc"

    def ingot(x, y, w, h, col):
        top = [(x + 8, y), (x + w - 8, y), (x + w, y + h * 0.35), (x, y + h * 0.35)]
        front = [(x, y + h * 0.35), (x + w, y + h * 0.35), (x + w, y + h), (x, y + h)]
        c.poly(front, dark(col, .18), sw=3.8)
        c.poly(top, light(col, .25), sw=3.8)
        c.line(x + 10, y + 3, x + w - 12, y + 3, "#ffffff", w=2.2)
        c.line(x + 4, y + h * 0.35 + 4, x + 4, y + h - 4, light(col, .3), w=2)

    ingot(14, 66, 50, 34, ps)
    ingot(64, 66, 50, 34, dark(ps, .06))
    ingot(38, 36, 52, 34, light(ps, .05))
    spark(c, 96, 28, 9, "#ffffff")
    return c


@tex("UI/HH/Casino/Symbols/core", "single", (1, 1), ppc=128, grain=0, doc="Slot symbol: an AI persona core (glowing)")
def sym_core(view, cells, masked):
    c = _icon(cells, masked)
    c.glow(64, 64, 62, "#7fe3f0", .55)
    shell = "#3b4252"
    hexp = [(64 + math.cos(math.radians(a)) * 48, 64 + math.sin(math.radians(a)) * 48) for a in range(-90, 270, 60)]
    c.poly(hexp, shell, sw=4.4)
    inner = [(64 + math.cos(math.radians(a)) * 36, 64 + math.sin(math.radians(a)) * 36) for a in range(-90, 270, 60)]
    c.poly(inner, "#2a303c", sw=2.4)
    for a in range(-90, 270, 60):
        x1, y1 = 64 + math.cos(math.radians(a)) * 36, 64 + math.sin(math.radians(a)) * 36
        x2, y2 = 64 + math.cos(math.radians(a)) * 24, 64 + math.sin(math.radians(a)) * 24
        c.line(x1, y1, x2, y2, "#5fd0e0", w=2.4)
    c.circle(64, 64, 20, "#5fd0e0", sw=3)
    c.circle(64, 64, 12, "#c9f6ff", sw=0, hl=False, sh=False)
    c.circle(59, 59, 4, "#ffffff", sw=0, hl=False, sh=False)
    arcline(c, "M30 40 Q40 26 58 20", light(shell, .45), 3)
    return c


@tex("UI/HH/Casino/Symbols/thrumbo", "single", (1, 1), ppc=128, grain=0, doc="Slot symbol: thrumbo head with its great horn (the jackpot)")
def sym_thrumbo(view, cells, masked):
    c = _icon(cells, masked)
    fur, fur_d, horn = "#f1ece0", "#cfc6b2", P["amber"]
    c.glow(64, 70, 60, "#ffe7a0", .35)
    # ears
    c.ellipse(22, 58, 15, 8, fur_d, sw=4)
    c.ellipse(106, 58, 15, 8, fur_d, sw=4)
    # head
    c.path("M64 30 Q100 30 102 66 Q104 96 88 112 Q76 122 64 122 Q52 122 40 112 Q24 96 26 66 Q28 30 64 30 Z", fur, sw=4.4)
    # fluffy brow
    c.path("M32 56 Q40 34 64 36 Q88 34 96 56 Q86 50 78 56 Q72 46 64 54 Q56 46 50 56 Q42 50 32 56 Z", fur_d, sw=3.2)
    # the great horn, spiral bands
    c.poly([(54, 40), (64, -2), (74, 40)], horn, sw=4)
    for t in (0.2, 0.42, 0.64):
        x0 = 54 + (64 - 54) * t; x1 = 74 + (64 - 74) * t; y = 40 + (-2 - 40) * t
        c.line(x0 + 1.5, y + 3, x1 - 1.5, y - 3, dark(horn, .45), w=2.6)
    # muzzle
    c.path("M44 92 Q44 76 64 76 Q84 76 84 92 Q86 112 64 114 Q42 112 44 92 Z", "#e3d8c2", sw=3.2)
    c.ellipse(56, 96, 3.6, 5, OUT, sw=0, hl=False, sh=False)
    c.ellipse(72, 96, 3.6, 5, OUT, sw=0, hl=False, sh=False)
    arcline(c, "M56 106 Q64 110 72 106", OUT, 2.6)
    # kind eyes
    for x in (46, 82):
        c.circle(x, 66, 7, "#2b2f38", sw=2.6, hl=False, sh=False)
        c.circle(x - 2, 64, 2.2, "#ffffff", sw=0, hl=False, sh=False)
    spark(c, 92, 14, 8, "#fff3c0")
    return c
