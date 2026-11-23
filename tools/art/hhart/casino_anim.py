"""Casino animation art (1.1.0, F30/F31): the slot cabinet, roulette wheels, cards, chips and particles of the Lucky Thrumbo window and the
practice games (UI/HH/Casino/Anim/...), plus the small map overlays and reaction icons at the machines and tables (Things/HH/Casino/Fx/...).

Geometry that the C# code relies on (keep in sync):
  SlotCabinet 1024x512: reel windows x 110/340/570, w 200, y 165, h 240; marquee x 70 y 34 w 760 h 92; lever pivot (918, 300), lever top y 74,
                       coin tray x 250 y 432 w 400 h 46                                  (SlotView.cs: windows, marquee, leverPivot, tray)
  WheelEU/WheelUS 512: pocket ring 0.66 .. 0.985 of the radius, pocket i centred at i * 360 / n clockwise from the top (WheelLayout order)
  WheelRim 512: wooden bowl 0.80 .. 1.0 with the ball track at 0.885                     (RouletteView.cs: BallTrack, PocketRing, NumberRing)
  MiniWheel/MiniWheelUS (map): pockets 0.48 .. 0.98 of its radius, same order as WheelEU/WheelUS (CasinoFx.cs: DrawRoulette)
"""
import math
from .style import *
from .parts import *
from .registry import tex
from .casino import extrude


EU = [0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26]
US = [0, 28, 9, 26, 30, 11, 7, 20, 32, 17, 5, 22, 34, 15, 3, 24, 36, 13, 1, 37, 27, 10, 25, 29, 12, 8, 19, 31, 18, 6, 21, 33, 16, 4, 23, 35, 14, 2]
REDS = {1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36}
POCKET_RED, POCKET_BLACK, POCKET_GREEN = "#b8352c", "#26211f", "#2f7a4f"


def pt(cx, cy, r, deg):
    a = math.radians(deg)
    return (cx + math.sin(a) * r, cy - math.cos(a) * r)


def ring_segment(c, cx, cy, r0, r1, a0, a1, fill, sw=0, n=4):
    pts = [pt(cx, cy, r1, a0 + (a1 - a0) * i / n) for i in range(n + 1)] + [pt(cx, cy, r0, a1 - (a1 - a0) * i / n) for i in range(n + 1)]
    c.poly(pts, fill, sw=sw)


def wheel_pockets(c, cx, cy, R, order, r0=0.66, r1=0.985, frets=True, cone=True):
    n = len(order); step = 360.0 / n
    c.circle(cx, cy, R * r1 + 2, "#5a3a22", sw=3.4, hl=False, sh=False)
    for i, p in enumerate(order):
        col = POCKET_GREEN if p in (0, 37) else POCKET_RED if p in REDS else POCKET_BLACK
        ring_segment(c, cx, cy, R * r0, R * r1, i * step - step / 2, i * step + step / 2, col, sw=0)
        # a lighter inner half where the ball rests
        ring_segment(c, cx, cy, R * r0, R * (r0 + (r1 - r0) * 0.42), i * step - step / 2, i * step + step / 2, dark(col, .25), sw=0)
    if frets:
        for i in range(n):
            a = i * step - step / 2
            x0, y0 = pt(cx, cy, R * r0, a); x1, y1 = pt(cx, cy, R * r1, a)
            c.line(x0, y0, x1, y1, "#d8d2c2", w=max(1.4, R * 0.012))
    c.raw('<circle cx="%s" cy="%s" r="%s" fill="none" stroke="#d8d2c2" stroke-width="%s"/>' % (f2(cx), f2(cy), f2(R * r0), f2(max(1.6, R * 0.014))))
    c.raw('<circle cx="%s" cy="%s" r="%s" fill="none" stroke="%s" stroke-width="%s"/>' % (f2(cx), f2(cy), f2(R * r1), OUT, f2(max(2, R * 0.016))))
    if cone:
        # the wooden cone inside, with grooves that make the turning visible
        c.circle(cx, cy, R * r0 - 1, P["wood"], sw=max(2, R * 0.014), hl=False, sh=False)
        c.circle(cx, cy, R * r0 * 0.78, P["wood_l"], sw=max(1.4, R * 0.01), hl=False, sh=False)
        for k in range(8):
            x0, y0 = pt(cx, cy, R * r0 * 0.3, k * 45 + 22.5); x1, y1 = pt(cx, cy, R * r0 * 0.95, k * 45 + 22.5)
            c.line(x0, y0, x1, y1, P["wood_d"], w=max(1.2, R * 0.01))


def _wheel(order):
    c = Canvas(4, 4)
    R = 254
    wheel_pockets(c, 256, 256, R, order)
    return c


@tex("UI/HH/Casino/Anim/WheelEU", "single", (4, 4), ppc=128, grain=0, doc="Roulette wheel, single zero (pockets in real order, numbers are drawn by the window)")
def wheel_eu(view, cells, masked):
    return _wheel(EU)


