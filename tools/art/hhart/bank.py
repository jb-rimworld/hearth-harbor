"""Silverline bank: ATM, teller counter, night safe, brand logos."""
from .style import *
from .parts import *
from .registry import tex
from .casino import canvas_for_
from .inn import _icon

EM = P["emerald"]


def bill(c, x, y, w, h):
    c.rect(x, y, w, h, "#b9d6a8", r=1.5, sw=1.6, hl=False, sh=False)
    c.circle(x + w / 2, y + h / 2, h * .26, "#6f9a5d", sw=0, hl=False, sh=False)


@tex("Things/HH/Bank/ATM", "multi", (1, 1), doc="Silverline cash machine, 1x1")
def atm(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    body = "#9aa0a4"
    face = cabinet(c, view, body, cap=20, margin=8, side_accent=dark(body, .3))
    x, y, w, h = face
    if view == "south":
        c.rect(x + 2, y - 2, w - 4, 12, EM, r=3, sw=2.4, hl=True)
        shield(c, x + w / 2 - 6, y - 1, 12, 11, P["paper"], sw=1.2)
        c.rect(x + 2, y + 14, w - 4, 34, "#1d2f2a", r=4, sw=3, hl=False, sh=False)
        c.rect(x + 7, y + 19, w - 14, 24, "#5fb890", r=2, sw=0, hl=False, sh=False)
        for i in range(3):
            c.line(x + 12, y + 25 + i * 7, x + w - 12 - i * 10, y + 25 + i * 7, "#2f6e55", w=2.2)
        # keypad
        for i in range(3):
            for j in range(3):
                c.rect(x + 8 + i * 13, y + 54 + j * 10, 10, 7, P["steel"], r=2, sw=1.8, hl=False, sh=False)
        c.rect(x + w - 34, y + 54, 26, 6, "#14100e", r=2, sw=1.6, hl=False, sh=False)  # card slot
        c.rect(x + 4, y + h - 24, w - 8, 18, "#1b1512", r=3, sw=2.6, hl=False, sh=False)
        bill(c, x + w / 2 - 10, y + h - 21, 20, 8)
    elif view == "north":
        for i in range(5):
            c.line(x + 4, y + 10 + i * 14, x + w - 4, y + 10 + i * 14, dark(body, .3), w=2.4)
        c.rect(x + w / 2 - 8, y + h - 26, 16, 12, P["iron"], r=3, sw=2.2)
    else:
        for i in range(4):
            c.line(x + 4, y + 14 + i * 18, x + w - 4, y + 14 + i * 18, dark(body, .3), w=2.4)
        c.rect(x + 8, y + 4, 12, 20, EM, r=3, sw=2.2)
    return c


@tex("Things/HH/Bank/BankCounter", "multi", (3, 1), masked=True, doc="Teller counter with glass panes, 3x1, stuffable top")
def bankcounter(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    f = slab(c, view, "#d4cfc2", apron=18, m="a")
    # glass partition along the back edge
    n = 3
    gap = 6
    pw = (f.w - gap * (n + 1)) / n
    for k in range(n):
        x, y, w, h = f.r(gap + k * (pw + gap), 4, pw, 22)
        c.rect(x, y, w, h, P["glass"], r=3, sw=2.8, hl=False, sh=False, m=None)
        c.line(x + 6, y + h - 5, x + 6 + 14, y + 5, "#e8f6f8", w=3)
        c.line(x + 22, y + h - 5, x + 22 + 8, y + 7, "#e8f6f8", w=2)
    # per window: ledger / tray / pen
    for k in range(n):
        cx0 = gap + k * (pw + gap)
        if k == 0:
            c.rect(*f.r(cx0 + pw * .14, 40, pw * .5, f.h - 52), P["paper"], r=3, sw=2.4, m=None)
            for j in range(3):
                c.line(*f.pt(cx0 + pw * .22, 52 + j * 10), *f.pt(cx0 + pw * .56, 52 + j * 10), "#a79f89", w=1.4)
            c.line(*f.pt(cx0 + pw * .7, 44), *f.pt(cx0 + pw * .78, 72), P["dsteel"], w=3.4)
        elif k == 1:
            c.rect(*f.r(cx0 + pw * .15, 42, pw * .5, 34), P["dsteel"], r=4, sw=2.6, m=None)
            coin(c, *f.pt(cx0 + pw * .3, 58), 8, P["silver"])
            coin(c, *f.pt(cx0 + pw * .5, 56), 8)
            c.rect(*f.r(cx0 + pw * .7, 44, 18, 28), EM, r=3, sw=2.4, m=None)  # stamp
        else:
            c.rect(*f.r(cx0 + pw * .12, 44, pw * .22, 26), "#c3b28a", r=3, sw=2.4, m=None)
            c.rect(*f.r(cx0 + pw * .4, 40, pw * .22, 30), "#d6c69a", r=3, sw=2.4, m=None)
            c.circle(*f.pt(cx0 + pw * .78, 58), 11, P["gold"], sw=2.4, m=None)
    return c


@tex("Things/HH/Bank/NightSafe", "multi", (1, 1), doc="Night safe: steel safe with dial and deposit chute, 1x1")
def nightsafe(view, cells, masked):
    c = canvas_for_(cells, view, masked)
    body = P["dsteel"]
    face = cabinet(c, view, body, cap=24, margin=8, side_accent=dark(body, .15))
    x, y, w, h = face
    # deposit slot on top cap
    c.rect(c.W / 2 - 20, 15, 40, 8, "#0f0b0a", r=3, sw=2, hl=False, sh=False)
    if view == "south":
        cx, cy = x + w / 2, y + h * .48
        c.circle(cx, cy, 34, P["iron"], sw=3.6)
        c.circle(cx, cy, 26, dark(P["iron"], .2), sw=2.4, hl=False, sh=False)
        c.circle(cx, cy, 12, P["steel"], sw=2.8)
        for a in range(0, 360, 90):
            c.line(cx, cy, cx + math.cos(math.radians(a + 45)) * 24, cy + math.sin(math.radians(a + 45)) * 24, P["steel"], w=4.4)
            c.line(cx, cy, cx + math.cos(math.radians(a + 45)) * 24, cy + math.sin(math.radians(a + 45)) * 24, OUT, w=1.2)
        c.circle(cx, cy, 4.5, EM, sw=1.6, hl=False, sh=False)
        for a in range(0, 360, 60):
            c.circle(cx + math.cos(math.radians(a)) * 30, cy + math.sin(math.radians(a)) * 30, 2.2, P["steel"], sw=0, hl=False, sh=False)
        c.circle(x + w - 10, y + 10, 4.2, EM, sw=1.6, hl=False, sh=False)
        c.rect(x + 2, y + h * .48 - 10, 6, 20, P["steel"], r=2, sw=2, hl=False, sh=False)
    else:
        for i in range(4):
            c.line(x + 6, y + 14 + i * 20, x + w - 6, y + 14 + i * 20, dark(body, .35), w=2.6)
    return c


# ---------------------------------------------------------------- brand logos (128 px, flat)
@tex("UI/HH/Bank/Logo_SL_SilverlineMutual", "single", (1, 1), ppc=128, grain=0, doc="Logo: Silverline Mutual (emerald shield, silver sweep)")
def logo_mutual(view, cells, masked):
    c = _icon(cells, masked)
    c.poly(shield_pts(16, 10, 96, 108), EM, sw=4)
    c.poly(shield_pts(26, 20, 76, 88), dark(EM, .12), sw=0)
    c.pline([(36, 82), (52, 62), (66, 72), (92, 40)], OUT, 12)
    c.pline([(36, 82), (52, 62), (66, 72), (92, 40)], P["silver"], 7)
    c.circle(92, 40, 7.5, P["silver"], sw=3)
    return c


@tex("UI/HH/Bank/Logo_SL_TooBigToFail", "single", (1, 1), ppc=128, grain=0, doc="Logo: Too Big To Fail (colossal bank front, cracked)")
def logo_tbtf(view, cells, masked):
    c = _icon(cells, masked)
    nv = P["navy"]
    c.circle(64, 64, 54, nv, sw=4)
    c.poly([(26, 50), (64, 26), (102, 50)], P["stone"], sw=3.4)
    c.rect(26, 50, 76, 8, P["paper"], r=2, sw=2.6)
    for i in range(4):
        c.rect(32 + i * 20, 60, 10, 34, P["paper"], r=2, sw=2.6)
    c.rect(22, 94, 84, 10, P["stone"], r=2, sw=2.8)
    c.pline([(64, 28), (58, 48), (70, 62), (60, 80), (68, 102)], OUT, 8)
    c.pline([(64, 28), (58, 48), (70, 62), (60, 80), (68, 102)], P["red_l"], 4)
    return c


@tex("UI/HH/Bank/Logo_SL_ImperialTreasury", "single", (1, 1), ppc=128, grain=0, doc="Logo: Imperial Treasury (crown and laurels)")
def logo_imperial(view, cells, masked):
    c = _icon(cells, masked)
    vi = P["violet"]
    c.circle(64, 64, 54, vi, sw=4)
    c.circle(64, 64, 44, dark(vi, .15), sw=0)
    for sx in (-1, 1):
        for i in range(5):
            y = 98 - i * 14
            x = 64 + sx * (30 + (i if i < 3 else 6 - i) * 3)
            c.ellipse(x, y, 5, 9, P["leaf"], sw=2, hl=False, sh=False)
    c.poly([(34, 78), (30, 44), (48, 58), (64, 36), (80, 58), (98, 44), (94, 78)], P["gold"], sw=3.6)
    c.rect(34, 78, 60, 10, P["gold"], r=3, sw=3.2)
    for x in (30, 64, 98):
        c.circle(x, 42 if x != 64 else 34, 5.5, P["red_l"], sw=2.4)
    c.circle(64, 83, 3.5, P["red_l"], sw=0, hl=False, sh=False)
    return c


@tex("UI/HH/Bank/Logo_SL_OrbitalExchange", "single", (1, 1), ppc=128, grain=0, doc="Logo: Orbital Exchange (planet, ring, coin moon)")
def logo_orbital(view, cells, masked):
    c = _icon(cells, masked)
    bl = P["blue"]
    c.circle(64, 64, 54, P["navy"], sw=4)
    for (x, y, r) in ((26, 34, 2), (100, 28, 2.4), (96, 100, 2), (30, 96, 2.4), (50, 20, 1.8)):
        c.dot(x, y, r, "#fff3c0")
    c.circle(64, 64, 26, bl, sw=3.6)
    c.pline([(46, 56), (60, 48)], light(bl, .5), 3)
    c.raw('<ellipse cx="64" cy="68" rx="50" ry="13" fill="none" stroke="#1c1411" stroke-width="10" transform="rotate(-18 64 68)"/>')
    c.raw('<ellipse cx="64" cy="68" rx="50" ry="13" fill="none" stroke="#c9ccd1" stroke-width="5" transform="rotate(-18 64 68)"/>')
    coin(c, 100, 38, 10)
    return c


@tex("UI/HH/Bank/Logo_SL_BlackLedger", "single", (1, 1), ppc=128, grain=0, doc="Logo: Black Ledger (dark book with red ribbon)")
def logo_ledger(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(24, 14, 80, 100, "#2c2a2e", r=8, sw=4)
    c.rect(24, 14, 14, 100, "#17151a", r=6, sw=3.6, hl=False)
    c.rect(48, 30, 46, 40, P["paper"], r=3, sw=2.6, hl=False)
    for j in range(3):
        c.line(54, 42 + j * 9, 88, 42 + j * 9, "#8d8678", w=2)
    c.poly([(80, 14), (80, 84), (89, 76), (98, 84), (98, 14)], P["red_l"], sw=3, hl=False)
    for (x, y) in ((28, 18), (100, 18), (28, 110), (100, 110)):
        c.circle(x, y, 4.2, P["silver"], sw=2, hl=False, sh=False)
    c.rect(46, 84, 34, 14, P["silver"], r=3, sw=2.6, hl=False)
    return c


@tex("UI/HH/Bank/Logo_SL_CompoundInterest", "single", (1, 1), ppc=128, grain=0, doc="Logo: Compound Interest (growing coin stacks, rising curve)")
def logo_compound(view, cells, masked):
    c = _icon(cells, masked)
    gr = P["green"]
    c.rect(10, 10, 108, 108, gr, r=22, sw=4)
    c.rect(18, 18, 92, 92, dark(gr, .12), r=16, sw=0, hl=False, sh=False)
    for i, (x, n) in enumerate(((34, 2), (62, 3), (90, 5))):
        coin_stack(c, x, 100 - 0, 11, n)
    c.pline([(26, 52), (52, 48), (74, 34), (96, 14)], OUT, 8)
    c.pline([(26, 52), (52, 48), (74, 34), (96, 14)], P["paper"], 3.6)
    c.poly([(86, 12), (106, 10), (98, 30)], P["paper"], sw=2.6)
    return c


@tex("UI/HH/Insurance/ClawbackLogo", "single", (1, 1), ppc=128, grain=0, doc="Logo: Clawback Mutual (shield with claw marks)")
def logo_clawback(view, cells, masked):
    c = _icon(cells, masked)
    rd = "#a3302a"
    c.poly(shield_pts(14, 8, 100, 112), "#3d3a40", sw=4)
    c.poly(shield_pts(24, 18, 80, 92), rd, sw=0)
    for k, dx in enumerate((-22, 0, 22)):
        d = "M%s 32 Q%s 64 %s 98" % (46 + dx + 12, 54 + dx, 40 + dx + 2)
        c.raw('<path d="%s" fill="none" stroke="#1c1411" stroke-width="12" stroke-linecap="round"/>' % d)
        c.raw('<path d="%s" fill="none" stroke="#e9e4d4" stroke-width="6" stroke-linecap="round"/>' % d)
    return c
