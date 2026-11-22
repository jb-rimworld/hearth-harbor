"""Hearth & Harbor house style: palette, colour helpers, Canvas with shaded primitives, Frame for plan views.

All drawing happens in "design units": 128 units = one map cell. The renderer scales to the requested pixels per cell.
Light comes from the top left: every shape gets a light line on its top/left inside edge and a shadow line on its
bottom/right inside edge (KONZEPT 14.1: flat fill + exactly one light and one shadow line, no gradients on game art).
"""
import math

U = 128  # design units per cell

# ---------------------------------------------------------------- palette
OUT = "#1c1411"        # warm near-black outline, never #000
P = dict(
    steel="#b9b2a3", dsteel="#2b2f38", iron="#4a4d55", paper="#e9e4d4", wood="#7a5638", wood_l="#97704a", wood_d="#58391f",
    red="#8a3b34", red_l="#c8372f", red_d="#5c2420", gold="#e0b44a", silver="#c9ccd1", copper="#b0613a",
    green="#4f7f5a", felt="#2f6b47", felt_d="#22503a", teal="#3f8f8a", blue="#5e8fc2", navy="#34425e", cream="#efe3c2",
    amber="#d9803f", emerald="#3fa66b", violet="#8e5cc8", stone="#8d8678", brick="#9a5a45", brick_d="#6e3f31",
    pale="#d8cdb2",  # neutral stuff-tint fill for masked (stuffable) surfaces
    glass="#9fc9d4", flame="#f2a03a", flame_l="#f7d66a", coal="#3a2420", leaf="#5d8a47", tomato="#c8402f",
    egg="#f4ecd4", yolk="#e8a62f", bread="#c58a45", bread_d="#9a6330", meat="#8e4a3a", meat_l="#b5604b", fish="#e58a78",
    rice="#f3eedd", noodle="#e6c777", broth="#b9763a", cream2="#f6f0dc", choc="#5a3524", cherry="#b3262c",
)


def rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def hexc(t):
    return "#%02x%02x%02x" % tuple(max(0, min(255, int(round(v)))) for v in t)


def mix(a, b, t):
    A, B = rgb(a), rgb(b)
    return hexc([A[i] + (B[i] - A[i]) * t for i in range(3)])


def light(c, t=0.18):
    return mix(c, "#fff1cf", t)


def dark(c, t=0.25):
    return mix(c, "#24140f", t)


def f2(v):
    return ("%.2f" % v).rstrip("0").rstrip(".")


