"""Sculpted models (rich stylized): animals, farmhands and trees shaped from metaballs so they
read soft and alive, with materials from the atlas (fur, foliage, bark). Front is -Y."""
import math

from mathutils import Vector

from fvlib import Part, cap, ell, noise3, rng


# ---- cow -------------------------------------------------------------------------------------------

def cow_body(p, s=1.0):
    """A dairy cow: deep barrel body, hips and shoulders, a long head with a pink muzzle,
    patches that follow the body, hooves, horns, ears, udder and a tufted tail."""
    def S(v):
        return tuple(c * s for c in v)
    els = [
        ell(S((0, 0.05, 0.8)), S((0.36, 0.62, 0.36))),
        ell(S((0, 0.45, 0.86)), S((0.34, 0.3, 0.34))),
        ell(S((0, -0.36, 0.86)), S((0.33, 0.3, 0.37))),
        cap(S((0, -0.5, 0.95)), S((0, -0.76, 1.08)), 0.21 * s, 0.16 * s),
        ell(S((0, -0.86, 1.07)), S((0.17, 0.24, 0.18)), rot=(-25, 0, 0)),
        ell(S((0, -1.04, 0.95)), S((0.15, 0.13, 0.12))),
        ell(S((0, 0.25, 0.5)), S((0.14, 0.16, 0.1))),
    ]
    for x in (-0.2, 0.2):
        for y in (-0.4, 0.46):
            els.append(cap(S((x, y, 0.66)), S((x, y, 0.1)), 0.1 * s, 0.075 * s))
            els.append(ell(S((x, y - 0.01, 0.06)), S((0.085, 0.095, 0.06))))

    def colour(c, n):
        x, y, z = c.x / s, c.y / s, c.z / s
        if z < 0.12:
            return "eye"                                    # hooves
        if y < -0.98 and z < 1.03:
            return "muzzle"
        if 0.08 < y < 0.42 and z < 0.58 and abs(x) < 0.16:
            return "cow_nose"                               # udder
        if y < -0.72:
            return "cow_cream"
        if noise3((x, y * 0.8, z), 1.9, 3) > 0.15 and z > 0.32:
            return "eye"                                    # black patches
        return "cow_cream"

    p.organic(els, colour, voxel=0.028 * s, ratio=0.3, smooth=6)
    for x in (-1, 1):
        p.ball("eye", r=0.04 * s, loc=(x * 0.15 * s, -0.98 * s, 1.15 * s), sub=2)
        p.ball("white", r=0.012 * s, loc=(x * 0.16 * s, -1.015 * s, 1.17 * s))
        p.ball("eye", r=0.022 * s, loc=(x * 0.06 * s, -1.17 * s, 0.96 * s), scale=(1, 0.5, 1.3))
        p.cone("horn", r=0.04 * s, h=0.16 * s, seg=10, loc=(x * 0.14 * s, -0.8 * s, 1.3 * s), rot=(0, x * 35, 0))
        p.ball("cow_cream", r=0.1 * s, loc=(x * 0.24 * s, -0.78 * s, 1.17 * s), scale=(1.5, 0.4, 0.7), rot=(0, x * -25, 0), sub=2)
        p.ball("cow_nose", r=0.065 * s, loc=(x * 0.25 * s, -0.8 * s, 1.17 * s), scale=(1.4, 0.3, 0.6), rot=(0, x * -25, 0), sub=2)
        for k in range(2):
            p.ball("cow_nose", r=0.022 * s, loc=((k * 2 - 1) * 0.05 * s + x * 0.0, 0.25 * s + x * 0.05 * s, 0.39 * s), scale=(1, 1, 1.4))
    p.cyl("cow_cream", r=0.025 * s, h=0.55 * s, seg=8, loc=(0, 0.93 * s, 0.68 * s), rot=(14, 0, 0))
    p.ball("eye", r=0.06 * s, loc=(0, 0.99 * s, 0.4 * s), scale=(0.9, 0.9, 1.6), sub=2)


def cow(col):
    grown = Part("Grown")
    cow_body(grown, 0.85)
    grown.build(col)
    product = Part("Product")
    product.rcyl("iron_light", r=0.13, h=0.36, seg=18, loc=(0.62, -0.25, 0.18), radius=0.04)
    product.rcyl("iron", r=0.135, h=0.04, seg=18, loc=(0.62, -0.25, 0.08), radius=0.01)
    product.rcyl("iron", r=0.135, h=0.04, seg=18, loc=(0.62, -0.25, 0.3), radius=0.01)
    product.rcyl("iron_light", r=0.08, h=0.12, seg=16, loc=(0.62, -0.25, 0.42), radius=0.02)
    product.rcyl("milk", r=0.07, h=0.02, seg=16, loc=(0.62, -0.25, 0.48), radius=0.005)
    product.build(col)


# ---- chicken ---------------------------------------------------------------------------------------

