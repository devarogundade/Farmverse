"""Animals and production buildings. Animals and buildings face -Y (their front)."""
import math

from fvlib import Part, empty, rng


# ---- animals ------------------------------------------------------------------------------

def hen_body(p, s=1.0, ox=0.0, oy=0.0):
    p.ball("hen", r=0.17 * s, loc=(ox, oy, 0.22 * s), sub=2, scale=(0.9, 1.2, 0.95))
    p.ball("hen", r=0.1 * s, loc=(ox, oy - 0.16 * s, 0.38 * s), sub=2)
    p.box("comb", size=(0.03 * s, 0.09 * s, 0.07 * s), loc=(ox, oy - 0.16 * s, 0.48 * s))
    p.cone("beak", r=0.03 * s, h=0.07 * s, seg=4, loc=(ox, oy - 0.27 * s, 0.37 * s), rot=(90, 0, 0))
    p.box("comb", size=(0.03 * s, 0.03 * s, 0.05 * s), loc=(ox, oy - 0.22 * s, 0.31 * s))
    p.box("hen", size=(0.12 * s, 0.1 * s, 0.14 * s), loc=(ox, oy + 0.18 * s, 0.33 * s), rot=(-30, 0, 0))
    for x in (-0.08, 0.08):
        p.box("hen", size=(0.03 * s, 0.18 * s, 0.11 * s), loc=(ox + x * 1.6 * s, oy, 0.24 * s))
        p.cyl("beak", r=0.012 * s, h=0.1 * s, seg=4, loc=(ox + x * s, oy, 0.05 * s))
    for x in (-0.045, 0.045):
        p.ball("black", r=0.015 * s, loc=(ox + x * s, oy - 0.24 * s, 0.41 * s))


def chicken(col):
    young = Part("Young")
    hen_body(young, 0.6)
    young.build(col)
    grown = Part("Grown")
    hen_body(grown)
    grown.build(col)
    product = Part("Product")
    product.cyl("hay", r=0.16, h=0.06, seg=8, loc=(0.32, 0.1, 0.03))
    for k in range(3):
        a = math.tau * k / 3
        product.ball("egg", r=0.045, loc=(0.32 + math.cos(a) * 0.05, 0.1 + math.sin(a) * 0.05, 0.08), scale=(1, 1, 1.3))
    product.build(col)


def cow_body(p, s=1.0):
    p.box("cow", size=(0.62 * s, 1.15 * s, 0.6 * s), loc=(0, 0, 0.82 * s))
    p.box("cow_patch", size=(0.63 * s, 0.4 * s, 0.35 * s), loc=(0, 0.2 * s, 0.9 * s))
    p.box("cow_patch", size=(0.3 * s, 0.25 * s, 0.61 * s), loc=(0.17 * s, -0.25 * s, 0.82 * s))
    for x in (-0.22, 0.22):
        for y in (-0.42, 0.42):
            p.box("cow", size=(0.14 * s, 0.14 * s, 0.55 * s), loc=(x * s, y * s, 0.3 * s))
            p.box("dark", size=(0.15 * s, 0.15 * s, 0.08 * s), loc=(x * s, y * s, 0.04 * s))
    # head and face
    p.box("cow", size=(0.4 * s, 0.42 * s, 0.4 * s), loc=(0, -0.72 * s, 1.0 * s))
    p.box("cow_nose", size=(0.36 * s, 0.14 * s, 0.22 * s), loc=(0, -0.95 * s, 0.92 * s))
    for x in (-0.09, 0.09):
        p.ball("black", r=0.03 * s, loc=(x * s, -0.94 * s, 1.08 * s))
        p.box("dark", size=(0.04 * s, 0.02 * s, 0.04 * s), loc=(x * 0.8 * s, -1.02 * s, 0.94 * s))
    for x in (-1, 1):
        p.box("cow", size=(0.18 * s, 0.06 * s, 0.09 * s), loc=(x * 0.27 * s, -0.66 * s, 1.13 * s))
        p.cone("horn", r=0.04 * s, h=0.14 * s, seg=5, loc=(x * 0.14 * s, -0.68 * s, 1.27 * s), rot=(0, x * 25, 0))
    p.box("cow_nose", size=(0.22 * s, 0.25 * s, 0.1 * s), loc=(0, 0.2 * s, 0.5 * s))
    p.box("cow", size=(0.04 * s, 0.04 * s, 0.5 * s), loc=(0, 0.6 * s, 0.75 * s), rot=(-15, 0, 0))
    p.ball("cow_patch", r=0.05 * s, loc=(0, 0.66 * s, 0.5 * s))


