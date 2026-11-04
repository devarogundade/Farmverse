"""Land, fields, pens, the well, signs and people."""
import math

from fvlib import Part, empty, rng

TILE = 2.6          # tile centre-to-corner, as WorldVisualCatalog.tileSize
FIELD = 1.62        # one of the four fields on a tile
RADIUS = 3          # land radius
SQRT3 = math.sqrt(3)


def tufts(p, colour, count, radius, seed, height=0.18, avoid=0.0):
    r = rng(seed)
    for _ in range(count):
        a = r.uniform(0, math.tau)
        d = math.sqrt(r.uniform(avoid * avoid / (radius * radius) if radius else 0, 1)) * radius
        x, y = math.cos(a) * d, math.sin(a) * d
        h = height * r.uniform(0.7, 1.3)
        p.cone(colour, r=0.07 * r.uniform(0.8, 1.3), h=h, seg=4, loc=(x, y, 0.06 + h / 2), rot=(r.uniform(-12, 12), r.uniform(-12, 12), r.uniform(0, 90)))


def flowers(p, count, radius, seed):
    r = rng(seed)
    for _ in range(count):
        a = r.uniform(0, math.tau)
        d = math.sqrt(r.random()) * radius
        x, y = math.cos(a) * d, math.sin(a) * d
        p.cyl("leaf_dark", r=0.012, h=0.16, seg=4, loc=(x, y, 0.13))
        p.ball(r.choice(["flower_pink", "flower_blue", "flower_white", "flower_yellow"]), r=0.045, loc=(x, y, 0.22), sub=1)


def land_tile(col):
    """An owned tile with nothing on it: lawn with tufts and a few flowers."""
    p = Part("Idle")
    p.hexagon("grass", r=TILE * 0.995, h=0.08, loc=(0, 0, 0.04))
    tufts(p, "grass_dark", 14, TILE * 0.75, 11)
    tufts(p, "grass_light", 8, TILE * 0.7, 12, height=0.14)
    flowers(p, 5, TILE * 0.65, 13)
    p.build(col)


def land_sale(col):
    """A tile for sale: rough meadow, a small sold-here signpost (the price is drawn by the UI)."""
    p = Part("Base")  # always shown; stages below are overlays
    p.hexagon("grass_dark", r=TILE * 0.995, h=0.06, loc=(0, 0, 0.03))
    tufts(p, "grass_light", 26, TILE * 0.82, 21, height=0.42)
    tufts(p, "hay", 10, TILE * 0.8, 22, height=0.35)
    p.cyl("wood_dark", r=0.05, h=1.0, seg=6, loc=(0, 0, 0.5))
    p.box("sign", size=(0.62, 0.06, 0.38), loc=(0, 0, 0.95), rot=(0, 0, 0))
    p.box("wood_dark", size=(0.66, 0.07, 0.04), loc=(0, 0, 1.16))
    p.build(col)
    # ready = a ring the player can buy now: little ribbon on the post
    r = Part("Ready")
    r.box("red_flag", size=(0.22, 0.02, 0.14), loc=(0.14, 0, 1.06), rot=(0, 0, 0))
    r.build(col)


def land_wild(col, seed=31):
    """Land beyond what can be bought yet: bushes, a rock, tall grass."""
    p = Part("None")
    p.hexagon("grass_dark", r=TILE * 0.995, h=0.05, loc=(0, 0, 0.025))
    r = rng(seed)
    tufts(p, "leaf_dark", 22, TILE * 0.85, seed + 1, height=0.5)
    for _ in range(3):
        a = r.uniform(0, math.tau)
        d = r.uniform(0.4, 1.6)
        p.ball(r.choice(["leaf", "leaf_dark", "oak"]), r=r.uniform(0.32, 0.5), loc=(math.cos(a) * d, math.sin(a) * d, 0.3), scale=(1, 1, 0.75))
    p.ball("rock", r=0.32, loc=(r.uniform(-1, 1), r.uniform(-1, 1), 0.12), scale=(1.3, 1, 0.6))
    p.build(col)