def hen_body(p, s=1.0):
    def S(v):
        return tuple(c * s for c in v)
    els = [
        ell(S((0, 0.05, 0.33)), S((0.2, 0.28, 0.21))),
        ell(S((0, -0.12, 0.38)), S((0.17, 0.16, 0.18))),
        cap(S((0, -0.15, 0.45)), S((0, -0.2, 0.6)), 0.1 * s, 0.085 * s),
        ell(S((0, -0.21, 0.64)), S((0.095, 0.11, 0.105))),
        ell(S((0, 0.29, 0.5)), S((0.09, 0.12, 0.17)), rot=(-35, 0, 0)),
    ]
    for x in (-1, 1):
        els.append(ell(S((x * 0.18, 0.07, 0.36)), S((0.06, 0.19, 0.12)), rot=(-12, 0, 0)))

    def colour(c, n):
        if abs(c.x) > 0.19 * s and c.z > 0.26 * s:
            return "hen"                                   # wing feathers a shade warmer
        return "hen_cream"

    p.organic(els, colour, voxel=0.014 * s, ratio=0.3, smooth=5)
    for x in (-1, 1):
        p.ball("eye", r=0.022 * s, loc=(x * 0.075 * s, -0.29 * s, 0.67 * s), sub=2)
        p.cyl("beak", r=0.02 * s, h=0.17 * s, seg=8, loc=(x * 0.08 * s, 0.03 * s, 0.1 * s))
        for a in (-35, 0, 35):
            p.cyl("beak", r=0.012 * s, h=0.1 * s, seg=6, loc=(x * 0.08 * s + math.sin(math.radians(a)) * 0.04 * s, 0.03 * s - math.cos(math.radians(a)) * 0.04 * s, 0.012 * s), rot=(90, 0, a))
    for k in range(5):
        a = (k - 2) * 14
        p.ball("hen", r=0.09 * s, loc=(math.sin(math.radians(a)) * 0.07 * s, 0.34 * s, 0.64 * s), scale=(0.3, 0.6, 1.5), rot=(-25, a, 0), sub=2)
    p.cone("beak", r=0.04 * s, h=0.1 * s, seg=10, loc=(0, -0.35 * s, 0.63 * s), rot=(100, 0, 0))
    for dy, dz, r in ((-0.27, 0.75, 0.04), (-0.21, 0.78, 0.05), (-0.15, 0.76, 0.045), (-0.1, 0.72, 0.035)):
        p.ball("comb", r=r * s, loc=(0, dy * s, dz * s), scale=(0.6, 1, 1.2), sub=2)
    p.ball("comb", r=0.032 * s, loc=(0, -0.31 * s, 0.54 * s), scale=(0.8, 0.8, 1.4), sub=2)


def chicken(col):
    grown = Part("Grown")
    hen_body(grown)
    grown.build(col)
    product = Part("Product")
    product.organic([ell((0.42, 0.25, 0.05), (0.2, 0.2, 0.07))], lambda c, n: "straw", voxel=0.02, ratio=0.3)
    for k in range(3):
        a = math.tau * k / 3
        product.ball("egg", r=0.055, loc=(0.42 + math.cos(a) * 0.07, 0.25 + math.sin(a) * 0.07, 0.13), scale=(1, 1, 1.3), sub=3)
    product.build(col)


# ---- farmhands -------------------------------------------------------------------------------------

def villager(col, shirt, skin, hat, seed):
    """A farmhand in overalls: soft body, rounded limbs, boots, hands, face, hair and a hat."""
    p = Part("Body")
    els = [
        ell((0, 0, 0.9), (0.19, 0.13, 0.24)),
        ell((0, 0, 0.66), (0.17, 0.12, 0.12)),
        cap((0, 0, 1.1), (0, 0, 1.22), 0.06),
        ell((0, -0.01, 1.33), (0.145, 0.145, 0.155)),
    ]
    for x in (-1, 1):
        els += [
            cap((x * 0.085, 0, 0.6), (x * 0.085, 0, 0.13), 0.075, 0.062),
            ell((x * 0.085, -0.04, 0.065), (0.07, 0.115, 0.06)),
            cap((x * 0.2, 0, 1.04), (x * 0.27, -0.03, 0.67), 0.058, 0.046),
            ell((x * 0.28, -0.04, 0.62), (0.042, 0.042, 0.052)),
        ]

    def colour(c, n):
        z, ax, y = c.z, abs(c.x), c.y
        if z < 0.12:
            return "boot"
        if z > 1.16:
            return skin
        if ax > 0.18 and z > 0.5:
            return skin if z < 0.67 else shirt              # hands, sleeves
        if z < 0.78:
            return "pants"
        if z < 1.0 and ax < 0.13 and y < 0.0:
            return "pants"                                  # overall bib
        if 0.06 < ax < 0.105 and y < 0.02 and z < 1.12:
            return "pants"                                  # straps
        return shirt

    p.organic(els, colour, voxel=0.014, ratio=0.4, smooth=5)
    for x in (-1, 1):
        p.ball("black", r=0.02, loc=(x * 0.055, -0.14, 1.35), sub=2)
        p.ball(skin, r=0.028, loc=(x * 0.145, -0.0, 1.33), scale=(0.6, 1, 1.2), sub=2)
        p.ball("gold", r=0.016, loc=(x * 0.08, -0.125, 0.98), sub=1)
    p.ball(skin, r=0.026, loc=(0, -0.16, 1.31), sub=2)
    p.ball("hair", r=0.152, loc=(0, 0.03, 1.37), scale=(1.03, 1.0, 0.78), sub=3)
    if hat == "straw":
        p.rcyl("straw_hat", r=0.3, h=0.035, seg=24, loc=(0, 0, 1.46), radius=0.012)
        p.rcyl("straw_hat", r=0.15, h=0.14, seg=20, loc=(0, 0, 1.53), radius=0.05, r2=0.13)
        p.rcyl("red_flag", r=0.152, h=0.035, seg=20, loc=(0, 0, 1.49), radius=0.01)
    else:
        p.ball(shirt, r=0.165, loc=(0, 0.0, 1.42), scale=(1, 1, 0.62), sub=3)
        p.rbox(shirt, size=(0.22, 0.17, 0.03), loc=(0, -0.19, 1.41), rot=(-8, 0, 0), radius=0.012)
    p.build(col)


