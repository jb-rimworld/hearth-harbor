"""UI icons for the integration modules: commands, casino, finance, guest wings, genes, issues, venue types, Ottoline.
All 128 px (ppc 128), flat, readable at 32-64 px: bold silhouettes, 3.5-4 unit outline, no text."""
from .style import *
from .parts import *
from .registry import tex
from .inn import _icon, person
from . import venues as _venues


def tline(c, pts, w, col=None):
    c.pline(pts, OUT, w + 5)
    c.pline(pts, col or P["paper"], w)


def tpath(c, d, w, col, ow=5):
    c.raw('<path d="%s" fill="none" stroke="%s" stroke-width="%s" stroke-linecap="round" stroke-linejoin="round"/>' % (d, OUT, f2(w + ow)))
    c.raw('<path d="%s" fill="none" stroke="%s" stroke-width="%s" stroke-linecap="round" stroke-linejoin="round"/>' % (d, col, f2(w)))


def disc(c, col, r=58):
    c.circle(64, 64, r, col, sw=4)
    c.circle(64, 64, r - 8, dark(col, .18), sw=0, hl=False, sh=False)


def star(c, x, y, r, col="#fff3c0", sw=2.4):
    pts = []
    for i in range(10):
        a = -math.pi / 2 + i * math.pi / 5
        rr = r if i % 2 == 0 else r * .45
        pts.append((x + math.cos(a) * rr, y + math.sin(a) * rr))
    c.poly(pts, col, sw=sw, hl=False)


def arrow(c, x1, y1, x2, y2, col, w=9, head=18):
    a = math.atan2(y2 - y1, x2 - x1)
    bx, by = x2 - math.cos(a) * head * .6, y2 - math.sin(a) * head * .6
    tline(c, [(x1, y1), (bx, by)], w, col)
    px, py = -math.sin(a), math.cos(a)
    c.poly([(x2, y2), (x2 - math.cos(a) * head + px * head * .7, y2 - math.sin(a) * head + py * head * .7),
            (x2 - math.cos(a) * head - px * head * .7, y2 - math.sin(a) * head - py * head * .7)], col, sw=3.2, hl=False)


def newspaper(c, col):
    c.rect(14, 24, 100, 84, P["paper"], r=6, sw=4)
    c.rect(14, 24, 100, 22, col, r=6, sw=4, hl=False)
    c.rect(22, 30, 40, 10, P["paper"], r=2, sw=0, hl=False, sh=False)


# ======================================================================== commands (venue sign)
@tex("UI/HH/Venues/Cmd_Hours", "single", (1, 1), ppc=128, grain=0, doc="Command icon: opening hours (clock)")
def cmd_hours(view, cells, masked):
    c = _icon(cells, masked)
    c.circle(64, 64, 54, P["paper"], sw=4.4)
    c.circle(64, 64, 44, P["cream"], sw=0, hl=False, sh=False)
    for i in range(12):
        a = math.radians(i * 30)
        l = 9 if i % 3 == 0 else 5
        c.line(64 + math.cos(a) * 38, 64 + math.sin(a) * 38, 64 + math.cos(a) * (38 - l), 64 + math.sin(a) * (38 - l), P["dsteel"], w=4 if i % 3 == 0 else 3)
    tline(c, [(64, 64), (64, 34)], 7, P["dsteel"])
    tline(c, [(64, 64), (86, 76)], 7, P["red_l"])
    c.circle(64, 64, 7, P["dsteel"], sw=3, hl=False, sh=False)
    return c


@tex("UI/HH/Venues/Cmd_Price", "single", (1, 1), ppc=128, grain=0, doc="Command icon: set price (tag with coin)")
def cmd_price(view, cells, masked):
    c = _icon(cells, masked)
    c.poly([(18, 30), (74, 30), (112, 68), (74, 106), (18, 106)], P["amber"], sw=4.2, hl=True)
    c.circle(36, 68, 9, P["cream"], sw=3.4, hl=False, sh=False)
    coin(c, 74, 68, 22)
    c.line(64, 68, 84, 68, dark(P["gold"], .45), w=4)
    return c


