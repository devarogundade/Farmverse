"""Crops (five growth stages each, a field filling one hex tile) and trees."""
import math

from fvlib import Part, rng

FIELD = 1.62
# a crop fills its tile with four beds of plants, 2 x 2, inside the hexagon (inradius 2.25)
BEDS = [(-0.82, -0.82), (0.82, -0.82), (-0.82, 0.82), (0.82, 0.82)]
_OFFSET = [0.0, 0.0]
STAGES = ["Seeded", "Sprout", "Young", "Grown", "Ready"]


def grid(rows=3, cols=3, inset=0.22, jitter=0.05, seed=1):
    r = rng(seed)
    span = FIELD - inset * 2
    for i in range(rows):
        for j in range(cols):
            x = _OFFSET[0] - span / 2 + span * (j + 0.5) / cols + r.uniform(-jitter, jitter)
            y = _OFFSET[1] - span / 2 + span * (i + 0.5) / rows + r.uniform(-jitter, jitter)
            yield x, y, r


def mounds(p, seed):
    for x, y, r in grid(seed=seed):
        p.ball("soil", r=0.08, loc=(x, y, 0.13), scale=(1.2, 1, 0.5))
        p.ball("wheat_dark", r=0.02, loc=(x + 0.02, y, 0.17))


def shoots(p, seed, colour="sprout", h=0.16, n=3):
    for x, y, r in grid(seed=seed):
        for k in range(n):
            a = math.tau * k / n + r.uniform(0, 1)
            p.cone(colour, r=0.03, h=h, seg=4, loc=(x + math.cos(a) * 0.03, y + math.sin(a) * 0.03, 0.12 + h / 2), rot=(math.cos(a) * 20, math.sin(a) * 20, 0))


def leaves(p, x, y, colour, count, length, lift, seed, width=0.06, droop=35):
    r = rng(seed)
    for k in range(count):
        a = 360 * k / count + r.uniform(-15, 15)
        rad = math.radians(a)
        p.box(colour, size=(width, length, 0.015), loc=(x + math.cos(rad) * length * 0.42, y + math.sin(rad) * length * 0.42, lift), rot=(droop * math.sin(rad) * -1, droop * math.cos(rad), a - 90))


# ---- each crop: (young, grown, ready) builders over the 3x3 grid --------------------------

def wheat(stage, p, seed):
    if stage == "Young":
        shoots(p, seed, "leaf_light", h=0.3, n=5)
        return
    colour = "leaf" if stage == "Grown" else "wheat"
    for x, y, r in grid(4, 4, inset=0.14, seed=seed):
        for k in range(4):
            dx, dy = r.uniform(-0.06, 0.06), r.uniform(-0.06, 0.06)
            h = r.uniform(0.6, 0.75)
            p.cyl(colour, r=0.012, h=h, seg=4, loc=(x + dx, y + dy, 0.12 + h / 2), rot=(r.uniform(-6, 6), r.uniform(-6, 6), 0))
            if stage == "Ready":
                p.ball("wheat_dark" if k % 2 else "wheat", r=0.035, loc=(x + dx, y + dy, 0.12 + h + 0.05), scale=(0.7, 0.7, 2.0))


def maize(stage, p, seed):
    for x, y, r in grid(2, 2, inset=0.35, seed=seed):
        h = {"Young": 0.45, "Grown": 1.15, "Ready": 1.3}[stage]
        p.cyl("leaf", r=0.03, h=h, seg=6, loc=(x, y, 0.12 + h / 2))
        leaves(p, x, y, "leaf" if stage != "Young" else "leaf_light", 6 if stage != "Young" else 4, 0.38 if stage != "Young" else 0.24, 0.12 + h * 0.55, seed + int(x * 100), width=0.08)
        if stage != "Young":
            leaves(p, x, y, "leaf_dark", 4, 0.32, 0.12 + h * 0.8, seed + 7, width=0.07)
            p.cone("husk", r=0.03, h=0.18, seg=4, loc=(x, y, 0.12 + h + 0.08))
        if stage == "Ready":
            for k in range(2):
                a = math.radians(90 + 180 * k)
                p.ball("maize", r=0.06, loc=(x + math.cos(a) * 0.07, y + math.sin(a) * 0.07, 0.12 + h * 0.6), scale=(0.8, 0.8, 2.0))
                p.box("husk", size=(0.08, 0.02, 0.16), loc=(x + math.cos(a) * 0.08, y + math.sin(a) * 0.08, 0.12 + h * 0.56))


