"""1.1.0 fix2: guest life on the map (F31). Small flecks for the moments of a stay: arrival suitcase, room key, coins for the till, a tip coin,
a waving hand, a dry puff, a sparkle, a heart for couples, and the departure review as 1-5 stars. 64 px per cell, bold silhouettes that read at
fleck size; warm outline, flat fills, one light line top left (house style)."""
import math
from .style import *
from .parts import coin, spark
from .registry import tex


def _star(c, cx, cy, ro, ri, fill, sw=3.6):
    pts = [(cx + math.cos(math.radians(a)) * (ro if i % 2 == 0 else ri), cy + math.sin(math.radians(a)) * (ro if i % 2 == 0 else ri))
           for i, a in enumerate(range(-90, 270, 36))]
    c.poly(pts, fill, sw=sw)
    if fill != "#8d8678":
        c.poly([(cx + (x - cx) * .5, cy + (y - cy) * .5 - ro * .06) for x, y in pts], light(fill, .45), sw=0)


@tex("Things/HH/Life/Suitcase", "single", (1, 1), doc="Fleck: a small leather suitcase above an arriving guest")
def life_suitcase(view, cells, masked):
    c = Canvas(1, 1)
    handle = [(48, 42), (48, 28), (80, 28), (80, 42)]                                 # handle: outline, then leather
    c.pline(handle, OUT, 11); c.pline(handle, P["wood_d"], 5)
    c.rect(20, 40, 88, 62, P["wood_l"], r=10, sw=4.2)                                # body
    c.rect(20, 40, 88, 14, light(P["wood_l"], .12), r=8, sw=0, hl=False, sh=False)    # lid edge
    c.line(20, 56, 108, 56, OUT, w=2.6)
    for x in (40, 88):                                                                # straps
        c.rect(x - 5, 40, 10, 62, P["amber"], r=2, sw=2.6, hl=False)
        c.rect(x - 7, 52, 14, 9, P["gold"], r=2, sw=2.2, hl=False, sh=False)
    c.rect(90, 70, 16, 22, P["cream"], r=3, sw=2.4, hl=False)                         # luggage tag
    c.dot(98, 74, 2.4, OUT)
    return c


@tex("Things/HH/Life/Key", "single", (1, 1), doc="Fleck: a brass room key with a little amber tag, at the check-in")
def life_key(view, cells, masked):
    c = Canvas(1, 1)
    c.circle(46, 46, 22, P["gold"], sw=4.2)
    c.circle(46, 46, 9, "#2a1c16", sw=0, hl=False, sh=False)
    c.poly([(60, 58), (104, 98), (96, 106), (52, 66)], P["gold"], sw=4)              # shaft
    c.poly([(86, 92), (94, 84), (102, 92), (94, 100)], dark(P["gold"], .15), sw=3)    # bit
    c.poly([(76, 82), (82, 76), (88, 82), (82, 88)], dark(P["gold"], .15), sw=3)
    c.line(28, 64, 18, 92, OUT, w=3)                                                  # tag string
    c.rect(6, 88, 26, 30, P["amber"], r=5, sw=3.2)
    c.dot(19, 95, 3, "#2a1c16")
    return c


@tex("Things/HH/Life/Coins", "single", (1, 1), doc="Fleck: three silver coins tossed towards the till")
def life_coins(view, cells, masked):
    c = Canvas(1, 1)
    coin(c, 42, 82, 24, color=P["silver"], sw=4.2)
    coin(c, 82, 64, 22, color=P["silver"], sw=4.2)
    coin(c, 52, 38, 18, color=P["gold"], sw=4)
    spark(c, 102, 28, 10, "#fff3c0")
    return c


