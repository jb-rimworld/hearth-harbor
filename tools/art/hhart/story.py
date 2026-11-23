"""1.1.0 content art: storyteller Hetty Hearthwell (F09), settings and module icons, comms console marks, mod icons (F34)."""
from .style import *
from .parts import *
from .registry import tex
from .inn import _icon, icon_bed, person
from .style import Canvas
from . import ui as _ui
from . import icons as _icons

EM = P["emerald"]


class CropCanvas(Canvas):
    """A canvas that draws in the coordinates of a bigger design and shows only the box (x, y, w, h) of it (tiny portrait = crop of the large one)."""

    def __init__(self, wc, hc, box, masked=False):
        super().__init__(wc, hc, masked)
        self.box = box

    def svg(self, mask=False):
        x, y, w, h = self.box
        items = self.mask if mask else self.body
        return '<svg xmlns="http://www.w3.org/2000/svg" viewBox="%s %s %s %s" width="%d" height="%d">%s</svg>' % (
            f2(x), f2(y), f2(w), f2(h), int(self.W), int(self.H), "".join(items))


# ============================================================================ Hetty Hearthwell
SKIN, SKIN_D = "#e7b38b", "#c98d68"
HAIR, HAIR_L, HAIR_D = "#a4532f", "#c8714a", "#6e3620"
VEST, BLOUSE, APRON = "#8a3b34", "#f3ead2", "#e9e4d4"
SHAWL, SHAWL_D = P["amber"], "#b0602c"


