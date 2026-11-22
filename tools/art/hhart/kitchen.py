"""Kitchen stations (Multi) and the twelve dish plates (Single)."""
from .style import *
from .parts import *
from .registry import tex
from .casino import canvas_for_

BAMBOO = "#c7a05a"
BAMBOO_D = "#8f6a30"


def bricks(c, x, y, w, h, base, rows=5, cols=4, sw=1.6):
    col = dark(base, 0.35)
    rh = h / rows
    for r in range(1, rows):
        c.line(x, y + r * rh, x + w, y + r * rh, col, w=sw)
    cw = w / cols
    for r in range(rows):
        off = (cw / 2) if r % 2 else 0
        for k in range(cols):
            xx = x + off + k * cw
            if x + 3 < xx < x + w - 3:
                c.line(xx, y + r * rh, xx, y + (r + 1) * rh, col, w=sw)


def knob(c, x, y, r=6, col=None):
    c.circle(x, y, r, col or P["steel"], sw=2, hl=True)
    c.line(x, y - r * .6, x, y, OUT, w=1.6)


# ------------------------------------------------------------------ prep counter
@tex("Things/HH/Kitchen/PrepCounter", "multi", (2, 1), masked=True, doc="Prep counter: chopping board, knife, vegetables, 2x1, stuffable top")
def prep(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    f = slab(c, view, P["pale"], apron=16, m="a")
    c.rect(*f.r(f.w * .08, f.h * .12, f.w * .46, f.h * .76), P["wood_l"], r=5, sw=2.6, m=None)
    c.rect(*f.r(f.w * .08 + 5, f.h * .12 + 5, f.w * .46 - 10, f.h * .76 - 10), P["wood"], r=3, sw=0, m=False, hl=False, sh=False)
    # carrot slices and a tomato
    for i in range(5):
        c.rect(*f.r(f.w * .15 + i * 11, f.h * .3, 8, f.h * .22), P["amber"], r=2, sw=1.6, hl=False, sh=False, m=None)
    c.circle(*f.pt(f.w * .27, f.h * .72), 11, P["tomato"], sw=2.2, m=None)
    # knife
    c.line(*f.pt(f.w * .34, f.h * .55), *f.pt(f.w * .56, f.h * .55), P["steel"], w=7, m=None)
    c.line(*f.pt(f.w * .34, f.h * .55), *f.pt(f.w * .56, f.h * .55), OUT, w=1.2)
    c.line(*f.pt(f.w * .56, f.h * .55), *f.pt(f.w * .64, f.h * .55), P["wood_d"], w=8, m=None)
    # bowl with chopped greens
    c.circle(*f.pt(f.w * .78, f.h * .42), 22, P["paper"], sw=2.8, m=None)
    c.circle(*f.pt(f.w * .78, f.h * .42), 15, P["leaf"], sw=1.6, hl=False, sh=False, m=None)
    c.circle(*f.pt(f.w * .78 - 4, f.h * .42 - 3), 4, light(P["leaf"], .3), sw=0, hl=False, sh=False, m=None)
    c.rect(*f.r(f.w * .86, f.h * .64, 20, 14), P["paper"], r=3, sw=2.2, m=None)
    return c


# ------------------------------------------------------------------ stock pot
@tex("Things/HH/Kitchen/StockPot", "multi", (1, 1), doc="Stock pot on a stone fire box, 1x1")
def stockpot(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    f = slab(c, view, P["stone"], apron=22, front=dark(P["stone"], .32))
    # fire slit in the front face
    c.rect(c.W / 2 - 22, c.H - 8 - 20, 44, 12, P["coal"], r=3, sw=2, hl=False, sh=False)
    c.rect(c.W / 2 - 14, c.H - 8 - 19, 28, 8, P["flame"], r=3, sw=0, hl=False, sh=False)
    cx, cy = f.pt(f.w / 2, f.h / 2)
    for sx in (-1, 1):
        c.rect(cx + sx * 40 - 8, cy - 7, 16, 14, P["iron"], r=4, sw=2.4)
    c.circle(cx, cy, 38, P["iron"], sw=3.5)
    c.circle(cx, cy, 31, P["dsteel"], sw=2.2, hl=False, sh=False)
    c.circle(cx, cy, 25, P["broth"], sw=0, hl=False, sh=False)
    for dx, dy, col in ((-9, -6, P["leaf"]), (8, 4, P["amber"]), (-3, 10, P["bread"]), (10, -10, P["leaf"])):
        c.circle(cx + dx, cy + dy, 4, col, sw=1.2, hl=False, sh=False)
    c.glow(cx - 8, cy - 9, 6, "#f3d9a2", .55)
    c.steam(cx + 2, cy - 18, 30, 2)
    return c


# ------------------------------------------------------------------ grill
@tex("Things/HH/Kitchen/Grill", "multi", (2, 1), doc="Charcoal grill with grate and meat, 2x1")
def grill(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    f = slab(c, view, P["iron"], apron=18, front=dark(P["iron"], .3))
    # glowing fire slit on the front
    c.rect(c.W / 2 - 26, c.H - 8 - 15, 52, 8, P["flame"], r=3, sw=2, hl=False, sh=False)
    bx, by, bw, bh = f.r(10, 10, f.w - 20, f.h - 20)
    c.rect(bx, by, bw, bh, P["coal"], r=6, sw=3, hl=False)
    # embers
    for i in range(14):
        ex = bx + 10 + (i * 37 % int(max(bw - 20, 1)))
        ey = by + 10 + (i * 53 % int(max(bh - 20, 1)))
        c.dot(ex, ey, 3.5, P["flame"] if i % 3 else P["flame_l"])
    # grate bars (along local x)
    n = 7
    for i in range(n):
        p0 = f.pt(14, 14 + i * (f.h - 28) / (n - 1))
        p1 = f.pt(f.w - 14, 14 + i * (f.h - 28) / (n - 1))
        c.line(*p0, *p1, P["dsteel"], w=5)
        c.line(*p0, *p1, light(P["dsteel"], .35), w=1.4)
    # food: sausage, steak, corn
    def meat_piece(lx, ly, lw, lh, col, r):
        c.rect(*f.r(lx, ly, lw, lh), col, r=r, sw=2.4, hl=True, sh=True, m=None)
        for k in range(3):
            c.line(*f.pt(lx + lw * (0.2 + 0.3 * k), ly + 3), *f.pt(lx + lw * (0.2 + 0.3 * k), ly + lh - 3), dark(col, .5), w=2)
    meat_piece(f.w * .18, f.h * .22, f.w * .22, f.h * .38, P["meat_l"], 9)
    meat_piece(f.w * .46, f.h * .44, f.w * .26, f.h * .2, P["meat"], 8)
    meat_piece(f.w * .62, f.h * .14, f.w * .2, f.h * .18, P["meat_l"], 8)
    c.steam(*f.pt(f.w * .3, f.h * .22), 26, 2)
    return c


# ------------------------------------------------------------------ oven
@tex("Things/HH/Kitchen/Oven", "multi", (2, 1), doc="Brick oven: glowing arch, iron door and thermometer, 2x1")
def oven(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    br = P["brick"]
    face = cabinet(c, view, br, cap=22, margin=9, accent_strip=True, side_accent=P["brick_d"])
    x, y, w, h = face
    if view == "south":
        bricks(c, x, y, w, h, br, cols=8)
        aw = min(w * .34, h * 1.25)
        ax, ay, ah = x + w * .12, y + h * .2, h * .76
        d = "M%s %s V%s Q%s %s %s %s Q%s %s %s %s V%s Z" % (f2(ax), f2(ay + ah), f2(ay + aw * .45), f2(ax), f2(ay - aw * .1), f2(ax + aw / 2), f2(ay - aw * .1), f2(ax + aw), f2(ay - aw * .1), f2(ax + aw), f2(ay + aw * .45), f2(ay + ah))
        c.path(d, "#241410", sw=3.2, m=None)
        c.glow(ax + aw / 2, ay + ah * .72, aw * .5, P["flame"], .55)
        flame(c, ax + aw / 2, ay + ah * .62, aw / 128 * 1.15)
        # iron door with vent slits and a thermometer
        dx, dy, dw, dh = x + w * .56, y + h * .28, w * .3, h * .68
        c.rect(dx, dy, dw, dh, P["dsteel"], r=5, sw=3, hl=True)
        for k in range(3):
            c.line(dx + 8, dy + 10 + k * 8, dx + dw - 8, dy + 10 + k * 8, "#14100e", w=3)
        c.circle(dx + dw - 12, dy + dh - 14, 4, P["steel"], sw=1.6, hl=False, sh=False)
        c.circle(x + w * .5, y + h * .22, 9, P["paper"], sw=2.4)
        c.line(x + w * .5, y + h * .22, x + w * .5 + 4, y + h * .22 - 5, P["red_l"], w=2)
        c.rect(x + w * .08, y + h * .02, w * .84, 7, P["dsteel"], r=2, sw=2, hl=False)
    elif view == "north":
        bricks(c, x, y, w, h, br, cols=8)
        for fx in (.28, .66):
            c.rect(x + w * fx, y - 2, w * .12, 16, P["iron"], r=3, sw=2.4)
    else:
        bricks(c, x, y, w, h, br, rows=9, cols=3)
        c.rect(x + w * .2, y + h * .12, w * .6, 12, P["iron"], r=3, sw=2.4)
    return c


# ------------------------------------------------------------------ wok station
@tex("Things/HH/Kitchen/WokStation", "multi", (1, 1), doc="Wok on a burner stand, 1x1")
def wok(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    f = slab(c, view, P["dsteel"], apron=20, front=dark(P["dsteel"], .1))
    knob(c, c.W / 2 - 22, c.H - 8 - 15, 7, P["steel"])
    knob(c, c.W / 2 + 22, c.H - 8 - 15, 7, P["steel"])
    cx, cy = f.pt(f.w / 2, f.h / 2)
    # flame tongues around the wok
    for i in range(10):
        a = math.radians(i * 36)
        c.circle(cx + math.cos(a) * 46, cy + math.sin(a) * 46, 5.5, P["flame"], sw=1.4, hl=False, sh=False)
    # handle (toward local +x)
    hx0, hy0 = f.pt(f.w / 2 + 38, f.h / 2)
    hx1, hy1 = f.pt(f.w / 2 + 68, f.h / 2)
    c.line(hx0, hy0, hx1, hy1, OUT, w=11)
    c.line(hx0, hy0, hx1, hy1, P["wood_d"], w=7)
    c.circle(cx, cy, 40, P["iron"], sw=3.6)
    c.circle(cx, cy, 33, "#2a2a2e", sw=1.6, hl=False, sh=False)
    c.circle(cx, cy, 27, P["noodle"], sw=0, hl=False, sh=False)
    for dx, dy, col, r in ((-10, -8, P["leaf"], 4), (8, -2, P["meat_l"], 5), (-4, 10, P["tomato"], 4), (12, 11, P["leaf"], 3.5), (-14, 5, P["egg"], 5)):
        c.circle(cx + dx, cy + dy, r, col, sw=1.2, hl=False, sh=False)
    c.steam(cx, cy - 20, 28, 2)
    return c


# ------------------------------------------------------------------ deep fryer
@tex("Things/HH/Kitchen/DeepFryer", "multi", (1, 1), doc="Electric deep fryer with two baskets, 1x1")
def fryer(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    st = "#a5aeb0"
    f = slab(c, view, st, apron=22, front=dark(st, .22))
    c.circle(c.W / 2 - 24, c.H - 8 - 13, 5.5, P["red_l"], sw=1.8, hl=False, sh=False)
    knob(c, c.W / 2 + 6, c.H - 8 - 13, 6, P["dsteel"])
    knob(c, c.W / 2 + 26, c.H - 8 - 13, 6, P["dsteel"])
    for k in range(2):
        gx, gy, gw, gh = f.r(10 + k * ((f.w - 30) / 2 + 10), 10, (f.w - 30) / 2, f.h - 20)
        c.rect(gx, gy, gw, gh, P["dsteel"], r=6, sw=3, hl=False)
        c.rect(gx + 6, gy + 6, gw - 12, gh - 12, P["yolk"], r=4, sw=1.6, hl=False, sh=False)
        c.rect(gx + 11, gy + 11, gw - 22, gh - 22, P["iron"], r=2, sw=2, hl=False, sh=False)
        for i in range(3):
            c.line(gx + 14, gy + 18 + i * (gh - 36) / 2, gx + gw - 14, gy + 18 + i * (gh - 36) / 2, P["steel"], w=1.4)
    for k in range(2):
        gx, gy, gw, gh = f.r(10 + k * ((f.w - 30) / 2 + 10), 10, (f.w - 30) / 2, f.h - 20)
        c.rect(gx + gw / 2 - 5, gy - 3, 10, 8, P["dsteel"], r=2, sw=2, hl=False, sh=False)
    return c


# ------------------------------------------------------------------ smoker
@tex("Things/HH/Kitchen/Smoker", "multi", (1, 1), doc="Upright smoker cabinet with chimney, 1x1")
def smoker(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    body = "#5a4e48"
    face = cabinet(c, view, body, cap=22, margin=9, side_accent=P["dsteel"])
    x, y, w, h = face
    # chimney pipe on the cap (drawn after so it overlaps the cap)
    px = c.W / 2 - 8 if view != "east" else c.W / 2 - 14
    c.rect(px, 4, 16, 28, P["iron"], r=4, sw=2.6)
    c.steam(px + 8, 6, 26, 2)
    if view == "south":
        c.rect(x + 3, y + 3, w - 6, h * .62, dark(body, .12), r=5, sw=3, hl=False)
        c.line(x + w / 2, y + 6, x + w / 2, y + h * .62, OUT, w=2)
        for i in range(3):
            c.line(x + 12, y + 18 + i * 14, x + w / 2 - 6, y + 18 + i * 14, light(body, .15), w=1.6)
        c.circle(x + w - 14, y + 20, 8, P["paper"], sw=2.2)
        c.line(x + w - 14, y + 20, x + w - 10, y + 15, P["red_l"], w=1.8)
        c.rect(x + w / 2 + 6, y + h * .3, 6, 18, P["steel"], r=2, sw=2)
        c.rect(x + 12, y + h * .72, w - 24, h * .2, P["coal"], r=4, sw=2.6, hl=False, sh=False)
        c.rect(x + 18, y + h * .79, w - 36, h * .06, P["flame"], r=2, sw=0, hl=False, sh=False)
    elif view == "north":
        for i in range(5):
            c.line(x + 6, y + 10 + i * 16, x + w - 6, y + 10 + i * 16, dark(body, .35), w=2.6)
    else:
        for i in range(4):
            c.line(x + 6, y + 14 + i * 20, x + w - 6, y + 14 + i * 20, dark(body, .3), w=2.4)
    return c


# ------------------------------------------------------------------ steamer
@tex("Things/HH/Kitchen/Steamer", "multi", (1, 1), doc="Bamboo steamer tower on a stove, 1x1")
def steamer(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    f = slab(c, view, P["dsteel"], apron=20, front=dark(P["dsteel"], .1))
    knob(c, c.W / 2 - 18, c.H - 8 - 13, 6, P["steel"])
    cx, cy = f.pt(f.w / 2, f.h / 2)
    c.circle(cx, cy, 42, BAMBOO_D, sw=3.4)
    c.circle(cx, cy, 35, BAMBOO, sw=2.2, hl=True)
    for i in range(-3, 4):
        w = math.sqrt(max(0, 33 ** 2 - (i * 10) ** 2))
        c.line(cx - w, cy + i * 10, cx + w, cy + i * 10, BAMBOO_D, w=1.4)
    for i in range(-3, 4):
        h = math.sqrt(max(0, 33 ** 2 - (i * 10) ** 2))
        c.line(cx + i * 10, cy - h, cx + i * 10, cy + h, mix(BAMBOO_D, BAMBOO, .4), w=1.1)
    c.circle(cx, cy, 8, BAMBOO_D, sw=2.2)
    c.steam(cx + 4, cy - 28, 30, 3, 8)
    return c


# ------------------------------------------------------------------ dough bench
@tex("Things/HH/Kitchen/DoughBench", "multi", (2, 1), masked=True, doc="Dough bench with flour, dough and rolling pin, 2x1, stuffable top")
def dough(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    f = slab(c, view, P["pale"], apron=16, m="a")
    for (cx, cy, r, op) in ((.38, .45, 30, .55), (.5, .6, 22, .5), (.28, .72, 16, .45)):
        c.glow(*f.pt(f.w * cx, f.h * cy), r, "#fbf7ea", op)
    # dough ball + rolled dough
    c.ellipse(*f.pt(f.w * .3, f.h * .46), 25, 20, P["cream2"], sw=2.8, m=None)
    c.ellipse(*f.pt(f.w * .3 - 4, f.h * .46 - 4), 9, 6, "#fffaf0", sw=0, m=False, hl=False, sh=False)
    c.rect(*f.r(f.w * .42, f.h * .22, f.w * .22, f.h * .54), P["cream2"], r=10, sw=2.6, m=None)
    # rolling pin
    a, b = f.pt(f.w * .34, f.h * .86), f.pt(f.w * .72, f.h * .86)
    c.line(*a, *b, OUT, w=16, m=False)
    c.line(*a, *b, P["wood_l"], w=11, m=None)
    c.line(*f.pt(f.w * .29, f.h * .86), *f.pt(f.w * .34, f.h * .86), P["wood_d"], w=8, m=None)
    c.line(*f.pt(f.w * .72, f.h * .86), *f.pt(f.w * .77, f.h * .86), P["wood_d"], w=8, m=None)
    # flour sack + bowl
    sx, sy, sw_, sh_ = f.r(f.w * .74, f.h * .12, 34, 40)
    c.rect(sx, sy, sw_, sh_, "#c9b07c", r=8, sw=2.8, m=None)
    c.rect(sx + 6, sy + sh_ * .4, sw_ - 12, sh_ * .26, P["paper"], r=2, sw=0, m=False, hl=False, sh=False)
    c.circle(*f.pt(f.w * .86, f.h * .66), 15, P["paper"], sw=2.6, m=None)
    c.circle(*f.pt(f.w * .86, f.h * .66), 10, "#fbf7ea", sw=0, hl=False, sh=False, m=None)
    return c


# ------------------------------------------------------------------ cold station
@tex("Things/HH/Kitchen/ColdStation", "multi", (2, 1), doc="Stainless cold station with chilled pans, 2x1")
def cold(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    st = "#a9b4b6"
    f = slab(c, view, st, apron=20, front=dark(st, .2))
    for k in range(3):
        c.rect(c.W / 2 - 70 + k * 50, c.H - 8 - 16, 40, 5, dark(st, .5), r=2, sw=0, hl=False, sh=False)
    c.circle(c.W - 34, c.H - 8 - 13, 4.5, P["blue"], sw=1.6, hl=False, sh=False)
    n = 3
    gap = 8
    pw = (f.w - gap * (n + 1)) / n
    foods = (P["leaf"], P["tomato"], P["yolk"])
    for k in range(n):
        x, y, w, h = f.r(gap + k * (pw + gap), 10, pw, f.h - 20)
        c.rect(x, y, w, h, "#7e8f98", r=5, sw=3, hl=False)
        c.rect(x + 6, y + 6, w - 12, h - 12, "#d9edf2", r=3, sw=1.4, hl=False, sh=False)
        for i in range(4):
            c.circle(x + w * (.28 + .15 * (i % 3)) + (i // 3) * 6, y + h * (.32 + .12 * i % .5), 6, foods[k], sw=1.4, hl=False, sh=False)
        spark(c, x + 12, y + 12, 6, "#ffffff")
    return c


# ================================================================== plates (64 px, Single)
def plate_canvas(cells, masked):
    from .style import Canvas
    c = Canvas(1, 1, masked)
    return c


def bowl(c, cx=64, cy=66, r=46, rim=None, inner=None):
    c.circle(cx, cy, r, rim or P["paper"], sw=3.6)
    c.circle(cx, cy, r * .8, inner or dark(P["paper"], .08), sw=1.8, hl=False, sh=False)


@tex("Things/HH/Kitchen/Plate_Soup", "single", (1, 1), doc="Item: soup bowl")
def p_soup(view, cells, masked):
    c = plate_canvas(cells, masked)
    bowl(c, rim=P["blue"], inner=light(P["blue"], .4))
    c.circle(64, 66, 31, P["broth"], sw=0, hl=False, sh=False)
    for x, y, col in ((50, 56, P["leaf"]), (72, 60, P["amber"]), (60, 76, P["bread"]), (78, 74, P["leaf"]), (48, 72, P["amber"])):
        c.circle(x, y, 5, col, sw=1.6, hl=False, sh=False)
    c.glow(56, 54, 8, "#f3d9a2", .5)
    c.steam(64, 34, 22, 2)
    return c


@tex("Things/HH/Kitchen/Plate_Stew", "single", (1, 1), doc="Item: stew bowl")
def p_stew(view, cells, masked):
    c = plate_canvas(cells, masked)
    bowl(c, rim=P["red"], inner=light(P["red"], .35))
    c.circle(64, 66, 31, dark(P["broth"], .35), sw=0, hl=False, sh=False)
    for x, y, col, r in ((52, 58, P["amber"], 7), (72, 56, P["yolk"], 6), (62, 74, P["meat_l"], 7), (78, 72, P["leaf"], 5), (48, 74, P["yolk"], 5), (66, 62, P["leaf"], 4)):
        c.circle(x, y, r, col, sw=1.8, hl=True)
    c.steam(66, 34, 22, 2)
    return c


@tex("Things/HH/Kitchen/Plate_Grill", "single", (1, 1), doc="Item: grilled meat plate")
def p_grill(view, cells, masked):
    c = plate_canvas(cells, masked)
    plate_base(c, 64, 66, 48)
    c.rect(32, 46, 48, 30, P["meat_l"], r=14, sw=2.8)
    for i in range(4):
        c.line(42 + i * 9, 51, 40 + i * 9 + 8, 71, dark(P["meat_l"], .5), w=2.6)
    c.rect(70, 70, 28, 14, P["meat"], r=7, sw=2.4)
    c.circle(48, 88, 6, P["leaf"], sw=1.8)
    c.circle(58, 90, 5, P["tomato"], sw=1.8)
    return c


@tex("Things/HH/Kitchen/Plate_Noodles", "single", (1, 1), doc="Item: noodle bowl")
def p_noodles(view, cells, masked):
    c = plate_canvas(cells, masked)
    bowl(c, rim=P["teal"], inner=light(P["teal"], .4))
    c.circle(64, 66, 31, P["noodle"], sw=0, hl=False, sh=False)
    for i in range(6):
        y = 48 + i * 7
        c.pline([(40, y), (50, y - 4), (60, y + 3), (70, y - 4), (80, y + 3), (88, y - 2)], dark(P["noodle"], .25), 2.2)
    c.ellipse(76, 58, 11, 9, P["egg"], sw=2)
    c.circle(76, 58, 4.5, P["yolk"], sw=0, hl=False, sh=False)
    c.circle(48, 74, 4, P["leaf"], sw=1.4, hl=False, sh=False)
    c.line(86, 24, 60, 60, OUT, w=6)
    c.line(86, 24, 60, 60, P["wood_l"], w=3)
    c.line(94, 28, 68, 62, OUT, w=6)
    c.line(94, 28, 68, 62, P["wood_l"], w=3)
    return c


@tex("Things/HH/Kitchen/Plate_Rice", "single", (1, 1), doc="Item: rice bowl")
def p_rice(view, cells, masked):
    c = plate_canvas(cells, masked)
    bowl(c, rim=P["wood_l"], inner=light(P["wood_l"], .3))
    c.ellipse(64, 62, 30, 25, P["rice"], sw=2.4)
    for i in range(14):
        c.dot(46 + (i * 17 % 36), 50 + (i * 11 % 26), 1.7, dark(P["rice"], .22))
    c.rect(56, 44, 16, 5, P["leaf"], r=2, sw=1.4, hl=False, sh=False)
    c.circle(88, 86, 6, P["tomato"], sw=1.8)
    return c


@tex("Things/HH/Kitchen/Plate_Bake", "single", (1, 1), doc="Item: baked pie")
def p_bake(view, cells, masked):
    c = plate_canvas(cells, masked)
    plate_base(c, 64, 66, 50)
    c.circle(64, 64, 34, P["bread"], sw=3)
    c.circle(64, 64, 26, light(P["bread"], .15), sw=1.4, hl=False, sh=False)
    for i in range(-2, 3):
        c.line(64 + i * 10 - 12, 40, 64 + i * 10 + 12, 88, P["bread_d"], w=2.4)
        c.line(64 + i * 10 + 12, 40, 64 + i * 10 - 12, 88, P["bread_d"], w=2.4)
    c.poly([(64, 64), (96, 52), (94, 78)], P["paper"], sw=2.4, hl=False)
    c.poly([(66, 64), (92, 55), (91, 74)], P["yolk"], sw=0, hl=False)
    return c


@tex("Things/HH/Kitchen/Plate_Fry", "single", (1, 1), doc="Item: fried fritters")
def p_fry(view, cells, masked):
    c = plate_canvas(cells, masked)
    plate_base(c, 64, 66, 50)
    for x, y, r in ((46, 60, 14), (74, 56, 15), (62, 78, 15), (84, 78, 11), (44, 82, 10)):
        c.circle(x, y, r, P["yolk"] if (x + y) % 3 else P["bread"], sw=2.6)
        c.circle(x - 3, y - 3, r * .3, light(P["yolk"], .5), sw=0, hl=False, sh=False)
    c.poly([(86, 40), (104, 48), (96, 64)], P["flame_l"], sw=2.4)
    return c


@tex("Things/HH/Kitchen/Plate_Salad", "single", (1, 1), doc="Item: salad")
def p_salad(view, cells, masked):
    c = plate_canvas(cells, masked)
    plate_base(c, 64, 66, 50, rim=P["paper"])
    for x, y, rx, ry in ((50, 58, 16, 12), (76, 58, 16, 12), (64, 74, 18, 12), (58, 48, 12, 9)):
        c.ellipse(x, y, rx, ry, P["leaf"], sw=2.4)
        c.line(x - rx * .6, y, x + rx * .6, y, light(P["leaf"], .3), w=1.4)
    for x, y in ((56, 62), (80, 70), (48, 76)):
        c.circle(x, y, 6, P["tomato"], sw=2)
    c.circle(70, 52, 4.5, P["yolk"], sw=1.6)
    return c


@tex("Things/HH/Kitchen/Plate_Raw", "single", (1, 1), doc="Item: raw fish slices")
def p_raw(view, cells, masked):
    c = plate_canvas(cells, masked)
    plate_base(c, 64, 66, 50, rim=P["dsteel"])
    for i in range(4):
        c.rect(38 + i * 12, 44 + (i % 2) * 3, 16, 38, P["fish"] if i % 2 else light(P["fish"], .18), r=7, sw=2.4)
        c.line(42 + i * 12, 50 + (i % 2) * 3, 42 + i * 12, 76, P["paper"], w=1.5)
    c.circle(88, 86, 6.5, P["leaf"], sw=2)
    c.rect(34, 88, 24, 6, P["paper"], r=3, sw=1.8, hl=False, sh=False)
    return c


@tex("Things/HH/Kitchen/Plate_Breakfast", "single", (1, 1), doc="Item: breakfast")
def p_breakfast(view, cells, masked):
    c = plate_canvas(cells, masked)
    plate_base(c, 64, 66, 50)
    c.poly([(36, 56), (52, 44), (70, 50), (76, 66), (64, 78), (44, 76)], P["egg"], sw=2.4)
    c.circle(56, 62, 9, P["yolk"], sw=2)
    c.circle(53, 59, 2.6, light(P["yolk"], .6), sw=0, hl=False, sh=False)
    for k in range(2):
        c.pline([(78 + k * 9, 38), (84 + k * 9, 52), (78 + k * 9, 64), (84 + k * 9, 78)], OUT, 11)
        c.pline([(78 + k * 9, 38), (84 + k * 9, 52), (78 + k * 9, 64), (84 + k * 9, 78)], P["meat_l"], 6.5)
    c.rect(60, 86, 26, 14, P["bread"], r=4, sw=2.4)
    return c


@tex("Things/HH/Kitchen/Plate_Dessert", "single", (1, 1), doc="Item: cake with cherry")
def p_dessert(view, cells, masked):
    c = plate_canvas(cells, masked)
    plate_base(c, 64, 66, 50, rim=P["pale"])
    c.rect(36, 52, 56, 36, P["choc"], r=8, sw=3)
    c.rect(36, 52, 56, 16, P["cream2"], r=8, sw=3)
    for i in range(4):
        c.rect(42 + i * 12, 64, 7, 8, P["cream2"], r=3, sw=0, hl=False, sh=False)
    c.circle(64, 46, 8.5, P["cherry"], sw=2.4)
    c.line(66, 40, 74, 28, P["leaf"], w=3)
    c.circle(62, 44, 2.4, "#ffd4d0", sw=0, hl=False, sh=False)
    return c


@tex("Things/HH/Kitchen/Plate_StreetFood", "single", (1, 1), doc="Item: skewers on a paper wrap")
def p_street(view, cells, masked):
    c = plate_canvas(cells, masked)
    c.poly([(26, 38), (98, 28), (104, 90), (32, 100)], "#d9c89d", sw=3.2)
    c.line(34, 46, 94, 38, "#c0ae82", w=1.6)
    for k, y in enumerate((52, 74)):
        c.line(24, y + 2, 108, y - 4 + k * 2, OUT, w=8)
        c.line(24, y + 2, 108, y - 4 + k * 2, P["wood_l"], w=4)
        for i, col in enumerate((P["meat_l"], P["leaf"], P["meat"], P["tomato"])):
            c.rect(36 + i * 17, y - 11 + k * 1, 15, 17, col, r=5, sw=2.4)
    return c


# ------------------------------------------------------------------ cookbook item
@tex("Things/HH/Kitchen/Cookbook", "single", (1, 1), doc="Item: greasy, dog-eared recipe book with a bookmark, 64 px")
def cookbook(view, cells, masked):
    c = canvas_for_(cells, "south", masked)
    cov = "#8a3b34"
    c.rect(32, 24, 70, 88, "#efe3c2", r=5, sw=3.4)
    c.rect(24, 18, 72, 88, cov, r=6, sw=3.8)
    c.rect(24, 18, 12, 88, dark(cov, .3), r=5, sw=3, hl=False)
    # crossed fork and ladle emblem
    c.line(52, 40, 78, 80, OUT, w=7)
    c.line(52, 40, 78, 80, P["gold"], w=3.4)
    c.line(78, 40, 52, 80, OUT, w=7)
    c.line(78, 40, 52, 80, P["gold"], w=3.4)
    c.circle(78, 82, 6, P["gold"], sw=2.4)
    for dx in (-4, 0, 4):
        c.line(52 + dx, 36, 52 + dx, 46, P["gold"], w=2)
    # grease stain and bookmark ribbon
    c.ellipse(80, 90, 12, 8, "#6b2a25", sw=0, hl=False, sh=False)
    c.poly([(64, 104), (64, 118), (70, 112), (76, 118), (76, 104)], P["amber"], sw=2.6, hl=False)
    c.line(30, 24, 90, 24, light(cov, .3), w=2)
    return c
