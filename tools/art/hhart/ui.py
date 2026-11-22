"""Main tab icon, window header illustration, guide icon."""
from .style import *
from .parts import *
from .registry import tex
from .inn import _icon
from .style import Canvas


def anchor(c, cx, top, s=1.0, col=None, sw=None):
    col = col or P["steel"]
    sw = sw or 4
    def pl(pts, w):
        c.pline(pts, OUT, w + 5)
        c.pline(pts, col, w)
    pl([(cx, top + 14 * s), (cx, top + 78 * s)], 7 * s)                      # shank
    pl([(cx - 16 * s, top + 34 * s), (cx + 16 * s, top + 34 * s)], 6 * s)   # stock
    c.raw('<circle cx="%s" cy="%s" r="%s" fill="none" stroke="%s" stroke-width="%s"/>' % (f2(cx), f2(top + 8 * s), f2(8 * s), OUT, f2(10 * s)))
    c.raw('<circle cx="%s" cy="%s" r="%s" fill="none" stroke="%s" stroke-width="%s"/>' % (f2(cx), f2(top + 8 * s), f2(8 * s), col, f2(5 * s)))
    # arms
    d = "M%s %s Q%s %s %s %s" % (f2(cx - 34 * s), f2(top + 62 * s), f2(cx - 28 * s), f2(top + 88 * s), f2(cx), f2(top + 90 * s))
    d2 = "M%s %s Q%s %s %s %s" % (f2(cx + 34 * s), f2(top + 62 * s), f2(cx + 28 * s), f2(top + 88 * s), f2(cx), f2(top + 90 * s))
    for dd in (d, d2):
        c.raw('<path d="%s" fill="none" stroke="%s" stroke-width="%s" stroke-linecap="round"/>' % (dd, OUT, f2(11 * s)))
        c.raw('<path d="%s" fill="none" stroke="%s" stroke-width="%s" stroke-linecap="round"/>' % (dd, col, f2(6 * s)))
    for sx in (-1, 1):
        c.poly([(cx + sx * 34 * s, top + 52 * s), (cx + sx * 44 * s, top + 70 * s), (cx + sx * 26 * s, top + 70 * s)], col, sw=3 * s, hl=False)


@tex("UI/HH/UI/MainTab", "single", (1, 1), ppc=128, grain=0, doc="Main tab icon: hearth flame over a harbour anchor")
def maintab(view, cells, masked):
    c = _icon(cells, masked)
    c.circle(64, 64, 58, P["navy"], sw=4)
    c.circle(64, 64, 50, dark(P["navy"], .2), sw=0)
    anchor(c, 64, 28, 0.95, P["steel"])
    flame(c, 64, 30, 0.62)
    return c


@tex("UI/HH/UI/Guide", "single", (1, 1), ppc=128, grain=0, doc="Guide icon: open book with a question mark")
def guide(view, cells, masked):
    c = _icon(cells, masked)
    c.poly([(10, 36), (60, 44), (60, 112), (10, 104)], P["paper"], sw=3.6, hl=False)
    c.poly([(118, 36), (68, 44), (68, 112), (118, 104)], P["paper"], sw=3.6, hl=False)
    c.poly([(8, 100), (60, 112), (68, 112), (120, 100), (120, 108), (68, 120), (60, 120), (8, 108)], P["wood_d"], sw=3, hl=False)
    for k in range(4):
        c.line(18, 54 + k * 11, 52, 58 + k * 11, "#a79f89", w=2)
    c.circle(88, 40, 30, P["amber"], sw=4)
    q = "M76 32 a13 13 0 1 1 20 11 c-5 3 -8 6 -8 11"
    c.raw('<path d="%s" fill="none" stroke="#1c1411" stroke-width="12" stroke-linecap="round"/>' % q)
    c.raw('<path d="%s" fill="none" stroke="#fbf1d6" stroke-width="6" stroke-linecap="round"/>' % q)
    c.circle(88, 66, 6, "#1c1411", sw=0, hl=False, sh=False)
    c.circle(88, 66, 3.2, "#fbf1d6", sw=0, hl=False, sh=False)
    return c