# ---- trees -----------------------------------------------------------------------------------------

def trunk_parts(s, height, r0, r1, seed, branches=3):
    els = [cap((0, 0, 0), (0, 0, height * s), r0 * s, r1 * s),
           ell((0, 0, 0.04 * s), (r0 * 1.6 * s, r0 * 1.6 * s, 0.08 * s))]
    r = rng(seed)
    for k in range(branches):
        a = math.tau * k / branches + r.random()
        z0 = (0.7 + 0.15 * r.random()) * height * s
        ln = 0.5 * s
        els.append(cap((0, 0, z0), (math.cos(a) * ln, math.sin(a) * ln, z0 + ln * 0.8), r1 * 0.75 * s, r1 * 0.4 * s))
    return els


def oak_shape(p, s, seed=5, detail=1.0):
    p.organic(trunk_parts(s, 1.5, 0.16, 0.11, seed), lambda c, n: "trunk", voxel=0.035 * s / detail ** 0.5, ratio=0.35 * detail, smooth=3)
    r = rng(seed)
    els = [ell((0, 0, 1.95 * s), (0.78 * s, 0.78 * s, 0.62 * s))]
    for k in range(9):
        a = math.tau * k / 9 + r.random() * 0.4
        d = r.uniform(0.5, 0.72) * s
        rad = r.uniform(0.36, 0.48) * s
        els.append(ell((math.cos(a) * d, math.sin(a) * d, (1.8 + r.uniform(-0.12, 0.4)) * s), (rad, rad, rad * 0.9)))
    for k in range(4):
        a = r.random() * math.tau
        rad = 0.4 * s
        els.append(ell((math.cos(a) * 0.25 * s, math.sin(a) * 0.25 * s, (2.35 + r.uniform(0, 0.15)) * s), (rad, rad, rad)))

    def colour(c, n):
        if n.z < -0.35:
            return "leaf_deep"
        if n.z > 0.4 and noise3(c, 2.4, seed) > -0.15:
            return "canopy_light"
        return "canopy"

    p.organic(els, colour, voxel=0.05 * s / detail ** 0.5, ratio=0.18 * detail, smooth=4)


def pine_shape(p, s, seed=9, detail=1.0):
    p.organic(trunk_parts(s, 1.0, 0.12, 0.07, seed, branches=0), lambda c, n: "trunk", voxel=0.03 * s / detail ** 0.5, ratio=0.35 * detail, smooth=3)
    r = rng(seed)
    els = []
    tiers = ((0.62, 1.0), (1.12, 0.82), (1.58, 0.64), (2.0, 0.47), (2.36, 0.3))
    for k, (z, rad) in enumerate(tiers):
        n = 10
        for i in range(n):
            a = math.tau * i / n + r.random() * 0.25 + k * 0.3
            d = rad * 0.6 * s
            els.append(ell((math.cos(a) * d, math.sin(a) * d, z * s), (rad * 0.42 * s, rad * 0.2 * s, rad * 0.16 * s),
                           rot=(0, 18, math.degrees(a))))
        els.append(cap((0, 0, (z - 0.12) * s), (0, 0, (z + 0.42) * s), rad * 0.62 * s, rad * 0.12 * s))

    def colour(c, n):
        if n.z < -0.25:
            return "pine_deep"
        return "pine_mid" if (n.z > 0.35 and noise3(c, 3.0, seed) > -0.2) else "pine_deep"

    p.organic(els, colour, voxel=0.045 * s / detail ** 0.5, ratio=0.16 * detail, smooth=3)
    p.cone("pine_mid", r=0.1 * s, h=0.35 * s, seg=10, loc=(0, 0, 2.85 * s))


