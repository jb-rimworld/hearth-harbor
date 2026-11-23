"""underworld textures."""
from .style import *
from .parts import *
from .registry import tex
from .casino import canvas_for_
from .inn import _icon, person


def ground(c, col, x=8, y=8, w=None, h=None, r=12):
    w = w or c.W - 2 * x
    h = h or c.H - 2 * y
    c.rect(x, y, w, h, col, r=r, sw=3.8, hl=False, sh=False)


# ------------------------------------------------------------------ back room door (one half leaf; the game draws it twice, the second mirrored)
@tex("Things/HH/Underworld/BackRoomDoor", "single", (1, 1), doc="Heavy door leaf: left half only, the game mirrors it and slides both apart")
def backroomdoor(view, cells, masked):
    c = canvas_for_(cells, "south", masked)
    wood = P["wood_d"]
    c.rect(2, 10, 68, 108, wood, r=5, sw=3.8)
    for y in (34, 62, 90):
        c.line(6, y, 66, y, dark(wood, .4), w=2.6)
    for y in (24, 96):
        c.rect(4, y, 64, 12, P["iron"], r=3, sw=2.8)
        for x in (12, 28, 44):
            c.dot(x, y + 6, 2.2, P["steel"])
    # lock plate near the meeting edge
    c.rect(48, 50, 18, 30, P["dsteel"], r=4, sw=2.8)
    c.circle(57, 62, 4.4, "#0e0a09", sw=0, hl=False, sh=False)
    c.rect(55.4, 62, 3.2, 10, "#0e0a09", r=1, sw=0, hl=False, sh=False)
    c.rect(44, 34, 22, 8, P["iron"], r=3, sw=2.4, hl=False)
    return c


# ------------------------------------------------------------------ fight pit marker
@tex("Things/HH/Underworld/FightPit", "single", (1, 1), doc="Fight pit marker: sand ring with rope posts and a red pennant")
def fightpit(view, cells, masked):
    c = canvas_for_(cells, "south", masked)
    c.ellipse(64, 66, 54, 50, "#c9a86a", sw=3.8)
    c.ellipse(64, 66, 42, 38, "#d8bb80", sw=0, hl=False, sh=False)
    c.raw('<ellipse cx="64" cy="66" rx="48" ry="44" fill="none" stroke="%s" stroke-width="3.4" stroke-dasharray="7 6"/>' % P["wood_d"])
    for i in range(8):
        a = math.radians(i * 45 + 22)
        c.rect(64 + math.cos(a) * 48 - 4, 66 + math.sin(a) * 44 - 6, 8, 12, P["wood"], r=2, sw=2.2, hl=False, sh=False)
    c.rect(60, 36, 8, 52, P["wood_d"], r=3, sw=3)
    c.poly([(68, 38), (98, 46), (68, 56)], P["red_l"], sw=3, hl=False)
    c.ellipse(64, 90, 12, 5, P["stone"], sw=2.6, hl=False, sh=False)
    for x, y in ((34, 74), (92, 80), (80, 100)):
        c.ellipse(x, y, 5, 2.6, "#b99a5c", sw=0, hl=False, sh=False)
    return c


# ------------------------------------------------------------------ hunting ground marker
@tex("Things/HH/Underworld/HuntingGround", "single", (1, 1), doc="Hunting ground marker: grass patch with a target board and a stuck arrow")
def huntingground(view, cells, masked):
    c = canvas_for_(cells, "south", masked)
    c.ellipse(64, 70, 54, 46, "#6b8f4e", sw=3.8)
    for x, y in ((28, 82), (96, 88), (44, 100), (84, 58)):
        c.pline([(x, y), (x - 3, y - 9)], "#3f6232", 3)
        c.pline([(x + 5, y), (x + 6, y - 10)], "#3f6232", 3)
    c.line(44, 30, 44, 96, OUT, w=9)
    c.line(44, 30, 44, 96, P["wood"], w=5)
    c.line(84, 30, 84, 96, OUT, w=9)
    c.line(84, 30, 84, 96, P["wood"], w=5)
    c.circle(64, 50, 28, P["paper"], sw=4)
    c.circle(64, 50, 19, P["red_l"], sw=2.6, hl=False, sh=False)
    c.circle(64, 50, 9, P["paper"], sw=2.4, hl=False, sh=False)
    c.circle(64, 50, 3.4, P["red_l"], sw=0, hl=False, sh=False)
    tline = lambda pts, w, col: (c.pline(pts, OUT, w + 4), c.pline(pts, col, w))
    tline([(84, 28), (62, 52)], 3, P["wood_l"])
    c.poly([(84, 28), (92, 24), (90, 34)], P["steel"], sw=1.6, hl=False)
    c.poly([(60, 54), (54, 62), (58, 56)], P["red_l"], sw=1.4, hl=False)
    return c


