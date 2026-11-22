"""Casino textures: slot machine, roulette, blackjack, poker, cashier's cage, chips."""
from .style import *
from .parts import *
from .registry import tex


def extrude(c, pts, top, apron=14, sw=3.5, m=None, front=None):
    """convex-ish shape with a front face at the screen bottom: pts in screen space"""
    front = front or dark(top, 0.3)
    for off in range(apron, 0, -3):
        c.poly([(x, y + off) for x, y in pts], front, sw=sw if off == apron else 0, m=m)
    c.poly(pts, top, sw=sw, m=m)


# ------------------------------------------------------------------ slot machine
@tex("Things/HH/Casino/SlotMachine", "multi", (1, 1), doc="One-armed Muffalo slot machine, 1x1")
def slot(view, cells, masked):
    c = canvas_for_(cells, view)
    body, mq = P["red"], P["gold"]
    if view == "south":
        c.rect(22, 10, 84, 110, body, r=14)
        c.rect(27, 15, 74, 33, mq, r=8, sw=2.6)
        c.rect(30, 52, 68, 33, P["dsteel"], r=6, sw=3, hl=False)
        for i, x in enumerate((35, 57, 79)):
            c.rect(x, 55, 14, 27, P["paper"], r=3, sw=2, hl=False, sh=False)
        c.circle(42, 68, 4.2, P["red_l"], sw=0, hl=False, sh=False)
        c.poly([(64, 61), (65.5, 65), (69.7, 65.2), (66.5, 67.8), (67.5, 71.9), (64, 69.6), (60.5, 71.9), (61.5, 67.8), (58.3, 65.2), (62.5, 65)], P["gold"], sw=1.2, hl=False)
        c.circle(86, 68, 4.2, P["red_l"], sw=0, hl=False, sh=False)
        c.rect(32, 91, 64, 14, P["red_d"], r=3, sw=2.4, hl=False)
        c.rect(53, 95, 22, 5, "#14100e", r=2, sw=0, hl=False, sh=False)
        c.rect(104, 31, 8, 38, P["steel"], r=4, sw=2.5)
        c.circle(108, 27, 7, P["red_l"], sw=2.5)
        c.line(26, 19, 98, 19, light(mq, .45), w=2)
    elif view == "north":
        c.rect(22, 10, 84, 110, dark(body, .1), r=14)
        c.rect(27, 15, 74, 20, mix(mq, body, .3), r=8, sw=2.6)
        for i in range(6):
            c.line(36, 48 + i * 9, 92, 48 + i * 9, dark(body, .5), w=3)
        c.rect(48, 100, 32, 10, P["red_d"], r=3, sw=2.4, hl=False)
        c.rect(16, 36, 8, 30, P["steel"], r=4, sw=2.5)  # lever stub on the other side
    else:  # east: profile, front toward the right
        c.rect(32, 10, 66, 110, body, r=12)
        c.rect(36, 15, 58, 26, mq, r=8, sw=2.6)  # marquee hood
        c.rect(78, 44, 16, 44, P["dsteel"], r=4, sw=2.6, hl=False)  # window slice on the front
        c.rect(77, 94, 17, 14, P["red_d"], r=3, sw=2.4, hl=False)
        for i in range(3):
            c.line(44, 52 + i * 10, 66, 52 + i * 10, dark(body, .4), w=2.4)
        c.rect(50, 34, 9, 40, P["steel"], r=4, sw=2.5)  # lever arm in profile
        c.circle(54.5, 30, 7.5, P["red_l"], sw=2.5)
    return c


def canvas_for_(cells, view, masked=False):
    from .style import Canvas
    w, h = cells
    if view == "east":
        w, h = h, w
    return Canvas(w, h, masked)