@tex("UI/HH/Casino/Anim/WheelUS", "single", (4, 4), ppc=128, grain=0, doc="Roulette wheel, double zero")
def wheel_us(view, cells, masked):
    return _wheel(US)


@tex("UI/HH/Casino/Anim/WheelRim", "single", (4, 4), ppc=128, grain=0, doc="Roulette bowl: wooden rim with the ball track and diamonds (does not turn)")
def wheel_rim(view, cells, masked):
    c = Canvas(4, 4)
    cx = cy = 256; R = 252
    wood, wood_d = P["wood"], P["wood_d"]
    # outer bowl ring (an annulus): even-odd path
    def annulus(r0, r1, fill, sw=3.6):
        d = "M%s %s a%s %s 0 1 0 %s 0 a%s %s 0 1 0 %s 0 Z M%s %s a%s %s 0 1 1 %s 0 a%s %s 0 1 1 %s 0 Z" % (
            f2(cx - r1), f2(cy), f2(r1), f2(r1), f2(2 * r1), f2(r1), f2(r1), f2(-2 * r1),
            f2(cx - r0), f2(cy), f2(r0), f2(r0), f2(2 * r0), f2(r0), f2(r0), f2(-2 * r0))
        c.raw('<path d="%s" fill="%s" fill-rule="evenodd" stroke="%s" stroke-width="%s"/>' % (d, fill, OUT, f2(sw)))
    annulus(R * 0.80, R, wood)
    annulus(R * 0.84, R * 0.95, "#c9b48c", 2.4)                       # the ball track
    c.raw('<circle cx="256" cy="256" r="%s" fill="none" stroke="%s" stroke-width="3"/>' % (f2(R * 0.975), light(wood, .3)))
    c.raw('<circle cx="256" cy="256" r="%s" fill="none" stroke="%s" stroke-width="2.4"/>' % (f2(R * 0.855), light("#c9b48c", .35)))
    # eight brass diamonds (deflectors) on the track
    for k in range(8):
        x, y = pt(cx, cy, R * 0.82, k * 45 + 22.5)
        a = math.radians(k * 45 + 22.5)
        dx, dy = math.sin(a), -math.cos(a)
        px, py = -dy, dx
        s = 9
        c.poly([(x + dx * s * 1.6, y + dy * s * 1.6), (x + px * s * .7, y + py * s * .7), (x - dx * s * 1.6, y - dy * s * 1.6), (x - px * s * .7, y - py * s * .7)], P["gold"], sw=2.2)
    # wood grain arcs on the rim
    for k in range(10):
        a0 = k * 36 + 5
        p0 = pt(cx, cy, R * 0.975 - 4, a0); p1 = pt(cx, cy, R * 0.975 - 4, a0 + 20)
        c.raw('<path d="M%s %s A%s %s 0 0 1 %s %s" fill="none" stroke="%s" stroke-width="2"/>' % (f2(p0[0]), f2(p0[1]), f2(R * 0.975 - 4), f2(R * 0.975 - 4), f2(p1[0]), f2(p1[1]), wood_d))
    return c


@tex("UI/HH/Casino/Anim/WheelTurret", "single", (1, 1), ppc=128, grain=0, doc="Roulette turret: brass cross with a knob (turns with the wheel)")
def wheel_turret(view, cells, masked):
    c = Canvas(1, 1)
    g = P["gold"]
    for k in range(4):
        a = math.radians(k * 90)
        x1, y1 = 64 + math.sin(a) * 58, 64 - math.cos(a) * 58
        c.line(64, 64, x1, y1, OUT, w=13)
        c.line(64, 64, x1, y1, g, w=8)
        c.circle(x1, y1, 7, g, sw=3)
    c.circle(64, 64, 22, g, sw=3.6)
    c.circle(64, 64, 12, light(g, .25), sw=2.4, hl=False)
    c.circle(59, 59, 4, "#fff6d0", sw=0, hl=False, sh=False)
    return c


@tex("UI/HH/Casino/Anim/Ball", "single", (0.5, 0.5), ppc=128, grain=0, doc="Ivory roulette ball (also the marquee bulbs and soft shadows, tinted)")
def ball(view, cells, masked):
    c = Canvas(0.5, 0.5)
    c.circle(32, 32, 29, "#f4efe2", sw=3.2)
    c.circle(24, 23, 8, "#ffffff", sw=0, hl=False, sh=False)
    return c


@tex("UI/HH/Casino/Anim/Glow", "single", (1, 1), ppc=128, grain=0, doc="Soft white glow (tinted by the window)")
def glow_tex(view, cells, masked):
    c = Canvas(1, 1)
    c.glow(64, 64, 64, "#ffffff", 1.0)
    return c