def bushy(colour_leaf, colour_leaf2, size):
    def build(stage, p, seed):
        for x, y, r in grid(seed=seed):
            s = {"Young": 0.55, "Grown": 1.0, "Ready": 1.0}[stage] * size
            p.ball(colour_leaf, r=0.16 * s, loc=(x, y, 0.12 + 0.12 * s), scale=(1.1, 1.1, 0.8))
            p.ball(colour_leaf2, r=0.12 * s, loc=(x + 0.06 * s, y - 0.05 * s, 0.12 + 0.22 * s), scale=(1, 1, 0.8))
    return build


def potato(stage, p, seed):
    bushy("leaf", "leaf_light", 1.15)(stage, p, seed)
    if stage == "Ready":
        for x, y, r in grid(seed=seed):
            for k in range(3):
                a = math.tau * k / 3 + r.random()
                p.ball("potato", r=0.065, loc=(x + math.cos(a) * 0.2, y + math.sin(a) * 0.2, 0.15), scale=(1.3, 1, 0.9))


def tomato(stage, p, seed):
    for x, y, r in grid(2, 2, inset=0.32, seed=seed):
        h = {"Young": 0.35, "Grown": 0.75, "Ready": 0.8}[stage]
        p.cyl("wood_light", r=0.015, h=h + 0.15, seg=4, loc=(x + 0.08, y, 0.12 + (h + 0.15) / 2))
        for k in range(3 if stage == "Young" else 5):
            z = 0.12 + h * (0.3 + 0.16 * k)
            p.ball("leaf" if k % 2 else "leaf_dark", r=0.13 if stage != "Young" else 0.09, loc=(x + r.uniform(-0.06, 0.06), y + r.uniform(-0.06, 0.06), z), scale=(1.2, 1.2, 0.7))
        if stage == "Ready":
            for k in range(6):
                a = math.tau * k / 6
                p.ball("tomato", r=0.06, loc=(x + math.cos(a) * 0.15, y + math.sin(a) * 0.15, 0.12 + h * (0.35 + 0.1 * (k % 3))), sub=2)


def onion(stage, p, seed):
    for x, y, r in grid(seed=seed):
        n = 3 if stage == "Young" else 5
        h = 0.25 if stage == "Young" else 0.42
        for k in range(n):
            a = math.tau * k / n
            p.cone("leaf" if k % 2 else "leaf_light", r=0.02, h=h, seg=4, loc=(x + math.cos(a) * 0.02, y + math.sin(a) * 0.02, 0.14 + h / 2), rot=(math.cos(a) * 12, math.sin(a) * 12, 0))
        if stage == "Ready":
            p.ball("onion", r=0.09, loc=(x, y, 0.16), sub=2, scale=(1, 1, 0.85))
            p.ball("onion_light", r=0.04, loc=(x, y, 0.24))


def beans(stage, p, seed):
    for x, y, r in grid(2, 2, inset=0.32, seed=seed):
        h = {"Young": 0.4, "Grown": 0.95, "Ready": 0.95}[stage]
        # a cane tripod with the vine climbing it
        for k in range(3):
            a = math.tau * k / 3
            p.cyl("wood_light", r=0.012, h=h + 0.2, seg=4, loc=(x + math.cos(a) * 0.08, y + math.sin(a) * 0.08, 0.12 + (h + 0.2) / 2), rot=(-math.sin(a) * 8, math.cos(a) * 8, 0))
        for k in range(4 if stage == "Young" else 7):
            z = 0.15 + h * (k / 7)
            a = k * 1.7
            p.ball("leaf" if k % 2 else "leaf_light", r=0.08, loc=(x + math.cos(a) * 0.09, y + math.sin(a) * 0.09, z), scale=(1.2, 1.2, 0.6))
        if stage == "Ready":
            for k in range(6):
                a = k * 1.1
                p.box("bean", size=(0.035, 0.035, 0.18), loc=(x + math.cos(a) * 0.14, y + math.sin(a) * 0.14, 0.2 + h * (k / 7)), rot=(0, 15, 0))


