"""Inn: front desk, guest bed / handling icons, wallet, travel bag."""
from .style import *
from .parts import *
from .registry import tex
from .casino import canvas_for_


@tex("Things/HH/Inn/FrontDesk", "multi", (2, 1), masked=True, doc="Reception desk: ledger, bell, key rack, 2x1, stuffable top")
def frontdesk(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    f = slab(c, view, P["pale"], apron=18, m="a")
    # key rack along the back edge
    c.rect(*f.r(8, 4, f.w - 16, 22), P["wood_d"], r=4, sw=2.6, m=None)
    n = 6
    for i in range(n):
        x, y = f.pt(24 + i * (f.w - 48) / (n - 1), 16)
        c.circle(x, y, 3, P["steel"], sw=1.4, hl=False, sh=False)
        if i != 3:
            c.line(x, y + 2, x, y + 9, P["steel"], w=2)
            c.circle(x, y + 12, 3.8, P["gold"], sw=1.6, hl=False, sh=False)
    # open ledger
    lx, ly, lw, lh = f.r(f.w * .16, f.h * .38, f.w * .3, f.h * .5)
    c.rect(lx, ly, lw, lh, P["red_d"], r=4, sw=2.8, m=None)
    c.rect(lx + 4, ly + 4, lw / 2 - 5, lh - 8, P["paper"], r=2, sw=1.6, hl=False, sh=False, m=None)
    c.rect(lx + lw / 2 + 1, ly + 4, lw / 2 - 5, lh - 8, P["paper"], r=2, sw=1.6, hl=False, sh=False, m=None)
    for k in range(4):
        c.line(lx + 8, ly + 11 + k * (lh - 24) / 3, lx + lw / 2 - 6, ly + 11 + k * (lh - 24) / 3, "#a79f89", w=1.4)
        c.line(lx + lw / 2 + 5, ly + 11 + k * (lh - 24) / 3, lx + lw - 9, ly + 11 + k * (lh - 24) / 3, "#a79f89", w=1.4)
    # bell
    bx, by = f.pt(f.w * .6, f.h * .64)
    c.circle(bx, by, 14, P["gold"], sw=2.8)
    c.circle(bx - 3, by - 3, 4, light(P["gold"], .5), sw=0, hl=False, sh=False)
    c.circle(bx, by, 4, P["red_l"], sw=1.6, hl=False, sh=False)
    # inkpot + quill
    ix, iy = f.pt(f.w * .78, f.h * .7)
    c.circle(ix, iy, 9, P["dsteel"], sw=2.4)
    c.line(ix, iy, *f.pt(f.w * .9, f.h * .42), OUT, w=5)
    c.line(ix, iy, *f.pt(f.w * .9, f.h * .42), P["paper"], w=2.4)
    # small silver dish
    c.circle(*f.pt(f.w * .5, f.h * .38), 7, P["silver"], sw=2)
    return c


# ---------------------------------------------------------------- UI glyphs
def _icon(cells, masked):
    from .style import Canvas
    return Canvas(1, 1, masked)


def person(c, x, y, s, col, head=None, sw=3):
    """simple pawn silhouette: head circle + shoulders. (x, y) = centre of the head, s = head radius"""
    c.rect(x - s * 1.5, y + s * 1.2, s * 3, s * 3.0, col, r=s * 1.1, sw=sw)
    c.circle(x, y, s, head or P["cream"], sw=sw)


@tex("UI/HH/Inn/GuestBed", "single", (1, 1), ppc=128, grain=0, doc="Icon: guest bed button")
def icon_bed(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(14, 44, 10, 62, P["wood"], r=4)
    c.rect(18, 38, 96, 52, P["cream"], r=10)
    c.rect(34, 52, 76, 38, P["amber"], r=8)
    c.rect(24, 46, 24, 20, P["paper"], r=8, sw=3)
    c.rect(104, 74, 10, 32, P["wood"], r=4)
    c.rect(16, 86, 100, 12, P["wood_d"], r=5, sw=3)
    for x in (44, 66, 88):
        c.line(x, 62, x, 84, dark(P["amber"], .3), w=2.4)
    key(c, 96, 40, 30)
    return c


@tex("UI/HH/Inn/Handling_Basics", "single", (1, 1), ppc=128, grain=0, doc="Icon: basics handling (reception bell)")
def icon_basics(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(22, 90, 84, 16, P["wood_d"], r=6)
    d = "M30 90 Q30 36 64 36 Q98 36 98 90 Z"
    c.path(d, P["amber"], sw=3.6)
    c.pline([(40, 84), (42, 60), (54, 48)], light(P["amber"], .5), 3.2)
    c.rect(56, 24, 16, 12, P["steel"], r=5, sw=3)
    c.rect(18, 86, 92, 10, P["steel"], r=5, sw=3)
    return c


@tex("UI/HH/Inn/Handling_Accompany", "single", (1, 1), ppc=128, grain=0, doc="Icon: accompany handling (two walking figures)")
def icon_accompany(view, cells, masked):
    c = _icon(cells, masked)
    person(c, 44, 44, 13, P["amber"])
    person(c, 84, 52, 11, P["blue"])
    c.pline([(20, 108), (50, 108)], OUT, 6)
    c.pline([(20, 108), (50, 108)], P["paper"], 2.5)
    c.pline([(62, 108), (108, 108)], OUT, 6)
    c.pline([(62, 108), (108, 108)], P["paper"], 2.5)
    return c


@tex("UI/HH/Inn/Handling_Recruit", "single", (1, 1), ppc=128, grain=0, doc="Icon: recruit handling (person with plus)")
def icon_recruit(view, cells, masked):
    c = _icon(cells, masked)
    person(c, 50, 42, 16, P["amber"])
    c.circle(92, 88, 24, P["emerald"], sw=3.6)
    c.rect(81, 84, 22, 8, P["paper"], r=2, sw=0, hl=False, sh=False)
    c.rect(88, 77, 8, 22, P["paper"], r=2, sw=0, hl=False, sh=False)
    return c


# ---------------------------------------------------------------- items
@tex("Things/HH/Inn/Wallet", "single", (1, 1), doc="Item: coin purse / wallet, 64 px")
def wallet(view, cells, masked):
    c = _icon(cells, masked)
    lea = "#8c5a38"
    c.rect(22, 34, 84, 64, lea, r=12, sw=3.6)
    c.path("M22 58 Q22 34 46 34 H82 Q106 34 106 58 V62 H22 Z", light(lea, .12), sw=3.6)
    c.pline([(30, 70), (98, 70)], dark(lea, .45), 1.8, extra='stroke-dasharray="5 4"')
    c.circle(64, 62, 9, P["gold"], sw=2.8)
    coin(c, 90, 30, 11)
    return c


@tex("Things/HH/Inn/TravelBag", "single", (1, 1), doc="Item: guest travel bag, 64 px")
def travelbag(view, cells, masked):
    c = _icon(cells, masked)
    col = P["teal"]
    c.pline([(40, 44), (44, 26), (84, 26), (88, 44)], OUT, 11)
    c.pline([(40, 44), (44, 26), (84, 26), (88, 44)], P["wood_d"], 6)
    c.rect(16, 42, 96, 62, col, r=18, sw=3.8)
    c.rect(24, 62, 80, 8, dark(col, .25), r=3, sw=0, hl=False, sh=False)
    c.rect(26, 74, 30, 22, light(col, .1), r=6, sw=2.6)
    c.rect(56, 36, 16, 20, P["wood_d"], r=4, sw=2.6)
    c.rect(60, 44, 8, 8, P["gold"], r=2, sw=1.6, hl=False, sh=False)
    return c
