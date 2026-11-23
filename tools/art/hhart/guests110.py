"""1.1.0 guests package: bed-use role icons, convert handling, dev group, night bell mote and bubbles.
All UI icons 128 px, flat, readable at 32-75 px: bold silhouettes, no text."""
import math
from .style import *
from .parts import *
from .registry import tex
from .inn import _icon, person
from .icons import tline, tpath, arrow


# ---------------------------------------------------------------- bed-use role icons: one bed, one corner badge per role
def _bed(c, x=0, y=0):
    """A small bed, 3/4 view from the side (same drawing as the guest bed icon, without the key)."""
    c.rect(10 + x, 40 + y, 10, 60, P["wood"], r=4)
    c.rect(14 + x, 34 + y, 88, 50, P["cream"], r=10)
    c.rect(30 + x, 48 + y, 70, 36, P["amber"], r=8)
    c.rect(20 + x, 42 + y, 24, 20, P["paper"], r=8, sw=3)
    c.rect(94 + x, 68 + y, 10, 32, P["wood"], r=4)
    c.rect(12 + x, 80 + y, 94, 12, P["wood_d"], r=5, sw=3)
    for xx in (40, 62, 84):
        c.line(xx + x, 58 + y, xx + x, 78 + y, dark(P["amber"], .3), w=2.4)


def _badge(c, col, cx=98, cy=96, r=28):
    c.circle(cx, cy, r, col, sw=4.2)
    c.circle(cx, cy, r - 7, light(col, .08), sw=0, hl=False, sh=False)


@tex("UI/HH/Inn/BedUse_Colonist", "single", (1, 1), ppc=128, grain=0, doc="Bed use icon: colonist bed (bed + little house)")
def bu_colonist(view, cells, masked):
    c = _icon(cells, masked)
    _bed(c, x=-4, y=-8)
    _badge(c, P["blue"])
    # house: roof triangle + body + door
    c.poly([(80, 96), (98, 78), (116, 96)], P["red_l"], sw=3.2)
    c.rect(84, 94, 28, 20, P["paper"], r=2, sw=3, hl=False)
    c.rect(94, 100, 8, 14, P["wood_d"], r=1, sw=2, hl=False, sh=False)
    return c


@tex("UI/HH/Inn/BedUse_Guest", "single", (1, 1), ppc=128, grain=0, doc="Bed use icon: guest bed (bed + key)")
def bu_guest(view, cells, masked):
    c = _icon(cells, masked)
    _bed(c, x=-4, y=-8)
    _badge(c, P["amber"])
    c.circle(88, 90, 12, P["gold"], sw=3.2)
    c.circle(88, 90, 4.4, "#2a1c16", sw=0, hl=False, sh=False)
    tline(c, [(98, 96), (116, 112)], 6, P["gold"])
    c.line(108, 106, 114, 100, OUT, w=3.6)
    c.line(113, 111, 119, 105, OUT, w=3.6)
    return c


@tex("UI/HH/Inn/BedUse_Patient", "single", (1, 1), ppc=128, grain=0, doc="Bed use icon: patient bed (bed + medical cross)")
def bu_patient(view, cells, masked):
    c = _icon(cells, masked)
    _bed(c, x=-4, y=-8)
    _badge(c, P["red_l"])
    c.rect(90, 80, 16, 32, P["paper"], r=3, sw=3, hl=False)
    c.rect(82, 88, 32, 16, P["paper"], r=3, sw=3, hl=False)
    c.rect(91, 82, 14, 28, P["paper"], r=2, sw=0, hl=False, sh=False)
    return c


@tex("UI/HH/Inn/BedUse_Surgery", "single", (1, 1), ppc=128, grain=0, doc="Bed use icon: surgery bed (bed + scalpel)")
def bu_surgery(view, cells, masked):
    c = _icon(cells, masked)
    _bed(c, x=-4, y=-8)
    _badge(c, P["teal"])
    # scalpel: handle + blade, tilted
    tline(c, [(84, 112), (104, 90)], 9, P["steel"])
    c.poly([(104, 90), (118, 74), (112, 94)], P["silver"], sw=3, hl=False)
    c.line(86, 110, 98, 97, P["iron"], w=2.6)
    return c


@tex("UI/HH/Inn/DevTools", "single", (1, 1), ppc=128, grain=0, doc="Command icon: dev tools group (gear with a small wrench)")
def dev_tools(view, cells, masked):
    c = _icon(cells, masked)
    # gear
    cx, cy = 58, 64
    pts = []
    for i in range(16):
        a = i * math.pi / 8
        r = 50 if i % 2 == 0 else 38
        pts.append((cx + math.cos(a - .12) * r, cy + math.sin(a - .12) * r))
        pts.append((cx + math.cos(a + .12) * r, cy + math.sin(a + .12) * r))
    c.poly(pts, P["steel"], sw=3.6)
    c.circle(cx, cy, 24, P["dsteel"], sw=3.4)
    c.circle(cx, cy, 11, P["steel"], sw=3, hl=False)
    # wrench
    tline(c, [(84, 112), (114, 82)], 9, P["amber"])
    c.circle(116, 80, 12, P["amber"], sw=3.4)
    c.circle(120, 76, 6, P["dsteel"], sw=0, hl=False, sh=False)
    return c