@tex("UI/HH/Casino/Anim/Felt", "single", (2, 2), ppc=128, grain=0, doc="Tileable casino felt (256 px): green with a quiet diamond weave")
def felt(view, cells, masked):
    c = Canvas(2, 2)
    base = "#2c6847"
    c.raw('<rect width="256" height="256" fill="%s"/>' % base)
    for i in range(-8, 9):
        o = i * 32
        c.raw('<path d="M%d 0 L%d 256" stroke="%s" stroke-opacity=".22" stroke-width="1.4"/>' % (o, o + 256, light(base, .25)))
        c.raw('<path d="M%d 0 L%d 256" stroke="%s" stroke-opacity=".22" stroke-width="1.4"/>' % (o + 256, o, dark(base, .3)))
    import random
    rnd = random.Random(5)
    for _ in range(260):
        x, y = rnd.uniform(0, 256), rnd.uniform(0, 256)
        c.raw('<circle cx="%s" cy="%s" r="%s" fill="%s" fill-opacity=".18"/>' % (f2(x), f2(y), f2(rnd.uniform(0.6, 1.4)), rnd.choice((light(base, .4), dark(base, .4)))))
    return c


# ------------------------------------------------------------------ the slot cabinet
@tex("UI/HH/Casino/Anim/SlotCabinet", "single", (8, 4), ppc=128, grain=0, doc="Slot cabinet front for the Lucky Thrumbo window, 1024x512 (reel windows left open as dark panels)")
def slot_cabinet(view, cells, masked):
    c = Canvas(8, 4)
    body, trim, red_d = "#8a3b34", "#5c2420", "#4a1d1a"
    # lever housing on the right side
    c.rect(880, 250, 70, 100, P["steel"], r=12, sw=3.6)
    c.rect(890, 262, 50, 76, P["iron"], r=8, sw=2.4, hl=False)
    # body
    c.rect(40, 18, 840, 478, body, r=34, sw=4.5)
    c.rect(52, 28, 816, 458, light(body, .06), r=28, sw=2.4, hl=False, sh=False)
    # marquee
    c.rect(66, 30, 768, 100, "#262b45", r=20, sw=3.6)
    c.rect(78, 42, 744, 76, "#303a63", r=14, sw=2, hl=False, sh=False)
    # thrumbo coin on the marquee, left
    cx, cy = 120, 80
    c.circle(cx, cy, 34, P["felt_d"], sw=3.2)
    c.circle(cx, cy, 26, P["felt"], sw=2.2, hl=False)
    c.poly([(cx - 8, cy + 10), (cx, cy - 26), (cx + 8, cy + 10)], P["amber"], sw=2.6)
    # reel window panel
    c.rect(90, 146, 700, 280, P["dsteel"], r=18, sw=4)
    for x in (110, 340, 570):
        c.rect(x - 6, 159, 212, 252, "#14100e", r=10, sw=3, hl=False, sh=False)
        c.rect(x, 165, 200, 240, "#efe7d3", r=6, sw=2.6, hl=False, sh=False)
    # payline arrows
    for x, d in ((96, 1), (784, -1)):
        c.poly([(x, 272), (x + d * 14, 285), (x, 298)], P["red_l"], sw=2.4)
    # lower panel and the coin tray
    c.rect(90, 432 - 4, 700, 58, red_d, r=10, sw=3.2, hl=False)
    c.rect(250, 432, 400, 46, "#1c1411", r=10, sw=3, hl=False, sh=False)
    c.rect(258, 440, 384, 30, "#2b2420", r=8, sw=1.6, hl=False, sh=False)
    coin_stack(c, 300, 462, 13, 2)
    coin_stack(c, 600, 462, 13, 3, P["silver"])
    # buttons at the bottom left / right of the tray
    for x, col in ((150, P["amber"]), (200, P["emerald"]), (700, P["blue"]), (750, P["red_l"])):
        c.circle(x, 456, 15, col, sw=3)
    # side trims
    c.rect(48, 140, 26, 340, trim, r=10, sw=2.6, hl=False)
    c.rect(806, 140, 26, 340, trim, r=10, sw=2.6, hl=False)
    for y in range(160, 470, 34):
        c.circle(61, y, 5, P["gold"], sw=1.6, hl=False, sh=False)
        c.circle(819, y, 5, P["gold"], sw=1.6, hl=False, sh=False)
    return c


