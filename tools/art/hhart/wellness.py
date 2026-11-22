"""wellness textures."""
from .style import *
from .parts import *
from .registry import tex
from .casino import canvas_for_
from .inn import _icon, person


# ------------------------------------------------------------------ hot tub
@tex("Things/HH/Wellness/HotTub", "multi", (2, 2), doc="Round wooden hot tub with heater, towels and steam, 2x2")
def hottub(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    cx, cy = 128, 124
    c.ellipse(cx, cy + 16, 108, 102, dark(P["wood"], .3), sw=3.8, hl=False, sh=False)
    c.ellipse(cx, cy, 108, 102, P["wood_l"], sw=3.8)
    for i in range(24):
        a = math.radians(i * 15)
        c.line(cx + math.cos(a) * 94, cy + math.sin(a) * 88, cx + math.cos(a) * 106, cy + math.sin(a) * 100, dark(P["wood_l"], .35), w=2.4)
    c.ellipse(cx, cy, 88, 82, "#2a6f93", sw=3.4, hl=False, sh=False)
    c.ellipse(cx, cy + 3, 80, 74, "#59b0d4", sw=0, hl=False, sh=False)
    for k, (dx, dy, r) in enumerate(((-30, -20, 7), (22, -34, 5), (40, 10, 8), (-10, 26, 6), (-46, 20, 5), (8, -4, 4), (60, -14, 4))):
        c.circle(cx + dx, cy + dy, r, "#c8ecf7", sw=1.6, hl=False, sh=False)
    c.pline([(cx - 60, cy - 6), (cx - 40, cy - 12), (cx - 20, cy - 6)], "#a7dcee", 3)
    c.pline([(cx + 20, cy + 40), (cx + 44, cy + 34), (cx + 64, cy + 40)], "#a7dcee", 3)
    f = Frame(view, (10, 10, 236, 236))
    # heater with a pipe into the tub (local top-right), towels (bottom-left)
    c.rect(*f.r(f.w - 62, 6, 44, 40), P["dsteel"], r=6, sw=3.2)
    c.rect(*f.r(f.w - 54, 14, 14, 14), P["flame"], r=3, sw=1.8, hl=False, sh=False)
    c.dot(*f.pt(f.w - 28, 20), 3.6, P["emerald"])
    c.line(*f.pt(f.w - 50, 46), *f.pt(f.w - 76, 62), OUT, w=9)
    c.line(*f.pt(f.w - 50, 46), *f.pt(f.w - 76, 62), P["copper"], w=5)
    for k in range(3):
        c.rect(*f.r(8, f.h - 52 + k * 0, 40, 14) if False else f.r(8, f.h - 46 + k * 10, 38, 11), ["#e9e4d4", "#9fc9d4", "#e9e4d4"][k], r=4, sw=2.4)
    c.steam(cx - 10, cy - 30, 40, 3, 10)
    return c


# ------------------------------------------------------------------ massage table
@tex("Things/HH/Wellness/MassageTable", "multi", (2, 1), doc="Padded massage table with face cradle, towel roll and oil bottles, 2x1")
def massagetable(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    f = slab(c, view, P["wood"], apron=18, m=None)
    pad = "#5fa8a0"
    c.rect(*f.r(8, 18, f.w - 16, f.h - 22), pad, r=12, sw=3.2)
    c.rect(*f.r(14, f.h / 2 - 6, 34, 30), dark(pad, .3), r=9, sw=2.8, hl=False)
    cx, cy = f.pt(31, f.h / 2 + 8)
    c.ellipse(cx, cy, 8, 10, "#1b1512", sw=2, hl=False, sh=False)
    # towel roll at the foot
    c.rect(*f.r(f.w - 50, f.h / 2 - 2, 36, 20), P["cream"], r=9, sw=2.8)
    c.line(*f.pt(f.w - 44, f.h / 2 + 8), *f.pt(f.w - 22, f.h / 2 + 8), "#cdbf99", w=2)
    # oil bottles on the shelf along the back edge
    for k, col in enumerate((P["amber"], P["leaf"], P["violet"], P["red_l"])):
        x, y = f.pt(70 + k * 18, 8)
        c.circle(x, y, 6, col, sw=2.2)
        c.circle(x, y, 2.4, light(col, .5), sw=0, hl=False, sh=False)
    return c


# ------------------------------------------------------------------ sauna stove
@tex("Things/HH/Wellness/SaunaStove", "single", (1, 1), doc="Stone-lined sauna stove with a heap of hot stones and a water bucket, 1x1")
def saunastove(view, cells, masked):
    c = canvas_for_(cells, "south", masked)
    body = P["iron"]
    c.rect(66, 0, 14, 28, P["dsteel"], r=3, sw=3, hl=False)  # flue
    c.rect(22, 38, 84, 80, body, r=10, sw=3.8)
    c.rect(26, 30, 76, 20, light(body, .16), r=7, sw=3)
    # stones heap
    for (x, y, r, col) in ((40, 30, 12, "#8d8678"), (62, 26, 14, "#a39b8a"), (86, 30, 12, "#8d8678"), (50, 18, 9, "#b3ab99"), (76, 18, 10, "#8d8678")):
        c.circle(x, y, r, col, sw=2.8)
    # door with fire window
    c.rect(36, 62, 56, 44, P["dsteel"], r=6, sw=3, hl=False)
    c.rect(44, 70, 40, 24, "#3a1d12", r=3, sw=2.2, hl=False, sh=False)
    c.glow(64, 86, 24, P["flame"], .6)
    flame(c, 64, 84, .42)
    c.rect(86, 80, 4, 12, P["steel"], r=2, sw=1.6, hl=False, sh=False)
    # bucket and ladle
    c.rect(8, 92, 22, 24, P["wood"], r=3, sw=2.8)
    c.rect(8, 98, 22, 4, P["iron"], r=1, sw=0, hl=False, sh=False)
    c.line(14, 92, 24, 80, OUT, w=6)
    c.line(14, 92, 24, 80, P["wood_l"], w=2.6)
    c.steam(64, 8, 20, 2, 7)
    return c


# ------------------------------------------------------------------ guest altar (DLC)
@tex("Things/HH/Dlc/GuestAltar", "single", (1, 1), doc="Small travellers' altar: stone slab, violet cloth, candles and a donation bowl, 1x1")
def guestaltar(view, cells, masked):
    c = canvas_for_(cells, "south", masked)
    f = slab(c, "south", P["stone"], apron=22, m=None)
    c.rect(*f.r(f.w * .22, 0, f.w * .56, f.h + 6), P["violet"], r=4, sw=2.8)
    c.line(*f.pt(f.w * .22 + 4, 8), *f.pt(f.w * .78 - 4, 8), light(P["violet"], .5), w=2)
    for fx in (.12, .88):
        x, y = f.pt(f.w * fx, f.h * .55)
        c.rect(x - 6, y - 8, 12, 26, P["cream"], r=4, sw=2.6)
        c.glow(x, y - 16, 20, "#ffd27a", .5)
        flame(c, x, y - 18, .26)
    bx, by = f.pt(f.w * .5, f.h * .62)
    c.ellipse(bx, by, 17, 10, P["gold"], sw=3)
    c.ellipse(bx, by - 2, 12, 6, "#6b4a1f", sw=0, hl=False, sh=False)
    coin(c, bx - 4, by - 4, 5)
    coin(c, bx + 5, by - 3, 5)
    c.circle(*f.pt(f.w * .5, f.h * .16), 6, P["yolk"], sw=2.2)
    return c