def cow(col):
    young = Part("Young")
    cow_body(young, 0.6)
    young.build(col)
    grown = Part("Grown")
    cow_body(grown)
    grown.build(col)
    product = Part("Product")
    product.cyl("metal", r=0.12, h=0.3, seg=10, loc=(0.6, -0.2, 0.15))
    product.cyl("milk", r=0.1, h=0.02, seg=10, loc=(0.6, -0.2, 0.3))
    product.cyl("metal_dark", r=0.13, h=0.03, seg=10, loc=(0.6, -0.2, 0.3))
    product.build(col)


# ---- buildings ----------------------------------------------------------------------------

def gable_roof(p, colour, w, d, h, z, loc=(0, 0), overhang=0.15):
    """A pitched roof: two slabs along the building's depth (Y)."""
    half = w / 2 + overhang
    pitch = math.degrees(math.atan2(h, half))
    slab = math.hypot(half, h)
    for side in (-1, 1):
        p.box(colour, size=(slab, d + overhang * 2, 0.1), loc=(loc[0] + side * half / 2, loc[1], z + h / 2 + 0.04), rot=(0, side * pitch, 0))
    # gable ends: wall-coloured triangles closing the roof
    tri = [(-w / 2, 0.0), (w / 2, 0.0), (0.0, h * (w / 2) / half)]
    p.prism("wood_light", tri, d - 0.02, loc=(loc[0], loc[1], z))
    # ridge cap
    p.box(colour, size=(0.14, d + overhang * 2 + 0.02, 0.12), loc=(loc[0], loc[1], z + h + 0.06))


def house(p, wall, roof, w, d, h, loc=(0, 0), roof_h=0.8):
    p.box("stone", size=(w + 0.12, d + 0.12, 0.18), loc=(loc[0], loc[1], 0.09))
    p.box(wall, size=(w, d, h), loc=(loc[0], loc[1], 0.18 + h / 2))
    # door and windows on the front (-Y)
    p.box("wood_dark", size=(0.6, 0.05, 1.0), loc=(loc[0], loc[1] - d / 2 - 0.01, 0.68))
    for x in (-w * 0.3, w * 0.3):
        if w > 1.6:
            p.box("glass", size=(0.42, 0.05, 0.36), loc=(loc[0] + x, loc[1] - d / 2 - 0.01, 0.18 + h * 0.62))
            p.box("wood_dark", size=(0.5, 0.06, 0.06), loc=(loc[0] + x, loc[1] - d / 2 - 0.02, 0.18 + h * 0.62 - 0.21))
    gable_roof(p, roof, w, d, roof_h, 0.18 + h, loc)


def broken(col, w, d):
    p = Part("Broken")
    p.box("plank", size=(1.0, 0.12, 0.05), loc=(w * 0.3, -d / 2 - 0.4, 0.05), rot=(0, 0, 25))
    p.box("plank", size=(0.8, 0.12, 0.05), loc=(-w * 0.2, -d / 2 - 0.5, 0.07), rot=(0, 0, -15))
    p.box("dark", size=(0.5, 0.06, 0.4), loc=(w * 0.15, -d / 2 - 0.02, 1.4), rot=(0, 20, 0))
    p.build(col)