def hetty(c, lantern_glow=True):
    """Waist-up portrait in a 580x620 design box: a warm innkeeper with a copper bun (pencil stuck in it), amber shawl, apron, a lantern and the guest ledger."""
    cx = 290
    # warm light behind her (UI art: soft glows are allowed, the figure itself stays flat)
    c.glow(cx, 300, 300, "#ffcf7a", .22)
    if lantern_glow:
        c.glow(164, 560, 104, "#ffd27a", .42)
    # ---- back hair and bun (head group sits a little lower than the design grid: shorter neck)
    c.raw('<g transform="translate(0 24)">')
    c.ellipse(cx, 206, 96, 102, HAIR_D, sw=4.5, hl=False)
    c.circle(306, 110, 48, HAIR, sw=4.5)
    for (a, b) in ((272, 96), (290, 84), (318, 88)):
        c.raw('<path d="M%d %d q24 10 40 34" fill="none" stroke="%s" stroke-width="4" stroke-linecap="round"/>' % (a, b, HAIR_D))
    # pencil through the bun
    c.raw('<g transform="rotate(-28 306 110)">')
    c.rect(236, 103, 140, 14, P["gold"], r=3, sw=3.4)
    c.rect(236, 103, 18, 14, "#e48f8a", r=3, sw=3.2, hl=False)
    c.poly([(376, 103), (396, 110), (376, 117)], P["wood_l"], sw=3)
    c.poly([(389, 107.5), (396, 110), (389, 112.5)], OUT, sw=0)
    c.raw('</g>')
    c.rect(258, 270, 64, 90, SKIN, r=24, sw=4.2, hl=False)   # neck
    c.raw('<path d="M262 318 Q290 338 318 318" fill="none" stroke="%s" stroke-width="5" stroke-linecap="round" opacity=".7"/>' % SKIN_D)
    c.raw('</g>')
    # ---- body: blouse shoulders, vest, shawl, apron
    body = "M118 620 C118 520 140 430 210 392 C240 376 262 368 290 368 C318 368 340 376 370 392 C440 430 462 520 462 620 Z"
    c.path(body, BLOUSE, sw=4.5)
    c.raw('<path d="M150 470 C160 440 180 420 205 405" fill="none" stroke="%s" stroke-width="3" stroke-linecap="round"/>' % light(BLOUSE, .5))
    vest = "M200 620 C196 540 206 460 232 410 L290 470 L348 410 C374 460 384 540 380 620 Z"
    c.path(vest, VEST, sw=4.2)
    c.raw('<path d="M244 440 C238 480 236 530 238 600" fill="none" stroke="%s" stroke-width="3" stroke-linecap="round"/>' % light(VEST, .25))
    for y in (500, 540, 580):
        c.circle(cx, y, 6, P["gold"], sw=2.6)
    # apron from the waist, with an amber hem and a pocket holding a wooden spoon
    c.path("M214 560 C230 552 350 552 366 560 L372 620 L208 620 Z", APRON, sw=4)
    c.rect(206, 548, 168, 16, SHAWL, r=6, sw=3.4)
    c.rect(318, 572, 40, 34, light(APRON, .1), r=4, sw=3)
    c.rect(330, 540, 8, 40, P["wood_l"], r=3, sw=2.6)
    c.ellipse(334, 534, 11, 15, P["wood_l"], sw=2.8)
    # collar
    c.poly([(256, 372), (290, 420), (272, 432), (240, 392)], P["paper"], sw=3.4, hl=False)
    c.poly([(324, 372), (290, 420), (308, 432), (340, 392)], P["paper"], sw=3.4, hl=False)
    # shawl over both shoulders, knotted on her chest
    c.path("M198 396 C230 380 252 376 262 380 C270 410 280 440 290 452 C270 452 230 470 200 500 C186 470 186 430 198 396 Z", SHAWL, sw=4.2)
    c.path("M382 396 C350 380 328 376 318 380 C310 410 300 440 290 452 C310 452 350 470 380 500 C394 470 394 430 382 396 Z", SHAWL, sw=4.2)
    for k in range(4):
        c.raw('<path d="M%d %d q14 26 10 60" fill="none" stroke="%s" stroke-width="3" stroke-linecap="round"/>' % (214 + k * 14, 398 + k * 6, SHAWL_D))
        c.raw('<path d="M%d %d q-14 26 -10 60" fill="none" stroke="%s" stroke-width="3" stroke-linecap="round"/>' % (366 - k * 14, 398 + k * 6, SHAWL_D))
    c.circle(cx, 456, 15, SHAWL_D, sw=3.6)
    c.poly([(282, 466), (270, 520), (288, 512)], SHAWL, sw=3.2, hl=False)
    c.poly([(298, 466), (314, 518), (296, 510)], SHAWL, sw=3.2, hl=False)
    # a ring of room keys on the apron string
    for i, dx in enumerate((-10, 2, 14)):
        key(c, 232 + dx, 586 + (i % 2) * 6, 24, P["gold"] if i != 1 else P["steel"])
    c.raw('<circle cx="238" cy="572" r="9" fill="none" stroke="%s" stroke-width="3.4"/>' % OUT)
    # ---- arms: right hand (viewer left) holds the lantern, left arm (viewer right) holds the ledger
    c.path("M214 396 C178 414 150 448 142 490 C140 506 172 514 180 498 C186 466 200 440 226 422 Z", BLOUSE, sw=4.2)
    c.raw('<path d="M196 418 C176 434 162 456 156 480" fill="none" stroke="%s" stroke-width="3" stroke-linecap="round"/>' % light(BLOUSE, .5))
    # lantern
    c.raw('<circle cx="160" cy="512" r="16" fill="none" stroke="%s" stroke-width="10"/>' % OUT)
    c.raw('<circle cx="160" cy="512" r="16" fill="none" stroke="%s" stroke-width="5"/>' % P["iron"])
    c.ellipse(160, 506, 19, 15, SKIN, sw=3.6)                                     # fist round the ring
    for k in range(3):
        c.line(152 + k * 7, 499, 151 + k * 7, 512, SKIN_D, w=2.2)
    c.poly([(132, 548), (188, 548), (178, 530), (142, 530)], P["dsteel"], sw=3.6, hl=False)
    c.rect(134, 548, 52, 60, "#ffe2a0", r=6, sw=4)
    for x in (134, 160, 186):
        c.line(x, 550, x, 606, P["dsteel"], w=5)
    flame(c, 160, 586, .55)
    c.rect(128, 604, 64, 14, P["dsteel"], r=4, sw=3.6, hl=False)
    if lantern_glow:
        c.glow(160, 578, 54, "#fff3c0", .55)
    # ledger, held against the hip by her left arm
    c.path("M366 392 C404 408 436 444 446 496 C452 532 436 556 404 556 L394 526 C412 510 414 468 392 430 Z", BLOUSE, sw=4.2)
    c.raw('<path d="M392 414 C414 432 428 460 432 492" fill="none" stroke="%s" stroke-width="3" stroke-linecap="round"/>' % light(BLOUSE, .5))
    c.raw('<g transform="rotate(-12 412 500)">')
    c.rect(352, 436, 120, 150, "#5c2c22", r=10, sw=4.4)
    c.rect(360, 444, 104, 134, "#6f3a2b", r=8, sw=0, hl=False, sh=False)
    c.rect(352, 436, 22, 150, "#43201a", r=8, sw=3.6, hl=False)
    for (x, y) in ((462, 446), (462, 576)):
        c.circle(x, y, 6, P["gold"], sw=2.4, hl=False, sh=False)
    c.rect(392, 476, 56, 34, P["paper"], r=4, sw=3)
    c.line(400, 488, 440, 488, "#a79f89", w=2.4)
    c.line(400, 498, 432, 498, "#a79f89", w=2.4)
    c.poly([(430, 584), (430, 618), (440, 608), (450, 618), (450, 584)], SHAWL, sw=3, hl=False)
    c.raw('</g>')
    c.ellipse(400, 540, 24, 17, SKIN, sw=3.8)
    for k in range(3):
        c.line(384 + k * 9, 532, 383 + k * 9, 548, SKIN_D, w=2.4)
    # ---- ears, face (same lowered head group)
    c.raw('<g transform="translate(0 24)">')
    for ex in (218, 362):
        c.ellipse(ex, 232, 15, 22, SKIN, sw=3.8)
        c.dot(ex, 254, 5.5, P["gold"])
    face = "M222 210 C222 150 252 124 290 124 C328 124 358 150 358 210 C358 262 334 308 290 312 C246 308 222 262 222 210 Z"
    c.path(face, SKIN, sw=4.5)
    c.raw('<path d="M232 200 C234 168 250 146 270 138" fill="none" stroke="%s" stroke-width="3" stroke-linecap="round"/>' % light(SKIN, .45))
    c.raw('<path d="M350 230 C344 270 322 298 296 306" fill="none" stroke="%s" stroke-width="3" stroke-linecap="round"/>' % dark(SKIN, .18))
    # fringe: a soft side sweep, a few lighter and a couple of grey strands
    fr = "M214 214 C206 150 248 112 296 112 C344 112 372 146 368 206 C352 176 330 160 300 156 C282 176 250 188 226 190 C222 198 218 206 214 214 Z"
    c.path(fr, HAIR, sw=4.5)
    for d, col in (("M250 142 C270 136 290 140 306 154", HAIR_L), ("M232 172 C246 160 262 152 280 150", HAIR_L), ("M318 132 C340 140 354 156 360 178", "#d9cfc2")):
        c.raw('<path d="%s" fill="none" stroke="%s" stroke-width="3.2" stroke-linecap="round"/>' % (d, col))
    c.path("M214 214 C206 240 210 266 222 284 C222 258 222 236 226 214 Z", HAIR, sw=3.6)
    c.path("M368 206 C374 236 370 264 358 282 C358 256 358 232 354 210 Z", HAIR, sw=3.6)
    # brows and smiling eyes (lids raised by the smile), freckles, blush
    for (x, s) in ((260, -1), (320, 1)):
        c.raw('<path d="M%d 196 Q%d 186 %d 192" fill="none" stroke="%s" stroke-width="6" stroke-linecap="round"/>' % (x - 18, x, x + 18, HAIR_D))
        c.ellipse(x, 222, 13, 9.5, "#fbf6ea", sw=3.2, hl=False, sh=False)
        c.circle(x + 1, 223, 7, "#4b3423", sw=0, hl=False, sh=False)
        c.circle(x + 1, 223, 3.4, OUT, sw=0, hl=False, sh=False)
        c.dot(x + 3.5, 220, 2.2, "#fff8e8")
        c.raw('<path d="M%d 220 Q%d 206 %d 220" fill="none" stroke="%s" stroke-width="5" stroke-linecap="round"/>' % (x - 15, x, x + 15, OUT))
        c.raw('<path d="M%d 233 Q%d 240 %d 233" fill="none" stroke="%s" stroke-width="2.6" stroke-linecap="round"/>' % (x - 11, x, x + 11, SKIN_D))
        c.raw('<path d="M%d 218 l%d -5" fill="none" stroke="%s" stroke-width="2.4" stroke-linecap="round"/>' % (x + 15 * s, 7 * s, SKIN_D))
        c.raw('<circle cx="%d" cy="258" r="20" fill="#e2826e" fill-opacity=".32"/>' % (x + 16 * s))
    for (x, y) in ((250, 246), (258, 252), (244, 254), (330, 246), (322, 252), (336, 254)):
        c.dot(x, y, 2.1, "#b9775a")
    # nose and a warm open smile
    c.raw('<path d="M292 232 Q300 252 290 258 Q284 260 280 256" fill="none" stroke="%s" stroke-width="3.6" stroke-linecap="round"/>' % SKIN_D)
    c.path("M264 272 Q290 282 316 272 Q310 292 290 294 Q270 292 264 272 Z", "#7a2f2a", sw=4)
    c.path("M268 274 Q290 283 312 274 L310 279 Q290 286 270 279 Z", "#fbf6ea", sw=0)
    c.raw('<path d="M279 289 Q290 292 301 289" fill="none" stroke="#c4605a" stroke-width="4" stroke-linecap="round"/>')
    c.raw('<path d="M256 266 Q259 272 264 272 M324 266 Q321 272 316 272" fill="none" stroke="%s" stroke-width="2.6" stroke-linecap="round"/>' % SKIN_D)
    c.raw('</g>')
    return c