@tex("UI/HH/Casino/Anim/SlotGlass", "single", (1, 1.25), ppc=128, grain=0, doc="Glass over one reel window: inner shadow at the top and bottom, a light streak")
def slot_glass(view, cells, masked):
    c = Canvas(1, 1.25)
    W, H = 128, 160
    c.raw('<defs><linearGradient id="sg" x1="0" y1="0" x2="0" y2="1">'
          '<stop offset="0" stop-color="#1c1411" stop-opacity=".78"/><stop offset=".22" stop-color="#1c1411" stop-opacity="0"/>'
          '<stop offset=".78" stop-color="#1c1411" stop-opacity="0"/><stop offset="1" stop-color="#1c1411" stop-opacity=".78"/></linearGradient></defs>')
    c.raw('<rect width="%s" height="%s" fill="url(#sg)"/>' % (f2(W), f2(H)))
    c.raw('<path d="M14 %s L40 %s L56 %s L30 %s Z" fill="#ffffff" fill-opacity=".14"/>' % (f2(H), f2(H), "0", "0"))
    c.raw('<rect x="1.5" y="1.5" width="%s" height="%s" fill="none" stroke="#1c1411" stroke-width="3"/>' % (f2(W - 3), f2(H - 3)))
    return c


@tex("UI/HH/Casino/Anim/LeverArm", "single", (0.125, 1), ppc=128, grain=0, doc="Slot lever arm (chrome rod, stretched by the window)")
def lever_arm(view, cells, masked):
    c = Canvas(0.125, 1)
    c.rect(2, 0, 12, 128, P["silver"], r=5, sw=2.6)
    c.line(6, 4, 6, 124, "#ffffff", w=2)
    return c


@tex("UI/HH/Casino/Anim/LeverKnob", "single", (0.5, 0.5), ppc=128, grain=0, doc="Slot lever knob")
def lever_knob(view, cells, masked):
    c = Canvas(0.5, 0.5)
    c.circle(32, 32, 28, P["red_l"], sw=3.6)
    c.circle(24, 23, 7, "#ffd0c8", sw=0, hl=False, sh=False)
    return c


# ------------------------------------------------------------------ cards (Thrumbo deck)
def _card_base(c, W, H, fill, edge):
    c.rect(3, 3, W - 6, H - 6, fill, r=14, sw=3.6, hl=False, sh=False)
    c.rect(11, 11, W - 22, H - 22, fill, r=9, sw=1.6, hl=False, sh=False)
    c.raw('<rect x="11" y="11" width="%s" height="%s" rx="9" fill="none" stroke="%s" stroke-width="1.6"/>' % (f2(W - 22), f2(H - 22), edge))


@tex("UI/HH/Casino/Anim/CardFace", "single", (1.375, 2), ppc=128, grain=0, doc="Playing card front (blank paper; rank, suit and portrait are drawn by the window)")
def card_face(view, cells, masked):
    c = Canvas(1.375, 2)
    _card_base(c, 176, 256, P["paper"], "#c9bfa6")
    return c


@tex("UI/HH/Casino/Anim/CardBack", "single", (1.375, 2), ppc=128, grain=0, doc="Playing card back: navy with the Thrumbo coin")
def card_back(view, cells, masked):
    c = Canvas(1.375, 2)
    W, H = 176, 256
    _card_base(c, W, H, "#2b3557", "#8aa0c8")
    c.raw('<defs><clipPath id="cb"><rect x="12" y="12" width="%s" height="%s" rx="8"/></clipPath></defs><g clip-path="url(#cb)">' % (f2(W - 24), f2(H - 24)))
    for i in range(-8, 10):
        o = i * 22
        c.raw('<path d="M%s 0 L%s 256" stroke="#3a4670" stroke-width="3"/>' % (f2(o), f2(o + 140)))
        c.raw('<path d="M%s 0 L%s 256" stroke="#33406a" stroke-width="3"/>' % (f2(o + 140), f2(o)))
    c.raw('</g>')
    c.raw('<rect x="11" y="11" width="%s" height="%s" rx="9" fill="none" stroke="#8aa0c8" stroke-width="2"/>' % (f2(W - 22), f2(H - 22)))
    cx, cy = W / 2, H / 2
    c.circle(cx, cy, 40, P["felt_d"], sw=3.6)
    c.circle(cx, cy, 31, P["felt"], sw=2.4, hl=False)
    for k in range(12):
        a = k * math.pi / 6
        c.line(cx + math.cos(a) * 37, cy + math.sin(a) * 37, cx + math.cos(a) * 32, cy + math.sin(a) * 32, "#f4ecd4", w=3.4)
    c.poly([(cx - 9, cy + 14), (cx, cy - 28), (cx + 9, cy + 14)], P["amber"], sw=2.8)
    for t in (0.3, 0.55):
        y = cy + 14 + (-28 - 14) * t
        c.line(cx - 9 * (1 - t) + 1, y + 2, cx + 9 * (1 - t) - 1, y - 2, dark(P["amber"], .45), w=2)
    return c