def tree(col, kind):
    shape = pine_shape if kind == "tree_pine" else oak_shape
    for stage, scale in (("Young", 0.45), ("Grown", 0.75), ("Ready", 1.0)):
        p = Part(stage)
        shape(p, scale)
        if stage == "Ready":
            for k, (x, y) in enumerate(((0.62, -0.5), (0.85, -0.25))):
                p.rcyl("trunk", r=0.1, h=0.55, seg=14, loc=(x, y, 0.1), rot=(0, 90, 30 * k), radius=0.03)
        p.build(col)


# ---- scenery ---------------------------------------------------------------------------------------

def scenery_tree_round(col):
    p = Part("Base")
    oak_shape(p, 1.15, seed=12)
    p.build(col)


def scenery_tree_pine(col):
    p = Part("Base")
    pine_shape(p, 1.25, seed=14)
    p.build(col)


def scenery_bush(col):
    p = Part("Base")
    r = rng(17)
    els = []
    for _ in range(6):
        rad = r.uniform(0.24, 0.34)
        els.append(ell((r.uniform(-0.3, 0.3), r.uniform(-0.3, 0.3), r.uniform(0.2, 0.35)), (rad, rad, rad * 0.9)))
    p.organic(els, lambda c, n: "leaf_deep" if n.z < -0.2 else ("canopy_light" if n.z > 0.6 else "canopy"), voxel=0.04, ratio=0.3, smooth=4)
    for k in range(6):
        a = r.random() * math.tau
        p.ball(["flower_pink", "flower_white", "flower_yellow"][k % 3], r=0.045, loc=(math.cos(a) * 0.4, math.sin(a) * 0.4, 0.42 + r.random() * 0.15), sub=2)
    p.build(col)


def scenery_rock(col):
    p = Part("Base")
    els = [ell((0, 0, 0.12), (0.5, 0.38, 0.3), rot=(0, 0, 20)),
           ell((0.36, 0.22, 0.08), (0.3, 0.24, 0.2)),
           ell((-0.32, -0.16, 0.06), (0.22, 0.22, 0.17))]
    p.organic(els, lambda c, n: "pebble_dark" if n.z < 0.2 else "pebble", voxel=0.05, ratio=0.06, smooth=2)
    p.build(col)


# ---- crops -----------------------------------------------------------------------------------------

from assets_world import CROP_STAGES, GROUND, SPOTS  # noqa: E402


def soil_mound(p, x, y, seed):
    r = rng(seed)
    p.organic([ell((x, y, GROUND - 0.02), (0.24, 0.22, 0.09))], lambda c, n: "tilled_light", voxel=0.03, ratio=0.35, smooth=3)
    for _ in range(3):
        p.ball("tilled", r=0.035, loc=(x + r.uniform(-0.15, 0.15), y + r.uniform(-0.15, 0.15), GROUND + 0.02), sub=1)


def seedling(p, x, y, seed, h=0.16):
    r = rng(seed)
    p.cyl("leaf", r=0.012, h=h, seg=6, loc=(x, y, GROUND + h / 2))
    for k in range(2):
        p.leaf("leaf_bright", (x, y, GROUND + h), r.uniform(0, 360) + k * 180, 0.14, 0.05, rise=35, droop=40)


def wheat_plant(p, x, y, s, ripe, seed):
    r = rng(seed)
    stalk = "wheat_gold" if ripe else "leaf"
    ear = "wheat_gold" if ripe else "leaf_light"
    for k in range(9):
        a = r.uniform(0, math.tau)
        d = r.uniform(0.02, 0.2) * s
        bx, by = x + math.cos(a) * d, y + math.sin(a) * d
        h = r.uniform(0.75, 1.0) * s
        lean = math.radians(r.uniform(4, 12))
        tx, ty = bx + math.cos(a) * h * math.sin(lean), by + math.sin(a) * h * math.sin(lean)
        p.cyl(stalk, r=0.012 * s + 0.004, h=h, seg=5, loc=((bx + tx) / 2, (by + ty) / 2, GROUND + h / 2),
              rot=(-math.degrees(math.sin(a) * lean), math.degrees(math.cos(a) * lean), 0))
        # the ear: a column of plump kernels and a few awns
        top = Vector((tx, ty, GROUND + h))
        for j in range(3):
            z = top.z + 0.04 * s + j * 0.055 * s
            side = -1 if j % 2 else 1
            p.ball(ear, r=0.026 * s + 0.006, loc=(top.x + side * 0.012 * s, top.y, z), scale=(1, 0.85, 1.9), sub=1)
        if ripe and k % 3 == 0:
            p.cone(ear, r=0.006, h=0.14 * s, seg=4, loc=(top.x, top.y, top.z + 0.26 * s))
        if k % 2 == 0:
            p.leaf(stalk if ripe else "leaf_bright", (bx, by, GROUND + h * 0.35), math.degrees(a) + 40, 0.3 * s, 0.025, rise=50, droop=70)