# 1.1.0 feedback: the vector Hetty above stood out next to the vanilla storytellers. The portraits are now painted in the
# vanilla storyteller style (inked comic illustration, cel shading, transparent background, outer silhouette line):
# generated with Nano Banana Pro (Gemini 3 Pro Image) from a written brief, vanilla portraits as style reference only,
# cut out and outlined by hand. The finished rasters live in tools/art/raster/ and are embedded here so the build keeps them.
# The vector function hetty() is kept for reference and is no longer rendered.
def _raster(c, name, w, h):
    import base64, os
    p = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "raster", name)
    b64 = base64.b64encode(open(p, "rb").read()).decode()
    c.raw('<image x="0" y="0" width="%d" height="%d" preserveAspectRatio="none" href="data:image/png;base64,%s"/>' % (w, h, b64))
    return c


@tex("UI/HH/Storyteller/HettyLarge", "single", (580 / 128, 620 / 128), ppc=256, grain=0,
     doc="Storyteller portrait Hetty Hearthwell, 1160x1240, painted in the vanilla storyteller style (raster source tools/art/raster/HettyLarge.png)")
def hetty_large(view, cells, masked):
    return _raster(Canvas(cells[0], cells[1], masked), "HettyLarge.png", 580, 620)


@tex("UI/HH/Storyteller/HettyTiny", "single", (122 / 128, 130 / 128), ppc=256, grain=0,
     doc="Storyteller portrait Hetty Hearthwell, tiny 244x260: face crop like the vanilla tiny portraits (raster source tools/art/raster/HettyTiny.png)")
