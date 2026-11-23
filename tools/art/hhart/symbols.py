"""Slot reel symbols for "The Lucky Thrumbo" window and the colony slot machines.
One 128 px UI icon per SlotPaytable.SymbolNames entry (cherry, lemon, plum, bell, bar, seven, muffalo), path UI/HH/Casino/Symbols/<id>.
Bold silhouettes, readable at 48-64 px on a cream card. Light edge top left, shadow edge bottom right, no text except the BAR letters (drawn as strokes)."""
from .style import *
from .parts import *
from .registry import tex
from .inn import _icon


def stroke(c, d, w, col, ow=5):
    c.raw('<path d="%s" fill="none" stroke="%s" stroke-width="%s" stroke-linecap="round" stroke-linejoin="round"/>' % (d, OUT, f2(w + ow)))
    c.raw('<path d="%s" fill="none" stroke="%s" stroke-width="%s" stroke-linecap="round" stroke-linejoin="round"/>' % (d, col, f2(w)))


def arcline(c, d, col, w=3):
    c.raw('<path d="%s" fill="none" stroke="%s" stroke-width="%s" stroke-linecap="round"/>' % (d, col, f2(w)))


def leaf(c, x, y, sx=1.0, rot=0, col=None):
    col = col or P["leaf"]
    c.raw('<g transform="translate(%s %s) rotate(%s) scale(%s 1)">' % (f2(x), f2(y), f2(rot), f2(sx)))
    c.path("M0 0 Q14 -26 44 -22 Q40 6 0 0 Z", col, sw=4)
    c.raw('<path d="M5 -3 Q22 -10 38 -17" fill="none" stroke="%s" stroke-width="2.4" stroke-linecap="round"/></g>' % light(col, .3))


def fruit(c, cx, cy, r, col):
    c.circle(cx, cy, r, col, sw=4.4)
    c.circle(cx - r * .38, cy - r * .4, r * .2, "#fff1e0", sw=0, hl=False, sh=False)


@tex("UI/HH/Casino/Symbols/cherry", "single", (1, 1), ppc=128, grain=0, doc="Slot symbol: cherries")
def sym_cherry(view, cells, masked):
    c = _icon(cells, masked)
    stroke(c, "M38 86 Q44 40 74 14", 6, P["leaf"], 4)
    stroke(c, "M92 90 Q90 40 74 14", 6, P["leaf"], 4)
    leaf(c, 76, 18, 1.0, -10)
    fruit(c, 38, 90, 27, P["cherry"])
    fruit(c, 92, 94, 27, P["red_l"])
    return c


@tex("UI/HH/Casino/Symbols/lemon", "single", (1, 1), ppc=128, grain=0, doc="Slot symbol: lemon")
def sym_lemon(view, cells, masked):
    c = _icon(cells, masked)
    body = "M12 74 Q8 62 18 60 Q32 22 72 24 Q108 26 112 60 Q124 60 116 78 Q112 108 70 108 Q22 108 12 74 Z"
    # tilted lemon: pointed nubs left and right
    c.raw('<g transform="rotate(-18 64 66)">')
    c.path("M6 66 Q14 56 24 56 Q34 30 64 30 Q96 30 106 56 Q116 58 122 66 Q116 74 106 78 Q96 102 64 102 Q34 102 24 78 Q14 76 6 66 Z", "#f1d23c", sw=4.4)
    c.path("M30 52 Q40 38 60 38", "none", sw=0)
    arcline(c, "M32 56 Q42 42 62 40", "#fff3b0", 4)
    arcline(c, "M96 86 Q84 98 62 98", "#c99a1e", 4)
    c.raw('</g>')
    leaf(c, 74, 34, 1.0, -50)
    return c


@tex("UI/HH/Casino/Symbols/plum", "single", (1, 1), ppc=128, grain=0, doc="Slot symbol: plum")
def sym_plum(view, cells, masked):
    c = _icon(cells, masked)
    stroke(c, "M66 30 Q64 18 72 8", 6, P["wood"], 3)
    leaf(c, 70, 20, 1.0, -20)
    col = "#7b3f9e"
    c.path("M64 28 Q108 24 112 72 Q112 112 64 116 Q16 112 16 72 Q20 24 64 28 Z", col, sw=4.4)
    arcline(c, "M64 32 Q56 66 64 112", dark(col, .45), 4)
    arcline(c, "M30 52 Q36 40 48 36", light(col, .45), 4.5)
    c.circle(38, 62, 6, light(col, .5), sw=0, hl=False, sh=False)
    arcline(c, "M96 86 Q90 102 74 108", dark(col, .4), 4)
    return c