def maize_plant(p, x, y, s, ripe, seed):
    r = rng(seed)
    h = 1.45 * s
    p.cyl("leaf", r=0.045 * s, h=h, seg=8, loc=(x, y, GROUND + h / 2))
    for k in range(6):
        z = GROUND + (0.18 + 0.15 * k) * s
        p.leaf("leaf_bright" if k % 2 else "leaf", (x, y, z), k * 137 + r.uniform(-15, 15), 0.62 * s * (1 - k * 0.08), 0.06 * s, rise=45, droop=85)
    for k in range(5):
        p.cone("husk" if not ripe else "wheat_gold", r=0.012, h=0.22 * s, seg=4, loc=(x, y, GROUND + h + 0.08 * s), rot=(25, 0, k * 72))
    if ripe:
        for side in (-1, 1):
            cx, cz = x + side * 0.1 * s, GROUND + 0.75 * s
            p.ball("husk", r=0.075 * s, loc=(cx, y - 0.03, cz - 0.05 * s), scale=(0.85, 0.85, 2.0), rot=(0, side * 22, 0), sub=2)
            p.ball("maize_gold", r=0.06 * s, loc=(cx + side * 0.03 * s, y - 0.06, cz + 0.07 * s), scale=(0.9, 0.9, 2.0), rot=(0, side * 22, 0), sub=2)


def foliage_clump(p, x, y, s, blobs, seed, base=0.3, spread=0.2, size=0.22, light="leaf_bright", dark="leaf_deep"):
    r = rng(seed)
    els = []
    for _ in range(blobs):
        a = r.uniform(0, math.tau)
        d = r.uniform(0, spread) * s
        rad = r.uniform(0.8, 1.15) * size * s
        els.append(ell((x + math.cos(a) * d, y + math.sin(a) * d, GROUND + (base + r.uniform(-0.05, 0.25)) * s), (rad, rad, rad * 0.8)))
    p.organic(els, lambda c, n: dark if n.z < -0.2 else (light if n.z > 0.5 else "leaf"), voxel=0.04 * s, ratio=0.16, smooth=4)


def tomato_plant(p, x, y, s, ripe, seed):
    p.cyl("orange_wood_dark", r=0.022, h=1.0 * s, seg=6, loc=(x + 0.1 * s, y + 0.12 * s, GROUND + 0.5 * s))
    foliage_clump(p, x, y, s, 6, seed, base=0.35, spread=0.18, size=0.17, light="leaf", dark="leaf_deep")
    r = rng(seed + 1)
    for k in range(6):
        a = r.uniform(0, math.tau)
        p.leaf("leaf", (x, y, GROUND + r.uniform(0.25, 0.65) * s), math.degrees(a), 0.22 * s, 0.07 * s, rise=20, droop=40)
    if ripe:
        for k, (dx, dy, dz) in enumerate(((0.2, -0.17, 0.32), (-0.2, -0.13, 0.48), (0.04, -0.24, 0.66), (-0.06, 0.22, 0.42), (0.22, 0.1, 0.55))):
            tx, ty, tz = x + dx * s, y + dy * s, GROUND + dz * s
            p.ball("tomato_red", r=0.095 * s, loc=(tx, ty, tz), scale=(1, 1, 0.88), sub=2)
            for j in range(4):
                p.leaf("leaf_bright", (tx, ty, tz + 0.08 * s), j * 90, 0.06 * s, 0.018, rise=10, droop=30, segs=2)


def onion_plant(p, x, y, s, ripe, seed):
    bulb = 0.17 * s if ripe else 0.1 * s
    colour = "onion_orange" if ripe else "onion_light"
    p.organic([ell((x, y, GROUND + bulb * 0.55), (bulb, bulb, bulb * 0.92)), cap((x, y, GROUND + bulb), (x, y, GROUND + bulb * 1.9), bulb * 0.32, 0.02)],
              lambda c, n: colour, voxel=0.02, ratio=0.2, smooth=4)
    r = rng(seed)
    for k in range(4):
        p.leaf("leaf_bright" if k % 2 else "leaf", (x, y, GROUND + bulb * 1.7), k * 90 + r.uniform(-20, 20), 0.5 * s, 0.03, rise=70, droop=110, segs=7)


def potato_plant(p, x, y, s, ripe, seed):
    foliage_clump(p, x, y, s, 7, seed, base=0.22, spread=0.22, size=0.17)
    r = rng(seed + 3)
    for k in range(7):
        p.leaf("leaf", (x, y, GROUND + 0.2 * s), k * 51 + r.uniform(-10, 10), 0.3 * s, 0.08 * s, rise=25, droop=50)
    if ripe:
        for dx, dy in ((0.3, -0.25), (-0.3, -0.2), (0.05, -0.38)):
            p.organic([ell((x + dx * s, y + dy * s, GROUND + 0.04), (0.12 * s, 0.09 * s, 0.075 * s), rot=(0, 0, dx * 90))], lambda c, n: "potato_brown",
                      voxel=0.03, ratio=0.2, smooth=3)
        for dx, dy in ((0.1, 0.05), (-0.12, -0.08), (0.0, 0.15)):
            for j in range(5):
                p.leaf("flower_white", (x + dx * s, y + dy * s, GROUND + 0.5 * s), j * 72, 0.04, 0.02, rise=10, droop=10, segs=2)
            p.ball("sun_yellow", r=0.015, loc=(x + dx * s, y + dy * s, GROUND + 0.51 * s))