def hetty_tiny(view, cells, masked):
    return _raster(Canvas(cells[0], cells[1], masked), "HettyTiny.png", 122, 130)


# ============================================================================ brand marks (comms console, settings header, mod icons)
def silverline_mark(c):
    c.circle(64, 64, 58, EM, sw=4.2)
    c.circle(64, 64, 49, dark(EM, .22), sw=0, hl=False, sh=False)
    coin(c, 64, 64, 34, P["silver"], sw=3.6)
    c.circle(64, 64, 22, light(P["silver"], .15), sw=2.4, hl=False, sh=False)
    c.pline([(26, 88), (48, 66), (62, 76), (102, 36)], OUT, 13)
    c.pline([(26, 88), (48, 66), (62, 76), (102, 36)], EM, 7)
    c.poly([(90, 30), (110, 28), (106, 48)], EM, sw=3.2, hl=False)
    return c


@tex("UI/HH/Bank/SilverlineMark", "single", (1, 1), ppc=128, grain=0, doc="Mark: Silverline (silver coin on an emerald disc, rising sweep) for the comms console and Silverline settings")
def mark_silverline(view, cells, masked):
    return silverline_mark(_icon(cells, masked))


@tex("UI/HH/Finance/FinanceMark", "single", (1, 1), ppc=128, grain=0, doc="Mark: investments and lottery (ticket with a rising chart and a star)")
def mark_finance(view, cells, masked):
    c = _icon(cells, masked)
    c.circle(64, 64, 58, P["navy"], sw=4.2)
    c.raw('<g transform="rotate(-10 64 64)">')
    c.rect(20, 36, 88, 58, P["paper"], r=8, sw=4)
    for y in range(44, 92, 9):
        c.dot(36, y, 2.2, "#a79f89")
    c.pline([(46, 82), (60, 70), (72, 76), (94, 50)], OUT, 10)
    c.pline([(46, 82), (60, 70), (72, 76), (94, 50)], EM, 5)
    c.raw('</g>')
    _icons.star(c, 96, 30, 15, P["gold"], sw=3)
    return c


