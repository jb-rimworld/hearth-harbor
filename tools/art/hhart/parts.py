"""Reusable small parts (chips, coins, cards, dice, bottles, books ...). All take design-unit coordinates in SCREEN space."""
import math
from .style import *


def chip_top(c, x, y, r, color, sw=2.2):
    c.circle(x, y, r, color, sw=sw, hl=False, sh=False)
    c.circle(x, y, r * 0.62, light(color, 0.35), sw=1.2, hl=False, sh=False)
    c.circle(x, y, r * 0.42, color, sw=0, hl=False, sh=False)
    for i in range(8):
        a = i * math.pi / 4
        x1, y1 = x + math.cos(a) * r * 0.74, y + math.sin(a) * r * 0.74
        x2, y2 = x + math.cos(a) * r * 0.97, y + math.sin(a) * r * 0.97
        c.line(x1, y1, x2, y2, "#f4ecd4", w=max(1.4, r * 0.16))


def chip_stack(c, x, y, rx, color, n=4, thick=None, sw=2.4, ry=None):
    """3/4 view stack of chips; (x, y) = centre of the bottom chip's top face."""
    ry = ry or rx * 0.55
    t = thick or rx * 0.32
    for i in range(n):
        yy = y - i * t
        c.rect(x - rx, yy, 2 * rx, t, dark(color, 0.12), r=1, sw=sw, hl=False, sh=False)
        c.ellipse(x, yy + t, rx, ry, dark(color, 0.2), sw=sw, hl=False, sh=False)
    # re-draw bodies from bottom to top so upper chips overlap lower
    for i in range(n):
        yy = y - i * t
        c.ellipse(x, yy + t, rx, ry, dark(color, 0.12), sw=sw, hl=False, sh=False)
        c.rect(x - rx + sw / 2, yy, 2 * rx - sw, t, color, r=0, sw=0, hl=False, sh=False)
        c.line(x - rx, yy, x - rx, yy + t, OUT, w=sw)
        c.line(x + rx, yy, x + rx, yy + t, OUT, w=sw)
        for k in range(-1, 2):
            c.line(x + k * rx * 0.55, yy + 1, x + k * rx * 0.55, yy + t - 1, "#f4ecd4", w=max(1.2, rx * 0.12), cap="butt")
        c.ellipse(x, yy, rx, ry, light(color, 0.1), sw=sw, hl=False, sh=False)
    c.ellipse(x, y - (n - 1) * t, rx * 0.55, ry * 0.55, color, sw=1.2, hl=False, sh=False)


def coin(c, x, y, r, color=None, sw=2.6):
    color = color or P["gold"]
    c.circle(x, y, r, color, sw=sw, hl=True)
    c.circle(x, y, r * 0.62, color, sw=1.4, hl=False, sh=False)
    c.line(x - r * 0.12, y - r * 0.32, x - r * 0.12, y + r * 0.32, dark(color, 0.35), w=max(1.2, r * 0.14))


def coin_stack(c, x, y, r, n=4, color=None, sw=2.4):
    color = color or P["gold"]
    ry = r * 0.5
    t = r * 0.34
    for i in range(n):
        yy = y - i * t
        c.rect(x - r, yy, 2 * r, t, dark(color, 0.15), r=1, sw=sw, hl=False, sh=False)
        c.ellipse(x, yy + t, r, ry, dark(color, 0.15), sw=sw, hl=False, sh=False)
        c.rect(x - r + sw / 2, yy, 2 * r - sw, t, color, r=0, sw=0, hl=False, sh=False)
        c.line(x - r, yy, x - r, yy + t, OUT, w=sw)
        c.line(x + r, yy, x + r, yy + t, OUT, w=sw)
        c.ellipse(x, yy, r, ry, light(color, 0.08), sw=sw, hl=False, sh=False)


def card(c, x, y, w, h, red=True, back=False, sw=2):
    c.rect(x, y, w, h, P["paper"] if not back else P["red"], r=3, sw=sw, hl=False, sh=False)
    if back:
        c.rect(x + w * .16, y + h * .14, w * .68, h * .72, P["red_d"], r=2, sw=1, hl=False, sh=False)
    else:
        col = P["red_l"] if red else P["dsteel"]
        c.circle(x + w / 2, y + h / 2, min(w, h) * 0.2, col, sw=0, hl=False, sh=False)