def beans_plant(p, x, y, s, ripe, seed):
    p.cyl("orange_wood_dark", r=0.022, h=1.3 * s, seg=6, loc=(x, y, GROUND + 0.65 * s))
    r = rng(seed)
    for k in range(9):
        z = GROUND + (0.2 + 0.11 * k) * s
        p.leaf("leaf" if k % 2 else "leaf_bright", (x, y, z), k * 100 + r.uniform(-20, 20), 0.2 * s, 0.08 * s, rise=15, droop=50)
    if ripe:
        for k, z in enumerate((0.35, 0.6, 0.85, 1.05)):
            a = math.radians(90 * k + 45)
            p.organic([cap((x + math.cos(a) * 0.12 * s, y + math.sin(a) * 0.12 * s, GROUND + z * s),
                           (x + math.cos(a) * 0.16 * s, y + math.sin(a) * 0.16 * s, GROUND + (z - 0.3) * s), 0.03 * s, 0.018 * s)],
                      lambda c, n: "bean_green", voxel=0.015, ratio=0.2, smooth=3)


def sunflower_plant(p, x, y, s, ripe, seed):
    h = 1.35 * s
    p.cyl("leaf", r=0.04 * s, h=h, seg=8, loc=(x, y, GROUND + h / 2))
    for k in range(5):
        p.leaf("leaf_bright" if k % 2 else "leaf", (x, y, GROUND + (0.3 + 0.18 * k) * s), k * 144, 0.32 * s, 0.13 * s, rise=25, droop=55)
    cy, cz = y - 0.06 * s, GROUND + h + 0.02 * s
    if ripe:
        p.rcyl("sun_seed", r=0.18 * s, h=0.07 * s, seg=20, loc=(x, cy, cz), rot=(70, 0, 0), radius=0.03)
        for k in range(16):
            a = math.tau * k / 16
            ox, oz = math.cos(a), math.sin(a)
            centre = Vector((x + ox * 0.17 * s, cy - oz * 0.17 * s * math.sin(math.radians(20)) - 0.02, cz + oz * 0.17 * s * math.cos(math.radians(20))))
            p.ball("sun_yellow", r=0.1 * s, loc=centre, scale=(0.32, 0.12, 1.0), rot=(70, 0, -math.degrees(a) + 90), sub=2)
    else:
        p.ball("leaf_bright", r=0.11 * s, loc=(x, cy, cz), scale=(1, 1, 0.8), sub=2)


CROP_PLANTS = {
    "crop_wheat": wheat_plant, "crop_maize": maize_plant, "crop_tomato": tomato_plant, "crop_onion": onion_plant,
    "crop_potato": potato_plant, "crop_beans": beans_plant, "crop_sunflower": sunflower_plant,
}


def crop(col, crop_id):
    plant = CROP_PLANTS[crop_id]
    for stage in CROP_STAGES:
        p = Part(stage)
        for k, (x, y) in enumerate(SPOTS):
            seed = k * 31 + len(crop_id)
            if stage == "Seeded":
                soil_mound(p, x, y, seed)
            elif stage == "Sprout":
                seedling(p, x, y, seed)
            else:
                sc = {"Young": 0.55, "Grown": 0.88, "Ready": 1.0}[stage]
                plant(p, x, y, sc, stage == "Ready", seed)
        p.build(col)


ASSETS = {
    "livestock_cow": cow,
    "livestock_chicken": chicken,
    "villager_a": lambda c: villager(c, "shirt_blue", "skin", "straw", 1),
    "villager_b": lambda c: villager(c, "shirt_red", "skin_dark", "cap", 2),
    "villager_c": lambda c: villager(c, "shirt_green", "skin", "straw", 3),
    "tree_oak": lambda c: tree(c, "tree_oak"),
    "tree_pine": lambda c: tree(c, "tree_pine"),
    "scenery_tree_round": scenery_tree_round,
    "scenery_tree_pine": scenery_tree_pine,
    "scenery_bush": scenery_bush,
    "scenery_rock": scenery_rock,
}
for _crop_id in CROP_PLANTS:
    ASSETS[_crop_id] = (lambda i: lambda c: crop(c, i))(_crop_id)


# ---- decorations -----------------------------------------------------------------------------------

import fvdetail as fd  # noqa: E402
from assets_world import TILE  # noqa: E402


def _lawn(p, seed):
    fd.grass_field(p, 6, TILE * 0.8, seed, height=0.2, avoid=1.0)