def thrumbo_coin(c):
    cx, cy, R = 64, 68, 52
    c.circle(cx, cy, R, P["felt_d"], sw=4)
    c.circle(cx, cy, R - 9, P["felt"], sw=2.6, hl=False)
    for i in range(12):
        a = i * math.pi / 6
        c.line(cx + math.cos(a) * (R - 3), cy + math.sin(a) * (R - 3), cx + math.cos(a) * (R - 8), cy + math.sin(a) * (R - 8), "#f4ecd4", w=4.2)
    head = [(34, 102), (38, 80), (44, 62), (54, 52), (66, 48), (80, 50), (92, 60), (98, 70), (96, 78), (88, 82), (80, 81), (73, 90), (68, 102)]
    c.poly([(36, 76), (24, 68), (26, 88), (34, 100), (44, 100)], "#5c2420", sw=3, hl=False)
    c.poly([(40, 64), (30, 56), (34, 76), (46, 74)], "#8a3b34", sw=3, hl=False)
    c.poly(head, "#efe3c2", sw=3.6)
    c.poly([(60, 52), (58, 38), (68, 48)], "#efe3c2", sw=3, hl=False)
    c.poly([(70, 94), (88, 84), (100, 86), (98, 92), (84, 100), (74, 102)], "#d8cdb2", sw=3, hl=False)
    c.circle(76, 62, 3.6, OUT, sw=0, hl=False, sh=False)
    c.circle(77, 61, 1.1, "#fff3c0", sw=0, hl=False, sh=False)
    c.circle(94, 74, 1.8, OUT, sw=0, hl=False, sh=False)
    c.poly([(72, 50), (86, 6), (92, 50)], P["amber"], sw=3.4)
    for t in (0.22, 0.42, 0.62, 0.8):
        x0 = 72 + (86 - 72) * t; x1 = 92 + (86 - 92) * t; y = 50 + (6 - 50) * t
        c.line(x0 + 1.5, y + 3, x1 - 1.5, y - 3, dark(P["amber"], .45), w=2.6)
    spark(c, 86, 6, 9, "#fff3c0")
    die(c, 104, 96, 24, 5)
    return c


@tex("UI/HH/Casino/ThrumboMark", "single", (1, 1), ppc=128, grain=0, doc="Mark: The Lucky Thrumbo, square (thrumbo head on a casino coin, die) for the comms console")
def mark_thrumbo(view, cells, masked):
    return thrumbo_coin(_icon(cells, masked))


@tex("UI/HH/Core/ModMark", "single", (1, 1), ppc=128, grain=0, doc="Mark: Hearth & Harbor (hearth flame over a harbour anchor), settings header")
def mark_hh(view, cells, masked):
    return _ui.maintab(view, cells, masked)


# ============================================================================ settings pages
@tex("UI/HH/Core/Settings/Modules", "single", (1, 1), ppc=128, grain=0, doc="Settings page icon: modules (three switches)")
def set_modules(view, cells, masked):
    c = _icon(cells, masked)
    for i, (on, col) in enumerate(((True, P["amber"]), (False, P["steel"]), (True, EM))):
        y = 20 + i * 34
        c.rect(14, y, 100, 26, dark(P["dsteel"], .1) if not on else light(col, .1), r=13, sw=4)
        c.circle(98 if on else 30, y + 13, 15, P["paper"], sw=3.6)
    return c