@tex("Things/HH/Life/TipCoin", "single", (1, 1), doc="Fleck: one coin flipped on its edge, a tip")
def life_tip_coin(view, cells, masked):
    c = Canvas(1, 1)
    c.ellipse(64, 66, 30, 13, dark(P["silver"], .1), sw=4.2, hl=False)
    c.ellipse(64, 60, 30, 13, P["silver"], sw=4.2)
    c.ellipse(64, 60, 18, 7, light(P["silver"], .2), sw=1.6, hl=False, sh=False)
    for a, r0, r1 in ((-120, 40, 52), (-90, 34, 48), (-60, 40, 52)):                  # flip lines
        x0, y0 = 64 + math.cos(math.radians(a)) * r0, 60 + math.sin(math.radians(a)) * r0
        x1, y1 = 64 + math.cos(math.radians(a)) * r1, 60 + math.sin(math.radians(a)) * r1
        c.line(x0, y0, x1, y1, OUT, w=6); c.line(x0, y0, x1, y1, "#fff3c0", w=2.6)
    return c


@tex("Things/HH/Life/Wave", "single", (1, 1), doc="Fleck: a waving hand with two motion arcs, goodbye")
def life_wave(view, cells, masked):
    c = Canvas(1, 1)
    skin = "#e8b98f"
    for i, (x, top) in enumerate(((44, 34), (58, 24), (72, 26), (86, 36))):         # fingers
        c.rect(x - 6, top, 12, 50, skin, r=6, sw=3.6, hl=(i == 0), sh=False)
    c.rect(38, 56, 56, 46, skin, r=16, sw=4.2)                                        # palm
    c.poly([(40, 84), (22, 64), (30, 58), (50, 76)], skin, sw=3.6)                    # thumb
    c.rect(44, 98, 44, 18, P["blue"], r=5, sw=3.6)                                    # sleeve
    for r in (56, 66):
        pts = [(64 + math.cos(math.radians(a)) * r, 66 + math.sin(math.radians(a)) * r) for a in range(-160, -110, 6)]
        c.pline(pts, OUT, 6); c.pline(pts, P["paper"], 2.6)
    return c


@tex("Things/HH/Life/Puff", "single", (1, 1), doc="Fleck: a dry grey puff after a poor review (meh)")
def life_puff(view, cells, masked):
    c = Canvas(1, 1)
    g = "#b4ad9e"
    for (x, y, r) in ((46, 70, 20), (66, 58, 24), (86, 70, 18), (64, 80, 18)):
        c.circle(x, y, r, g, sw=3.6, hl=False, sh=False)
    for (x, y, r) in ((46, 70, 20), (66, 58, 24), (86, 70, 18), (64, 80, 18)):
        c.circle(x, y, r - 3.4, g, sw=0, hl=False, sh=False)
    c.pline([(52, 54), (60, 46), (72, 44)], light(g, .4), 3.4)
    c.circle(104, 46, 6, g, sw=2.6, hl=False, sh=False)
    return c


@tex("Things/HH/Life/Sparkle", "single", (1, 1), doc="Fleck: a sparkle around a five-star review")
def life_sparkle(view, cells, masked):
    c = Canvas(1, 1)
    c.glow(64, 64, 58, "#fff0b0", .7)
    spark(c, 64, 64, 42, "#fffbe8")
    return c


@tex("Things/HH/Life/Heart", "single", (1, 1), doc="Fleck: a small heart when a couple lies down in their shared bed")
def life_heart(view, cells, masked):
    c = Canvas(1, 1)
    d = "M64 108 C22 80 12 46 36 32 C50 24 60 32 64 44 C68 32 78 24 92 32 C116 46 106 80 64 108 Z"
    c.path(d, P["red_l"], sw=4.4)
    c.pline([(32, 58), (34, 44), (46, 38)], light(P["red_l"], .5), 4.4)
    return c


def _stars(n):
    def fn(view, cells, masked):
        c = Canvas(2, 0.5)
        c.rect(6, 6, 244, 52, P["paper"], r=26, sw=3.6)
        for i in range(5):
            _star(c, 32 + i * 48, 33, 21, 9, P["gold"] if i < n else "#8d8678", sw=3.2)
        return c
    fn.__name__ = "life_stars%d" % n
    return fn


for _n in range(1, 6):
    tex("Things/HH/Life/Stars%d" % _n, "single", (2, 0.5), doc="Fleck: departure review, %d of 5 stars on a paper pill" % _n)(_stars(_n))