def arc_pts(cx, cy, rx, ry, a0, a1, n=24):
    """points on an ellipse arc, angles in degrees (0 = +x, 90 = +y/down)"""
    return [(cx + rx * math.cos(math.radians(a0 + (a1 - a0) * i / n)), cy + ry * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]


def rrect_pts(x, y, w, h, r, n=6):
    r = min(r, w / 2, h / 2)
    pts = []
    for cx, cy, a in ((x + w - r, y + r, -90), (x + w - r, y + h - r, 0), (x + r, y + h - r, 90), (x + r, y + r, 180)):
        pts += arc_pts(cx, cy, r, r, a, a + 90, n)
    return pts


class Canvas:
    """Collects SVG for the colour image and (optionally) the stuff mask in one pass."""

    def __init__(self, wc, hc, masked=False):
        self.wc, self.hc = wc, hc
        self.W, self.H = wc * U, hc * U
        self.masked = masked
        self.body = []
        self.mask = []

    # ---- low level
    def _add(self, svg, msvg=None):
        self.body.append(svg)
        if msvg:
            self.mask.append(msvg)

    def raw(self, svg):
        self.body.append(svg)

    def _mfill(self, m, fill):
        # mask colours: R = main stuff colour, G = second colour, black = untinted
        return {"a": "#ff0000", "b": "#00ff00"}.get(m, "#000000")

    def _style(self, fill, sw, stroke):
        s = 'fill="%s"' % (fill if fill else "none")
        if sw and stroke:
            s += ' stroke="%s" stroke-width="%s" stroke-linejoin="round" stroke-linecap="round"' % (stroke, f2(sw))
        return s

    # ---- shapes
    def path(self, d, fill=None, sw=0, stroke=OUT, m=None, extra=""):
        m_svg = None
        if m is not False:
            mf = self._mfill(m, fill)
            m_svg = '<path d="%s" fill="%s" %s/>' % (d, mf if fill else "none", ('stroke="#000000" stroke-width="%s" stroke-linejoin="round"' % f2(max(sw, 2))) if (sw and m is None) else "")
        self._add('<path d="%s" %s %s/>' % (d, self._style(fill, sw, stroke), extra), m_svg)

    def poly(self, pts, fill=None, sw=3, stroke=OUT, m=None, close=True, extra="", **_ignored):
        d = "M" + " L".join("%s %s" % (f2(x), f2(y)) for x, y in pts) + (" Z" if close else "")
        self.path(d, fill, sw, stroke, m, extra)

    def rect(self, x, y, w, h, fill, r=6, sw=3.5, hl=True, m=None, sh=True, lt=0.2, dk=0.25, extra=""):
        """rounded rectangle with outline and top-left light line / bottom-right shadow line."""
        r = min(r, w / 2, h / 2)
        d = "M%s %s H%s Q%s %s %s %s V%s Q%s %s %s %s H%s Q%s %s %s %s V%s Q%s %s %s %s Z" % (
            f2(x + r), f2(y), f2(x + w - r), f2(x + w), f2(y), f2(x + w), f2(y + r), f2(y + h - r), f2(x + w), f2(y + h), f2(x + w - r), f2(y + h),
            f2(x + r), f2(x), f2(y + h), f2(x), f2(y + h - r), f2(y + r), f2(x), f2(y), f2(x + r), f2(y))
        self.path(d, fill, sw, OUT, m, extra)
        if m is False:
            return
        self._edges(x, y, w, h, r, fill, sw, hl, sh, lt, dk)

    def _edges(self, x, y, w, h, r, fill, sw, hl, sh, lt, dk):
        ins = sw / 2 + 1.1
        lw = 2 if min(w, h) > 26 else (1.4 if min(w, h) > 10 else 0)
        if lw == 0 or not fill:
            return
        rr = max(r - 1, 0)
        if hl:
            c = light(fill, lt)
            self.raw('<path d="M%s %s H%s M%s %s V%s" fill="none" stroke="%s" stroke-width="%s" stroke-linecap="round"/>' % (
                f2(x + rr + 1), f2(y + ins), f2(x + w - rr - 1), f2(x + ins), f2(y + rr + 1), f2(y + h - rr - 1), c, lw))
        if sh:
            c = dark(fill, dk)
            self.raw('<path d="M%s %s H%s M%s %s V%s" fill="none" stroke="%s" stroke-width="%s" stroke-linecap="round"/>' % (
                f2(x + rr + 1), f2(y + h - ins), f2(x + w - rr - 1), f2(x + w - ins), f2(y + rr + 1), f2(y + h - rr - 1), c, lw))

    def ellipse(self, cx, cy, rx, ry, fill, sw=3.5, hl=True, m=None, sh=True, lt=0.2, dk=0.25):
        m_svg = None
        if m is not False:
            m_svg = '<ellipse cx="%s" cy="%s" rx="%s" ry="%s" fill="%s" %s/>' % (f2(cx), f2(cy), f2(rx), f2(ry), self._mfill(m, fill), ('stroke="#000" stroke-width="2"' if (sw and m is None) else ""))
        self._add('<ellipse cx="%s" cy="%s" rx="%s" ry="%s" %s/>' % (f2(cx), f2(cy), f2(rx), f2(ry), self._style(fill, sw, OUT)), m_svg)
        if m is False or not fill or min(rx, ry) < 7:
            return
        ins = sw / 2 + 1.1
        lw = 2 if min(rx, ry) > 13 else 1.4
        if hl:
            pts = arc_pts(cx, cy, rx - ins, ry - ins, 150, 255, 10)
            self.raw('<path d="M%s" fill="none" stroke="%s" stroke-width="%s" stroke-linecap="round"/>' % (" L".join("%s %s" % (f2(a), f2(b)) for a, b in pts), light(fill, lt), lw))
        if sh:
            pts = arc_pts(cx, cy, rx - ins, ry - ins, -30, 75, 10)
            self.raw('<path d="M%s" fill="none" stroke="%s" stroke-width="%s" stroke-linecap="round"/>' % (" L".join("%s %s" % (f2(a), f2(b)) for a, b in pts), dark(fill, dk), lw))

    def circle(self, cx, cy, r, fill, **k):
        self.ellipse(cx, cy, r, r, fill, **k)

    def line(self, x1, y1, x2, y2, color=OUT, w=2.5, m=False, cap="round", extra=""):
        self._add('<line x1="%s" y1="%s" x2="%s" y2="%s" stroke="%s" stroke-width="%s" stroke-linecap="%s" %s/>' % (f2(x1), f2(y1), f2(x2), f2(y2), color, f2(w), cap, extra),
                  None if m is False else '<line x1="%s" y1="%s" x2="%s" y2="%s" stroke="%s" stroke-width="%s"/>' % (f2(x1), f2(y1), f2(x2), f2(y2), self._mfill(m, color), f2(w)))

    def pline(self, pts, color=OUT, w=2.5, fill="none", extra=""):
        self.raw('<path d="M%s" fill="%s" stroke="%s" stroke-width="%s" stroke-linecap="round" stroke-linejoin="round" %s/>' % (" L".join("%s %s" % (f2(a), f2(b)) for a, b in pts), fill, color, f2(w), extra))

    def dot(self, cx, cy, r, color):
        self.raw('<circle cx="%s" cy="%s" r="%s" fill="%s"/>' % (f2(cx), f2(cy), f2(r), color))

    def glow(self, cx, cy, r, color, op=0.5):
        """soft light: radial gradient that fades to nothing at the edge"""
        self._gid = getattr(self, "_gid", 0) + 1
        gid = "g%d" % self._gid
        self.raw('<defs><radialGradient id="%s"><stop offset="0" stop-color="%s" stop-opacity="%s"/><stop offset="1" stop-color="%s" stop-opacity="0"/></radialGradient></defs>'
                 '<circle cx="%s" cy="%s" r="%s" fill="url(#%s)"/>' % (gid, color, op, color, f2(cx), f2(cy), f2(r), gid))

    def steam(self, cx, cy, h=28, n=2, w=9):
        for i in range(n):
            x = cx + (i - (n - 1) / 2) * w * 1.6
            self.raw('<path d="M%s %s q%s %s 0 %s t0 %s" fill="none" stroke="#f4efe4" stroke-opacity="0.8" stroke-width="3" stroke-linecap="round"/>' % (f2(x), f2(cy), f2(w * 0.6), f2(-h * .25), f2(-h * .5), f2(-h * .5)))

    # ---- output
    def svg(self, mask=False):
        items = self.mask if mask else self.body
        bg = '<rect width="%d" height="%d" fill="#000" fill-opacity="0"/>' % (self.W, self.H)
        return '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 %d %d" width="%d" height="%d">%s%s</svg>' % (self.W, self.H, self.W, self.H, bg, "".join(items))


class Frame:
    """Maps a local south-facing layout (x right, y toward the viewer/front) onto the screen for a view.
    region = screen rect (x, y, w, h) of the TOP surface. Local size is (w, h) for south/north and (h, w) for east/west."""

    def __init__(self, view, region):
        self.view = view
        self.TX, self.TY, self.TW, self.TH = region
        if view == "east":
            self.w, self.h = self.TH, self.TW
        else:
            self.w, self.h = self.TW, self.TH

    def pt(self, x, y):
        v = self.view
        if v == "south":
            return (self.TX + x, self.TY + y)
        if v == "north":
            return (self.TX + self.TW - x, self.TY + self.TH - y)
        return (self.TX + y, self.TY + self.TH - x)  # east

    def r(self, x, y, w, h):
        a = self.pt(x, y)
        b = self.pt(x + w, y + h)
        return (min(a[0], b[0]), min(a[1], b[1]), abs(b[0] - a[0]), abs(b[1] - a[1]))

    def pts(self, pts):
        return [self.pt(x, y) for x, y in pts]

    def swap(self):
        """True when local x/y are swapped on screen (width/height of rectangles swap)"""
        return self.view == "east"


def slab(c, view, top, apron=16, margin=8, r=9, front=None, m="a", inset=5, sw=3.5):
    """3/4-view furniture block: front face (apron) at the screen bottom plus a top surface. Returns Frame of the top surface."""
    X0, Y0, X1, Y1 = margin, margin, c.W - margin, c.H - margin
    front = front or dark(top, 0.28)
    c.rect(X0, Y1 - apron - r - 2, X1 - X0, apron + r + 2, front, r=r, sw=sw, m=m, hl=False, dk=0.3)
    c.rect(X0, Y0, X1 - X0, Y1 - apron - Y0, top, r=r, sw=sw, m=m)
    return Frame(view, (X0 + inset + 4, Y0 + inset + 4, X1 - X0 - 2 * inset - 8, Y1 - apron - Y0 - 2 * inset - 8))


def cabinet(c, view, body, w_frac=1.0, cap=20, margin=10, r=9, m=None, side_accent=None, bottom_pad=None, accent_strip=True):
    """Upright machine seen at 3/4 from the south. Returns the face rect (x, y, w, h) to put details on.
    south: front face. north: back face. east: side face with a thin front strip on the right."""
    bp = margin if bottom_pad is None else bottom_pad
    bw = (c.W - 2 * margin) * (w_frac if view in ("south", "north") else min(1.0, w_frac * 0.78))
    x0 = (c.W - bw) / 2
    y0, y1 = margin, c.H - bp
    capc = light(body, 0.16)
    c.rect(x0, y0, bw, y1 - y0, body, r=r, m=m)
    c.rect(x0 + 3, y0 + 3, bw - 6, cap, capc, r=r - 3, sw=2, hl=False, sh=False, m=m)
    face = (x0 + 8, y0 + cap + 7, bw - 16, y1 - y0 - cap - 14)
    if view == "east" and accent_strip:
        sx = x0 + bw - 17
        c.rect(sx, y0 + cap + 6, 11, y1 - y0 - cap - 12, side_accent or dark(body, 0.22), r=3, sw=2, hl=False, m=m)
        face = (x0 + 8, y0 + cap + 7, bw - 30, y1 - y0 - cap - 14)
    return face
