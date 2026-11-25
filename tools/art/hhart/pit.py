"""Command icons of the fight pit (1.2.0 package B). 128 px, flat, readable at 32-64 px, no text."""
import math
from .style import *
from .parts import *
from .registry import tex
from .inn import _icon, person


def _disc(c, col, r=58):
    c.circle(64, 64, r, col, sw=4)
    c.circle(64, 64, r - 8, dark(col, .18), sw=0, hl=False, sh=False)


def _ring(c, col=None, r=40):
    col = col or P["wood"]
    c.raw('<circle cx="64" cy="70" r="%d" fill="none" stroke="%s" stroke-width="15"/>' % (r, OUT))
    c.raw('<circle cx="64" cy="70" r="%d" fill="none" stroke="%s" stroke-width="9"/>' % (r, col))
    for i in range(8):
        a = math.radians(i * 45 + 22.5)
        c.circle(64 + math.cos(a) * r, 70 + math.sin(a) * r, 6, P["wood_d"], sw=2.4, hl=False, sh=False)


def _paw(c, x, y, s, col):
    c.ellipse(x, y + s * .35, s * .62, s * .5, col, sw=3.4)
    for dx, dy in ((-.62, -.38), (-.22, -.72), (.22, -.72), (.62, -.38)):
        c.ellipse(x + dx * s, y + dy * s, s * .2, s * .26, col, sw=3, hl=False)


def _glove(c, x, y, r, col):
    c.circle(x, y, r, col, sw=3.6)
    c.rect(x - r * .55, y + r * .7, r * 1.1, r * .7, P["paper"], r=3, sw=3, hl=False)


@tex("UI/HH/Underworld/PitOpen", "single", (1, 1), ppc=128, grain=0, doc="Command icon: pit open (fenced ring with a pennant)")
def p_open(view, cells, masked):
    c = _icon(cells, masked)
    _ring(c)
    c.line(64, 72, 64, 18, OUT, 9); c.line(64, 72, 64, 18, P["steel"], 4)
    c.poly([(66, 18), (100, 28), (66, 40)], P["red_l"], sw=3.4)
    return c


@tex("UI/HH/Underworld/PitAnimals", "single", (1, 1), ppc=128, grain=0, doc="Command icon: animals may fight (paw print)")
def p_animals(view, cells, masked):
    c = _icon(cells, masked)
    _disc(c, P["green"])
    _paw(c, 64, 68, 40, P["cream"])
    return c


@tex("UI/HH/Underworld/PitGuests", "single", (1, 1), ppc=128, grain=0, doc="Command icon: guest challengers (visitor with a boxing glove)")
def p_guests(view, cells, masked):
    c = _icon(cells, masked)
    person(c, 46, 36, 20, P["blue"], sw=4)
    _glove(c, 94, 86, 22, P["red_l"])
    return c


@tex("UI/HH/Underworld/PitPrisoners", "single", (1, 1), ppc=128, grain=0, doc="Command icon: prisoners may fight (figure behind bars)")
def p_prisoners(view, cells, masked):
    c = _icon(cells, masked)
    person(c, 64, 40, 20, P["amber"], sw=4)
    c.rect(18, 14, 92, 8, P["iron"], r=2, sw=3, hl=False)
    c.rect(18, 108, 92, 8, P["iron"], r=2, sw=3, hl=False)
    for x in (28, 50, 78, 100):
        c.line(x, 20, x, 110, OUT, 10); c.line(x, 20, x, 110, P["steel"], 5)
    return c


@tex("UI/HH/Underworld/PitLethal", "single", (1, 1), ppc=128, grain=0, doc="Command icon: to the death (skull on a red disc)")
def p_lethal(view, cells, masked):
    c = _icon(cells, masked)
    _disc(c, P["red_d"])
    c.circle(64, 56, 30, P["paper"], sw=4)
    c.rect(48, 72, 32, 22, P["paper"], r=5, sw=4)
    c.circle(53, 56, 8, P["dsteel"], sw=0, hl=False, sh=False)
    c.circle(75, 56, 8, P["dsteel"], sw=0, hl=False, sh=False)
    for x in (56, 64, 72):
        c.line(x, 80, x, 92, P["dsteel"], 3)
    return c


@tex("UI/HH/Underworld/PitFighters", "single", (1, 1), ppc=128, grain=0, doc="Command icon: Fighters window (two fighters facing each other with a list line)")
def p_fighters(view, cells, masked):
    c = _icon(cells, masked)
    person(c, 36, 34, 17, P["amber"], sw=4)
    person(c, 92, 34, 17, P["blue"], sw=4)
    c.rect(18, 100, 92, 12, P["paper"], r=4, sw=3, hl=False)
    c.line(30, 106, 96, 106, P["iron"], 3)
    return c


@tex("UI/HH/Underworld/PitStart", "single", (1, 1), ppc=128, grain=0, doc="Command icon: start a bout (ring bell with a hammer)")
def p_start(view, cells, masked):
    c = _icon(cells, masked)
    c.path("M28 84 Q28 34 64 30 Q100 34 100 84 Z", P["gold"], sw=4.2)
    c.rect(20, 82, 88, 14, P["copper"], r=5, sw=3.6)
    c.circle(64, 104, 8, P["dsteel"], sw=3, hl=False)
    c.line(92, 20, 112, 46, OUT, 11); c.line(92, 20, 112, 46, P["wood_l"], 6)
    c.rect(82, 10, 22, 14, P["iron"], r=3, sw=3, hl=False)
    return c


@tex("UI/HH/Underworld/PitOpponent", "single", (1, 1), ppc=128, grain=0, doc="Command icon: call an opponent (paw print with a silver coin)")
def p_opponent(view, cells, masked):
    c = _icon(cells, masked)
    _paw(c, 52, 62, 38, P["brick"])
    coin(c, 96, 94, 20, P["silver"])
    return c