def decor_bench(col):
    p = Part("Idle")
    for k in range(3):
        p.rbox("plank", size=(1.5, 0.13, 0.05), loc=(0, -0.12 + k * 0.14, 0.46), radius=0.012)
    for k in range(2):
        p.rbox("plank", size=(1.5, 0.05, 0.12), loc=(0, 0.26, 0.66 + k * 0.17), rot=(-12, 0, 0), radius=0.012)
    for x in (-0.62, 0.62):
        p.rbox("iron", size=(0.06, 0.5, 0.05), loc=(x, 0.05, 0.42), radius=0.01)
        p.rbox("iron", size=(0.06, 0.05, 0.44), loc=(x, -0.16, 0.22), radius=0.01)
        p.rbox("iron", size=(0.06, 0.05, 0.86), loc=(x, 0.27, 0.43), rot=(-12, 0, 0), radius=0.01)
        p.rbox("iron", size=(0.06, 0.36, 0.05), loc=(x, 0.0, 0.62), radius=0.01)
    _lawn(p, 301)
    p.build(col)


def decor_mailbox(col):
    p = Part("Idle")
    p.rbox("wood_dark", size=(0.1, 0.1, 1.0), loc=(0, 0, 0.5), radius=0.015)
    p.rbox("wood_dark", size=(0.3, 0.12, 0.05), loc=(0, 0, 0.98), radius=0.01)
    p.rbox("roof_blue", size=(0.28, 0.5, 0.18), loc=(0, 0, 1.1), radius=0.02)
    p.rcyl("roof_blue", r=0.14, h=0.5, seg=16, loc=(0, 0, 1.19), rot=(90, 0, 0), radius=0.01)
    p.rbox("red_flag", size=(0.03, 0.04, 0.24), loc=(0.16, 0.12, 1.22), radius=0.005)
    p.rbox("red_flag", size=(0.03, 0.12, 0.08), loc=(0.16, 0.06, 1.3), radius=0.005)
    fd.flowers(p, 4, 0.45, 302, avoid=0.15)
    _lawn(p, 303)
    p.build(col)


def decor_lamp_post(col):
    p = Part("Idle")
    p.rcyl("iron", r=0.16, h=0.18, seg=12, loc=(0, 0, 0.09), radius=0.03, r2=0.12)
    p.rcyl("iron", r=0.05, h=2.0, seg=10, loc=(0, 0, 1.1), radius=0.01, r2=0.04)
    p.rcyl("iron", r=0.08, h=0.06, seg=10, loc=(0, 0, 2.1), radius=0.01)
    fd.lantern(p, 0, 0, 2.13)
    p.rbox("iron", size=(0.04, 0.4, 0.04), loc=(0, 0.12, 1.75), radius=0.01)
    _lawn(p, 304)
    p.build(col)


def decor_beehive(col):
    p = Part("Idle")
    p.rbox("wood_dark", size=(0.8, 0.7, 0.18), loc=(0, 0, 0.09), radius=0.02)
    for i in range(3):
        p.rbox("cream" if i % 2 else "white", size=(0.62, 0.52, 0.22), loc=(0, 0, 0.3 + i * 0.23), radius=0.02)
        p.rbox("wood_dark", size=(0.64, 0.54, 0.02), loc=(0, 0, 0.19 + i * 0.23), radius=0.005)
    p.rbox("wood_dark", size=(0.2, 0.04, 0.04), loc=(0, -0.27, 0.22), radius=0.01)
    p.rcyl("roof_green", r=0.55, h=0.16, seg=4, loc=(0, 0, 1.0), rot=(0, 0, 45), radius=0.02, r2=0.12)
    for k in range(4):
        a = k * 1.7
        p.ball("sun_yellow", r=0.03, loc=(math.cos(a) * 0.5, math.sin(a) * 0.4, 0.6 + k * 0.12), scale=(1.3, 1, 1), sub=1)
    fd.flowers(p, 7, 1.1, 305, avoid=0.6)
    _lawn(p, 306)
    p.build(col)


def decor_fountain(col):
    p = Part("Idle")
    p.rcyl("stone", r=1.05, h=0.45, seg=24, loc=(0, 0, 0.22), radius=0.06)
    p.rcyl("pond", r=0.9, h=0.02, seg=24, loc=(0, 0, 0.4), radius=0.005)
    p.rcyl("stone", r=0.16, h=0.9, seg=12, loc=(0, 0, 0.8), radius=0.03, r2=0.12)
    p.rcyl("stone", r=0.45, h=0.12, seg=20, loc=(0, 0, 1.22), radius=0.04, r2=0.3)
    p.rcyl("pond", r=0.38, h=0.02, seg=20, loc=(0, 0, 1.28), radius=0.005)
    p.ball("stone", r=0.1, loc=(0, 0, 1.4), sub=2)
    for k in range(6):
        a = k * math.tau / 6
        p.ball("water", r=0.05, loc=(math.cos(a) * 0.5, math.sin(a) * 0.5, 0.48), scale=(1, 1, 1.6), sub=1)
    _lawn(p, 307)
    p.build(col)


