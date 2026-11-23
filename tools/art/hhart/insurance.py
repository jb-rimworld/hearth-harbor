"""Clawback Mutual UI icons (1.1.0): the five cover-status icons (pale base so the game can tint them green / yellow / red / grey) and the 'claim possible' letter icon.
All 128 px, flat, readable at 18-32 px: bold silhouette, no text."""
from .style import *
from .parts import *
from .registry import tex
from .inn import _icon
from .icons import tline, tpath

PALE = "#e9e4d4"
PALE_D = "#cfc8b4"


@tex("UI/HH/Insurance/Status/Pension", "single", (1, 1), ppc=128, grain=0, doc="Status icon: pension (armchair with a coin)")
def st_pension(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(30, 38, 68, 46, PALE, r=14, sw=4)                 # backrest
    c.rect(18, 66, 92, 28, PALE_D, r=10, sw=4)               # seat + arms
    c.rect(24, 54, 18, 40, PALE, r=8, sw=3.6)                # left arm
    c.rect(86, 54, 18, 40, PALE, r=8, sw=3.6)                # right arm
    c.rect(30, 96, 10, 18, PALE_D, r=3, sw=3, hl=False)      # feet
    c.rect(88, 96, 10, 18, PALE_D, r=3, sw=3, hl=False)
    coin(c, 64, 22, 13, "#f0d27a", sw=3.4)
    return c


@tex("UI/HH/Insurance/Status/Health", "single", (1, 1), ppc=128, grain=0, doc="Status icon: health plan (cross)")
def st_health(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(14, 14, 100, 100, PALE_D, r=24, sw=4.4)
    c.poly([(50, 24), (78, 24), (78, 50), (104, 50), (104, 78), (78, 78), (78, 104), (50, 104), (50, 78), (24, 78), (24, 50), (50, 50)], PALE, sw=4)
    return c


@tex("UI/HH/Insurance/Status/Accident", "single", (1, 1), ppc=128, grain=0, doc="Status icon: accident plan (bandage)")
def st_accident(view, cells, masked):
    c = _icon(cells, masked)
    c.raw('<g transform="rotate(-38 64 64)">')
    c.rect(8, 40, 112, 48, PALE, r=22, sw=4.4)
    c.rect(44, 40, 40, 48, PALE_D, r=4, sw=3.2, hl=False)
    for (x, y) in ((54, 52), (66, 52), (54, 70), (66, 70)):
        c.circle(x, y, 3.2, "#7d7566", sw=0, hl=False, sh=False)
    c.raw('</g>')
    return c


@tex("UI/HH/Insurance/Status/Liability", "single", (1, 1), ppc=128, grain=0, doc="Status icon: liability (umbrella)")
def st_liability(view, cells, masked):
    c = _icon(cells, masked)
    c.path("M12 66 Q12 18 64 18 Q116 18 116 66 Q102 56 90 66 Q77 54 64 66 Q51 54 38 66 Q26 56 12 66 Z", PALE, sw=4.2)
    tpath(c, "M64 66 L64 102 Q64 114 52 112", 7, PALE_D)
    c.circle(64, 14, 5, PALE_D, sw=3)
    return c


@tex("UI/HH/Insurance/Status/Legal", "single", (1, 1), ppc=128, grain=0, doc="Status icon: legal shield (scales)")
def st_legal(view, cells, masked):
    c = _icon(cells, masked)
    tline(c, [(64, 22), (64, 104)], 8, PALE_D)
    tline(c, [(24, 38), (104, 38)], 7, PALE_D)
    c.rect(36, 104, 56, 12, PALE, r=4, sw=3.6)
    for x in (24, 104):
        tline(c, [(x, 38), (x - 14, 74)], 3.2, PALE_D)
        tline(c, [(x, 38), (x + 14, 74)], 3.2, PALE_D)
        c.path("M%d 74 Q%d 94 %d 74 Z" % (x - 18, x, x + 18), PALE, sw=3.6)
    c.circle(64, 20, 8, PALE, sw=3.6)
    return c


@tex("UI/HH/Insurance/ClaimPossible", "single", (1, 1), ppc=128, grain=0, doc="Letter icon: a claim is possible (envelope with a clock)")
def claim_possible(view, cells, masked):
    c = _icon(cells, masked)
    c.rect(10, 28, 100, 72, PALE, r=8, sw=4.2)
    c.poly([(12, 32), (60, 70), (108, 32)], PALE_D, sw=3.6, hl=False)
    c.circle(96, 92, 28, "#f0d27a", sw=4.2)
    c.circle(96, 92, 21, PALE, sw=0, hl=False, sh=False)
    tline(c, [(96, 92), (96, 76)], 6, "#3d3a40")
    tline(c, [(96, 92), (108, 98)], 6, "#a3302a")
    return c