def plot_base(col):
    """The plot's ground: a dirt path ring around its tiles, where villagers walk."""
    reach = TILE * (SQRT3 * RADIUS + 1.0)
    p = Part("Base")
    outer = reach + 1.5
    inner = reach - 0.05
    # six trapezoid slabs make the ring
    for i in range(6):
        a0 = math.radians(60 * i + 30)
        a1 = math.radians(60 * (i + 1) + 30)
        mid = (a0 + a1) / 2
        length = 2 * ((outer + inner) / 2) * math.sin(math.radians(30)) + 0.2
        w = outer - inner
        cx = math.cos(mid) * (outer + inner) / 2 * math.cos(math.radians(30))
        cy = math.sin(mid) * (outer + inner) / 2 * math.cos(math.radians(30))
        p.box("path", size=(w, length, 0.04), loc=(cx, cy, 0.01), rot=(0, 0, math.degrees(mid)))
    # stones along the path edge
    r = rng(41)
    for i in range(36):
        a = math.radians(i * 10 + r.uniform(-3, 3))
        d = outer * math.cos(math.radians(30)) - 0.1
        p.ball("stone", r=r.uniform(0.08, 0.15), loc=(math.cos(a) * d, math.sin(a) * d, 0.03), scale=(1.2, 1, 0.5))
    p.build(col)


def field(col):
    """The tilled bed a crop grows in: dark soil with ridges filling the tile, a wooden edge."""
    p = Part("Base")  # the same at every stage; the crop model stands on it
    p.hexagon("soil_dark", r=TILE * 0.9, h=0.08, loc=(0, 0, 0.08))
    span = 3.3
    for i in range(6):
        y = -span / 2 + span * (i + 0.5) / 6
        width = span * (1.0 - abs(y) / (TILE * 1.2))
        p.box("soil", size=(width, 0.3, 0.06), loc=(0, y, 0.14))
    p.hexagon("wood", r=TILE * 0.93, h=0.06, loc=(0, 0, 0.06))
    p.build(col)


def pen(col):
    """A fenced paddock filling a tile, with a trough and straw."""
    p = Part("Idle")
    p.hexagon("hay", r=TILE * 0.9, h=0.05, loc=(0, 0, 0.025))
    tufts(p, "grass_light", 10, TILE * 0.7, 61, height=0.1)
    ring = TILE * 0.86
    posts = []
    for i in range(6):
        a = math.radians(60 * i + 30)
        posts.append((math.cos(a) * ring, math.sin(a) * ring))
    for i, (x, y) in enumerate(posts):
        p.cyl("wood_dark", r=0.06, h=0.75, seg=6, loc=(x, y, 0.375))
        nx, ny = posts[(i + 1) % 6]
        if i == 4:
            continue  # the gate side stays open
        mx, my = (x + nx) / 2, (y + ny) / 2
        ang = math.degrees(math.atan2(ny - y, nx - x))
        length = math.hypot(nx - x, ny - y)
        for z in (0.32, 0.6):
            p.box("wood", size=(length, 0.05, 0.08), loc=(mx, my, z), rot=(0, 0, ang))
    # trough
    p.box("wood", size=(0.9, 0.34, 0.24), loc=(0.2, ring * 0.55, 0.12))
    p.box("feed", size=(0.8, 0.25, 0.04), loc=(0.2, ring * 0.55, 0.23))
    # a little shelter
    p.box("wood_light", size=(0.9, 0.8, 0.6), loc=(-1.0, 0.6, 0.3))
    p.box("roof_red", size=(1.1, 1.0, 0.08), loc=(-1.0, 0.6, 0.66), rot=(10, 0, 0))
    p.build(col)