def decor_hay_bales(col):
    p = Part("Idle")
    fd.hay_bale(p, -0.4, 0.0, 0.0, 1.0, rot=0)
    fd.hay_bale(p, 0.42, 0.1, 0.0, 1.0, rot=15)
    fd.hay_bale(p, 0.0, 0.05, 0.62, 0.9, rot=-10)
    p.organic([ell((0, 0, 0.0), (0.9, 0.7, 0.06))], lambda c, n: "straw", voxel=0.04, ratio=0.15, smooth=2)
    _lawn(p, 308)
    p.build(col)


def decor_flower_bed(col):
    p = Part("Idle")
    p.organic([ell((0, 0, 0.02), (1.05, 1.05, 0.14))], lambda c, n: "tilled", voxel=0.05, ratio=0.15, smooth=3)
    r = rng(309)
    for k in range(16):
        a = k * math.tau / 16
        p.rcyl("pebble", r=r.uniform(0.09, 0.13), h=0.12, seg=8, loc=(math.cos(a) * 1.12, math.sin(a) * 1.12, 0.05), radius=0.03)
    fd.flowers(p, 22, 0.95, 310, z=0.08)
    p.build(col)


def decor_default(col):
    p = Part("Idle")
    p.rbox("plank", size=(0.8, 0.8, 0.42), loc=(0, 0, 0.21), radius=0.03)
    for x in (-0.4, 0.4):
        for y in (-0.4, 0.4):
            p.rbox("wood_dark", size=(0.08, 0.08, 0.44), loc=(x, y, 0.22), radius=0.01)
    p.organic([ell((0, 0, 0.7), (0.42, 0.42, 0.36)), ell((0.15, 0.1, 0.92), (0.25, 0.25, 0.22))],
              lambda c, n: "leaf_deep" if n.z < 0 else "canopy", voxel=0.04, ratio=0.2, smooth=4)
    _lawn(p, 311)
    p.build(col)


def sign_default(col):
    p = Part("Idle")
    for x in (-0.38, 0.38):
        p.rbox("wood_dark", size=(0.08, 0.08, 1.2), loc=(x, 0, 0.6), radius=0.015)
    p.rbox("sign", size=(0.95, 0.07, 0.48), loc=(0, -0.05, 1.0), radius=0.02)
    p.rbox("wood_dark", size=(1.0, 0.08, 0.05), loc=(0, -0.05, 1.25), radius=0.01)
    _lawn(p, 312)
    p.build(col)


def seasonal_default(col):
    p = Part("Idle")
    p.rcyl("chimney_orange", r=0.42, h=0.4, seg=16, loc=(0, 0, 0.2), radius=0.04, r2=0.34)
    pine_shape(p, 0.42, seed=313)
    for k in range(7):
        a = k * 2.4
        z = 0.45 + k * 0.1
        p.ball(["red_flag", "gold", "flower_blue"][k % 3], r=0.04, loc=(math.cos(a) * 0.3 * (1.3 - k * 0.12), math.sin(a) * 0.3 * (1.3 - k * 0.12), z), sub=1)
    p.build(col)


def scenery_flowers(col):
    p = Part("Base")
    fd.grass_field(p, 8, 0.9, 314, height=0.3)
    fd.flowers(p, 12, 0.85, 315)
    p.build(col)


def scenery_pond(col):
    p = Part("Base")
    r = rng(316)
    for k in range(18):
        a = k * math.tau / 18
        fd.stone(p, math.cos(a) * 1.5, math.sin(a) * 1.3, -0.04, r.uniform(0.14, 0.22), 400 + k)
    p.organic([ell((0, 0, -0.02), (1.45, 1.25, 0.08))], lambda c, n: "pond", voxel=0.06, ratio=0.12, smooth=2)
    for k in range(4):
        a = 1.2 + k * 0.7
        p.rcyl("leaf_bright", r=0.16, h=0.02, seg=12, loc=(math.cos(a) * 0.7, math.sin(a) * 0.6, 0.08), radius=0.005)
    p.ball("flower_pink", r=0.05, loc=(math.cos(1.9) * 0.7, math.sin(1.9) * 0.6, 0.12), sub=1)
    fd.grass_field(p, 8, 1.7, 317, height=0.4, avoid=1.45)
    p.build(col)


ASSETS.update({
    "decor_bench": decor_bench, "decor_mailbox": decor_mailbox, "decor_lamp_post": decor_lamp_post,
    "decor_beehive": decor_beehive, "decor_fountain": decor_fountain, "decor_hay_bales": decor_hay_bales,
    "decor_flower_bed": decor_flower_bed, "decor_default": decor_default, "sign_default": sign_default,
    "seasonal_default": seasonal_default, "scenery_flowers": scenery_flowers, "scenery_pond": scenery_pond,
})