# ------------------------------------------------------------------ roulette
def wheel(c, cx, cy, R):
    c.circle(cx, cy, R, P["wood_d"], sw=3.5)
    c.circle(cx, cy, R * 0.86, P["wood"], sw=2, hl=False, sh=False)
    n = 18
    for i in range(n):
        a0, a1 = 360 * i / n, 360 * (i + 1) / n
        col = P["red_l"] if i % 2 else "#2a2325"
        if i == 0:
            col = P["felt"]
        pts = [(cx, cy)] + arc_pts(cx, cy, R * 0.78, R * 0.78, a0, a1, 3)
        c.poly(pts, col, sw=0)
    for i in range(n):
        a = math.radians(360 * i / n)
        c.line(cx + math.cos(a) * R * .38, cy + math.sin(a) * R * .38, cx + math.cos(a) * R * .8, cy + math.sin(a) * R * .8, "#e9e4d4", w=1.5)
    c.circle(cx, cy, R * 0.4, P["wood_l"], sw=2.4)
    c.circle(cx, cy, R * 0.22, P["steel"], sw=2.2)
    c.circle(cx, cy, R * 0.07, P["gold"], sw=1.2, hl=False, sh=False)
    c.circle(cx + R * .52, cy - R * .42, R * 0.07, "#f6f2e6", sw=1.2, hl=False, sh=False)


@tex("Things/HH/Casino/RouletteTable", "multi", (3, 2), doc="Roulette table with wheel and betting layout, 3x2")
def roulette(view, cells, masked):
    c = canvas_for_(cells, view)
    f = slab(c, view, P["wood"], apron=18, r=11, m=None)
    c.rect(*f.r(5, 5, f.w - 10, f.h - 10), P["felt"], r=7, sw=2.6, hl=False)
    R = min(f.h * 0.4, f.w * 0.22)
    cx, cy = f.pt(R * 1.25 + 14, f.h / 2)
    wheel(c, cx, cy, R)
    # betting grid: 3 rows x 12 cols + zero
    gx, gy, gw, gh = f.w * 0.5, f.h / 2 - 28, f.w * 0.44, 56
    cols = 12
    cw = gw / cols
    for col in range(cols):
        for row in range(3):
            colr = P["red_l"] if (col + row) % 2 else "#2a2325"
            c.rect(*f.r(gx + col * cw + 1, gy + row * 18 + 1, cw - 2, 16), colr, r=1, sw=1.2, hl=False, sh=False)
    c.rect(*f.r(gx - 16, gy + 1, 14, 52), P["felt_d"], r=2, sw=1.2, hl=False, sh=False)
    for i, (px, py, col) in enumerate(((gx + 40, gy + 26, P["red_l"]), (gx + 90, gy + 8, P["blue"]), (gx + 100, gy + 40, P["emerald"]))):
        chip_top(c, *f.pt(px, py), 7.5, col)
    chip_top(c, *f.pt(gx + 150, gy + 14), 7.5, P["red_l"])
    return c


@tex("Things/HH/Casino/BlackjackTable", "multi", (2, 2), doc="Semicircle blackjack table with card spots, 2x2")
def blackjack(view, cells, masked):
    c = canvas_for_(cells, view)
    ap = 16
    m = 12
    X0, Y0, X1, Y1 = m, m, c.W - m, c.H - m - ap
    f = Frame(view, (X0, Y0, X1 - X0, Y1 - Y0))
    W, H = f.w, f.h
    # half disc, flat side at local y = 0 (dealer side), curve toward the players
    outer = f.pts([(0, 0), (W, 0)] + arc_pts(W / 2, 0, W / 2, H, 0, 180, 36))
    extrude(c, outer, P["wood"], apron=ap, sw=3.5)
    inner = f.pts([(9, 9), (W - 9, 9)] + arc_pts(W / 2, 9, W / 2 - 9, H - 18, 0, 180, 36))
    c.poly(inner, P["felt"], sw=2.4, hl=False)
    # card spots (arc of 3 circles) and bet lines
    for i, a in enumerate((35, 90, 145)):
        px = W / 2 + math.cos(math.radians(a)) * W * .3
        py = 26 + math.sin(math.radians(a)) * H * .52
        sx, sy = f.pt(px, py)
        c.circle(sx, sy, 15, P["felt_d"], sw=1.6, hl=False, sh=False)
        c.circle(sx, sy, 10.5, P["felt"], sw=0, hl=False, sh=False)
    # dealer tray + shoe
    c.rect(*f.r(W / 2 - 38, 12, 40, 18), P["dsteel"], r=3, sw=2.4, hl=False)
    for k in range(4):
        chip_top(c, *f.pt(W / 2 - 31 + k * 9.5, 21), 4.2, (P["red_l"], P["emerald"], P["blue"], P["gold"])[k], sw=1.2)
    c.rect(*f.r(W / 2 + 14, 10, 24, 20), P["dsteel"], r=3, sw=2.4, hl=False)
    for (px, py, rd) in ((W / 2 - 22, 52, True), (W / 2 - 8, 56, False)):
        sx, sy = f.pt(px, py)
        card(c, sx - 8, sy - 11, 16, 22, red=rd)
    chip_top(c, *f.pt(W / 2 + 40, 84), 8, P["red_l"])
    chip_top(c, *f.pt(W / 2 - 52, 80), 8, P["blue"])
    return c