@tex("UI/HH/Core/Settings/Difficulty", "single", (1, 1), ppc=128, grain=0, doc="Settings page icon: hosting difficulty (slider with seven steps and a star knob)")
def set_difficulty(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(12, 58, 104, 14, P["dsteel"], r=7, sw=4)
    c.rect(14, 60, 58, 10, P["amber"], r=5, sw=0, hl=False, sh=False)
    for i in range(7):
        x = 18 + i * 15.3
        c.rect(x - 2.5, 82, 5, 14 if i % 3 == 0 else 9, P["paper"], r=2, sw=2.4, hl=False, sh=False)
    c.circle(72, 65, 20, P["paper"], sw=4)
    _icons.star(c, 72, 65, 13, P["amber"], sw=2.6)
    for i, h in enumerate((10, 18, 26)):
        c.rect(22 + i * 14, 40 - h, 10, h, (P["green"], P["gold"], P["red_l"])[i], r=2, sw=3, hl=False)
    return c


@tex("UI/HH/Core/Settings/General", "single", (1, 1), ppc=128, grain=0, doc="Settings page icon: general (cog)")
def set_general(view, cells, masked):
    c = _icon(cells, masked)
    pts = []
    for i in range(48):
        a = i * math.pi * 2 / 48
        tooth = (i // 3) % 2 == 0
        r = 56 if tooth else 44
        pts.append((64 + math.cos(a) * r, 64 + math.sin(a) * r))
    c.poly(pts, P["steel"], sw=4.2)
    c.circle(64, 64, 32, dark(P["steel"], .12), sw=3, hl=False, sh=False)
    c.circle(64, 64, 16, P["dsteel"], sw=4)
    c.circle(64, 64, 6, P["amber"], sw=0, hl=False, sh=False)
    return c


@tex("UI/HH/Core/Settings/Diagnosis", "single", (1, 1), ppc=128, grain=0, doc="Settings page icon: diagnosis (magnifier over a log page with a pulse line)")
def set_diagnosis(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(14, 10, 72, 96, P["paper"], r=6, sw=4)
    for k in range(5):
        c.line(24, 24 + k * 12, 70 - (k % 2) * 14, 24 + k * 12, "#a79f89", w=3)
    c.pline([(22, 90), (36, 90), (42, 76), (50, 100), (56, 86), (76, 86)], OUT, 8)
    c.pline([(22, 90), (36, 90), (42, 76), (50, 100), (56, 86), (76, 86)], P["red_l"], 4)
    c.line(92, 92, 114, 116, OUT, w=16)
    c.line(92, 92, 114, 116, P["wood_d"], w=9)
    c.circle(82, 74, 26, "#cfe6ea", sw=5)
    c.raw('<path d="M70 64 Q76 56 86 56" fill="none" stroke="#ffffff" stroke-width="4" stroke-linecap="round"/>')
    return c


@tex("UI/HH/Core/Settings/About", "single", (1, 1), ppc=128, grain=0, doc="Settings page icon: about (info sign on an amber disc)")
def set_about(view, cells, masked):
    c = _icon(cells, masked)
    c.circle(64, 64, 56, P["amber"], sw=4.2)
    c.circle(64, 64, 46, dark(P["amber"], .15), sw=0, hl=False, sh=False)
    c.circle(64, 36, 9, P["paper"], sw=3.6)
    c.rect(54, 54, 20, 46, P["paper"], r=6, sw=3.8)
    c.rect(48, 92, 32, 10, P["paper"], r=4, sw=3.4, hl=False)
    return c


# ============================================================================ module icons (sidebar, modules page, Silverline standalone)
@tex("UI/HH/Core/Module_Inn", "single", (1, 1), ppc=128, grain=0, doc="Module icon: Inn & Hotel (guest bed with a key)")
def mod_inn(view, cells, masked):
    return icon_bed(view, cells, masked)


@tex("UI/HH/Core/Module_ShopsFood", "single", (1, 1), ppc=128, grain=0, doc="Module icon: Shops & Food (striped awning over a steaming plate)")
def mod_shops(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(16, 48, 96, 66, P["wood"], r=6, sw=4)
    c.rect(24, 56, 80, 50, dark(P["wood"], .3), r=4, sw=0, hl=False, sh=False)
    n = 5
    for i in range(n):
        x = 10 + i * 108 / n
        c.path("M%s 22 H%s V44 Q%s 56 %s 44 Z" % (f2(x), f2(x + 108 / n), f2(x + 54 / n), f2(x)), P["red_l"] if i % 2 == 0 else P["paper"], sw=3.4)
    c.rect(8, 14, 112, 12, P["wood_d"], r=4, sw=3.4, hl=False)
    plate_base(c, 64, 92, 26)
    c.ellipse(64, 86, 16, 10, P["bread"], sw=3)
    c.steam(58, 72, h=20, n=2, w=8)
    return c


@tex("UI/HH/Core/Module_Casino", "single", (1, 1), ppc=128, grain=0, doc="Module icon: Casino (same emblem as the casino venue type)")
def mod_casino(view, cells, masked):
    return _icons.venue_casino(view, cells, masked)


@tex("UI/HH/Core/Module_Wellness", "single", (1, 1), ppc=128, grain=0, doc="Module icon: Wellness (warm stones over water, steam)")
def mod_wellness(view, cells, masked):
    c = _icon(cells, masked)
    c.ellipse(64, 98, 56, 18, P["teal"], sw=4)
    c.raw('<path d="M20 98 q11 -6 22 0 t22 0 t22 0 t22 0" fill="none" stroke="%s" stroke-width="3.2" stroke-linecap="round"/>' % light(P["teal"], .45))
    c.ellipse(64, 80, 34, 14, P["stone"], sw=4)
    c.ellipse(64, 60, 26, 11, light(P["stone"], .12), sw=4)
    c.ellipse(64, 43, 18, 8, light(P["stone"], .22), sw=3.8)
    c.steam(64, 30, h=24, n=3, w=9)
    return c


@tex("UI/HH/Core/Module_CityLife", "single", (1, 1), ppc=128, grain=0, doc="Module icon: City Life (three houses with lit windows)")
def mod_city(view, cells, masked):
    c = _icon(cells, masked)
    blds = ((10, 50, 34, 66, P["brick"]), (46, 26, 36, 90, P["navy"]), (84, 58, 34, 58, P["wood"]))
    for (x, y, w, h, col) in blds:
        c.rect(x, y, w, h, col, r=3, sw=4)
        for row in range(int((h - 18) // 16)):
            for k in range(2):
                lit = (row + k + x) % 3 != 0
                c.rect(x + 7 + k * (w - 22) / 1.0 * .5 + k * 4, y + 9 + row * 16, 8, 9, P["flame_l"] if lit else dark(col, .4), r=1.5, sw=1.8, hl=False, sh=False)
    c.poly([(46, 26), (64, 8), (82, 26)], P["red"], sw=3.6, hl=False)
    c.rect(6, 112, 116, 8, P["dsteel"], r=3, sw=3, hl=False)
    return c


@tex("UI/HH/Core/Module_Underworld", "single", (1, 1), ppc=128, grain=0, doc="Module icon: Underworld (dark half mask in a back-room doorway)")
def mod_underworld(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(22, 8, 84, 112, "#2c2a2e", r=10, sw=4)
    c.rect(30, 16, 68, 104, "#17151a", r=6, sw=0, hl=False, sh=False)
    c.glow(64, 120, 50, P["red_l"], .35)
    mask = "M18 60 C26 40 52 42 64 52 C76 42 102 40 110 60 C108 78 90 88 74 82 C70 78 66 74 64 74 C62 74 58 78 54 82 C38 88 20 78 18 60 Z"
    c.path(mask, P["red"], sw=4.2)
    for x in (44, 84):
        c.ellipse(x, 62, 11, 7, "#17151a", sw=2.6, hl=False, sh=False)
    c.raw('<path d="M30 54 C38 46 50 46 58 52" fill="none" stroke="%s" stroke-width="3" stroke-linecap="round"/>' % light(P["red"], .35))
    return c


@tex("UI/HH/Core/Module_Bank", "single", (1, 1), ppc=128, grain=0, doc="Module icon: Bank (the Silverline mark)")
def mod_bank(view, cells, masked):
    return silverline_mark(_icon(cells, masked))


@tex("UI/HH/Core/Module_AiFlavour", "single", (1, 1), ppc=128, grain=0, doc="Module icon: AI flavour (speech bubble with a spark)")
def mod_ai(view, cells, masked):
    c = _icon(cells, masked)
    c.path("M16 26 Q16 14 28 14 H100 Q112 14 112 26 V78 Q112 90 100 90 H52 L30 112 L34 90 H28 Q16 90 16 78 Z", P["paper"], sw=4.2)
    for k, x in enumerate((40, 64, 88)):
        c.circle(x, 52, 7, (P["amber"], EM, P["blue"])[k], sw=2.8)
    spark(c, 104, 18, 13, P["gold"])
    return c