@tex("UI/HH/UI/CardHeader", "single", (8, 2), ppc=128, grain=0, doc="Window header illustration 1024x256: hearth fire + harbour anchor, no text")
def cardheader(view, cells, masked):
    c = Canvas(8, 2, masked)
    W, H = 1024, 256
    c.raw('<defs><linearGradient id="sky" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#4a2c1d"/><stop offset=".38" stop-color="#8a4c2a"/><stop offset=".62" stop-color="#d9803f"/><stop offset="1" stop-color="#3c4a68"/></linearGradient>'
          '<linearGradient id="sky2" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#1c1411" stop-opacity=".55"/><stop offset=".7" stop-color="#1c1411" stop-opacity="0"/></linearGradient>'
          '<radialGradient id="glowh" cx=".5" cy=".5" r=".5"><stop offset="0" stop-color="#ffd27a" stop-opacity=".75"/><stop offset="1" stop-color="#ffd27a" stop-opacity="0"/></radialGradient>'
          '<radialGradient id="sun" cx=".5" cy=".5" r=".5"><stop offset="0" stop-color="#ffe9a8"/><stop offset=".55" stop-color="#f5b85a" stop-opacity=".8"/><stop offset="1" stop-color="#f5b85a" stop-opacity="0"/></radialGradient></defs>')
    c.raw('<rect width="%d" height="%d" fill="url(#sky)"/><rect width="%d" height="%d" fill="url(#sky2)"/>' % (W, H, W, H))
    # sun glow over the harbour
    c.raw('<circle cx="650" cy="168" r="150" fill="url(#sun)"/>')
    # far hills / skyline
    c.raw('<path d="M360 176 L420 150 L470 164 L520 140 L580 166 L640 150 L700 168 L760 150 L820 170 L900 148 L1024 172 L1024 256 L360 256 Z" fill="#5b3d37" opacity=".85"/>')
    # sea
    c.raw('<rect x="330" y="176" width="694" height="80" fill="#2f4a5c"/>')
    for i, y in enumerate((186, 200, 214, 230, 244)):
        c.raw('<path d="M330 %d q40 -8 80 0 t80 0 t80 0 t80 0 t80 0 t80 0 t80 0 t80 0 t80 0" fill="none" stroke="#6f93a2" stroke-opacity="%s" stroke-width="3"/>' % (y, f2(.6 - i * .09)))
    c.raw('<path d="M560 180 L740 180 L720 256 L580 256 Z" fill="#f5b85a" opacity=".35"/>')
    # pier and boat
    c.rect(520, 186, 330, 10, P["wood_d"], r=3, sw=3, hl=False)
    for x in (540, 600, 660, 720, 780, 840):
        c.rect(x, 186, 9, 42, P["wood_d"], r=2, sw=2.6, hl=False, sh=False)
    c.poly([(660, 214), (760, 214), (742, 238), (680, 238)], P["red"], sw=3.4, hl=False)
    c.line(710, 214, 710, 150, OUT, w=7)
    c.line(710, 214, 710, 150, P["wood_l"], w=3.2)
    c.poly([(714, 154), (714, 206), (756, 206)], P["cream"], sw=3, hl=False)
    # lighthouse on the right
    c.poly([(930, 80), (968, 80), (978, 190), (920, 190)], P["paper"], sw=3.6, hl=False)
    c.poly([(936, 120), (964, 120), (968, 142), (932, 142)], P["red_l"], sw=0, hl=False)
    c.poly([(924, 160), (974, 160), (978, 180), (920, 180)], P["red_l"], sw=0, hl=False)
    c.rect(926, 62, 48, 18, P["dsteel"], r=3, sw=3.2, hl=False)
    c.poly([(930, 62), (950, 38), (970, 62)], P["red"], sw=3.4, hl=False)
    c.glow(950, 70, 40, "#ffe9a8", .45)
    c.dot(950, 71, 6, "#fff3c0")
    # big anchor in the centre-right, slightly translucent as a watermark
    c.raw('<g opacity="0.88">')
    anchor(c, 440, 38, 1.7, P["steel"])
    c.raw('</g>')
    # hearth on the left: brick block with arch, flames and mantle
    c.rect(0, 0, 290, H, "#3a2217", r=0, sw=0, hl=False, sh=False)
    c.raw('<rect x="0" y="0" width="290" height="256" fill="#5a3524" opacity=".6"/>')
    for r in range(0, 256, 28):
        c.line(0, r, 290, r, "#2e1a12", w=2.4)
        for k in range(0, 300, 52):
            c.line(k + (26 if (r // 28) % 2 else 0), r, k + (26 if (r // 28) % 2 else 0), r + 28, "#2e1a12", w=2.4)
    c.raw('<circle cx="150" cy="190" r="170" fill="url(#glowh)"/>')
    c.rect(24, 176, 252, 64, P["wood_d"], r=6, sw=3.8, hl=False)  # hearth stone base / mantle
    d = "M62 236 V150 Q62 96 150 96 Q238 96 238 150 V236 Z"
    c.path(d, "#1b110c", sw=4.6)
    c.raw('<path d="%s" fill="#2a1710" opacity=".0"/>' % d)
    for (fx, fy, s) in ((150, 200, 1.35), (110, 214, .85), (192, 214, .85)):
        flame(c, fx, fy, s)
    c.rect(70, 232, 160, 12, "#2b1a12", r=3, sw=3.4, hl=False)
    # logs
    c.rect(92, 220, 118, 14, P["wood"], r=6, sw=3.2)
    c.rect(100, 232, 100, 10, P["wood_l"], r=5, sw=3)
    # hanging lantern
    c.line(290, 0, 290, 30, OUT, w=4)
    c.rect(276, 30, 28, 34, P["dsteel"], r=6, sw=3.2)
    c.rect(282, 36, 16, 22, P["flame_l"], r=3, sw=0, hl=False, sh=False)
    c.glow(290, 47, 40, "#ffd27a", .35)
    # soft vignette to keep the edges calm
    c.raw('<rect width="%d" height="%d" fill="none" stroke="#1c1411" stroke-width="8" opacity=".9"/>' % (W, H))
    return c