def mill(col):
    """Windmill (flour): stone tower, wooden cap, four sails on a hub that turns."""
    base = Part("Base")
    base.cyl("stone", r=1.25, h=0.3, seg=10, loc=(0, 0, 0.15))
    base.cyl("cream", r=1.0, h=3.4, seg=10, loc=(0, 0, 1.9), r2=0.72)
    base.cyl("wood", r=0.85, h=0.6, seg=10, loc=(0, 0, 3.9))
    base.cone("roof_red", r=0.95, h=0.9, seg=10, loc=(0, 0, 4.65))
    base.box("wood_dark", size=(0.62, 0.05, 1.0), loc=(0, -0.97, 0.8))
    base.box("glass", size=(0.34, 0.05, 0.34), loc=(0, -0.83, 2.4))
    base.cyl("wood_dark", r=0.12, h=0.5, seg=8, loc=(0, -0.95, 3.95), rot=(90, 0, 0))
    for x in (-1.6, 1.6):
        base.box("feed", size=(0.4, 0.4, 0.5), loc=(x * 0.7, -1.3, 0.25))
    base.build(col)
    sails = Part("Spin_Sails")
    hub = (0, -1.25, 3.95)
    sails.cyl("wood_dark", r=0.18, h=0.2, seg=8, loc=hub, rot=(90, 0, 0))
    for k in range(4):
        a = math.radians(45 + 90 * k)
        cx, cz = math.cos(a) * 1.15, math.sin(a) * 1.15
        sails.box("wood", size=(0.1, 0.06, 2.3), loc=(hub[0] + cx * 0.5, hub[1], hub[2] + cz * 0.5), rot=(0, -math.degrees(a) + 90, 0))
        sails.box("canvas", size=(0.5, 0.03, 1.7), loc=(hub[0] + cx * 0.62 + math.cos(a + 1.57) * 0.2, hub[1] - 0.02, hub[2] + cz * 0.62 + math.sin(a + 1.57) * 0.2), rot=(0, -math.degrees(a) + 90, 0))
    sails.build(col, parent=None, origin=hub)
    broken(col, 2.0, 2.0)


def feed_mill(col):
    """Feed mill: barn with a grain hopper and a turning water wheel."""
    base = Part("Base")
    house(base, "wood_light", "roof_brown", 2.4, 2.2, 1.7)
    base.box("wood", size=(0.7, 0.7, 0.9), loc=(0.6, 0.4, 3.0))
    base.cone("wood", r=0.5, h=0.5, seg=4, loc=(0.6, 0.4, 2.4), rot=(180, 0, 45))
    base.box("water", size=(0.5, 1.8, 0.1), loc=(1.62, 0, 0.12))
    base.box("feed", size=(0.5, 0.4, 0.4), loc=(-1.0, -1.45, 0.2))
    base.box("feed", size=(0.4, 0.4, 0.35), loc=(-0.5, -1.5, 0.18))
    base.build(col)
    wheel = Part("Spin_Wheel")
    c = (1.45, 0, 0.8)
    wheel.cyl("wood_dark", r=0.75, h=0.12, seg=12, loc=c, rot=(0, 90, 0))
    wheel.cyl("wood", r=0.6, h=0.14, seg=12, loc=c, rot=(0, 90, 0))
    for k in range(8):
        a = math.tau * k / 8
        wheel.box("plank", size=(0.3, 0.06, 0.32), loc=(c[0], c[1] + math.cos(a) * 0.75, c[2] + math.sin(a) * 0.75), rot=(math.degrees(a), 0, 0))
    wheel.build(col, origin=c)
    broken(col, 2.4, 2.2)