@tex("UI/HH/Casino/Anim/Suit0", "single", (0.5, 0.5), ppc=128, grain=0, doc="Card suit 0: muffalo (red)")
def suit_muffalo(view, cells, masked):
    c = Canvas(0.5, 0.5)
    col = "#b83a2c"
    c.path("M14 22 Q2 20 4 8 Q8 16 16 15 Z", "#efe3c2", sw=2.2)
    c.path("M50 22 Q62 20 60 8 Q56 16 48 15 Z", "#efe3c2", sw=2.2)
    c.path("M32 8 Q52 8 52 30 Q52 50 40 58 Q32 62 24 58 Q12 50 12 30 Q12 8 32 8 Z", col, sw=3)
    c.path("M22 44 Q22 36 32 36 Q42 36 42 44 Q42 56 32 57 Q22 56 22 44 Z", light(col, .35), sw=2)
    c.dot(27, 46, 2.2, OUT); c.dot(37, 46, 2.2, OUT)
    c.dot(24, 28, 2.6, OUT); c.dot(40, 28, 2.6, OUT)
    return c


@tex("UI/HH/Casino/Anim/Suit1", "single", (0.5, 0.5), ppc=128, grain=0, doc="Card suit 1: thrumbo horn (dark)")
def suit_horn(view, cells, masked):
    c = Canvas(0.5, 0.5)
    col = "#2b2f38"
    c.poly([(20, 58), (34, 4), (46, 58)], col, sw=3)
    for t in (0.25, 0.5, 0.72):
        y = 58 + (4 - 58) * t
        c.line(20 + 14 * t + 2, y + 3, 46 - 12 * t - 2, y - 3, light(col, .45), w=2.4)
    return c


@tex("UI/HH/Casino/Anim/Suit2", "single", (0.5, 0.5), ppc=128, grain=0, doc="Card suit 2: gear (dark)")
def suit_gear(view, cells, masked):
    c = Canvas(0.5, 0.5)
    col = "#2b2f38"
    pts = []
    for i in range(16):
        a0 = i / 16.0 * 2 * math.pi; a1 = (i + 1) / 16.0 * 2 * math.pi
        r = 29 if i % 2 == 0 else 22
        pts += [(32 + math.cos(a0) * r, 32 + math.sin(a0) * r), (32 + math.cos(a1) * r, 32 + math.sin(a1) * r)]
    c.poly(pts, col, sw=2.6)
    c.circle(32, 32, 9, "#e9e4d4", sw=2.4, hl=False, sh=False)
    return c


@tex("UI/HH/Casino/Anim/Suit3", "single", (0.5, 0.5), ppc=128, grain=0, doc="Card suit 3: healroot blossom (red)")
def suit_healroot(view, cells, masked):
    c = Canvas(0.5, 0.5)
    c.raw('<path d="M32 60 Q30 48 32 36" fill="none" stroke="%s" stroke-width="7" stroke-linecap="round"/>' % OUT)
    c.raw('<path d="M32 60 Q30 48 32 36" fill="none" stroke="%s" stroke-width="3.6" stroke-linecap="round"/>' % P["leaf"])
    c.path("M32 52 Q18 54 12 44 Q24 40 32 50 Z", P["leaf"], sw=2.2)
    c.path("M32 50 Q46 52 52 42 Q40 38 32 48 Z", P["leaf"], sw=2.2)
    col = "#c23a3a"
    for k in range(5):
        a = math.radians(k * 72 - 90)
        c.circle(32 + math.cos(a) * 11, 24 + math.sin(a) * 11, 9, col, sw=2.4)
    c.circle(32, 24, 6, "#f2c94c", sw=2, hl=False, sh=False)
    return c


def _portrait(c, skin, hair, coat, hat=None):
    """head and shoulders, in a framed oval: the J/Q/K faces of the Thrumbo deck (generic rim folk, no real persons)"""
    c.rect(6, 6, 116, 116, "#efe6cf", r=20, sw=3, hl=False, sh=False)
    c.rect(12, 74, 104, 50, coat, r=26, sw=3.4)
    c.line(64, 80, 64, 122, dark(coat, .3), w=3)
    c.circle(64, 52, 26, skin, sw=3.4)
    c.path("M38 48 Q40 22 64 22 Q88 22 90 48 Q82 34 64 34 Q46 34 38 48 Z", hair, sw=3)
    c.dot(55, 54, 2.8, OUT); c.dot(73, 54, 2.8, OUT)
    c.raw('<path d="M56 64 Q64 69 72 64" fill="none" stroke="%s" stroke-width="2.6" stroke-linecap="round"/>' % OUT)
    if hat:
        hat(c)


@tex("UI/HH/Casino/Anim/FaceJ", "single", (1, 1), ppc=128, grain=0, doc="Card portrait J: a frontier ranger with a wide hat")
def face_j(view, cells, masked):
    c = Canvas(1, 1)
    def hat(c):
        c.ellipse(64, 30, 42, 8, P["wood_d"], sw=3)
        c.path("M44 30 Q46 8 64 8 Q82 8 84 30 Z", P["wood"], sw=3)
        c.line(46, 26, 82, 26, P["amber"], w=3)
    _portrait(c, "#e2b48c", "#5a3524", "#4f7f5a", hat)
    return c