# ------------------------------------------------------------------ fence counter
@tex("Things/HH/Underworld/FenceCounter", "multi", (2, 1), doc="Scarred counter with a shaded lamp, strongbox and wrapped parcels, 2x1")
def fencecounter(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    wood = "#5c4130"
    f = slab(c, view, wood, apron=18, m=None)
    for k, x in enumerate((30, 80, 146, 200)):
        c.line(*f.pt(x, 10 + (k % 2) * 6), *f.pt(x + 26, 18 + (k % 2) * 4), dark(wood, .45), w=2)
    # strongbox
    sx, sy, sw_, sh_ = f.r(16, 20, 46, f.h - 30)
    c.rect(sx, sy, sw_, sh_, P["iron"], r=5, sw=3)
    c.rect(sx, sy + sh_ * .38, sw_, 6, P["dsteel"], r=2, sw=0, hl=False, sh=False)
    c.circle(sx + sw_ / 2, sy + sh_ * .55, 5.5, P["gold"], sw=2.2)
    # parcels with string
    for (px, py, pw, ph, col) in ((78, 26, 36, 28, "#cbb68a"), (92, 54, 30, 22, "#b9a276")):
        x, y, w, h = f.r(px, py, pw, ph)
        c.rect(x, y, w, h, col, r=3, sw=2.6)
        c.line(x + w / 2, y + 2, x + w / 2, y + h - 2, "#6b5230", w=2.2)
        c.line(x + 2, y + h / 2, x + w - 2, y + h / 2, "#6b5230", w=2.2)
    # loupe and ledger
    x, y, w, h = f.r(134, 30, 36, 38)
    c.rect(x, y, w, h, P["red_d"], r=3, sw=2.6)
    c.rect(x + 4, y + 4, w - 8, h - 8, P["paper"], r=2, sw=1.4, hl=False, sh=False)
    lx, ly = f.pt(190, 56)
    c.circle(lx, ly, 10, P["glass"], sw=2.8, hl=False)
    c.line(lx + 7, ly + 7, lx + 17, ly + 17, OUT, w=6)
    c.line(lx + 7, ly + 7, lx + 17, ly + 17, P["wood_l"], w=3)
    # shaded lamp
    lx, ly = f.pt(222, 26)
    c.glow(lx, ly, 30, "#ffbf5a", .5)
    c.poly([(lx - 12, ly + 6), (lx - 5, ly - 10), (lx + 5, ly - 10), (lx + 12, ly + 6)], "#2c5a45", sw=2.8, hl=False)
    c.dot(lx, ly + 7, 3.4, "#ffe9a8")
    return c


# ------------------------------------------------------------------ organ clinic: sealed cold cases, nothing graphic
@tex("Things/HH/Underworld/OrganClinic", "multi", (2, 1), doc="Cold counter with sealed transport cases and a frost light (no organs shown), 2x1")
def organclinic(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    f = slab(c, view, "#aebcc4", apron=18, front="#6f808a", m=None)
    for k, x in enumerate((14, 90)):
        bx, by, bw, bh = f.r(x, 14, 66, f.h - 24)
        c.rect(bx, by, bw, bh, "#e9eef0", r=7, sw=3.4)
        c.rect(bx + 5, by + 5, bw - 10, bh * .35, "#7fb9d6", r=4, sw=2.4, hl=False, sh=False)
        c.rect(bx + bw / 2 - 9, by + bh * .55, 18, 8, P["steel"], r=2, sw=2, hl=False, sh=False)
        # snowflake
        sx, sy = bx + bw / 2, by + bh * .22
        for a in (0, 60, 120):
            r = math.radians(a)
            c.line(sx - math.cos(r) * 9, sy - math.sin(r) * 9, sx + math.cos(r) * 9, sy + math.sin(r) * 9, "#ffffff", w=2.4)
    x, y, w, h = f.r(f.w - 70, 16, 54, 30)
    c.rect(x, y, w, h, P["dsteel"], r=4, sw=2.8, hl=False)
    c.dot(x + 10, y + h / 2, 4, "#7fd1ff")
    c.dot(x + 24, y + h / 2, 4, "#7fd1ff")
    c.dot(x + 38, y + h / 2, 4, P["red_l"])
    x, y, w, h = f.r(f.w - 60, 56, 40, 22)
    c.rect(x, y, w, h, P["paper"], r=2, sw=2.4, hl=False)
    c.line(x + 6, y + 8, x + w - 6, y + 8, "#a79f89", w=2)
    c.line(x + 6, y + 14, x + w - 14, y + 14, "#a79f89", w=2)
    return c


# ------------------------------------------------------------------ smuggler dock (3x3 pad)
@tex("Things/HH/Underworld/SmugglerDock", "single", (3, 3), doc="Bare landing pad with hazard corners, a shaded lamp and mooring bollards, 3x3")
def smuggerdock(view, cells, masked):
    c = canvas_for_(cells, "south", masked)
    W = c.W
    c.rect(8, 8, W - 16, W - 16, "#3a3e47", r=16, sw=4.4)
    c.rect(22, 22, W - 44, W - 44, "#4a4f5a", r=10, sw=0, hl=False, sh=False)
    c.raw('<circle cx="%s" cy="%s" r="62" fill="none" stroke="#e0b44a" stroke-width="5" stroke-dasharray="20 12"/>' % (W / 2, W / 2))
    c.circle(W / 2, W / 2, 14, "#3a3e47", sw=0, hl=False, sh=False)
    for (x, y, sx, sy) in ((16, 16, 1, 1), (W - 16, 16, -1, 1), (16, W - 16, 1, -1), (W - 16, W - 16, -1, -1)):
        for i in range(3):
            d = i * 16
            c.poly([(x + sx * d, y + sy * (d + 12)), (x + sx * (d + 12), y + sy * d), (x + sx * (d + 22), y + sy * d), (x + sx * d, y + sy * (d + 22))], "#e0b44a" if i % 2 == 0 else "#2a2a2e", sw=0, hl=False)
    for (x, y) in ((W / 2 - 40, W - 30), (W / 2 + 40, W - 30)):
        c.circle(x, y, 9, P["iron"], sw=3.2)
        c.circle(x, y, 4, P["dsteel"], sw=0, hl=False, sh=False)
    for (x, y) in ((50, 56), (W - 46, 120), (70, W - 48), (W - 60, W - 70)):
        c.dot(x, y, 2.4, "#6e7380")
    for k in (1, 2):
        c.line(22, 22 + k * (W - 44) / 3, W - 22, 22 + k * (W - 44) / 3, "#3a3e47", w=2.4)
        c.line(22 + k * (W - 44) / 3, 22, 22 + k * (W - 44) / 3, W - 22, "#3a3e47", w=2.4)
    # shaded lamp on a short post, top-right
    lx, ly = W - 44, 44
    c.glow(lx, ly + 10, 70, "#ffbf5a", .5)
    c.rect(lx - 3, ly, 6, 40, P["iron"], r=2, sw=2.8, hl=False, sh=False)
    c.poly([(lx - 20, ly + 6), (lx - 8, ly - 18), (lx + 8, ly - 18), (lx + 20, ly + 6)], "#2c5a45", sw=3.4, hl=False)
    c.dot(lx, ly + 8, 5, "#ffe9a8")
    # coiled rope
    c.circle(48, W - 44, 16, "#b79a62", sw=3.2, hl=False, sh=False)
    c.circle(48, W - 44, 8, "#4a4f5a", sw=2.4, hl=False, sh=False)
    return c