@tex("Things/HH/Casino/PokerTable", "multi", (3, 2), doc="Oval poker table with pot and dealer button, 3x2")
def poker(view, cells, masked):
    c = canvas_for_(cells, view)
    f = slab(c, view, P["wood"], apron=18, r=60, m=None, inset=8)
    c.rect(*f.r(9, 9, f.w - 18, f.h - 18), P["felt"], r=52, sw=2.6, hl=False)
    # pot in centre
    cx, cy = f.pt(f.w / 2, f.h / 2)
    for i, (dx, dy, col) in enumerate(((-14, -4, P["red_l"]), (6, -10, P["blue"]), (14, 6, P["emerald"]), (-6, 10, P["gold"]))):
        chip_top(c, cx + dx, cy + dy, 9, col)
    # community cards row
    for i in range(5):
        sx, sy = f.pt(f.w / 2 - 72 + i * 36, f.h / 2 - 54)
        card(c, sx - 9, sy - 12, 18, 24, red=i % 2 == 0, back=i > 3)
    # hole card pairs and button
    for (px, py) in ((60, f.h - 24), (f.w - 60, f.h - 24), (f.w / 2, 24)):
        sx, sy = f.pt(px, py)
        card(c, sx - 14, sy - 10, 14, 20, back=True)
        card(c, sx - 2, sy - 10, 14, 20, back=True)
    c.circle(*f.pt(f.w / 2 + 70, f.h - 34), 8, P["paper"], sw=2.2)
    return c


@tex("Things/HH/Casino/CashierCage", "multi", (2, 2), doc="Cashier's cage: counter with barred window and cash drawer, 2x2")
def cage(view, cells, masked):
    c = canvas_for_(cells, view)
    W, H = c.W, c.H
    wood = P["wood"]
    if view in ("south", "north"):
        c.rect(12, 12, W - 24, H - 24, P["iron"], r=10)
        # barred window frame
        c.rect(26, 26, W - 52, 96, P["dsteel"], r=6, sw=3.2, hl=False)
        for i in range(9):
            x = 36 + i * (W - 72) / 8
            c.line(x, 30, x, 118, P["steel"], w=5)
            c.line(x, 30, x, 118, OUT, w=1.2)
        c.rect(22, 118, W - 44, 8, P["steel"], r=3, sw=2.4)
        # counter front
        c.rect(12, 128, W - 24, H - 140, wood, r=8)
        if view == "south":
            c.rect(100, 138, 56, 22, P["dsteel"], r=4, sw=2.6, hl=False)  # cash slot
            c.rect(108, 146, 40, 5, "#120d0b", r=2, sw=0, hl=False, sh=False)
            coin_stack(c, 70, 200, 11, 4)
            coin_stack(c, 188, 200, 11, 3, P["silver"])
            c.rect(22, 214, W - 44, 18, P["wood_d"], r=3, sw=2.4, hl=False)
        else:
            for i in range(4):
                c.line(30, 150 + i * 20, W - 30, 150 + i * 20, dark(wood, .35), w=3)
    else:
        c.rect(30, 12, W - 60, H - 24, P["iron"], r=10)
        c.rect(44, 28, W - 150, 94, P["dsteel"], r=5, sw=3, hl=False)
        for i in range(4):
            c.line(52, 40 + i * 20, W - 114, 40 + i * 20, P["steel"], w=5)
        c.rect(W - 78, 28, 42, 190, wood, r=6)
        c.rect(44, 128, W - 150, H - 150, wood, r=6)
    return c


@tex("Things/HH/Casino/Chips", "single", (1, 1), doc="Item: loose casino chips, 64 px")
def chips(view, cells, masked):
    c = canvas_for_(cells, view)
    chip_stack(c, 38, 62, 17, P["red_l"], 5)
    chip_stack(c, 82, 76, 17, P["emerald"], 4)
    chip_stack(c, 80, 42, 15, P["blue"], 3)
    chip_top(c, 30, 98, 13, P["gold"])
    return c