def die(c, x, y, s, n=5):
    c.rect(x, y, s, s, P["paper"], r=s * 0.2, sw=2.4)
    pips = {1: [(.5, .5)], 2: [(.28, .28), (.72, .72)], 3: [(.28, .28), (.5, .5), (.72, .72)], 4: [(.28, .28), (.72, .28), (.28, .72), (.72, .72)],
            5: [(.28, .28), (.72, .28), (.5, .5), (.28, .72), (.72, .72)], 6: [(.28, .25), (.72, .25), (.28, .5), (.72, .5), (.28, .75), (.72, .75)]}[n]
    for px, py in pips:
        c.circle(x + px * s, y + py * s, s * 0.085, P["dsteel"], sw=0, hl=False, sh=False)


def shield_pts(x, y, w, h):
    return ([(x, y + h * 0.08), (x + w * .5, y), (x + w, y + h * 0.08), (x + w, y + h * 0.5)] + arc_pts(x + w * .5, y + h * 0.5, w * .5, h * .5, 0, 180, 14)[1:-1] + [(x, y + h * 0.5)])


def shield(c, x, y, w, h, fill, sw=3.2):
    c.poly(shield_pts(x, y, w, h), fill, sw=sw)
    return


def bottle(c, x, y, w, h, color, label=None):
    c.rect(x, y + h * 0.3, w, h * 0.7, color, r=w * 0.25, sw=2.2)
    c.rect(x + w * 0.3, y, w * 0.4, h * 0.38, color, r=2, sw=2.2)
    c.rect(x + w * 0.28, y - 3, w * 0.44, 6, P["gold"], r=2, sw=1.6, hl=False, sh=False)
    if label:
        c.rect(x + 2, y + h * 0.52, w - 4, h * 0.22, label, r=1, sw=0, hl=False, sh=False)


def key(c, x, y, s=20, color=None):
    color = color or P["gold"]
    c.circle(x, y, s * .32, color, sw=2.2)
    c.circle(x, y, s * .12, "#2a1c16", sw=0, hl=False, sh=False)
    c.rect(x - s * .08, y + s * .25, s * .16, s * .6, color, r=1, sw=2, hl=False, sh=False)
    c.rect(x + s * .06, y + s * .6, s * .18, s * .09, color, r=0, sw=1.6, hl=False, sh=False)


def plate_base(c, cx, cy, r, rim=None, sw=3):
    c.circle(cx, cy, r, rim or P["paper"], sw=sw)
    c.circle(cx, cy, r * 0.72, light(rim or P["paper"], 0.1), sw=1.4, hl=False, sh=False)


def spark(c, x, y, s, color="#fff3c0"):
    c.pline([(x - s, y), (x, y - s * .35), (x + s, y), (x, y + s * .35), (x - s, y)], color, 1, fill=color)
    c.pline([(x, y - s), (x + s * .35, y), (x, y + s), (x - s * .35, y), (x, y - s)], color, 1, fill=color)


def flame(c, cx, cy, s=1.0):
    d = "M%s %s C%s %s %s %s %s %s C%s %s %s %s %s %s C%s %s %s %s %s %s Z" % (
        f2(cx), f2(cy - 46 * s), f2(cx + 8 * s), f2(cy - 26 * s), f2(cx + 26 * s), f2(cy - 16 * s), f2(cx + 22 * s), f2(cy + 8 * s),
        f2(cx + 18 * s), f2(cy + 30 * s), f2(cx - 18 * s), f2(cy + 30 * s), f2(cx - 22 * s), f2(cy + 8 * s),
        f2(cx - 26 * s), f2(cy - 10 * s), f2(cx - 8 * s), f2(cy - 22 * s), f2(cx), f2(cy - 46 * s))
    c.path(d, P["flame"], sw=3.4 * s)
    d2 = "M%s %s C%s %s %s %s %s %s C%s %s %s %s %s %s C%s %s %s %s %s %s Z" % (
        f2(cx + 2 * s), f2(cy - 14 * s), f2(cx + 8 * s), f2(cy - 4 * s), f2(cx + 12 * s), f2(cy + 2 * s), f2(cx + 8 * s), f2(cy + 18 * s),
        f2(cx + 5 * s), f2(cy + 26 * s), f2(cx - 10 * s), f2(cy + 26 * s), f2(cx - 10 * s), f2(cy + 14 * s),
        f2(cx - 11 * s), f2(cy + 4 * s), f2(cx - 2 * s), f2(cy - 2 * s), f2(cx + 2 * s), f2(cy - 14 * s))
    c.path(d2, P["flame_l"], sw=0)
