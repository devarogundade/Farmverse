"""Flat UI control icons for Farmverse: a solid round badge with a white glyph.
No gradients, no drop shadows, no perspective: every icon is drawn front-on from simple shapes at
4x size and downsampled for clean edges. The painted item icons (coins, chests, food...) stay as
they are; only the controls (close, plus, minus, search, settings...) use this flat set.
python flat_icons.py <out_dir> [size]"""
import sys, os, math
from PIL import Image, ImageDraw

OUT = sys.argv[1]
SIZE = int(sys.argv[2]) if len(sys.argv) > 2 else 512
SS = 4
N = SIZE * SS
W = (255, 255, 255, 255)
os.makedirs(OUT, exist_ok=True)

PAL = {
    "red": (229, 72, 59), "green": (67, 176, 71), "blue": (47, 136, 224), "orange": (242, 140, 40),
    "purple": (139, 92, 246), "teal": (20, 168, 160), "slate": (84, 98, 120), "yellow": (250, 196, 45),
    "brown": (176, 112, 64),
}


def darker(c, f=0.78):
    return tuple(int(v * f) for v in c[:3]) + (255,)


def canvas():
    return Image.new("RGBA", (N, N), (0, 0, 0, 0))


def badge(d, col):
    r = N * 0.47
    c = N / 2
    d.ellipse([c - r, c - r, c + r, c + r], fill=darker(col))
    r2 = r - N * 0.035
    d.ellipse([c - r2, c - r2, c + r2, c + r2], fill=col + (255,))


def P(x, y):
    """Glyph coordinates are in a -1..1 box inside the badge."""
    s = N * 0.27
    return (N / 2 + x * s, N / 2 + y * s)


SW = N * 0.075  # glyph stroke width


def line(d, pts, w=SW, col=W):
    pts = [P(*p) for p in pts]
    d.line(pts, fill=col, width=int(w), joint="curve")
    for p in (pts[0], pts[-1]):
        d.ellipse([p[0] - w / 2, p[1] - w / 2, p[0] + w / 2, p[1] + w / 2], fill=col)


def ring(d, cx, cy, r, w=SW, col=W):
    x, y = P(cx, cy)
    rr = r * N * 0.27
    d.ellipse([x - rr, y - rr, x + rr, y + rr], outline=col, width=int(w))


def disc(d, cx, cy, r, col=W):
    x, y = P(cx, cy)
    rr = r * N * 0.27
    d.ellipse([x - rr, y - rr, x + rr, y + rr], fill=col)


def poly(d, pts, col=W):
    d.polygon([P(*p) for p in pts], fill=col)


def rrect(d, x0, y0, x1, y1, rad, col=W):
    a, b = P(x0, y0), P(x1, y1)
    d.rounded_rectangle([a[0], a[1], b[0], b[1]], radius=rad * N * 0.27, fill=col)


def arc(d, cx, cy, r, a0, a1, w=SW, col=W):
    x, y = P(cx, cy)
    rr = r * N * 0.27
    d.arc([x - rr, y - rr, x + rr, y + rr], a0, a1, fill=col, width=int(w))


ICONS = {}


def icon(name, col):
    def deco(fn):
        ICONS[name] = (col, fn)
        return fn
    return deco


@icon("close", "red")
def _(d):
    line(d, [(-0.75, -0.75), (0.75, 0.75)], SW * 1.5); line(d, [(0.75, -0.75), (-0.75, 0.75)], SW * 1.5)


@icon("plus", "green")
def _(d):
    line(d, [(-0.85, 0), (0.85, 0)], SW * 1.6); line(d, [(0, -0.85), (0, 0.85)], SW * 1.6)


@icon("minus", "orange")
def _(d):
    line(d, [(-0.85, 0), (0.85, 0)], SW * 1.6)


@icon("check", "green")
def _(d):
    line(d, [(-0.8, 0.05), (-0.25, 0.6), (0.85, -0.6)], SW * 1.5)


@icon("back", "blue")
def _(d):
    line(d, [(0.85, 0), (-0.75, 0)], SW * 1.4); line(d, [(-0.2, -0.6), (-0.8, 0), (-0.2, 0.6)], SW * 1.4)


@icon("search", "blue")
def _(d):
    ring(d, -0.18, -0.18, 0.6, SW * 1.3); line(d, [(0.28, 0.28), (0.85, 0.85)], SW * 1.6)


@icon("settings_gear", "slate")
def _(d):
    teeth = 8
    pts = []
    for i in range(teeth * 4):
        a = i / (teeth * 4) * 2 * math.pi
        r = 0.95 if (i % 4) in (1, 2) else 0.72
        pts.append((r * math.cos(a), r * math.sin(a)))
    poly(d, pts)
    disc(d, 0, 0, 0.32, ICONS["settings_gear"][2] if len(ICONS["settings_gear"]) > 2 else PAL["slate"] + (255,))


@icon("info", "blue")
def _(d):
    disc(d, 0, -0.62, 0.2); line(d, [(0, -0.12), (0, 0.78)], SW * 1.6)


@icon("help", "blue")
def _(d):
    arc(d, 0, -0.3, 0.52, 185, 400, SW * 1.05)
    line(d, [(0.4, -0.02), (0.03, 0.22), (0.03, 0.4)], SW * 1.1)
    disc(d, 0.03, 0.78, 0.16)


@icon("warning", "yellow")
def _(d):
    pass  # drawn specially (triangle)