def compost_bin(col):
    """Compost bins: three slatted wooden boxes heaped with compost."""
    base = Part("Base")
    base.box("soil_dark", size=(3.0, 1.6, 0.06), loc=(0, 0, 0.03))
    for i, x in enumerate((-1.0, 0.0, 1.0)):
        for y in (-0.7, 0.7):
            base.box("wood", size=(0.95, 0.08, 0.8), loc=(x, y, 0.42))
        for xx in (x - 0.47, x + 0.47):
            base.box("wood_dark", size=(0.08, 1.45, 0.85), loc=(xx, 0, 0.45))
        base.ball("compost", r=0.45, loc=(x, 0, 0.55 + 0.05 * i), scale=(1, 1.4, 0.6))
    base.box("wood_light", size=(0.12, 0.12, 1.2), loc=(1.75, -0.4, 0.6), rot=(0, 20, 0))
    base.box("metal", size=(0.3, 0.05, 0.25), loc=(1.95, -0.4, 0.1))
    base.build(col)
    broken(col, 3.0, 1.6)


def oven(col):
    """Bakery: a cottage with a brick oven and a smoking chimney."""
    base = Part("Base")
    house(base, "cream", "roof_red", 2.6, 2.2, 1.7)
    base.cyl("brick", r=0.7, h=1.0, seg=10, loc=(1.7, -0.3, 0.5), r2=0.5)
    base.ball("brick", r=0.55, loc=(1.7, -0.3, 1.0), scale=(1, 1, 0.6))
    base.box("dark", size=(0.4, 0.1, 0.32), loc=(1.7, -0.95, 0.55))
    base.box("bread", size=(0.5, 0.3, 0.12), loc=(-1.1, -1.45, 0.5))
    base.box("wood", size=(0.8, 0.5, 0.45), loc=(-1.1, -1.45, 0.22))
    base.build(col)
    chim = Part("Chimney_Stack")
    chim.box("brick", size=(0.34, 0.34, 1.0), loc=(0.75, 0.45, 2.4))
    chim.box("stone_dark", size=(0.4, 0.4, 0.08), loc=(0.75, 0.45, 2.92))
    chim.build(col)
    empty("Chimney", col, loc=(0.75, 0.45, 3.05))
    broken(col, 2.6, 2.2)


def kettle(col):
    """Jam kitchen: open shed with a copper kettle over a fire."""
    base = Part("Base")
    base.box("stone", size=(2.8, 2.2, 0.15), loc=(0, 0, 0.075))
    for x in (-1.25, 1.25):
        for y in (-0.95, 0.95):
            base.box("wood_dark", size=(0.14, 0.14, 2.0), loc=(x, y, 1.1))
    gable_roof(base, "roof_green", 2.6, 2.0, 0.7, 2.1)
    base.cyl("stone_dark", r=0.6, h=0.35, seg=10, loc=(0, -0.1, 0.32))
    base.cyl("copper", r=0.55, h=0.6, seg=12, loc=(0, -0.1, 0.8), r2=0.65)
    base.cyl("jam", r=0.58, h=0.04, seg=12, loc=(0, -0.1, 1.1))
    base.box("wood", size=(1.0, 0.5, 0.8), loc=(0.8, 0.6, 0.4))
    for k in range(4):
        base.cyl("glass", r=0.07, h=0.16, seg=8, loc=(0.5 + k * 0.2, 0.6, 0.88))
        base.cyl("jam", r=0.06, h=0.1, seg=8, loc=(0.5 + k * 0.2, 0.6, 0.86))
    base.build(col)
    empty("Chimney", col, loc=(0, -0.1, 1.2))
    broken(col, 2.6, 2.0)


def dairy(col):
    """Dairy: a white barn with milk churns and a cheese rack."""
    base = Part("Base")
    house(base, "white", "roof_blue", 2.8, 2.4, 1.9)
    for k in range(3):
        base.cyl("metal", r=0.16, h=0.5, seg=10, loc=(-1.0 + k * 0.38, -1.55, 0.25))
        base.cyl("metal_dark", r=0.1, h=0.08, seg=10, loc=(-1.0 + k * 0.38, -1.55, 0.53))
    base.box("wood", size=(0.9, 0.4, 0.06), loc=(1.0, -1.5, 0.6))
    for k in range(3):
        base.cyl("cheese", r=0.13, h=0.1, seg=10, loc=(0.75 + k * 0.25, -1.5, 0.68))
    base.build(col)
    chim = Part("Chimney_Stack")
    chim.box("stone", size=(0.3, 0.3, 0.9), loc=(-0.8, 0.5, 2.55))
    chim.build(col)
    empty("Chimney", col, loc=(-0.8, 0.5, 3.05))
    broken(col, 2.8, 2.4)