# ---------------------------------------------------------------- convert handling: an open book with a star, arrow to a person
@tex("UI/HH/Inn/Handling_Convert", "single", (1, 1), ppc=128, grain=0, doc="Icon: convert handling (book of beliefs, radiant star, arrow to a person)")
def icon_convert(view, cells, masked):
    c = _icon(cells, masked)
    # open book
    c.poly([(10, 66), (44, 56), (58, 62), (58, 112), (44, 106), (10, 114)], P["paper"], sw=3.6)
    c.poly([(58, 62), (72, 56), (106, 66), (106, 114), (72, 106), (58, 112)], P["cream"], sw=3.6)
    c.line(58, 62, 58, 112, OUT, w=3.4)
    for i in range(3):
        c.line(18, 72 + i * 11, 48, 68 + i * 11, "#a79f89", w=2)
        c.line(68, 68 + i * 11, 98, 72 + i * 11, "#a79f89", w=2)
    # radiant star over the book
    for k in range(8):
        a = -math.pi / 2 + k * math.pi / 4
        c.line(58 + math.cos(a) * 20, 32 + math.sin(a) * 20, 58 + math.cos(a) * 29, 32 + math.sin(a) * 29, P["gold"], w=4)
    star_pts = []
    for i in range(10):
        a = -math.pi / 2 + i * math.pi / 5
        rr = 16 if i % 2 == 0 else 7
        star_pts.append((58 + math.cos(a) * rr, 32 + math.sin(a) * rr))
    c.poly(star_pts, P["gold"], sw=3, hl=False)
    # arrow to a person on the right
    arrow(c, 82, 40, 104, 40, P["emerald"], w=7, head=14)
    c.circle(116, 40, 9, P["cream"], sw=3)
    return c


# ---------------------------------------------------------------- night bell: mote, ring arcs, "heard it" bubble icon
def _bell_body(c, cx, top, w, h):
    d = "M%s %s Q%s %s %s %s Q%s %s %s %s Q%s %s %s %s Q%s %s %s %s Z" % (
        f2(cx - w * .5), f2(top + h), f2(cx - w * .5), f2(top + h * .15), f2(cx), f2(top),
        f2(cx + w * .5), f2(top + h * .15), f2(cx + w * .5), f2(top + h),
        f2(cx + w * .5), f2(top + h), f2(cx + w * .5), f2(top + h),
        f2(cx + w * .5), f2(top + h), f2(cx - w * .5), f2(top + h))
    return d


@tex("Things/HH/Venues/Bell_Mote", "single", (1, 1), ppc=96, grain=0, doc="Mote: brass service bell with a clapper, swings while it rings (96 px)")
def bell_mote(view, cells, masked):
    c = _icon(cells, masked)
    # handle ring + bell dome + flared lip + clapper
    c.circle(64, 20, 8, P["gold"], sw=3.6, hl=False)
    c.circle(64, 20, 3.2, "#2a1c16", sw=0, hl=False, sh=False)
    d = "M20 96 Q22 30 64 28 Q106 30 108 96 Q116 98 116 106 L12 106 Q12 98 20 96 Z"
    c.path(d, P["gold"], sw=4.2)
    c.pline([(34, 90), (36, 56), (50, 40)], light(P["gold"], .55), 4)
    c.rect(14, 98, 100, 10, dark(P["gold"], .12), r=5, sw=3.4, hl=False)
    c.circle(64, 116, 8, P["copper"], sw=3.4, hl=False)
    return c


@tex("Things/HH/Venues/RingArc", "single", (1, 1), ppc=96, grain=0, doc="Mote: two sound arcs that spread from the ringing bell (96 px)")
def ring_arc(view, cells, masked):
    c = _icon(cells, masked)
    for r, w in ((30, 7), (50, 6)):
        pts = arc_pts(64, 70, r, r, 200, 340, 16)
        tline(c, pts, w, P["paper"])
    return c


@tex("UI/HH/Venues/HeardBell", "single", (1, 1), ppc=128, grain=0, doc="Thought bubble icon: heard the night bell (small bell with sound arcs)")
def heard_bell(view, cells, masked):
    c = _icon(cells, masked)
    d = "M30 92 Q32 34 64 32 Q96 34 98 92 Q104 94 104 102 L24 102 Q24 94 30 92 Z"
    c.path(d, P["gold"], sw=4.2)
    c.pline([(42, 86), (44, 58), (54, 46)], light(P["gold"], .55), 3.6)
    c.circle(64, 22, 7, P["gold"], sw=3.4, hl=False)
    c.circle(64, 112, 7, P["copper"], sw=3.2, hl=False)
    for r in (46, 58):
        for a0, a1 in ((-40, 40), (140, 220)):
            tline(c, arc_pts(64, 68, r, r, a0, a1, 10), 5, P["paper"])
    return c