@tex("UI/HH/Casino/Anim/FaceQ", "single", (1, 1), ppc=128, grain=0, doc="Card portrait Q: a noblewoman with a tiara")
def face_q(view, cells, masked):
    c = Canvas(1, 1)
    def hat(c):
        c.poly([(44, 26), (50, 12), (57, 22), (64, 8), (71, 22), (78, 12), (84, 26)], P["gold"], sw=2.6)
        c.circle(64, 18, 3.2, P["red_l"], sw=1.2, hl=False, sh=False)
        c.path("M36 50 Q30 84 40 96 Q44 70 42 52 Z", "#8a3b34", sw=2.4)
        c.path("M92 50 Q98 84 88 96 Q84 70 86 52 Z", "#8a3b34", sw=2.4)
    _portrait(c, "#f0cfb0", "#8a3b34", "#8e5cc8", hat)
    return c


@tex("UI/HH/Casino/Anim/FaceK", "single", (1, 1), ppc=128, grain=0, doc="Card portrait K: a bearded leader with a crown")
def face_k(view, cells, masked):
    c = Canvas(1, 1)
    def hat(c):
        c.poly([(40, 28), (42, 8), (52, 18), (64, 4), (76, 18), (86, 8), (88, 28)], P["gold"], sw=2.6)
        c.line(42, 26, 86, 26, dark(P["gold"], .3), w=3)
        c.path("M40 58 Q42 88 64 92 Q86 88 88 58 Q80 72 64 72 Q48 72 40 58 Z", "#d8d2c2", sw=2.6)
    _portrait(c, "#d9a57e", "#d8d2c2", "#34425e", hat)
    return c


@tex("UI/HH/Casino/Anim/Shoe", "single", (1.5, 1), ppc=128, grain=0, doc="Dealer's card shoe with a card at the mouth")
def shoe(view, cells, masked):
    c = Canvas(1.5, 1)
    c.poly([(14, 36), (180, 20), (186, 112), (10, 116)], P["dsteel"], sw=3.6)
    c.poly([(14, 36), (180, 20), (176, 34), (20, 48)], light(P["dsteel"], .2), sw=2.4)
    c.rect(28, 60, 58, 40, "#14100e", r=8, sw=2.6, hl=False, sh=False)
    c.poly([(30, 66), (78, 58), (84, 96), (36, 104)], "#2b3557", sw=2.4)
    c.line(104, 90, 168, 84, P["gold"], w=3)
    return c


# ------------------------------------------------------------------ chips, coins, particles, plates
@tex("UI/HH/Casino/Anim/Chip", "single", (0.5, 0.5), ppc=128, grain=0, doc="Casino chip, near white (tinted by the window)")
def chip_ui(view, cells, masked):
    c = Canvas(0.5, 0.5)
    c.circle(32, 32, 28, "#eeeeee", sw=3.4, hl=False, sh=False)
    c.circle(32, 32, 17, "#ffffff", sw=1.6, hl=False, sh=False)
    for i in range(8):
        a = i * math.pi / 4
        c.line(32 + math.cos(a) * 21, 32 + math.sin(a) * 21, 32 + math.cos(a) * 27, 32 + math.sin(a) * 27, "#bdbdbd", w=4.6)
    return c


@tex("UI/HH/Casino/Anim/Coin", "single", (0.5, 0.5), ppc=128, grain=0, doc="Gold coin particle")
def coin_ui(view, cells, masked):
    c = Canvas(0.5, 0.5)
    coin(c, 32, 32, 27)
    return c


@tex("UI/HH/Casino/Anim/Sparkle", "single", (0.5, 0.5), ppc=128, grain=0, doc="Sparkle particle (four-point star)")
def sparkle_ui(view, cells, masked):
    c = Canvas(0.5, 0.5)
    c.glow(32, 32, 30, "#fff3c0", .7)
    spark(c, 32, 32, 26, "#fffbe8")
    return c


@tex("UI/HH/Casino/Anim/Confetti", "single", (0.25, 0.25), ppc=128, grain=0, doc="Confetti particle (white, tinted)")
def confetti_ui(view, cells, masked):
    c = Canvas(0.25, 0.25)
    c.rect(6, 10, 20, 12, "#ffffff", r=2, sw=1.6, hl=False, sh=False)
    return c


@tex("UI/HH/Casino/Anim/BigWinPlate", "single", (4, 1.25), ppc=128, grain=0, doc="Plate behind 'Big win!' and blackjack results")
def bigwin_plate(view, cells, masked):
    c = Canvas(4, 1.25)
    W, H = 512, 160
    c.rect(6, 6, W - 12, H - 12, "#2a1f3a", r=26, sw=4)
    c.rect(18, 18, W - 36, H - 36, "#3a2b52", r=18, sw=2.4, hl=False, sh=False)
    c.raw('<rect x="18" y="18" width="%s" height="%s" rx="18" fill="none" stroke="%s" stroke-width="3"/>' % (f2(W - 36), f2(H - 36), P["amber"]))
    for (x, y, s) in ((40, 34, 9), (472, 36, 8), (60, 118, 7), (452, 116, 9), (256, 22, 6)):
        spark(c, x, y, s, "#ffe7a0")
    return c


