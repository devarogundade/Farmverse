"""Small realistic details shared by the world models: grass clumps, flowers, sacks, stones,
hay bales, lanterns, potted plants."""
import math

from fvlib import cap, ell, rng


def grass_clump(p, x, y, z, height, seed, colours=("turf_dark", "turf", "turf_light"), blades=7):
    """A clump of real blades fanning out from one point (cheap three-sided blades)."""
    r = rng(seed)
    for k in range(blades):
        heading = k * 360.0 / blades + r.uniform(-25, 25)
        h = height * r.uniform(0.7, 1.15)
        p.blade(colours[k % len(colours)], (x + r.uniform(-0.03, 0.03), y + r.uniform(-0.03, 0.03), z), heading,
                h, 0.018 + 0.008 * r.random(), rise=r.uniform(68, 85), droop=r.uniform(35, 75))


def grass_field(p, count, radius, seed, z=0.0, height=0.22, avoid=0.0, colours=("turf_dark", "turf", "turf_light")):
    r = rng(seed)
    for i in range(count):
        a = r.uniform(0, math.tau)
        d = math.sqrt(r.uniform((avoid / radius) ** 2 if radius else 0, 1)) * radius
        grass_clump(p, math.cos(a) * d, math.sin(a) * d, z, height, seed * 100 + i, colours, blades=r.randint(5, 8))


def flower(p, x, y, z, colour, seed, height=0.2):
    """A small flower: a stem, two leaves and five petals around a yellow heart."""
    r = rng(seed)
    p.cyl("leaf", r=0.008, h=height, seg=5, loc=(x, y, z + height / 2))
    for k in range(2):
        p.leaf("leaf_bright", (x, y, z + height * 0.3), r.uniform(0, 360) + k * 180, 0.07, 0.025, rise=30, droop=40, segs=3)
    top = z + height
    for k in range(5):
        p.leaf(colour, (x, y, top), k * 72 + r.uniform(-6, 6), 0.045, 0.026, rise=20, droop=10, fold=0.05, segs=2, thick=0.004)
    p.ball("sun_yellow" if colour != "flower_yellow" else "sun_seed", r=0.016, loc=(x, y, top + 0.01), sub=1)


def flowers(p, count, radius, seed, z=0.0, avoid=0.0):
    r = rng(seed)
    colours = ["flower_pink", "flower_white", "flower_yellow", "flower_blue"]
    for i in range(count):
        a = r.uniform(0, math.tau)
        d = math.sqrt(r.uniform((avoid / radius) ** 2 if radius else 0, 1)) * radius
        flower(p, math.cos(a) * d, math.sin(a) * d, z, r.choice(colours), seed * 50 + i, height=r.uniform(0.14, 0.26))


def stone(p, x, y, z, size, seed, colour="pebble"):
    """A weathered stone: a few melted lumps, low-poly facets kept."""
    r = rng(seed)
    els = [ell((x, y, z + size * 0.25), (size, size * r.uniform(0.7, 0.95), size * 0.55), rot=(0, 0, r.uniform(0, 180)))]
    for _ in range(2):
        a = r.uniform(0, math.tau)
        els.append(ell((x + math.cos(a) * size * 0.45, y + math.sin(a) * size * 0.45, z + size * 0.18),
                       (size * 0.55, size * 0.45, size * 0.4)))
    p.organic(els, lambda c, n: colour if n.z > 0.1 else "pebble_dark", voxel=max(0.015, size * 0.12), ratio=0.12, smooth=2)


def sack(p, x, y, z, colour="canvas", s=1.0, lean=0.0):
    """A filled grain sack: a soft lumpy body, gathered neck and a rope tie."""
    els = [ell((x, y, z + 0.22 * s), (0.2 * s, 0.17 * s, 0.22 * s), rot=(0, lean, 0)),
           ell((x, y, z + 0.1 * s), (0.22 * s, 0.19 * s, 0.11 * s)),
           cap((x, y, z + 0.36 * s), (x, y, z + 0.5 * s), 0.07 * s, 0.05 * s)]
    p.organic(els, lambda c, n: colour, voxel=0.02 * s, ratio=0.25, smooth=4)
    p.rcyl("rope", r=0.06 * s, h=0.04 * s, seg=10, loc=(x, y, z + 0.43 * s), radius=0.01)
    p.cone(colour, r=0.08 * s, h=0.08 * s, seg=8, loc=(x, y, z + 0.53 * s), rot=(180, 0, 0))


def hay_bale(p, x, y, z, s=1.0, rot=0.0):
    """A round hay bale with its twine bands."""
    p.rcyl("straw", r=0.38 * s, h=0.62 * s, seg=20, loc=(x, y, z + 0.38 * s), rot=(90, 0, rot), radius=0.08 * s)
    for k in (-1, 1):
        dx, dy = math.cos(math.radians(rot + 90)) * 0.18 * s * k, math.sin(math.radians(rot + 90)) * 0.18 * s * k
        p.rcyl("rope", r=0.385 * s, h=0.03 * s, seg=20, loc=(x + dx, y + dy, z + 0.38 * s), rot=(90, 0, rot), radius=0.008)


def lantern(p, x, y, z):
    p.rbox("iron", size=(0.16, 0.16, 0.03), loc=(x, y, z), radius=0.01)
    p.rbox("window_glow", size=(0.12, 0.12, 0.18), loc=(x, y, z + 0.1), radius=0.02)
    p.cone("iron", r=0.12, h=0.08, seg=4, loc=(x, y, z + 0.23), rot=(0, 0, 45))
    p.ball("iron", r=0.02, loc=(x, y, z + 0.29))


def potted_plant(p, x, y, z, seed, flowers_colour="flower_pink"):
    p.rcyl("chimney_orange", r=0.12, h=0.18, seg=14, loc=(x, y, z + 0.09), radius=0.02, r2=0.09)
    p.rcyl("tilled", r=0.105, h=0.02, seg=14, loc=(x, y, z + 0.18), radius=0.005)
    p.organic([ell((x, y, z + 0.3), (0.13, 0.13, 0.12))], lambda c, n: "leaf" if n.z < 0.4 else "leaf_bright", voxel=0.02, ratio=0.25, smooth=3)
    r = rng(seed)
    for k in range(4):
        a = r.uniform(0, math.tau)
        p.ball(flowers_colour, r=0.03, loc=(x + math.cos(a) * 0.09, y + math.sin(a) * 0.09, z + 0.36), sub=1)


def plank_stack(p, x, y, z, seed):
    r = rng(seed)
    for k in range(3):
        p.rbox("wood_light", size=(0.9, 0.16, 0.05), loc=(x, y + r.uniform(-0.02, 0.02), z + 0.03 + k * 0.055), rot=(0, 0, r.uniform(-4, 4)), radius=0.01)