@icon("bell", "orange")
def _(d):
    poly(d, [(-0.62, 0.45), (-0.5, -0.15), (-0.35, -0.55), (0, -0.72), (0.35, -0.55), (0.5, -0.15), (0.62, 0.45), (0.78, 0.6), (-0.78, 0.6)])
    disc(d, 0, 0.78, 0.2); disc(d, 0, -0.8, 0.12)


@icon("account", "blue")
def _(d):
    disc(d, 0, -0.35, 0.42)
    x0, y0 = P(-0.8, 0.25); x1, y1 = P(0.8, 1.35)
    d.pieslice([x0, y0, x1, y1], 180, 360, fill=W)


@icon("filter", "teal")
def _(d):
    poly(d, [(-0.85, -0.7), (0.85, -0.7), (0.18, 0.1), (0.18, 0.75), (-0.18, 0.9), (-0.18, 0.1)])


@icon("language_globe", "teal")
def _(d):
    ring(d, 0, 0, 0.85, SW * 0.95)
    x0, y0 = P(-0.36, -0.85); x1, y1 = P(0.36, 0.85)
    d.ellipse([x0, y0, x1, y1], outline=W, width=int(SW * 0.7))
    line(d, [(-0.82, 0), (0.82, 0)], SW * 0.7)
    arc(d, 0, -1.25, 0.95, 50, 130, SW * 0.6); arc(d, 0, 1.25, 0.95, 230, 310, SW * 0.6)


@icon("lock", "slate")
def _(d):
    arc(d, 0, -0.25, 0.5, 180, 360, SW * 1.0)
    line(d, [(-0.46, -0.25), (-0.46, 0.1)], SW * 1.0); line(d, [(0.46, -0.25), (0.46, 0.1)], SW * 1.0)
    rrect(d, -0.75, 0.0, 0.75, 0.9, 0.2)
    disc(d, 0, 0.42, 0.14, PAL["slate"] + (255,))


@icon("unlock", "green")
def _(d):
    arc(d, 0.42, -0.5, 0.46, 180, 360, SW * 1.0)
    line(d, [(0.84, -0.5), (0.84, -0.3)], SW * 1.0); line(d, [(0.0, -0.5), (0.0, 0.1)], SW * 1.0)
    rrect(d, -0.75, 0.0, 0.75, 0.9, 0.2)
    disc(d, 0, 0.42, 0.14, PAL["green"] + (255,))


@icon("mail", "orange")
def _(d):
    rrect(d, -0.85, -0.55, 0.85, 0.6, 0.12)
    line(d, [(-0.7, -0.4), (0, 0.12), (0.7, -0.4)], SW * 0.9, PAL["orange"] + (255,))


@icon("sound", "purple")
def _(d):
    poly(d, [(-0.85, -0.28), (-0.45, -0.28), (0.0, -0.72), (0.0, 0.72), (-0.45, 0.28), (-0.85, 0.28)])
    arc(d, 0.05, 0, 0.42, -45, 45, SW); arc(d, 0.05, 0, 0.78, -45, 45, SW)


@icon("music", "purple")
def _(d):
    disc(d, -0.45, 0.55, 0.28); disc(d, 0.55, 0.38, 0.28)
    line(d, [(-0.2, 0.55), (-0.2, -0.6)], SW); line(d, [(0.8, 0.38), (0.8, -0.78)], SW)
    poly(d, [(-0.25, -0.5), (0.85, -0.85), (0.85, -0.55), (-0.25, -0.2)])


@icon("trash", "red")
def _(d):
    rrect(d, -0.55, -0.35, 0.55, 0.88, 0.12)
    line(d, [(-0.8, -0.55), (0.8, -0.55)], SW * 1.2); line(d, [(-0.22, -0.78), (0.22, -0.78)], SW * 1.1)
    for x in (-0.25, 0.0, 0.25):
        line(d, [(x, -0.12), (x, 0.62)], SW * 0.7, PAL["red"] + (255,))


def render(name):
    col, fn = ICONS[name]
    colv = PAL[col]
    im = canvas()
    d = ImageDraw.Draw(im)
    if name == "warning":
        c = N / 2
        tri = [(c, N * 0.05), (N * 0.96, N * 0.9), (N * 0.04, N * 0.9)]
        d.polygon(tri, fill=darker(colv))
        m = N * 0.045
        tri2 = [(c, N * 0.05 + m * 1.9), (N * 0.96 - m * 1.7, N * 0.9 - m), (N * 0.04 + m * 1.7, N * 0.9 - m)]
        d.polygon(tri2, fill=colv + (255,))
        dark = (70, 52, 20, 255)
        line(d, [(0, -0.45), (0, 0.45)], SW * 1.5, dark)
        disc(d, 0, 0.95, 0.19, dark)
    else:
        badge(d, colv)
        if name == "settings_gear":
            # gear in white with the badge colour as its centre hole
            teeth = 8
            pts = []
            for i in range(teeth * 4):
                a = i / (teeth * 4) * 2 * math.pi + math.pi / (teeth * 4)
                r = 0.95 if (i % 4) in (1, 2) else 0.72
                pts.append((r * math.cos(a), r * math.sin(a)))
            poly(d, pts)
            disc(d, 0, 0, 0.32, colv + (255,))
        else:
            fn(d)
    return im.resize((SIZE, SIZE), Image.LANCZOS)


for name in ICONS:
    render(name).save(os.path.join(OUT, "ico_%s.png" % name))
print("ICONS", len(ICONS))
