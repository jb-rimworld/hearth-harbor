"""city textures."""
from .style import *
from .parts import *
from .registry import tex
from .casino import canvas_for_
from .inn import _icon, person


def hline(c, f, x0, x1, y, col, w=3):
    c.line(*f.pt(x0, y), *f.pt(x1, y), col, w=w)


# ------------------------------------------------------------------ park bench
@tex("Things/HH/City/ParkBench", "multi", (2, 1), doc="Park bench for two: slatted seat, backrest, iron arms, 2x1")
def parkbench(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    f = slab(c, view, P["wood_l"], apron=14, m=None)
    c.rect(*f.r(0, 0, f.w, 22), P["wood_d"], r=4, sw=2.8)
    hline(c, f, 8, f.w - 8, 11, dark(P["wood_d"], .4), 2.6)
    n = 3
    for k in range(n):
        hline(c, f, 6, f.w - 6, 30 + k * (f.h - 34) / (n - 0.6), dark(P["wood_l"], .32), 3)
    for x in (0, f.w - 10):
        c.rect(*f.r(x, 22, 10, f.h - 22), P["iron"], r=3, sw=2.4)
    # a fallen leaf and a gum wrapper
    lx, ly = f.pt(f.w * .62, f.h * .66)
    c.ellipse(lx, ly, 9, 5, P["amber"], sw=1.6, hl=False, sh=False)
    c.line(lx - 7, ly + 1, lx + 8, ly - 1, dark(P["amber"], .4), w=1.4)
    return c


# ------------------------------------------------------------------ projector screen
@tex("Things/HH/City/ProjectorScreen", "multi", (3, 1), doc="Projector screen on a stand with a ceiling projector, 3x1")
def projector(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    W, H = c.W, c.H
    if view in ("south", "north"):
        for x in (46, W - 46 - 14):
            c.rect(x, 82, 14, 36, P["iron"], r=4, sw=2.8)
            c.rect(x - 12, 110, 38, 10, P["dsteel"], r=4, sw=2.6, hl=False)
        c.rect(24, 16, W - 48, 74, P["dsteel"], r=8, sw=3.8)
        if view == "south":
            c.rect(34, 25, W - 68, 56, P["paper"], r=3, sw=2.6, hl=False, sh=False)
            c.raw('<path d="M%d 80 L%d 26 L%d 26 L%d 80 Z" fill="#ffffff" opacity=".45"/>' % (80, 130, 150, 100))
            c.raw('<path d="M%d 80 L%d 26 L%d 26 L%d 80 Z" fill="#ffffff" opacity=".3"/>' % (170, 230, 246, 186))
            tx = W / 2
            c.raw('<path d="M%s 26 L%s 80 L%s 80 Z" fill="#fff3c0" opacity=".28"/>' % (f2(tx), f2(tx - 70), f2(tx + 70)))
            c.line(34, 81, W - 34, 81, "#c9bfa4", w=2)
        else:
            for k in range(1, 5):
                c.line(34, 16 + k * 14, W - 34, 16 + k * 14, "#4a4f5a", w=3)
            c.line(40, 24, W - 40, 82, "#4a4f5a", w=3)
            c.line(W - 40, 24, 40, 82, "#4a4f5a", w=3)
        # ceiling projector on a bracket, in front of the top frame
        bx = W / 2 - 26
        c.rect(W / 2 - 4, 0, 8, 8, P["iron"], r=2, sw=2, hl=False, sh=False)
        c.rect(bx, 6, 52, 26, P["steel"], r=6, sw=3)
        if view == "south":
            c.circle(W / 2, 22, 8, P["glass"], sw=2.6, hl=False)
            c.circle(W / 2, 22, 3.4, "#fff3c0", sw=0, hl=False, sh=False)
            c.rect(bx + 6, 11, 14, 5, P["dsteel"], r=2, sw=0, hl=False, sh=False)
            c.dot(bx + 44, 14, 2.8, P["red_l"])
        else:
            c.rect(bx + 10, 12, 32, 10, P["dsteel"], r=2, sw=0, hl=False, sh=False)
    else:  # east: edge-on, long axis vertical
        for y in (22, H - 22 - 22):
            c.rect(18, y, 70, 22, P["dsteel"], r=5, sw=2.8, hl=False)
        c.rect(46, 14, 36, H - 28, P["iron"], r=6, sw=3.4)
        c.rect(70, 18, 9, H - 36, P["paper"], r=3, sw=2.2, hl=False, sh=False)
        for k in range(5):
            c.line(50, 60 + k * 66, 66, 60 + k * 66, dark(P["iron"], .4), w=3)
        # projector above the middle, lens toward the screen face
        c.rect(30, H / 2 - 28, 56, 50, P["steel"], r=6, sw=3)
        c.rect(34, H / 2 - 22, 22, 14, P["dsteel"], r=3, sw=0, hl=False, sh=False)
        c.circle(86, H / 2 - 3, 8, P["glass"], sw=2.6, hl=False)
        c.dot(86, H / 2 - 3, 3, "#fff3c0")
    return c


# ------------------------------------------------------------------ salon chair
@tex("Things/HH/City/SalonChair", "multi", (1, 1), doc="Salon chair with cape and wall mirror, 1x1")
def salonchair(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    leather, cape = P["red"], "#a9bcd4"

    def mirror(x, y, rx, ry):
        c.ellipse(x, y, rx, ry, P["gold"], sw=3.4)
        c.ellipse(x, y, rx - 7, ry - 6, "#cfe7ee", sw=2.2, hl=False, sh=False)
        c.line(x - rx * .4, y - ry * .35, x - rx * .1, y - ry * .62, "#ffffff", w=3)
    if view in ("south", "north"):
        mirror(64, 18, 30, 12)
        c.rect(52, 104, 24, 14, P["steel"], r=4, sw=2.8)
        c.rect(40, 112, 48, 10, P["dsteel"], r=4, sw=2.6, hl=False)
        if view == "south":
            c.rect(22, 58, 14, 40, P["wood_d"], r=5, sw=2.8)
            c.rect(92, 58, 14, 40, P["wood_d"], r=5, sw=2.8)
            c.rect(34, 36, 60, 34, dark(leather, .1), r=12, sw=3.4)
            c.rect(44, 28, 40, 16, leather, r=7, sw=3)
            c.rect(30, 62, 68, 34, P["red_l"], r=10, sw=3.4)
            c.poly([(34, 66), (94, 66), (104, 108), (24, 108)], cape, sw=3.4, hl=True)
            for x in (50, 64, 78):
                c.line(x, 72, x + (x - 64) * .3, 104, dark(cape, .22), w=2.4)
            c.rect(40, 100, 48, 12, P["steel"], r=4, sw=2.6, hl=False, sh=False)
        else:
            c.rect(34, 34, 60, 66, dark(leather, .2), r=12, sw=3.6)
            c.rect(44, 26, 40, 16, dark(leather, .1), r=7, sw=3)
            for y in (52, 68, 84):
                c.line(44, y, 84, y, dark(leather, .45), w=2.6)
            c.rect(22, 54, 14, 36, P["wood_d"], r=5, sw=2.8)
            c.rect(92, 54, 14, 36, P["wood_d"], r=5, sw=2.8)
    else:  # east: profile, chair faces right, mirror at the right
        mirror(112, 60, 10, 40)
        c.rect(46, 100, 24, 16, P["steel"], r=4, sw=2.8)
        c.rect(30, 110, 56, 10, P["dsteel"], r=4, sw=2.6, hl=False)
        c.rect(18, 28, 22, 74, dark(leather, .1), r=10, sw=3.4)
        c.rect(14, 20, 20, 24, leather, r=8, sw=3)
        c.rect(28, 78, 56, 22, P["red_l"], r=8, sw=3.4)
        c.rect(34, 62, 40, 14, P["wood_d"], r=5, sw=2.8)
        c.poly([(40, 80), (86, 80), (92, 112), (46, 112)], cape, sw=3.4, hl=True)
        c.rect(80, 96, 22, 10, P["steel"], r=4, sw=2.6, hl=False, sh=False)
    return c


# ------------------------------------------------------------------ taxi stand + cab
def checker(c, x, y, w, h, n, col1="#f2d04a", col2="#2a2a2e", ):
    s = w / n
    rows = max(1, int(round(h / s)))
    for r in range(rows):
        for k in range(n):
            c.rect(x + k * s, y + r * (h / rows), s, h / rows, col1 if (k + r) % 2 == 0 else col2, r=0, sw=0, hl=False, sh=False)
    c.raw('<rect x="%s" y="%s" width="%s" height="%s" fill="none" stroke="%s" stroke-width="2.4"/>' % (f2(x), f2(y), f2(w), f2(h), OUT))


@tex("Things/HH/City/TaxiStand", "single", (2, 2), doc="Taxi stand: painted pad, signpost and callbox, 2x2")
def taxistand(view, cells, masked):
    c = canvas_for_(cells, "south", masked)
    c.rect(10, 10, 236, 236, "#5e6068", r=14, sw=4)
    c.rect(20, 20, 216, 216, "#6a6c74", r=8, sw=0, hl=False, sh=False)
    c.raw('<rect x="30" y="30" width="196" height="196" rx="6" fill="none" stroke="#f2d04a" stroke-width="7" stroke-dasharray="26 16"/>')
    checker(c, 30, 206, 196, 18, 14)
    # painted cab pictogram
    c.rect(78, 100, 100, 46, "#f2d04a", r=14, sw=3.4)
    c.rect(106, 108, 44, 30, "#6a6c74", r=6, sw=0, hl=False, sh=False)
    c.rect(120, 92, 16, 10, "#f2d04a", r=3, sw=3, hl=False, sh=False)
    for x in (90, 154):
        c.circle(x, 152, 8, "#2a2a2e", sw=2.6, hl=False, sh=False)
    # signpost (top-left)
    c.rect(40, 40, 10, 72, P["steel"], r=3, sw=3)
    c.rect(24, 30, 42, 24, "#f2d04a", r=5, sw=3.4)
    checker(c, 28, 34, 34, 8, 6)
    c.circle(45, 117, 10, P["stone"], sw=3, hl=False)
    # callbox (top-right)
    c.rect(188, 38, 34, 62, P["teal"], r=6, sw=3.6)
    c.rect(194, 46, 22, 14, P["glass"], r=2, sw=2.2, hl=False, sh=False)
    c.circle(205, 74, 6, P["red_l"], sw=2.4, hl=False)
    c.rect(196, 84, 18, 6, P["dsteel"], r=2, sw=0, hl=False, sh=False)
    c.rect(198, 98, 14, 18, P["iron"], r=3, sw=3, hl=False)
    return c


@tex("Things/HH/City/TaxiCab", "single", (2, 2), ppc=96, doc="Hovering taxi seen from above for the skyfaller, drawn at 2.2 cells")
def taxicab(view, cells, masked):
    c = canvas_for_(cells, "south", masked)
    body = "#f2c63a"
    for (x, y) in ((50, 82), (206, 82), (50, 176), (206, 176)):
        c.glow(x, y, 26, "#7fd1ff", .6)
        c.circle(x, y, 11, "#9bdcff", sw=3.2, hl=False, sh=False)
        c.circle(x, y, 5, "#ffffff", sw=0, hl=False, sh=False)
    c.rect(28, 76, 200, 104, body, r=34, sw=4.4)
    c.rect(34, 120, 188, 16, "#2a2a2e", r=0, sw=0, hl=False, sh=False)
    checker(c, 34, 120, 188, 16, 22)
    c.rect(64, 84, 96, 88, light(body, .12), r=22, sw=3.4)
    c.rect(176, 90, 36, 76, P["glass"], r=12, sw=3.4, hl=False)
    c.line(184, 100, 192, 96, "#ffffff", w=3)
    c.rect(88, 100, 44, 56, P["glass"], r=10, sw=2.8, hl=False, sh=False)
    c.rect(94, 70, 40, 20, P["paper"], r=4, sw=3)
    checker(c, 98, 73, 32, 6, 8)
    c.circle(220, 96, 7, "#fff3c0", sw=2.4, hl=False, sh=False)
    c.circle(220, 164, 7, "#fff3c0", sw=2.4, hl=False, sh=False)
    return c
