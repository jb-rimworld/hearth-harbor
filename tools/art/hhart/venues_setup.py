"""1.1.0 venue icons: setup guide (F12), work side (F13), honor box (F15), name dice (F33).
All 128 px UI icons (ppc 128), flat, readable at 24-64 px: bold silhouettes, warm outline, no text."""
from .style import *
from .parts import *
from .registry import tex
from .inn import _icon
from .icons import tline, arrow


def _badge(c, col):
    c.circle(64, 64, 54, col, sw=4.4)
    c.circle(64, 64, 44, light(col, .12), sw=0, hl=False, sh=False)


# ------------------------------------------------------------------ checklist states (drawn at 24 px in the setup tab)
@tex("UI/HH/Venues/Setup_Ok", "single", (1, 1), ppc=128, grain=0, doc="Setup checklist: done (green badge with a tick)")
def setup_ok(view, cells, masked):
    c = _icon(cells, masked)
    _badge(c, P["emerald"])
    tline(c, [(38, 66), (56, 84), (92, 44)], 13, P["cream2"])
    return c


@tex("UI/HH/Venues/Setup_Missing", "single", (1, 1), ppc=128, grain=0, doc="Setup checklist: required and missing (red badge with a cross)")
def setup_missing(view, cells, masked):
    c = _icon(cells, masked)
    _badge(c, P["red_l"])
    tline(c, [(44, 44), (84, 84)], 13, P["cream2"])
    tline(c, [(84, 44), (44, 84)], 13, P["cream2"])
    return c


@tex("UI/HH/Venues/Setup_Warn", "single", (1, 1), ppc=128, grain=0, doc="Setup checklist: open point / recommended (amber badge with an exclamation mark)")
def setup_warn(view, cells, masked):
    c = _icon(cells, masked)
    _badge(c, P["amber"])
    tline(c, [(64, 36), (64, 72)], 13, P["cream2"])
    c.circle(64, 92, 8, P["cream2"], sw=3.4, hl=False, sh=False)
    return c


@tex("UI/HH/Venues/Setup_Show", "single", (1, 1), ppc=128, grain=0, doc="Setup checklist: show what is there (eye)")
def setup_show(view, cells, masked):
    c = _icon(cells, masked)
    c.path("M 8 64 Q 64 6 120 64 Q 64 122 8 64 Z", P["paper"], sw=4.4)
    c.circle(64, 64, 22, P["blue"], sw=3.6)
    c.circle(64, 64, 10, P["dsteel"], sw=0, hl=False, sh=False)
    c.dot(57, 57, 5, "#ffffff")
    return c


# ------------------------------------------------------------------ commands
@tex("UI/HH/Venues/Cmd_Setup", "single", (1, 1), ppc=128, grain=0, doc="Command icon: venue setup guide (clipboard with ticks and a hammer)")
def cmd_setup(view, cells, masked):
    c = _icon(cells, masked)
    # clipboard
    c.rect(16, 18, 74, 98, P["wood"], r=8, sw=4.2)
    c.rect(24, 30, 58, 80, P["paper"], r=4, sw=3, hl=False)
    c.rect(38, 12, 30, 14, P["steel"], r=4, sw=3.2)
    for i, ok in enumerate((True, True, False)):
        y = 46 + i * 22
        if ok:
            tline(c, [(30, y), (35, y + 5), (44, y - 5)], 4.5, P["emerald"])
        else:
            c.rect(29, y - 6, 13, 13, P["cream"], r=2, sw=2.6, hl=False, sh=False)
        c.line(50, y, 74, y, "#a79f89", w=4)
    # hammer
    c.poly([(80, 116), (72, 108), (104, 64), (114, 72)], P["wood_l"], sw=3.4, hl=False)
    c.poly([(92, 50), (118, 70), (124, 62), (100, 42)], P["iron"], sw=3.4, hl=True)
    return c


@tex("UI/HH/Venues/Cmd_Honor", "single", (1, 1), ppc=128, grain=0, doc="Command icon: honor system (wooden honesty box with a coin slot and a falling coin)")
def cmd_honor(view, cells, masked):
    c = _icon(cells, masked)
    # box
    c.rect(18, 56, 92, 60, P["wood"], r=6, sw=4.2)
    c.rect(18, 56, 92, 16, P["wood_l"], r=5, sw=3.4)
    c.rect(48, 60, 32, 7, P["coal"], r=3, sw=0, hl=False, sh=False)
    for x in (40, 64, 88):
        c.line(x, 76, x, 110, dark(P["wood"], .35), w=3)
    # heart plaque (trust)
    hx, hy = 64, 94
    c.path("M %s %s C %s %s %s %s %s %s C %s %s %s %s %s %s Z" % (
        f2(hx), f2(hy + 12), f2(hx - 18), f2(hy), f2(hx - 12), f2(hy - 14), f2(hx), f2(hy - 6),
        f2(hx + 12), f2(hy - 14), f2(hx + 18), f2(hy), f2(hx), f2(hy + 12)), P["red_l"], sw=3)
    # falling coin
    coin(c, 64, 30, 16)
    c.line(44, 14, 44, 30, P["gold"], w=3)
    c.line(84, 12, 84, 26, P["gold"], w=3)
    return c


@tex("UI/HH/Venues/Cmd_NameDice", "single", (1, 1), ppc=128, grain=0, doc="Command icon: suggest a business name (two dice)")
def cmd_namedice(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(14, 46, 58, 58, P["paper"], r=12, sw=4.2)
    for (x, y) in ((28, 60), (43, 75), (58, 90)):
        c.dot(x, y, 6, P["dsteel"])
    c.poly([(70, 22), (114, 34), (104, 78), (60, 66)], P["red_l"], sw=4.2)
    for (x, y) in ((78, 38), (96, 44), (74, 54), (92, 60)):
        c.dot(x, y, 5.5, P["cream2"])
    return c


@tex("UI/HH/Core/Cmd_FlipSide", "single", (1, 1), ppc=128, grain=0, doc="Command icon: switch work side (counter seen from above with a curved two-way arrow)")
def cmd_flipside(view, cells, masked):
    c = _icon(cells, masked)
    # counter top
    c.rect(18, 50, 92, 28, P["wood_l"], r=5, sw=4.2)
    c.rect(18, 70, 92, 8, P["wood_d"], r=3, sw=0, hl=False, sh=False)
    # worker spot below (old side, faded) and above (new side)
    c.circle(64, 100, 10, P["steel"], sw=3, hl=False)
    c.circle(64, 28, 10, P["amber"], sw=3.2)
    # curved arrow from bottom right to top right
    pts = arc_pts(64, 64, 52, 46, 40, -40, 16)
    tline(c, pts[:-1], 7, P["emerald"])
    ex, ey = pts[-1]
    c.poly([(ex - 10, ey + 4), (ex + 12, ey - 4), (ex, ey + 16)], P["emerald"], sw=3, hl=False)
    return c