def sunflower(stage, p, seed):
    for x, y, r in grid(2, 2, inset=0.35, seed=seed):
        h = {"Young": 0.5, "Grown": 1.35, "Ready": 1.5}[stage]
        p.cyl("leaf", r=0.03, h=h, seg=6, loc=(x, y, 0.12 + h / 2))
        leaves(p, x, y, "leaf", 4, 0.22, 0.12 + h * 0.4, seed + 3, width=0.12, droop=20)
        if stage == "Young":
            continue
        top = 0.12 + h
        if stage == "Grown":
            p.ball("leaf_light", r=0.1, loc=(x, y - 0.04, top), scale=(1, 0.6, 1))
            continue
        p.cyl("sun_seed", r=0.13, h=0.05, seg=10, loc=(x, y - 0.08, top), rot=(75, 0, 0))
        for k in range(12):
            a = math.tau * k / 12
            p.box("sun_petal", size=(0.07, 0.02, 0.13), loc=(x + math.cos(a) * 0.17, y - 0.1, top + math.sin(a) * 0.17), rot=(0, -math.degrees(a) + 90, 0))


CROPS = {
    "crop_wheat": wheat,
    "crop_maize": maize,
    "crop_potato": potato,
    "crop_tomato": tomato,
    "crop_onion": onion,
    "crop_beans": beans,
    "crop_sunflower": sunflower,
}


def crop(col, crop_id, seed=101):
    build = CROPS[crop_id]
    for stage in STAGES:
        p = Part(stage)
        for k, (ox, oy) in enumerate(BEDS):
            _OFFSET[0], _OFFSET[1] = ox, oy
            if stage == "Seeded":
                mounds(p, seed + k)
            elif stage == "Sprout":
                shoots(p, seed + k)
            else:
                build(stage, p, seed + k)
        _OFFSET[0], _OFFSET[1] = 0.0, 0.0
        p.build(col)


# ---- trees --------------------------------------------------------------------------------

def pine_shape(p, scale, seed):
    r = rng(seed)
    p.cyl("bark", r=0.12 * scale, h=0.8 * scale, seg=7, loc=(0, 0, 0.4 * scale), r2=0.09 * scale)
    tiers = 4
    for k in range(tiers):
        z = 0.55 * scale + k * 0.62 * scale
        rad = (1.0 - k * 0.2) * 0.85 * scale
        p.cone("pine" if k % 2 == 0 else "pine_light", r=rad, h=1.05 * scale, seg=8, loc=(0, 0, z + 0.52 * scale), rot=(0, 0, r.uniform(0, 45)))


def oak_shape(p, scale, seed):
    r = rng(seed)
    p.cyl("bark", r=0.16 * scale, h=1.3 * scale, seg=7, loc=(0, 0, 0.65 * scale), r2=0.11 * scale)
    p.cyl("bark", r=0.06 * scale, h=0.6 * scale, seg=5, loc=(0.18 * scale, 0, 1.25 * scale), rot=(0, 35, 0))
    for k in range(5):
        a = math.tau * k / 5 + r.random()
        d = 0.42 * scale
        p.ball("oak" if k % 2 else "oak_light", r=0.62 * scale, loc=(math.cos(a) * d, math.sin(a) * d, 1.75 * scale + r.uniform(-0.15, 0.2) * scale), scale=(1, 1, 0.85))
    p.ball("oak", r=0.72 * scale, loc=(0, 0, 2.15 * scale), scale=(1, 1, 0.8))


def tree(col, kind, seed=201):
    shape = pine_shape if kind == "tree_pine" else oak_shape
    for stage, scale in (("Young", 0.4), ("Grown", 0.72), ("Ready", 1.0)):
        p = Part(stage)
        shape(p, scale, seed)
        p.ball("soil_dark", r=0.3 * max(scale, 0.6), loc=(0, 0, 0.0), scale=(1.4, 1.4, 0.2))
        p.build(col)