def oil_press(col):
    """Oil press: a shed with a big screw press and a turning wheel."""
    base = Part("Base")
    house(base, "wood_light", "roof_red", 2.2, 2.0, 1.6, loc=(0.5, 0.4), roof_h=0.7)
    base.box("stone", size=(1.4, 1.4, 0.12), loc=(-1.1, -0.6, 0.06))
    base.cyl("wood", r=0.45, h=0.5, seg=10, loc=(-1.1, -0.6, 0.37))
    for x in (-1.5, -0.7):
        base.box("wood_dark", size=(0.12, 0.12, 1.5), loc=(x, -0.6, 0.85))
    base.box("wood_dark", size=(1.0, 0.16, 0.16), loc=(-1.1, -0.6, 1.6))
    base.cyl("metal_dark", r=0.06, h=1.0, seg=8, loc=(-1.1, -0.6, 1.05))
    for k in range(3):
        base.cyl("oil", r=0.1, h=0.26, seg=8, loc=(0.3 + k * 0.25, -1.0, 0.13))
    base.build(col)
    wheel = Part("Spin_Wheel")
    c = (-1.1, -0.6, 1.75)
    for k in range(4):
        a = math.tau * k / 4
        wheel.box("wood", size=(0.9, 0.08, 0.08), loc=(c[0], c[1], c[2]), rot=(0, 0, math.degrees(a)))
    wheel.cyl("wood_dark", r=0.12, h=0.12, seg=8, loc=c)
    wheel.build(col, origin=c)
    broken(col, 2.2, 2.0)


def sawmill(col):
    """Sawmill: an open shed, a log on the bench and a turning round saw."""
    base = Part("Base")
    base.box("plank", size=(3.0, 2.2, 0.12), loc=(0, 0, 0.06))
    for x in (-1.35, 1.35):
        for y in (-0.95, 0.95):
            base.box("wood_dark", size=(0.14, 0.14, 2.0), loc=(x, y, 1.1))
    gable_roof(base, "roof_brown", 2.8, 2.0, 0.6, 2.1)
    base.box("wood", size=(2.2, 0.6, 0.7), loc=(0, -0.2, 0.45))
    base.cyl("bark", r=0.22, h=2.0, seg=8, loc=(-0.2, -0.2, 0.98), rot=(0, 90, 0))
    for k in range(3):
        base.cyl("bark", r=0.2, h=1.6, seg=8, loc=(0.4, 0.75 + k * 0.0, 0.25 + k * 0.38), rot=(0, 90, 0))
    for k in range(4):
        base.box("plank", size=(1.4, 0.3, 0.05), loc=(-0.6, 0.7, 0.15 + k * 0.06))
    base.build(col)
    saw = Part("Spin_Saw")
    c = (0.75, -0.2, 0.95)
    saw.cyl("metal", r=0.38, h=0.03, seg=16, loc=c, rot=(90, 0, 0))
    saw.cyl("metal_dark", r=0.08, h=0.06, seg=8, loc=c, rot=(90, 0, 0))
    saw.build(col, origin=c)
    broken(col, 3.0, 2.2)


MACHINES = {
    "machine_mill": mill,
    "machine_feed_mill": feed_mill,
    "machine_compost_bin": compost_bin,
    "machine_oven": oven,
    "machine_kettle": kettle,
    "machine_dairy": dairy,
    "machine_oil_press": oil_press,
    "machine_sawmill": sawmill,
}