@tex("UI/HH/Casino/Symbols/bell", "single", (1, 1), ppc=128, grain=0, doc="Slot symbol: golden bell")
def sym_bell(view, cells, masked):
    c = _icon(cells, masked)
    g = P["gold"]
    c.circle(64, 18, 9, g, sw=4)
    c.path("M64 24 Q96 26 100 66 Q102 90 118 102 L118 106 Q118 110 114 110 L14 110 Q10 110 10 106 L10 102 Q26 90 28 66 Q32 26 64 24 Z", g, sw=4.4)
    arcline(c, "M44 40 Q34 52 34 74", "#fff0b0", 5)
    arcline(c, "M90 52 Q92 70 100 86", dark(g, .3), 4)
    arcline(c, "M20 102 H108", dark(g, .3), 4)
    c.circle(64, 112, 12, dark(g, .1), sw=4)
    c.circle(60, 108, 3.5, "#fff0b0", sw=0, hl=False, sh=False)
    return c


@tex("UI/HH/Casino/Symbols/bar", "single", (1, 1), ppc=128, grain=0, doc="Slot symbol: BAR plate (letters as strokes)")
def sym_bar(view, cells, masked):
    c = _icon(cells, masked)
    pl = P["navy"]
    c.rect(6, 30, 116, 68, pl, r=10, sw=4.4)
    c.rect(13, 37, 102, 54, dark(pl, .15), r=6, sw=2.2, hl=False, sh=False)
    col = "#f4ecd4"
    w = 9
    # B
    stroke(c, "M20 50 H32 Q42 50 42 58 Q42 66 32 66 H20 M20 66 H34 Q45 66 45 75 Q45 84 34 84 H20 Z", w, col, 4)
    # A
    stroke(c, "M52 84 L64 48 L76 84 M57 71 H71", w, col, 4)
    # R
    stroke(c, "M84 84 V50 H96 Q106 50 106 59 Q106 68 96 68 H84 M96 68 L108 84", w, col, 4)
    return c


@tex("UI/HH/Casino/Symbols/seven", "single", (1, 1), ppc=128, grain=0, doc="Slot symbol: red seven")
def sym_seven(view, cells, masked):
    c = _icon(cells, masked)
    red = "#d63a2f"
    c.poly([(18, 14), (112, 14), (112, 34), (62, 116), (36, 116), (80, 36), (18, 36)], red, sw=5, hl=False, sh=False)
    arcline(c, "M24 22 H104", "#ff9f8a", 4)
    arcline(c, "M24 22 V32", "#ff9f8a", 4)
    arcline(c, "M104 36 L58 110", dark(red, .4), 4)
    arcline(c, "M72 40 L40 108", "#ff8a78", 3.5)
    star(c, 98, 100, 12) if False else None
    return c


@tex("UI/HH/Casino/Symbols/muffalo", "single", (1, 1), ppc=128, grain=0, doc="Slot symbol: muffalo head")
def sym_muffalo(view, cells, masked):
    c = _icon(cells, masked)
    fur, fur_d, horn = "#8a5e3c", "#5f3f27", "#efe3c2"
    # horns (behind head)
    c.path("M34 52 Q6 50 8 22 Q14 38 36 36 Z", horn, sw=4)
    c.path("M94 52 Q122 50 120 22 Q114 38 92 36 Z", horn, sw=4)
    # ears
    c.ellipse(22, 62, 15, 9, fur_d, sw=4)
    c.ellipse(106, 62, 15, 9, fur_d, sw=4)
    # head
    c.path("M64 14 Q104 14 104 58 Q106 90 90 110 Q76 120 64 120 Q52 120 38 110 Q22 90 24 58 Q24 14 64 14 Z", fur, sw=4.4)
    # shaggy forelock
    c.path("M30 40 Q40 16 64 18 Q88 16 98 40 Q88 34 80 42 Q72 32 64 44 Q56 32 48 42 Q40 34 30 40 Z", fur_d, sw=3.5)
    # muzzle
    c.path("M38 84 Q38 66 64 66 Q90 66 90 84 Q92 108 64 112 Q36 108 38 84 Z", "#d9b88a", sw=3.5)
    c.ellipse(54, 90, 5, 7, OUT, sw=0, hl=False, sh=False)
    c.ellipse(74, 90, 5, 7, OUT, sw=0, hl=False, sh=False)
    arcline(c, "M54 104 Q64 110 74 104", OUT, 3)
    # eyes
    for x in (44, 84):
        c.circle(x, 58, 8, "#fff7e6", sw=3, hl=False, sh=False)
        c.circle(x + (1 if x > 64 else -1), 59, 4, OUT, sw=0, hl=False, sh=False)
    arcline(c, "M28 56 Q30 42 38 34", light(fur, .25), 3)
    return c
