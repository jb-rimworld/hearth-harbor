"""Venues: signs, shop counter, vending machine, venue-type icons."""
from .style import *
from .parts import *
from .registry import tex
from .casino import canvas_for_
from .inn import _icon


def sign_board(c, x, y, w, h, accent):
    c.rect(x, y, w, h, P["wood_l"], r=7)
    c.rect(x + 8, y + 8, w - 16, h - 16, P["cream"], r=4, sw=2.4, hl=False)
    c.rect(x + 8, y + 8, w - 16, 9, accent, r=3, sw=0, hl=False, sh=False)
    c.line(x + 18, y + h * .6, x + w - 18, y + h * .6, "#cdbf99", w=2.2)
    c.line(x + 18, y + h * .6 + 12, x + w - 38, y + h * .6 + 12, "#cdbf99", w=2.2)


@tex("Things/HH/Venues/VenueSign", "multi", (1, 1), doc="Freestanding venue sign: post with hanging board, 1x1")
def venuesign(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    if view != "east":
        c.rect(52, 100, 24, 18, P["stone"], r=5, sw=3.2)
        c.rect(58, 8, 12, 100, P["wood_d"], r=4, sw=3)
        c.rect(24, 12, 90, 11, P["wood_d"], r=4, sw=3)
        c.circle(64, 9, 7, P["amber"], sw=2.6)
        c.line(32, 22, 32, 36, P["iron"], w=3)
        c.line(104, 22, 104, 36, P["iron"], w=3)
        if view == "south":
            sign_board(c, 16, 34, 96, 58, P["amber"])
        else:
            c.rect(16, 34, 96, 58, P["wood"], r=7)
            c.line(26, 52, 102, 52, dark(P["wood"], .3), w=3)
            c.line(26, 72, 102, 72, dark(P["wood"], .3), w=3)
    else:
        c.rect(46, 100, 36, 18, P["stone"], r=5, sw=3.2)
        c.rect(56, 8, 16, 100, P["wood_d"], r=4, sw=3)
        c.rect(40, 12, 48, 11, P["wood_d"], r=4, sw=3)
        c.circle(64, 9, 7, P["amber"], sw=2.6)
        c.rect(60, 34, 9, 58, P["wood_l"], r=3, sw=2.8)
        c.line(48, 22, 48, 36, P["iron"], w=3)
        c.line(82, 22, 82, 36, P["iron"], w=3)
    return c


@tex("Things/HH/Venues/VenueSignWall", "multi", (1, 1), doc="Wall-mounted venue sign on an iron bracket, 1x1")
def venuesign_wall(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    if view != "east":
        c.rect(10, 12, 108, 10, P["iron"], r=4, sw=3)
        c.rect(14, 8, 10, 22, P["iron"], r=3, sw=2.6)
        c.rect(104, 8, 10, 22, P["iron"], r=3, sw=2.6)
        c.line(30, 22, 30, 38, P["iron"], w=3)
        c.line(98, 22, 98, 38, P["iron"], w=3)
        if view == "south":
            sign_board(c, 14, 36, 100, 70, P["amber"])
        else:
            c.rect(14, 36, 100, 70, P["wood"], r=7)
            c.line(24, 58, 104, 58, dark(P["wood"], .3), w=3)
            c.line(24, 82, 104, 82, dark(P["wood"], .3), w=3)
    else:
        c.rect(46, 8, 36, 22, P["iron"], r=4, sw=3)
        c.line(64, 22, 64, 38, P["iron"], w=3)
        c.rect(58, 36, 12, 70, P["wood_l"], r=3, sw=2.8)
    return c


@tex("Things/HH/Venues/ShopCounter", "multi", (2, 1), masked=True, doc="Shop counter with till, scale and basket, 2x1, stuffable top")
def shopcounter(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    f = slab(c, view, P["pale"], apron=18, m="a")
    # till
    tx, ty, tw, th = f.r(f.w * .08, f.h * .14, 52, f.h * .62)
    c.rect(tx, ty, tw, th, P["dsteel"], r=6, sw=3, m=None)
    c.rect(tx + 6, ty + 6, tw - 12, th * .3, P["glass"], r=2, sw=1.6, hl=False, sh=False, m=None)
    for i in range(3):
        for j in range(2):
            c.rect(tx + 8 + i * 12, ty + th * .5 + j * 10, 8, 6, P["steel"], r=1, sw=1.2, hl=False, sh=False, m=None)
    # coin dish
    cx, cy = f.pt(f.w * .36, f.h * .55)
    c.circle(cx, cy, 13, P["silver"], sw=2.4, m=None)
    coin(c, cx - 3, cy - 2, 5.5)
    coin(c, cx + 4, cy + 3, 5.5)
    # scale
    sx, sy = f.pt(f.w * .5, f.h * .4)
    c.circle(sx, sy, 15, P["iron"], sw=2.8, m=None)
    c.circle(sx, sy, 10, P["paper"], sw=1.6, hl=False, sh=False, m=None)
    c.line(sx, sy, sx + 5, sy - 6, P["red_l"], w=2)
    # basket with fruit and bread
    bx, by, bw, bh = f.r(f.w * .62, f.h * .14, f.w * .2, f.h * .62)
    c.rect(bx, by, bw, bh, P["bread_d"], r=7, sw=2.8, m=None)
    mx, my = bx + bw / 2, by + bh / 2
    c.circle(mx - 8, my - 10, 8, P["tomato"], sw=2, m=None)
    c.circle(mx + 6, my, 8, P["leaf"], sw=2, m=None)
    c.ellipse(mx - 4, my + 14, 10, 6, P["bread"], sw=2, m=None)
    # paper bag
    c.rect(*f.r(f.w * .86, f.h * .3, 22, 30), "#cbb68a", r=4, sw=2.4, m=None)
    return c


@tex("Things/HH/Venues/VendingMachine", "multi", (1, 1), doc="Vending machine with a glass front, 1x1")
def vending(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    body = P["teal"]
    face = cabinet(c, view, body, cap=18, margin=8, side_accent=dark(body, .3), accent_strip=True)
    x, y, w, h = face
    if view == "south":
        c.rect(x, y - 2, w, 12, P["amber"], r=3, sw=2.2, hl=False)
        c.rect(x, y + 14, w * .68, h * .62, P["glass"], r=4, sw=3, hl=False, sh=False)
        cols = (P["red_l"], P["yolk"], P["blue"], P["leaf"])
        for row in range(3):
            for k in range(4):
                c.rect(x + 6 + k * (w * .68 - 10) / 4, y + 20 + row * (h * .62 - 14) / 3, 11, 15, cols[(k + row) % 4], r=3, sw=1.8, hl=False, sh=False)
            c.line(x + 4, y + 38 + row * (h * .62 - 14) / 3, x + w * .68 - 2, y + 38 + row * (h * .62 - 14) / 3, dark(P["glass"], .45), w=2)
        px = x + w * .74
        c.rect(px, y + 14, w * .26, 38, P["dsteel"], r=3, sw=2.4, hl=False)
        for i in range(3):
            for j in range(3):
                c.rect(px + 4 + i * 6.5, y + 18 + j * 6.5, 5, 5, P["steel"], r=1, sw=0, hl=False, sh=False)
        c.rect(px + 2, y + 58, w * .26 - 4, 6, "#14100e", r=2, sw=0, hl=False, sh=False)
        c.rect(x + 4, y + h - 26, w - 8, 20, "#1b1512", r=4, sw=2.6, hl=False, sh=False)
        c.rect(x + 12, y + h - 20, w - 24, 8, dark(body, .45), r=2, sw=0, hl=False, sh=False)
    else:
        for i in range(5):
            c.line(x + 4, y + 12 + i * 15, x + w - 4, y + 12 + i * 15, dark(body, .3), w=2.4)
    return c


# ---------------------------------------------------------------- type icons
@tex("UI/HH/Venues/Type_Tavern", "single", (1, 1), ppc=128, grain=0, doc="Icon: tavern (ale mug)")
def t_tavern(view, cells, masked):
    c = _icon(cells, masked)
    col = P["amber"]
    c.pline([(92, 52), (110, 52), (118, 62), (118, 80), (108, 90), (92, 90)], OUT, 13)
    c.pline([(92, 52), (110, 52), (118, 62), (118, 80), (108, 90), (92, 90)], P["steel"], 7)
    c.rect(22, 40, 72, 76, col, r=10, sw=3.6)
    for x in (42, 64):
        c.line(x, 56, x, 104, light(col, .4), w=3.4)
    for x, y, r in ((30, 36, 13), (52, 30, 15), (76, 36, 13)):
        c.circle(x + 5, y, r, P["paper"], sw=3.2)
    return c


@tex("UI/HH/Venues/Type_Shop", "single", (1, 1), ppc=128, grain=0, doc="Icon: shop (striped awning)")
def t_shop(view, cells, masked):
    c = _icon(cells, masked)
    col = P["teal"]
    c.rect(20, 56, 88, 56, P["cream"], r=4, sw=3.6)
    c.rect(48, 70, 32, 42, col, r=3, sw=3)
    c.rect(26, 66, 18, 16, P["glass"], r=2, sw=2.6, hl=False)
    c.rect(86, 66, 16, 16, P["glass"], r=2, sw=2.6, hl=False)
    for i in range(6):
        x = 14 + i * 16.7
        c.poly([(x, 26), (x + 16.7, 26), (x + 20, 58), (x - 3, 58)], col if i % 2 == 0 else P["paper"], sw=3, hl=False)
    c.rect(12, 20, 104, 10, dark(col, .25), r=4, sw=3.2)
    return c


@tex("UI/HH/Venues/Type_Restaurant", "single", (1, 1), ppc=128, grain=0, doc="Icon: restaurant (cloche)")
def t_restaurant(view, cells, masked):
    c = _icon(cells, masked)
    col = P["red_l"]
    c.rect(12, 92, 104, 12, P["steel"], r=6, sw=3.4)
    c.path("M22 92 Q22 42 64 42 Q106 42 106 92 Z", col, sw=3.6)
    c.pline([(30, 86), (32, 66), (44, 54)], light(col, .5), 3.4)
    c.circle(64, 36, 8, P["steel"], sw=3)
    c.rect(54, 38, 20, 6, P["steel"], r=2, sw=0, hl=False, sh=False)
    return c


@tex("UI/HH/Venues/Type_Casino", "single", (1, 1), ppc=128, grain=0, doc="Icon: casino (die and chip)")
def t_casino(view, cells, masked):
    c = _icon(cells, masked)
    col = P["felt"]
    chip_top(c, 84, 84, 26, col, sw=3.4)
    chip_top(c, 84, 84, 26, col, sw=3.4)
    c.rect(18, 22, 66, 66, P["paper"], r=14, sw=3.8)
    for px, py in ((.28, .28), (.72, .28), (.5, .5), (.28, .72), (.72, .72)):
        c.circle(18 + px * 66, 22 + py * 66, 6.5, col, sw=2.4, hl=False, sh=False)
    return c


@tex("UI/HH/Venues/Type_Spa", "single", (1, 1), ppc=128, grain=0, doc="Icon: spa (lotus and drop)")
def t_spa(view, cells, masked):
    c = _icon(cells, masked)
    col = P["blue"]
    c.path("M64 112 Q14 100 8 52 Q52 60 64 112 Z", light(col, .25), sw=3.4)
    c.path("M64 112 Q114 100 120 52 Q76 60 64 112 Z", light(col, .25), sw=3.4)
    c.path("M64 112 Q28 96 34 36 Q64 54 64 112 Z", col, sw=3.4)
    c.path("M64 112 Q100 96 94 36 Q64 54 64 112 Z", col, sw=3.4)
    c.path("M64 112 Q40 76 64 30 Q88 76 64 112 Z", light(col, .1), sw=3.6)
    c.pline([(56, 90), (56, 62)], "#ffffff", 3)
    c.pline([(22, 120), (50, 120)], P["glass"], 4)
    c.pline([(78, 120), (106, 120)], P["glass"], 4)
    c.path("M64 2 Q74 14 74 20 A10 10 0 0 1 54 20 Q54 14 64 2 Z", P["glass"], sw=3)
    return c


@tex("UI/HH/Venues/Type_Lounge", "single", (1, 1), ppc=128, grain=0, doc="Icon: lounge (cocktail glass)")
def t_lounge(view, cells, masked):
    c = _icon(cells, masked)
    col = P["violet"]
    c.rect(54, 66, 20, 38, P["steel"], r=6, sw=3.2)
    c.rect(32, 104, 64, 12, P["steel"], r=6, sw=3.4)
    c.poly([(14, 22), (114, 22), (64, 72)], P["glass"], sw=3.8, hl=False)
    c.poly([(24, 32), (104, 32), (64, 70)], col, sw=0, hl=False)
    c.pline([(30, 28), (60, 56)], light(col, .5), 3.2)
    c.line(92, 10, 74, 40, OUT, w=7)
    c.line(92, 10, 74, 40, P["wood_l"], w=3)
    c.circle(96, 22, 9, P["leaf"], sw=3)
    return c