def well(col):
    """The plot's well (A64): stone ring, roof, crank and bucket; water shows when full."""
    base = Part("Base")  # always shown; water / dry floor are the stage overlays
    base.cyl("stone", r=0.62, h=0.7, seg=12, loc=(0, 0, 0.35))
    base.cyl("stone_dark", r=0.5, h=0.72, seg=12, loc=(0, 0, 0.37))
    for x in (-0.55, 0.55):
        base.box("wood_dark", size=(0.1, 0.1, 1.5), loc=(x, 0, 1.05))
    base.box("roof_red", size=(1.5, 0.9, 0.08), loc=(0, 0.22, 1.86), rot=(28, 0, 0))
    base.box("roof_red", size=(1.5, 0.9, 0.08), loc=(0, -0.22, 1.86), rot=(-28, 0, 0))
    base.cyl("wood", r=0.06, h=1.2, seg=8, loc=(0, 0, 1.25), rot=(0, 90, 0))
    base.box("wood_dark", size=(0.05, 0.05, 0.28), loc=(0.68, 0, 1.12))
    base.cyl("rope", r=0.012, h=0.5, seg=4, loc=(0, 0, 0.98))
    base.cyl("wood_light", r=0.13, h=0.18, seg=8, loc=(0, 0, 0.66), r2=0.15)
    base.build(col)
    full = Part("Ready")
    full.cyl("water", r=0.49, h=0.02, seg=12, loc=(0, 0, 0.72))
    full.build(col)
    low = Part("Broken")  # no well level yet: just the stone ring, dry
    low.cyl("soil_dry", r=0.49, h=0.02, seg=12, loc=(0, 0, 0.72))
    low.build(col)


def plot_sale(col):
    """A plot position the player can buy next: corner stakes, rope and a big sign."""
    p = Part("Idle")
    ring = TILE * (SQRT3 * RADIUS) * 0.6
    for i in range(6):
        a = math.radians(60 * i + 30)
        x, y = math.cos(a) * ring, math.sin(a) * ring
        p.cyl("wood", r=0.08, h=0.8, seg=6, loc=(x, y, 0.4))
        p.box("red_flag", size=(0.25, 0.02, 0.15), loc=(x + 0.12, y, 0.72))
    p.cyl("wood_dark", r=0.09, h=2.2, seg=6, loc=(-0.6, 0, 1.1))
    p.cyl("wood_dark", r=0.09, h=2.2, seg=6, loc=(0.6, 0, 1.1))
    p.box("sign", size=(1.7, 0.1, 0.9), loc=(0, 0, 1.75))
    p.box("wood_dark", size=(1.8, 0.12, 0.08), loc=(0, 0, 2.22))
    p.box("wood_dark", size=(1.8, 0.12, 0.08), loc=(0, 0, 1.28))
    p.build(col)


def villager(col, shirt, skin, hat, seed):
    """A farmhand: boots, trousers, shirt, arms, head, straw hat or cap."""
    p = Part("Body")
    for x in (-0.09, 0.09):
        p.box("boot", size=(0.12, 0.2, 0.1), loc=(x, 0.03, 0.05))
        p.box("pants", size=(0.13, 0.14, 0.5), loc=(x, 0, 0.35))
    p.box(shirt, size=(0.38, 0.24, 0.48), loc=(0, 0, 0.84))
    for x in (-0.24, 0.24):
        p.box(shirt, size=(0.1, 0.12, 0.42), loc=(x, 0, 0.84), rot=(0, 0, 0))
        p.ball(skin, r=0.055, loc=(x, 0, 0.6))
    p.ball(skin, r=0.17, loc=(0, 0, 1.24), sub=2)
    p.ball("hair", r=0.172, loc=(0, 0.03, 1.27), sub=2, scale=(1, 1, 0.8))
    if hat == "straw":
        p.cyl("straw_hat", r=0.3, h=0.03, seg=12, loc=(0, 0, 1.37))
        p.cyl("straw_hat", r=0.15, h=0.14, seg=12, loc=(0, 0, 1.44))
        p.cyl("red_flag", r=0.152, h=0.03, seg=12, loc=(0, 0, 1.39))
    else:
        p.cyl(shirt, r=0.17, h=0.1, seg=12, loc=(0, 0, 1.38))
        p.box(shirt, size=(0.16, 0.14, 0.03), loc=(0, -0.2, 1.35))
    p.ball("black", r=0.022, loc=(-0.06, -0.15, 1.27))
    p.ball("black", r=0.022, loc=(0.06, -0.15, 1.27))
    p.build(col)