@tex("UI/HH/Casino/Anim/ThrumboRun", "single", (2, 1.25), ppc=128, grain=0, doc="A thrumbo running (side view) for the jackpot")
def thrumbo_run(view, cells, masked):
    c = Canvas(2, 1.25)
    fur, fur_d, horn = "#f1ece0", "#cfc6b2", P["amber"]
    # legs: short and strong, mid-gallop
    for (x1, y1, x2, y2) in ((66, 112, 46, 150), (92, 116, 104, 152), (160, 114, 146, 152), (186, 110, 206, 146)):
        c.line(x1, y1, x2, y2, OUT, w=24)
        c.line(x1, y1, x2, y2, fur_d, w=17)
        c.ellipse(x2, y2 + 2, 11, 5, "#5a4a3a", sw=2.4, hl=False, sh=False)
    # a bulky, shaggy body
    c.path("M40 92 Q40 50 110 46 Q176 42 196 74 Q206 100 184 118 Q150 132 100 130 Q48 128 40 92 Z", fur, sw=4.4)
    for (x, y) in ((64, 116), (92, 124), (122, 126), (152, 120), (176, 112)):
        c.raw('<path d="M%s %s q4 8 8 0" fill="none" stroke="%s" stroke-width="2.6" stroke-linecap="round"/>' % (f2(x), f2(y), fur_d))
    c.path("M46 74 Q24 64 16 82 Q30 84 42 94 Z", fur_d, sw=3)                  # tail tuft
    c.raw('<path d="M60 62 Q110 44 170 56" fill="none" stroke="%s" stroke-width="3" stroke-linecap="round"/>' % light(fur, .5))
    # head forward and low, the horn on the forehead
    c.path("M180 64 Q196 42 222 46 Q246 50 248 74 Q248 92 228 96 Q208 98 196 108 Q186 92 180 64 Z", fur, sw=4.2)
    c.path("M184 56 Q196 38 214 42 Q200 50 196 62 Z", fur_d, sw=2.6)          # brow tuft
    c.poly([(214, 48), (238, 4), (230, 52)], horn, sw=3.6)
    for t in (0.3, 0.55):
        x0 = 214 + (238 - 214) * t; x1 = 230 + (238 - 230) * t; y = 50 + (4 - 50) * t
        c.line(x0 + 1, y + 1, x1 - 1, y - 1, dark(horn, .45), w=2.2)
    c.circle(226, 66, 3.8, OUT, sw=0, hl=False, sh=False)
    c.circle(225, 65, 1.2, "#ffffff", sw=0, hl=False, sh=False)
    c.ellipse(244, 82, 2.6, 3.4, OUT, sw=0, hl=False, sh=False)
    return c


# ------------------------------------------------------------------ map overlays (Things/HH/Casino/Fx, 64 px per cell)
def _mini_wheel(order):
    c = Canvas(2, 2)
    wheel_pockets(c, 128, 128, 125, order, r0=0.48, r1=0.98, frets=True, cone=True)
    c.circle(128, 128, 18, P["gold"], sw=3)
    return c


@tex("Things/HH/Casino/Fx/MiniWheel", "single", (2, 2), doc="Map overlay: the turning roulette wheel, single zero (pockets 0.48 .. 0.98)")
def mini_wheel(view, cells, masked):
    return _mini_wheel(EU)


@tex("Things/HH/Casino/Fx/MiniWheelUS", "single", (2, 2), doc="Map overlay: the turning roulette wheel, double zero (pockets 0.48 .. 0.98)")
def mini_wheel_us(view, cells, masked):
    return _mini_wheel(US)


@tex("Things/HH/Casino/Fx/Ball", "single", (0.5, 0.5), doc="Map overlay: roulette ball")
def fx_ball(view, cells, masked):
    c = Canvas(0.5, 0.5)
    c.circle(32, 32, 26, "#f6f2e6", sw=5)
    c.circle(24, 24, 7, "#ffffff", sw=0, hl=False, sh=False)
    return c


@tex("Things/HH/Casino/Fx/CardFace", "single", (0.5, 0.75), doc="Map overlay: a dealt card, face up")
def fx_card_face(view, cells, masked):
    c = Canvas(0.5, 0.75)
    c.rect(4, 4, 56, 84, P["paper"], r=8, sw=4, hl=False, sh=False)
    c.circle(32, 46, 11, P["red_l"], sw=2, hl=False, sh=False)
    return c