@tex("UI/HH/Venues/Cmd_Rename", "single", (1, 1), ppc=128, grain=0, doc="Command icon: rename (sign board with pencil)")
def cmd_rename(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(10, 34, 82, 58, P["cream"], r=8, sw=4.2)
    c.rect(18, 42, 66, 10, P["amber"], r=3, sw=0, hl=False, sh=False)
    for i, w in enumerate((56, 42)):
        c.line(20, 66 + i * 12, 20 + w, 66 + i * 12, "#b9ab84", w=5)
    # pencil, 45 degrees
    c.poly([(70, 98), (74, 78), (106, 38), (118, 48), (86, 88)], P["yolk"], sw=3.6, hl=False)
    c.poly([(106, 38), (112, 30), (122, 40), (118, 48)], P["red_l"], sw=3.2, hl=False)
    c.poly([(70, 98), (74, 78), (86, 88)], P["cream"], sw=3, hl=False)
    c.poly([(70, 98), (71.5, 90.5), (77, 94)], P["dsteel"], sw=0, hl=False)
    return c


@tex("UI/HH/Venues/Cmd_TakeGoods", "single", (1, 1), ppc=128, grain=0, doc="Command icon: take goods (crate with arrow out)")
def cmd_takegoods(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(16, 62, 80, 52, P["wood"], r=6, sw=4.2)
    for x in (34, 56, 78):
        c.line(x, 66, x, 110, dark(P["wood"], .35), w=3.4)
    c.rect(16, 62, 80, 12, P["wood_l"], r=5, sw=3.4)
    arrow(c, 82, 48, 112, 18, P["emerald"], w=9, head=22)
    c.circle(30, 40, 11, P["tomato"], sw=3.2)
    c.circle(54, 44, 10, P["leaf"], sw=3.2)
    return c


# ======================================================================== casino
@tex("UI/HH/Casino/OpenToGuests", "single", (1, 1), ppc=128, grain=0, doc="Command icon: open to guests (open door, welcome arrow)")
def casino_open(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(54, 14, 62, 102, P["wood_d"], r=6, sw=4.2)
    c.poly([(54, 14), (92, 26), (92, 104), (54, 116)], P["wood"], sw=3.8, hl=False)
    c.circle(84, 66, 4.6, P["gold"], sw=2.2, hl=False, sh=False)
    arrow(c, 6, 66, 50, 66, P["emerald"], w=11, head=24)
    return c


@tex("UI/HH/Casino/Practice", "single", (1, 1), ppc=128, grain=0, doc="Command icon: practice with house chips (dashed pretend chip and card)")
def casino_practice(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(52, 14, 52, 74, P["paper"], r=7, sw=4)
    c.circle(78, 51, 14, P["red_l"], sw=0, hl=False, sh=False)
    c.circle(44, 84, 34, P["cream"], sw=4.2)
    c.raw('<circle cx="44" cy="84" r="22" fill="none" stroke="%s" stroke-width="5" stroke-dasharray="9 7"/>' % P["blue"])
    c.circle(44, 84, 10, P["blue"], sw=3, hl=False, sh=False)
    return c


@tex("UI/HH/Casino/Simulate", "single", (1, 1), ppc=128, grain=0, doc="Command icon: simulate (die and fast-forward)")
def casino_simulate(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(10, 26, 60, 60, P["paper"], r=13, sw=4)
    for px, py in ((.28, .28), (.72, .28), (.5, .5), (.28, .72), (.72, .72)):
        c.circle(10 + px * 60, 26 + py * 60, 6, P["felt"], sw=2.2, hl=False, sh=False)
    c.poly([(62, 70), (96, 92), (62, 114)], P["emerald"], sw=4, hl=False)
    c.poly([(92, 70), (122, 92), (92, 114)], P["emerald"], sw=4, hl=False)
    return c


@tex("UI/HH/Casino/Volatility", "single", (1, 1), ppc=128, grain=0, doc="Command icon: volatility (jagged chart)")
def casino_volatility(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(10, 14, 108, 100, P["dsteel"], r=12, sw=4.2)
    c.line(24, 100, 106, 100, "#6e7380", w=3.4)
    c.line(24, 100, 24, 28, "#6e7380", w=3.4)
    pts = [(26, 84), (42, 40), (56, 90), (72, 30), (86, 76), (100, 36)]
    tline(c, pts, 7, P["yolk"])
    for p in pts[1::2]:
        c.circle(p[0], p[1], 6, P["red_l"], sw=2.6, hl=False, sh=False)
    return c


@tex("UI/HH/Casino/VenueCasino", "single", (1, 1), ppc=128, grain=0, doc="Venue type icon: casino (same emblem as the Venues type icon)")
def venue_casino(view, cells, masked):
    return _venues.t_casino(view, cells, masked)


# ======================================================================== bank / finance
@tex("UI/HH/Bank/DirectDeposit", "single", (1, 1), ppc=128, grain=0, doc="Command icon: direct deposit (arrow into a coin purse)")
def direct_deposit(view, cells, masked):
    c = _icon(cells, masked)
    lea = "#8c5a38"
    c.rect(14, 64, 100, 52, lea, r=14, sw=4.2)
    c.path("M14 84 Q14 64 34 64 H94 Q114 64 114 84 Z", light(lea, .14), sw=4.2)
    c.circle(64, 92, 10, P["gold"], sw=3.2)
    arrow(c, 64, 6, 64, 56, P["emerald"], w=14, head=26)
    coin(c, 100, 36, 13)
    return c


@tex("UI/HH/Bank/Ottoline", "single", (1, 1), ppc=128, grain=0, doc="Portrait: Clerk Ottoline (eyeshade, round glasses, hair bun)")
def ottoline(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(6, 6, 116, 116, "#2f5c4a", r=18, sw=4.2)
    # jacket + collar
    c.path("M14 122 Q14 92 44 86 L84 86 Q114 92 114 122 Z", "#1f3b32", sw=4)
    c.poly([(50, 86), (64, 104), (78, 86)], P["paper"], sw=3, hl=False)
    c.poly([(60, 98), (64, 92), (68, 98), (66, 112), (62, 112)], P["emerald"], sw=2.2, hl=False)
    # hair: bun + back
    c.circle(64, 22, 14, "#5a3524", sw=3.6)
    c.circle(64, 52, 36, "#5a3524", sw=4)
    # face
    c.ellipse(64, 58, 28, 32, P["cream"], sw=4)
    # eyeshade band
    c.poly([(34, 40), (94, 40), (100, 50), (28, 50)], "#6fbf8f", sw=3.4, hl=False)
    # glasses
    for x in (52, 76):
        c.circle(x, 62, 10, "#dcefee", sw=3.4, hl=False, sh=False)
        c.dot(x, 63, 2.8, OUT)
    c.line(62, 62, 66, 62, OUT, w=3)
    c.pline([(54, 82), (64, 87), (74, 82)], OUT, 3.4)
    c.circle(35, 62, 4, P["cream"], sw=2.6, hl=False, sh=False)
    c.circle(93, 62, 4, P["cream"], sw=2.6, hl=False, sh=False)
    return c


@tex("UI/HH/Finance/NewsChance", "single", (1, 1), ppc=128, grain=0, doc="Icon: chance news item (newspaper with a die)")
def news_chance(view, cells, masked):
    c = _icon(cells, masked)
    newspaper(c, P["blue"])
    for i in range(3):
        c.line(22, 58 + i * 12, 60, 58 + i * 12, "#a79f89", w=4)
    c.rect(66, 56, 44, 44, P["cream"], r=10, sw=3.6)
    for px, py in ((.3, .3), (.7, .3), (.5, .5), (.3, .7), (.7, .7)):
        c.circle(66 + px * 44, 56 + py * 44, 4.4, P["blue"], sw=1.8, hl=False, sh=False)
    return c


@tex("UI/HH/Finance/NewsYou", "single", (1, 1), ppc=128, grain=0, doc="Icon: news about you (newspaper with a portrait)")
def news_you(view, cells, masked):
    c = _icon(cells, masked)
    newspaper(c, P["amber"])
    for i in range(3):
        c.line(22, 76 + i * 11, 54, 76 + i * 11, "#a79f89", w=4)
    c.rect(62, 54, 44, 48, "#cfc5a8", r=6, sw=3.2)
    c.circle(84, 70, 9, P["amber"], sw=3, hl=False, sh=False)
    c.path("M66 100 Q66 82 84 82 Q102 82 102 100 Z", P["amber"], sw=3)
    return c


# ======================================================================== inn
@tex("UI/HH/Inn/GuestWingExpand", "single", (1, 1), ppc=128, grain=0, doc="Command icon: expand guest wing (room outline growing, plus)")
def wing_expand(view, cells, masked):
    c = _icon(cells, masked)
    c.raw('<rect x="14" y="40" width="64" height="64" rx="8" fill="%s" stroke="%s" stroke-width="5" stroke-dasharray="10 8"/>' % ("none", P["steel"]))
    c.rect(14, 54, 58, 50, P["cream"], r=8, sw=4.2)
    c.rect(22, 68, 42, 22, P["amber"], r=5, sw=3, hl=False)
    c.rect(22, 62, 14, 12, P["paper"], r=4, sw=2.4, hl=False, sh=False)
    c.circle(96, 36, 26, P["emerald"], sw=4.2)
    c.rect(82, 31, 28, 10, P["paper"], r=3, sw=0, hl=False, sh=False)
    c.rect(91, 22, 10, 28, P["paper"], r=3, sw=0, hl=False, sh=False)
    return c


@tex("UI/HH/Inn/GuestWingClear", "single", (1, 1), ppc=128, grain=0, doc="Command icon: clear guest wing (room with a cross)")
def wing_clear(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(14, 54, 72, 56, P["cream"], r=8, sw=4.2)
    c.rect(24, 72, 52, 24, P["amber"], r=5, sw=3, hl=False)
    c.rect(24, 64, 16, 14, P["paper"], r=4, sw=2.4, hl=False, sh=False)
    c.circle(92, 36, 28, P["red_l"], sw=4.2)
    tline(c, [(80, 24), (104, 48)], 8, P["paper"])
    tline(c, [(104, 24), (80, 48)], 8, P["paper"])
    return c


@tex("UI/HH/Inn/SendAway", "single", (1, 1), ppc=128, grain=0, doc="Command icon: send guest away (door with outgoing arrow)")
def send_away(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(14, 12, 58, 104, P["wood"], r=6, sw=4.2)
    c.rect(24, 24, 38, 38, P["wood_l"], r=3, sw=2.8, hl=False)
    c.rect(24, 70, 38, 36, P["wood_l"], r=3, sw=2.8, hl=False)
    c.circle(62, 68, 4.4, P["gold"], sw=2, hl=False, sh=False)
    arrow(c, 60, 64, 116, 64, P["red_l"], w=12, head=26)
    return c


# ======================================================================== venue types (city, underworld)
@tex("UI/HH/City/Type_Cinema", "single", (1, 1), ppc=128, grain=0, doc="Venue type icon: cinema (clapperboard)")
def t_cinema(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(12, 46, 104, 70, P["dsteel"], r=8, sw=4.2)
    c.rect(20, 78, 88, 30, "#3d4452", r=4, sw=0, hl=False, sh=False)
    for i in range(5):
        c.circle(30 + i * 17, 93, 6, P["yolk"], sw=2.2, hl=False, sh=False)
    # clapper arm, tilted open
    c.poly([(12, 40), (116, 24), (120, 42), (14, 58)], P["paper"], sw=4, hl=False)
    for i in range(5):
        x = 16 + i * 21
        c.poly([(x, 40.5 - i * 2.2), (x + 11, 38.8 - i * 2.2), (x + 3, 55.5 - i * 2.2), (x - 8, 57 - i * 2.2)], P["dsteel"], sw=0, hl=False)
    c.poly([(12, 40), (116, 24), (120, 42), (14, 58)], None, sw=4, hl=False)
    return c


@tex("UI/HH/City/Type_Gallery", "single", (1, 1), ppc=128, grain=0, doc="Venue type icon: gallery (framed picture)")
def t_gallery(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(10, 18, 108, 92, P["gold"], r=8, sw=4.4)
    c.rect(24, 32, 80, 64, "#bfe0e6", r=3, sw=3.4, hl=False, sh=False)
    c.poly([(24, 96), (50, 56), (66, 78), (80, 62), (104, 96)], P["green"], sw=3.4, hl=False)
    c.circle(84, 46, 8, P["yolk"], sw=2.8, hl=False, sh=False)
    return c


@tex("UI/HH/City/Type_Salon", "single", (1, 1), ppc=128, grain=0, doc="Venue type icon: salon (scissors and comb)")
def t_salon(view, cells, masked):
    c = _icon(cells, masked)
    # comb
    c.rect(22, 80, 90, 28, P["teal"], r=6, sw=4)
    for i in range(8):
        c.rect(26 + i * 10.5, 100, 6, 16, P["teal"], r=2, sw=2.6, hl=False, sh=False)
    # scissors
    tline(c, [(34, 20), (92, 74)], 9, P["steel"])
    tline(c, [(94, 20), (36, 74)], 9, P["silver"])
    for x in (32, 96):
        c.raw('<circle cx="%d" cy="86" r="12" fill="none" stroke="%s" stroke-width="13"/>' % (x - 2 if x < 60 else x + 2, OUT))
        c.raw('<circle cx="%d" cy="86" r="12" fill="none" stroke="%s" stroke-width="7"/>' % (x - 2 if x < 60 else x + 2, P["red_l"]))
    c.circle(64, 47, 5, P["dsteel"], sw=2.6, hl=False, sh=False)
    return c


@tex("UI/HH/Underworld/DrugLounge", "single", (1, 1), ppc=128, grain=0, doc="Venue type icon: back-room lounge (brazier with curling smoke and a leaf; no drug paraphernalia)")
def t_druglounge(view, cells, masked):
    c = _icon(cells, masked)
    disc(c, "#4b3a63", 58)
    c.path("M30 78 H98 Q96 108 64 112 Q32 108 30 78 Z", P["iron"], sw=4.2)
    c.rect(26, 72, 76, 12, P["dsteel"], r=5, sw=3.6)
    c.circle(64, 72, 7, P["flame"], sw=0)
    for x, ph in ((46, 0), (64, 1), (82, 0)):
        tpath(c, "M%d 66 q%d -12 0 -22 t0 -22" % (x, 10 if ph == 0 else -10), 5, "#d7cbe8", ow=4)
    c.ellipse(98, 40, 9, 5, P["leaf"], sw=2.6, hl=False, sh=False)
    return c


# ======================================================================== wellness
@tex("UI/HH/Wellness/ColonistsMayUse", "single", (1, 1), ppc=128, grain=0, doc="Command icon: colonists may use (colonist with a tick)")
def w_colonists(view, cells, masked):
    c = _icon(cells, masked)
    person(c, 48, 38, 20, P["amber"], sw=4)
    c.circle(92, 90, 26, P["emerald"], sw=4.2)
    tline(c, [(80, 90), (89, 100), (106, 78)], 8, P["paper"])
    return c


@tex("UI/HH/Wellness/GuestsMayUse", "single", (1, 1), ppc=128, grain=0, doc="Command icon: guests may use (visitor with a key)")
def w_guests(view, cells, masked):
    c = _icon(cells, masked)
    person(c, 48, 38, 20, P["blue"], sw=4)
    c.circle(92, 90, 26, P["gold"], sw=4.2)
    key(c, 92, 78, 38, P["paper"])
    return c


# ======================================================================== DLC: genes and issues
def _gene(c, col):
    disc(c, col, 58)


@tex("UI/HH/Dlc/Genes/Hospitable", "single", (1, 1), ppc=128, grain=0, doc="Gene icon: hospitable (house with an open door)")
def g_hospitable(view, cells, masked):
    c = _icon(cells, masked)
    _gene(c, P["amber"])
    c.poly([(24, 62), (64, 26), (104, 62)], P["red_l"], sw=4, hl=False)
    c.rect(32, 60, 64, 42, P["cream"], r=3, sw=4)
    c.poly([(54, 102), (54, 72), (76, 66), (76, 102)], P["wood_d"], sw=3.4, hl=False)
    c.rect(54, 72, 22, 30, "#ffd27a", r=2, sw=0, hl=False, sh=False)
    c.poly([(54, 102), (54, 72), (66, 70), (66, 104)], P["wood"], sw=3.2, hl=False)
    return c


@tex("UI/HH/Dlc/Genes/AddictionProof", "single", (1, 1), ppc=128, grain=0, doc="Gene icon: addiction-proof (shield with a crossed capsule)")
def g_addprf(view, cells, masked):
    c = _icon(cells, masked)
    _gene(c, P["navy"])
    c.poly(shield_pts(30, 20, 68, 84), P["emerald"], sw=4)
    c.poly([(40, 76), (68, 48), (82, 62), (54, 90)], P["paper"], sw=3.4, hl=False)
    c.poly([(68, 48), (76, 40), (90, 54), (82, 62)], P["red_l"], sw=3.2, hl=False)
    tline(c, [(34, 30), (94, 98)], 7, P["red_l"])
    return c


@tex("UI/HH/Dlc/Genes/Compulsive", "single", (1, 1), ppc=128, grain=0, doc="Gene icon: compulsive (die with a looping arrow)")
def g_compulsive(view, cells, masked):
    c = _icon(cells, masked)
    _gene(c, P["violet"])
    c.rect(40, 40, 48, 48, P["paper"], r=11, sw=3.8)
    for px, py in ((.3, .3), (.7, .3), (.5, .5), (.3, .7), (.7, .7)):
        c.circle(40 + px * 48, 40 + py * 48, 4.6, P["dsteel"], sw=0, hl=False, sh=False)
    tpath(c, "M98 56 A38 38 0 1 0 90 98", 7, P["yolk"])
    c.poly([(108, 48), (110, 72), (88, 62)], P["yolk"], sw=3.2, hl=False)
    return c


@tex("UI/HH/Dlc/Genes/Dexterous", "single", (1, 1), ppc=128, grain=0, doc="Gene icon: dexterous fingers (open hand with sparkle)")
def g_dexterous(view, cells, masked):
    c = _icon(cells, masked)
    _gene(c, P["teal"])
    sk = "#e9c9a0"
    for x, top in ((42, 38), (54, 28), (66, 26), (78, 32)):
        c.rect(x - 5.5, top, 11, 56 - (top - 28) * .1, sk, r=5.5, sw=3.4)
    c.poly([(36, 66), (24, 52), (32, 46), (46, 60)], sk, sw=3.4, hl=False)
    c.rect(36, 62, 52, 40, sk, r=12, sw=3.8)
    spark(c, 98, 34, 11)
    return c


@tex("UI/HH/Dlc/Genes/HighRoller", "single", (1, 1), ppc=128, grain=0, doc="Gene icon: high roller (crowned die)")
def g_highroller(view, cells, masked):
    c = _icon(cells, masked)
    _gene(c, P["felt"])
    c.rect(34, 52, 60, 52, P["paper"], r=12, sw=3.8)
    for px, py in ((.28, .3), (.72, .3), (.28, .7), (.72, .7), (.5, .5)):
        c.circle(34 + px * 60, 52 + py * 52, 5, P["red_l"], sw=0, hl=False, sh=False)
    c.poly([(36, 52), (32, 24), (48, 38), (64, 18), (80, 38), (96, 24), (92, 52)], P["gold"], sw=3.8)
    c.rect(36, 48, 56, 8, P["gold"], r=3, sw=3, hl=False)
    return c


@tex("UI/HH/Dlc/Genes/NumberSense", "single", (1, 1), ppc=128, grain=0, doc="Gene icon: number sense (abacus)")
def g_numsense(view, cells, masked):
    c = _icon(cells, masked)
    _gene(c, P["blue"])
    c.rect(26, 30, 76, 70, P["wood_l"], r=6, sw=4)
    cols = (P["red_l"], P["yolk"], P["emerald"], P["paper"])
    for i in range(4):
        y = 44 + i * 16
        c.line(30, y, 98, y, OUT, w=3.4)
        for k in range(3 + i % 2):
            c.circle(40 + k * 14 + (i * 9) % 20, y, 6.2, cols[i], sw=2.6, hl=False, sh=False)
    return c


@tex("UI/HH/Dlc/Genes/RefinedPalate", "single", (1, 1), ppc=128, grain=0, doc="Gene icon: refined palate (wine glass with a sparkle)")
def g_palate(view, cells, masked):
    c = _icon(cells, masked)
    _gene(c, P["red_d"])
    c.rect(58, 70, 12, 28, P["glass"], r=4, sw=3.4)
    c.rect(40, 98, 48, 10, P["steel"], r=5, sw=3.4)
    c.path("M36 24 H92 Q96 76 64 78 Q32 76 36 24 Z", "#dcefee", sw=4)
    c.path("M39 46 H89 Q89 72 64 74 Q39 72 39 46 Z", P["red_l"], sw=0)
    spark(c, 98, 30, 12)
    return c


@tex("UI/HH/Dlc/Genes/SilverTongue", "single", (1, 1), ppc=128, grain=0, doc="Gene icon: silver tongue (speech bubble with a silver coin)")
def g_silver(view, cells, masked):
    c = _icon(cells, masked)
    _gene(c, P["navy"])
    c.poly([(26, 32), (102, 32), (102, 86), (68, 86), (46, 106), (50, 86), (26, 86)], P["paper"], sw=4, hl=True)
    coin(c, 64, 58, 19, P["silver"])
    return c


@tex("UI/HH/Dlc/Genes/TirelessSmile", "single", (1, 1), ppc=128, grain=0, doc="Gene icon: tireless smile (smiley with a spark)")
def g_smile(view, cells, masked):
    c = _icon(cells, masked)
    _gene(c, P["amber"])
    c.circle(62, 66, 36, P["yolk"], sw=4)
    for x in (50, 74):
        c.ellipse(x, 56, 4.8, 7.5, OUT, sw=0, hl=False, sh=False)
    tpath(c, "M42 74 Q62 100 84 74", 5.5, OUT, ow=0)
    spark(c, 100, 28, 13)
    return c


@tex("UI/HH/Dlc/Issues/Indulgences", "single", (1, 1), ppc=128, grain=0, doc="Issue icon: indulgences (scroll with a wax seal and a coin)")
def i_indulg(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(26, 18, 76, 92, P["paper"], r=8, sw=4)
    c.rect(18, 12, 92, 14, P["bread"], r=7, sw=3.8)
    c.rect(18, 102, 92, 14, P["bread"], r=7, sw=3.8)
    for i in range(3):
        c.line(38, 44 + i * 12, 90, 44 + i * 12, "#a79f89", w=4)
    c.circle(78, 86, 14, P["red_l"], sw=3.6)
    c.circle(78, 86, 6, light(P["red_l"], .3), sw=0, hl=False, sh=False)
    coin(c, 38, 90, 14)
    return c


@tex("UI/HH/Dlc/Issues/PaidAscension", "single", (1, 1), ppc=128, grain=0, doc="Issue icon: paid ascension (stairs to a star, with a coin)")
def i_ascension(view, cells, masked):
    c = _icon(cells, masked)
    st = P["stone"]
    for i in range(4):
        c.rect(14 + i * 20, 104 - (i + 1) * 20, 100 - i * 20, 20, st if i % 2 else light(st, .12), r=3, sw=3.6, hl=True)
    star(c, 92, 26, 22, "#ffe58a", sw=3.6)
    coin(c, 38, 52, 14)
    return c


@tex("UI/HH/Venues/Cmd_Restock", "single", (1, 1), ppc=128, grain=0, doc="Command icon: restock (arrow down into a crate of goods)")
def cmd_restock(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(16, 66, 96, 50, P["wood"], r=6, sw=4.2)
    for x in (38, 64, 90):
        c.line(x, 70, x, 112, dark(P["wood"], .35), w=3.4)
    c.rect(16, 66, 96, 12, P["wood_l"], r=5, sw=3.4)
    c.circle(38, 62, 11, P["tomato"], sw=3.2)
    c.circle(90, 62, 10, P["leaf"], sw=3.2)
    arrow(c, 64, 6, 64, 54, P["emerald"], w=10, head=24)
    return c


@tex("UI/HH/Inn/DoorPolicy", "single", (1, 1), ppc=128, grain=0, doc="Command icon: door policy (door with a hanging board showing a tick and a cross)")
def door_policy(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(10, 12, 60, 104, P["wood"], r=6, sw=4.2)
    c.rect(20, 24, 40, 34, P["wood_l"], r=3, sw=2.8, hl=False)
    c.rect(20, 66, 40, 40, P["wood_l"], r=3, sw=2.8, hl=False)
    c.circle(60, 70, 4.4, P["gold"], sw=2, hl=False, sh=False)
    c.line(78, 22, 78, 34, P["iron"], w=3.4)
    c.line(116, 22, 116, 34, P["iron"], w=3.4)
    c.rect(72, 30, 48, 84, P["cream"], r=6, sw=4)
    tline(c, [(82, 56), (90, 66), (108, 44)], 7, P["emerald"])
    tline(c, [(84, 84), (108, 106)], 7, P["red_l"])
    tline(c, [(108, 84), (84, 106)], 7, P["red_l"])
    return c