@tex("Things/HH/Casino/Fx/CardBack", "single", (0.5, 0.75), doc="Map overlay: a dealt card, face down")
def fx_card_back(view, cells, masked):
    c = Canvas(0.5, 0.75)
    c.rect(4, 4, 56, 84, "#2b3557", r=8, sw=4, hl=False, sh=False)
    c.rect(12, 12, 40, 68, "#3a4670", r=4, sw=1.6, hl=False, sh=False)
    c.circle(32, 46, 9, P["amber"], sw=2, hl=False, sh=False)
    return c


@tex("Things/HH/Casino/Fx/Chip", "single", (0.5, 0.5), doc="Map overlay: a chip of a player's stack")
def fx_chip(view, cells, masked):
    c = Canvas(0.5, 0.5)
    chip_top(c, 32, 32, 26, P["red_l"], sw=4)
    return c


@tex("Things/HH/Casino/Fx/SlotLight", "single", (1, 0.5), doc="Map overlay: the marquee lighting up after a big win")
def fx_slot_light(view, cells, masked):
    c = Canvas(1, 0.5)
    c.glow(64, 32, 60, "#ffe08a", .9)
    for i in range(6):
        c.circle(14 + i * 20, 12, 4.5, "#fff6d0", sw=1.6, hl=False, sh=False)
        c.circle(14 + i * 20, 52, 4.5, "#fff6d0", sw=1.6, hl=False, sh=False)
    return c


# ------------------------------------------------------------------ reaction icons (flecks above gamblers, 64 px)
@tex("Things/HH/Casino/Fx/FleckCoin", "single", (1, 1), doc="Reaction: a coin flipped up on a win")
def fleck_coin(view, cells, masked):
    c = Canvas(1, 1)
    coin(c, 64, 64, 34, sw=4.4)
    spark(c, 98, 30, 12, "#fff3c0")
    return c


@tex("Things/HH/Casino/Fx/FleckSparkle", "single", (1, 1), doc="Reaction: a sparkle of a big win")
def fleck_sparkle(view, cells, masked):
    c = Canvas(1, 1)
    c.glow(64, 64, 60, "#fff0b0", .8)
    spark(c, 64, 64, 46, "#fffbe8")
    return c


@tex("Things/HH/Casino/Fx/FleckCheer", "single", (1, 1), doc="Reaction: a cheer (star burst with rays)")
def fleck_cheer(view, cells, masked):
    c = Canvas(1, 1)
    star = [(64 + math.cos(math.radians(a)) * (46 if i % 2 == 0 else 20), 64 + math.sin(math.radians(a)) * (46 if i % 2 == 0 else 20)) for i, a in enumerate(range(-90, 270, 36))]
    c.poly(star, P["gold"], sw=4.4)
    c.poly([(64 + (x - 64) * 0.55, 64 + (y - 64) * 0.55) for x, y in star], light(P["gold"], .45), sw=0)
    for a in (-150, -30, 30, 150):
        x0, y0 = 64 + math.cos(math.radians(a)) * 52, 64 + math.sin(math.radians(a)) * 52
        x1, y1 = 64 + math.cos(math.radians(a)) * 62, 64 + math.sin(math.radians(a)) * 62
        c.line(x0, y0, x1, y1, OUT, w=7); c.line(x0, y0, x1, y1, "#fff0b0", w=3.4)
    return c


@tex("Things/HH/Casino/Fx/FleckGroan", "single", (1, 1), doc="Reaction: a little rain cloud after a losing streak")
def fleck_groan(view, cells, masked):
    c = Canvas(1, 1)
    cl = "#8d93a3"
    for (x, y, r) in ((44, 60, 20), (64, 50, 24), (86, 60, 19)):
        c.circle(x, y, r, cl, sw=4)
    c.rect(28, 58, 76, 22, cl, r=11, sw=4)
    c.rect(32, 56, 68, 20, cl, r=10, sw=0, hl=False, sh=False)
    for x in (44, 64, 84):
        c.line(x, 88, x - 4, 104, OUT, w=7); c.line(x, 88, x - 4, 104, P["blue"], w=3.6)
    return c


@tex("Things/HH/Casino/Fx/FleckNearMiss", "single", (1, 1), doc="Reaction: '!' for a near miss at the slots")
def fleck_nearmiss(view, cells, masked):
    c = Canvas(1, 1)
    c.circle(64, 60, 40, "#f4ecd4", sw=4.4)
    c.poly([(48, 96), (58, 90), (44, 112)], "#f4ecd4", sw=3.6)
    c.rect(57, 32, 14, 38, P["red_l"], r=6, sw=3.2)
    c.circle(64, 82, 7.5, P["red_l"], sw=3)
    return c


@tex("Things/HH/Casino/Fx/FleckChips", "single", (1, 1), doc="Reaction: chips cashed out with a smile")
def fleck_chips(view, cells, masked):
    c = Canvas(1, 1)
    chip_stack(c, 50, 82, 22, P["emerald"], 4)
    chip_stack(c, 84, 90, 20, P["red_l"], 3)
    coin(c, 92, 40, 16)
    return c
