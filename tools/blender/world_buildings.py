"""Buildings and props of the world beyond the farm, modelled from scratch: town houses, shops,
a town hall, a school, a cafe, a clinic, a community hall, market stalls, a park, the seaport
(pier, boats, warehouse, crane, lighthouse), the railway (station, train), a helipad with a
helicopter, a windmill, silos, a water tower, a stone bridge and street furniture.
Front faces -Y; the origin is the centre of the footprint at ground level."""
import math

import fvdetail as fd
from assets_real import oak_shape, pine_shape
from fvlib import Part, cap, ell, empty, rng


# ---- building kit ----------------------------------------------------------------------------------

def gable_roof(p, w, d, z, rise, colour, overhang=0.3, along="x", trim="trim", gable="wall_cream"):
    """Two roof slabs meeting at a ridge along X (or Y), gable ends filled, fascia trim."""
    if along == "x":
        half = d / 2 + overhang
        pitch = math.degrees(math.atan2(rise, d / 2))
        slab = math.hypot(half, rise * half / (d / 2))
        for s in (-1, 1):
            p.rbox(colour, size=(w + overhang * 2, slab, 0.16), loc=(0, s * half / 2, z + rise / 2 + 0.05), rot=(s * -pitch, 0, 0), radius=0.03)
        p.prism(gable, [(-d / 2, 0.0), (d / 2, 0.0), (0.0, rise)], w - 0.02, loc=(0, 0, z), rot=(0, 0, 90))
        p.rcyl(colour, r=0.1, h=w + overhang * 2, seg=10, loc=(0, 0, z + rise + 0.08), rot=(0, 90, 0), radius=0.02)
    else:
        half = w / 2 + overhang
        pitch = math.degrees(math.atan2(rise, w / 2))
        slab = math.hypot(half, rise * half / (w / 2))
        for s in (-1, 1):
            p.rbox(colour, size=(slab, d + overhang * 2, 0.16), loc=(s * half / 2, 0, z + rise / 2 + 0.05), rot=(0, s * pitch, 0), radius=0.03)
        p.prism(gable, [(-w / 2, 0.0), (w / 2, 0.0), (0.0, rise)], d - 0.02, loc=(0, 0, z))
        p.rcyl(colour, r=0.1, h=d + overhang * 2, seg=10, loc=(0, 0, z + rise + 0.08), rot=(90, 0, 0), radius=0.02)


def window(p, x, y, z, w=0.7, h=0.9, face="-y", shutter=None, glass="glass_day", frame="trim"):
    """A framed window with a cross bar and optional shutters, on a wall facing `face`."""
    if face in ("-y", "+y"):
        sy = -1 if face == "-y" else 1
        p.rbox(frame, size=(w + 0.14, 0.1, h + 0.14), loc=(x, y + sy * 0.02, z), radius=0.02)
        p.rbox(glass, size=(w, 0.11, h), loc=(x, y + sy * 0.03, z), radius=0.01)
        p.rbox(frame, size=(0.05, 0.13, h), loc=(x, y + sy * 0.04, z), radius=0.005)
        p.rbox(frame, size=(w, 0.13, 0.05), loc=(x, y + sy * 0.04, z), radius=0.005)
        p.rbox(frame, size=(w + 0.24, 0.16, 0.06), loc=(x, y + sy * 0.06, z - h / 2 - 0.06), radius=0.01)
        if shutter:
            for s in (-1, 1):
                p.rbox(shutter, size=(w * 0.42, 0.06, h + 0.06), loc=(x + s * (w / 2 + w * 0.26), y + sy * 0.03, z), radius=0.01)
    else:
        sx = -1 if face == "-x" else 1
        p.rbox(frame, size=(0.1, w + 0.14, h + 0.14), loc=(x + sx * 0.02, y, z), radius=0.02)
        p.rbox(glass, size=(0.11, w, h), loc=(x + sx * 0.03, y, z), radius=0.01)
        p.rbox(frame, size=(0.13, 0.05, h), loc=(x + sx * 0.04, y, z), radius=0.005)
        p.rbox(frame, size=(0.13, w, 0.05), loc=(x + sx * 0.04, y, z), radius=0.005)
        if shutter:
            for s in (-1, 1):
                p.rbox(shutter, size=(0.06, w * 0.42, h + 0.06), loc=(x + sx * 0.03, y + s * (w / 2 + w * 0.26), z), radius=0.01)


def door(p, x, y, z=0.0, w=0.9, h=1.9, colour="door_brown", frame="trim"):
    p.rbox(frame, size=(w + 0.2, 0.12, h + 0.12), loc=(x, y - 0.02, z + h / 2 + 0.03), radius=0.02)
    p.rbox(colour, size=(w, 0.14, h), loc=(x, y - 0.04, z + h / 2), radius=0.02)
    for k in range(3):
        p.rbox(colour, size=(w * 0.7, 0.16, 0.04), loc=(x, y - 0.05, z + h * (0.25 + 0.27 * k)), radius=0.005)
    p.ball("gold", r=0.05, loc=(x + w * 0.32, y - 0.13, z + h * 0.5), sub=1)
    p.rbox("stone", size=(w + 0.5, 0.6, 0.12), loc=(x, y - 0.3, z + 0.06), radius=0.02)


def box_body(p, w, d, h, wall, z=0.0, plinth="stone_dark"):
    p.rbox(plinth, size=(w + 0.16, d + 0.16, 0.4), loc=(0, 0, z + 0.2), radius=0.03)
    p.rbox(wall, size=(w, d, h), loc=(0, 0, z + 0.4 + h / 2), radius=0.04)
    for x in (-w / 2, w / 2):
        for y in (-d / 2, d / 2):
            p.rbox("trim", size=(0.16, 0.16, h), loc=(x, y, z + 0.4 + h / 2), radius=0.02)


def house(col, wall="wall_cream", roof="roof_red", shutter="hull_blue", w=6.0, d=5.0, storeys=1, seed=1, porch=True):
    p = Part("Base")
    floor_h = 2.6
    h = floor_h * storeys
    box_body(p, w, d, h, wall)
    for s in range(storeys):
        zc = 0.4 + floor_h * s + 1.45
        xs = [-w / 2 + 1.0, w / 2 - 1.0] if s == 0 else [-w / 2 + 1.0, 0.0, w / 2 - 1.0]
        for x in xs:
            window(p, x, -d / 2, zc, shutter=shutter)
        window(p, -w / 2, 0.0, zc, face="-x", shutter=shutter)
        window(p, w / 2, 0.0, zc, face="+x", shutter=shutter)
        if s:
            p.rbox("trim", size=(w + 0.1, d + 0.1, 0.14), loc=(0, 0, 0.4 + floor_h * s), radius=0.02)
    door(p, 0.0 if storeys > 1 else 0.0, -d / 2, 0.4)
    gable_roof(p, w, d, 0.4 + h, d * 0.42, roof, gable=wall)
    p.rbox("brick", size=(0.6, 0.6, 1.8), loc=(w * 0.28, d * 0.12, 0.4 + h + d * 0.3), radius=0.04)
    p.rbox("stone_dark", size=(0.72, 0.72, 0.12), loc=(w * 0.28, d * 0.12, 0.4 + h + d * 0.3 + 0.95), radius=0.02)
    if porch:
        p.rbox(roof, size=(2.0, 1.1, 0.1), loc=(0, -d / 2 - 0.5, 0.4 + 2.35), rot=(-14, 0, 0), radius=0.02)
        for x in (-0.85, 0.85):
            p.rcyl("trim", r=0.06, h=2.3, seg=10, loc=(x, -d / 2 - 0.95, 0.4 + 1.15), radius=0.01)
    # a flower box under each front window, hedges and a picket fence around the yard
    r = rng(seed)
    for x in (-w / 2 + 1.0, w / 2 - 1.0):
        p.rbox("wood_dark", size=(0.9, 0.25, 0.2), loc=(x, -d / 2 - 0.15, 0.4 + 0.85), radius=0.02)
        for k in range(4):
            p.ball(r.choice(["flower_pink", "flower_yellow", "flower_white", "flower_blue"]), r=0.08, loc=(x - 0.3 + k * 0.2, -d / 2 - 0.15, 0.4 + 1.0), sub=1)
    for x in (-w / 2 - 0.4, w / 2 + 0.4):
        p.organic([ell((x, -d / 2 + 0.6, 0.45), (0.45, 1.0, 0.5))], lambda c, n: "leaf_deep" if n.z < 0.2 else "canopy", voxel=0.12, ratio=0.15, smooth=3)
    picket_fence(p, [(-w / 2 - 1.2, -d / 2 - 2.6), (-1.2, -d / 2 - 2.6)], "white")
    picket_fence(p, [(1.2, -d / 2 - 2.6), (w / 2 + 1.2, -d / 2 - 2.6)], "white")
    p.rbox("pebble", size=(1.6, 2.4, 0.06), loc=(0, -d / 2 - 1.5, 0.03), radius=0.02)
    p.build(col)


def picket_fence(p, pts, colour="white", spacing=0.35):
    for i in range(len(pts) - 1):
        (ax, ay), (bx, by) = pts[i], pts[i + 1]
        ln = math.hypot(bx - ax, by - ay)
        ang = math.degrees(math.atan2(by - ay, bx - ax))
        n = max(1, int(ln / spacing))
        for k in range(n + 1):
            t = k / n
            p.rbox(colour, size=(0.08, 0.05, 0.85), loc=(ax + (bx - ax) * t, ay + (by - ay) * t, 0.42), rot=(0, 0, ang), radius=0.01)
        for z in (0.3, 0.65):
            p.rbox(colour, size=(ln, 0.04, 0.07), loc=((ax + bx) / 2, (ay + by) / 2 - 0.04, z), rot=(0, 0, ang), radius=0.01)


def awning(p, w, x, y, z, a="awning_red", b="white", depth=1.2, stripes=8):
    for k in range(stripes):
        sw = w / stripes
        p.rbox(a if k % 2 == 0 else b, size=(sw + 0.01, depth, 0.08), loc=(x - w / 2 + sw * (k + 0.5), y - depth / 2, z), rot=(-20, 0, 0), radius=0.01)
    for k in range(stripes):
        sw = w / stripes
        p.cone(a if k % 2 == 0 else b, r=sw * 0.5, h=0.25, seg=3, loc=(x - w / 2 + sw * (k + 0.5), y - depth - 0.02, z - 0.32), rot=(180, 0, 0))


def shop(col, kind="bakery"):
    styles = {"bakery": ("wall_yellow", "roof_red", "awning_red", "bread"), "grocer": ("wall_mint", "roof_slate", "awning_green", "tomato_red"),
              "fish": ("wall_blue", "roof_slate", "awning_blue", "hull_blue"), "tailor": ("wall_pink", "roof_red", "awning_red", "shirt_blue")}
    wall, roof, aw, goods = styles[kind]
    p = Part("Base")
    w, d = 6.5, 5.5
    box_body(p, w, d, 5.2, wall)
    # shop front: big display windows, door, awning, sign
    p.rbox("wood_dark", size=(w - 0.6, 0.14, 2.4), loc=(0, -d / 2 - 0.02, 1.6), radius=0.02)
    for x in (-1.9, 1.9):
        p.rbox("glass_day", size=(2.0, 0.16, 1.7), loc=(x, -d / 2 - 0.03, 1.75), radius=0.01)
        for k in range(3):
            p.ball(goods, r=0.16, loc=(x - 0.5 + k * 0.5, -d / 2 - 0.05, 1.1), scale=(1.2, 0.8, 0.8), sub=2)
    door(p, 0.0, -d / 2, 0.4, w=1.0, h=2.1, colour="wood_dark")
    awning(p, w - 0.4, 0, -d / 2, 3.15, aw, "white")
    p.rbox("wood_dark", size=(3.2, 0.16, 0.6), loc=(0, -d / 2 - 0.08, 3.8), radius=0.03)
    p.rbox("sign", size=(3.0, 0.18, 0.46), loc=(0, -d / 2 - 0.1, 3.8), radius=0.02)
    p.ball(goods, r=0.18, loc=(0, -d / 2 - 0.22, 3.8), scale=(1.4, 0.5, 0.9), sub=2)
    for x in (-2.0, 0.0, 2.0):
        window(p, x, -d / 2, 4.6, w=0.8, h=0.9, shutter="trim")
    gable_roof(p, w, d, 5.6, 2.0, roof, gable=wall)
    # a bench and a planter out front
    fd.potted_plant(p, -3.0, -d / 2 - 0.9, 0.0, 7, "flower_pink")
    fd.potted_plant(p, 3.0, -d / 2 - 0.9, 0.0, 8, "flower_yellow")
    p.build(col)


def town_hall(col):
    p = Part("Base")
    w, d, h = 14.0, 8.0, 6.0
    box_body(p, w, d, h, "brick")
    p.rbox("trim", size=(w + 0.3, d + 0.3, 0.4), loc=(0, 0, 0.4 + h), radius=0.04)
    for x in (-5.0, -2.6, 2.6, 5.0):
        for z in (2.0, 4.6):
            window(p, x, -d / 2, z, w=1.0, h=1.4, frame="trim")
    # portico: steps, columns, pediment
    for k in range(3):
        p.rbox("stone", size=(6.0 - k * 0.4, 2.6 - k * 0.5, 0.25), loc=(0, -d / 2 - 1.3 + k * 0.25, 0.12 + k * 0.25), radius=0.02)
    for x in (-2.2, -0.75, 0.75, 2.2):
        p.rcyl("white", r=0.28, h=5.2, seg=16, loc=(x, -d / 2 - 1.6, 3.2), radius=0.04)
    p.rbox("white", size=(5.8, 2.4, 0.4), loc=(0, -d / 2 - 1.0, 5.9), radius=0.03)
    p.prism("white", [(-3.0, 0.0), (3.0, 0.0), (0.0, 1.3)], 2.4, loc=(0, -d / 2 - 1.0, 6.1))
    door(p, 0.0, -d / 2, 0.4, w=1.6, h=2.8, colour="door_brown")
    gable_roof(p, w, d, 0.8 + h, 2.6, "roof_slate", gable="brick")
    # the clock tower
    p.rbox("brick", size=(2.6, 2.6, 5.0), loc=(0, 0.5, 0.8 + h + 2.0), radius=0.04)
    p.rbox("trim", size=(2.9, 2.9, 0.3), loc=(0, 0.5, 0.8 + h + 4.6), radius=0.03)
    p.rcyl("white", r=0.9, h=0.16, seg=24, loc=(0, 0.5 - 1.34, 0.8 + h + 3.3), rot=(90, 0, 0), radius=0.03)
    p.rcyl("gold", r=0.98, h=0.12, seg=24, loc=(0, 0.5 - 1.32, 0.8 + h + 3.3), rot=(90, 0, 0), radius=0.03)
    p.rbox("black", size=(0.08, 0.06, 0.65), loc=(0, 0.5 - 1.46, 0.8 + h + 3.55), radius=0.01)
    p.rbox("black", size=(0.5, 0.06, 0.08), loc=(0.22, 0.5 - 1.46, 0.8 + h + 3.3), radius=0.01)
    p.rcyl("roof_slate", r=2.0, h=2.6, seg=4, loc=(0, 0.5, 0.8 + h + 6.0), rot=(0, 0, 45), radius=0.03, r2=0.05)
    p.cyl("iron", r=0.04, h=1.4, seg=6, loc=(0, 0.5, 0.8 + h + 7.9))
    p.rbox("awning_blue", size=(0.8, 0.04, 0.5), loc=(0.42, 0.5, 0.8 + h + 8.3), radius=0.01)
    for x in (-5.5, 5.5):
        p.organic([ell((x, -d / 2 - 1.2, 0.6), (1.4, 0.6, 0.6))], lambda c, n: "leaf_deep" if n.z < 0.2 else "canopy", voxel=0.15, ratio=0.15, smooth=3)
    p.build(col)


def school(col):
    p = Part("Base")
    w, d, h = 12.0, 6.0, 3.4
    box_body(p, w, d, h, "wall_yellow")
    for x in (-4.5, -2.5, 2.5, 4.5):
        window(p, x, -d / 2, 2.2, w=1.2, h=1.3, shutter=None)
    door(p, 0.0, -d / 2, 0.4, w=1.4, h=2.2, colour="hull_blue")
    gable_roof(p, w, d, 0.4 + h, 2.2, "roof_red", gable="wall_yellow")
    # bell tower
    p.rbox("white", size=(1.2, 1.2, 1.4), loc=(0, 0, 0.4 + h + 2.6), radius=0.03)
    p.ball("gold", r=0.3, loc=(0, 0, 0.4 + h + 2.6), sub=2)
    p.rcyl("roof_red", r=1.0, h=1.0, seg=4, loc=(0, 0, 0.4 + h + 3.8), rot=(0, 0, 45), radius=0.02, r2=0.02)
    # playground: swings and a slide
    sx, sy = -3.5, -d / 2 - 5.0
    for x in (sx - 1.2, sx + 1.2):
        for y in (sy - 0.6, sy + 0.6):
            p.cyl("hull_red", r=0.06, h=2.4, seg=8, loc=(x, y * 0 + sy + (y - sy) * 0.4, 1.15), rot=((y - sy) * 18, 0, 0))
    p.cyl("hull_red", r=0.07, h=2.6, seg=8, loc=(sx, sy, 2.3), rot=(0, 90, 0))
    for x in (sx - 0.5, sx + 0.5):
        p.cyl("rope", r=0.015, h=1.6, seg=4, loc=(x - 0.15, sy, 1.5))
        p.cyl("rope", r=0.015, h=1.6, seg=4, loc=(x + 0.15, sy, 1.5))
        p.rbox("wood", size=(0.45, 0.2, 0.05), loc=(x, sy, 0.7), radius=0.01)
    tx = 3.5
    p.rbox("hull_blue", size=(0.8, 0.8, 1.6), loc=(tx, sy + 0.8, 0.8), radius=0.03)
    p.rbox("awning_red", size=(0.7, 2.6, 0.08), loc=(tx, sy - 0.4, 0.85), rot=(-34, 0, 0), radius=0.02)
    p.rbox("sand", size=(9.0, 4.0, 0.06), loc=(0, sy, 0.03), radius=0.03)
    p.build(col)


def cafe(col):
    p = Part("Base")
    w, d = 6.0, 5.0
    box_body(p, w, d, 3.2, "wall_pink")
    p.rbox("glass_day", size=(4.2, 0.16, 1.8), loc=(0.6, -d / 2 - 0.03, 1.7), radius=0.01)
    door(p, -2.0, -d / 2, 0.4, w=0.9, h=2.1, colour="hull_white")
    awning(p, w - 0.4, 0, -d / 2, 3.1, "awning_green", "white", stripes=10)
    gable_roof(p, w, d, 3.6, 1.8, "roof_slate", gable="wall_pink")
    # terrace: tables with parasols and chairs
    for k, x in enumerate((-2.0, 0.6, 3.2)):
        y = -d / 2 - 3.0
        p.rcyl("white", r=0.45, h=0.06, seg=14, loc=(x, y, 0.75), radius=0.01)
        p.cyl("iron", r=0.04, h=0.75, seg=6, loc=(x, y, 0.37))
        p.cyl("iron", r=0.03, h=2.3, seg=6, loc=(x, y, 1.15))
        p.cone("awning_red" if k % 2 == 0 else "awning_green", r=1.3, h=0.6, seg=8, loc=(x, y, 2.4))
        for a in (0, 180):
            cx = x + math.cos(math.radians(a)) * 0.7
            p.rbox("wood", size=(0.4, 0.4, 0.05), loc=(cx, y, 0.45), radius=0.01)
            p.rbox("wood", size=(0.05, 0.4, 0.45), loc=(cx + math.cos(math.radians(a)) * 0.2, y, 0.68), radius=0.01)
    p.rbox("cobble" if False else "stone", size=(9.0, 4.4, 0.06), loc=(0.6, -d / 2 - 2.6, 0.03), radius=0.02)
    p.build(col)


def clinic(col):
    p = Part("Base")
    w, d, h = 9.0, 6.0, 5.6
    box_body(p, w, d, h, "white")
    for x in (-3.0, 3.0):
        for z in (1.9, 4.4):
            window(p, x, -d / 2, z, w=1.2, h=1.1)
    p.rbox("glass_day", size=(2.4, 0.16, 2.4), loc=(0, -d / 2 - 0.03, 1.6), radius=0.02)
    p.rbox("awning_blue", size=(3.4, 1.6, 0.14), loc=(0, -d / 2 - 0.8, 3.0), radius=0.02)
    for x in (-1.5, 1.5):
        p.cyl("white", r=0.07, h=2.6, seg=8, loc=(x, -d / 2 - 1.5, 1.5))
    # red cross sign
    p.rbox("white", size=(1.4, 0.2, 1.4), loc=(0, -d / 2 - 0.1, 5.0), radius=0.06)
    p.rbox("awning_red", size=(0.9, 0.24, 0.28), loc=(0, -d / 2 - 0.12, 5.0), radius=0.02)
    p.rbox("awning_red", size=(0.28, 0.24, 0.9), loc=(0, -d / 2 - 0.12, 5.0), radius=0.02)
    p.rbox("concrete", size=(w + 0.4, d + 0.4, 0.3), loc=(0, 0, 0.4 + h + 0.15), radius=0.03)
    p.build(col)


def community_hall(col):
    """The team centre: a round-roofed hall with flags and a notice board."""
    p = Part("Base")
    w, d, h = 10.0, 7.0, 4.2
    box_body(p, w, d, h, "wall_blue")
    for x in (-3.4, -1.2, 1.2, 3.4):
        window(p, x, -d / 2, 2.6, w=1.2, h=1.6, frame="white")
    door(p, 0.0, -d / 2, 0.4, w=1.6, h=2.4, colour="hull_blue", frame="white")
    gable_roof(p, w, d, 0.4 + h, 2.2, "roof_slate", gable="wall_blue")
    for k, x in enumerate((-4.0, 4.0)):
        p.cyl("iron", r=0.05, h=6.0, seg=6, loc=(x, -d / 2 - 1.5, 3.0))
        p.rbox(["awning_red", "awning_green"][k], size=(1.2, 0.04, 0.8), loc=(x + 0.62, -d / 2 - 1.5, 5.5), radius=0.01)
    p.rbox("wood_dark", size=(1.6, 0.12, 1.2), loc=(2.8, -d / 2 - 1.0, 1.4), radius=0.02)
    for x in (2.4, 3.2):
        p.cyl("wood_dark", r=0.05, h=1.6, seg=6, loc=(x, -d / 2 - 1.0, 0.8))
    p.rbox("sign", size=(1.4, 0.13, 1.0), loc=(2.8, -d / 2 - 1.07, 1.4), radius=0.02)
    p.build(col)


def market_stall(col, colour="awning_red", goods=("tomato_red", "maize_gold", "onion_orange")):
    p = Part("Base")
    p.rbox("wood", size=(2.6, 1.4, 0.9), loc=(0, 0, 0.45), radius=0.03)
    for x in (-1.2, 1.2):
        for y in (-0.6, 0.6):
            p.cyl("wood_dark", r=0.05, h=2.4, seg=6, loc=(x, y, 1.2))
    awning(p, 2.8, 0, 0.75, 2.5, colour, "white", depth=1.8, stripes=7)
    r = rng(len(colour))
    for k, g in enumerate(goods):
        x = -0.85 + k * 0.85
        p.rbox("plank", size=(0.75, 0.6, 0.25), loc=(x, -0.2, 1.0), radius=0.02)
        for j in range(5):
            p.ball(g, r=0.1, loc=(x + r.uniform(-0.25, 0.25), -0.2 + r.uniform(-0.18, 0.18), 1.18), sub=1)
    fd.sack(p, 1.6, -0.6, 0.0, "canvas", 0.9)
    p.build(col)


def park(col):
    p = Part("Base")
    p.organic([ell((0, 0, -0.05), (9.0, 7.0, 0.12))], lambda c, n: "grass", voxel=0.3, ratio=0.1, smooth=2)
    p.rcyl("stone", r=2.0, h=0.5, seg=24, loc=(0, 0, 0.25), radius=0.06)
    p.rcyl("pond", r=1.75, h=0.02, seg=24, loc=(0, 0, 0.46), radius=0.005)
    p.rcyl("stone", r=0.25, h=1.4, seg=12, loc=(0, 0, 0.9), radius=0.04)
    p.ball("water", r=0.25, loc=(0, 0, 1.75), scale=(1, 1, 1.6), sub=2)
    for k in range(4):
        a = k * math.tau / 4 + 0.4
        x, y = math.cos(a) * 3.6, math.sin(a) * 3.0
        p.rbox("plank", size=(1.6, 0.5, 0.06), loc=(x, y, 0.45), rot=(0, 0, math.degrees(a) + 90), radius=0.01)
        p.rbox("iron", size=(1.5, 0.06, 0.45), loc=(x, y, 0.22), rot=(0, 0, math.degrees(a) + 90), radius=0.01)
    for k, (x, y) in enumerate(((6.0, 3.0), (-6.0, 3.5), (5.5, -3.5), (-6.5, -2.5))):
        oak_shape_at(p, x, y, 1.0, 120 + k, detail=0.5)
    fd.flowers(p, 20, 8.0, 130, avoid=2.6)
    p.build(col)


def oak_shape_at(p, x, y, s, seed, detail=1.0):
    import bmesh
    from mathutils import Matrix
    before = set(p.bm.verts)
    oak_shape(p, s, seed=seed, detail=detail)
    fresh = [v for v in p.bm.verts if v not in before]
    bmesh.ops.transform(p.bm, matrix=Matrix.Translation((x, y, 0)), verts=fresh)


def pine_shape_at(p, x, y, s, seed, detail=1.0):
    import bmesh
    from mathutils import Matrix
    before = set(p.bm.verts)
    pine_shape(p, s, seed=seed, detail=detail)
    fresh = [v for v in p.bm.verts if v not in before]
    bmesh.ops.transform(p.bm, matrix=Matrix.Translation((x, y, 0)), verts=fresh)


# ---- seaport ---------------------------------------------------------------------------------------

def pier(col, length=30.0, width=4.0):
    """A plank pier on piles running out along -Y, with bollards, a lamp and crates."""
    p = Part("Base")
    for k in range(int(length / 0.5)):
        y = -k * 0.5
        p.rbox("plank", size=(width, 0.46, 0.12), loc=(0, y, 0.6), radius=0.01)
    for k in range(0, int(length / 3) + 1):
        y = -k * 3.0
        for x in (-width / 2 + 0.2, width / 2 - 0.2):
            p.rcyl("wood_dark", r=0.18, h=4.0, seg=10, loc=(x, y, -1.4), radius=0.02)
            p.rcyl("wood_dark", r=0.12, h=0.6, seg=8, loc=(x, y, 0.9), radius=0.02)
    for x in (-width / 2 + 0.2, width / 2 - 0.2):
        p.rbox("wood_dark", size=(0.1, length, 0.1), loc=(x, -length / 2, 1.1), radius=0.01)
    for k in range(3):
        p.rcyl("iron", r=0.18, h=0.4, seg=12, loc=(-width / 2 + 0.5, -6 - k * 9, 0.85), radius=0.04)
    for k in range(2):
        p.cyl("iron", r=0.06, h=3.2, seg=8, loc=(width / 2 - 0.4, -4 - k * 14, 2.2))
        p.rbox("lamp_glass", size=(0.3, 0.3, 0.4), loc=(width / 2 - 0.4, -4 - k * 14, 3.9), radius=0.04)
    p.rbox("orange_wood", size=(0.8, 0.8, 0.8), loc=(0.6, -length + 2.0, 1.06), radius=0.03)
    p.rbox("orange_wood", size=(0.7, 0.7, 0.7), loc=(0.5, -length + 2.2, 1.8), rot=(0, 0, 20), radius=0.03)
    for k in range(3):
        p.rcyl("hull_red" if k % 2 else "hull_blue", r=0.35, h=0.9, seg=14, loc=(-0.8 + k * 0.1, -length + 3.5 + k * 0.8, 1.11), radius=0.04)
    p.build(col)


def boat(col, hull="hull_blue", cabin="hull_white", sail=False, length=7.0):
    p = Part("Base")
    els = [ell((0, 0, 0.3), (length * 0.17, length * 0.5, 0.75)), ell((0, -length * 0.33, 0.45), (length * 0.1, length * 0.22, 0.6))]

    def colour(c, n):
        if c.z > 0.85:
            return "plank"
        return hull if c.z > -0.1 else "hull_white"
    p.organic(els, colour, voxel=0.12, ratio=0.12, smooth=3)
    p.rbox("plank", size=(length * 0.3, length * 0.82, 0.08), loc=(0, 0.1, 0.92), radius=0.02)
    if sail:
        p.cyl("wood_dark", r=0.08, h=7.0, seg=8, loc=(0, -0.5, 4.3))
        p.prism("canvas", [(0.0, 0.0), (0.0, 5.6), (2.6, 0.4)], 0.05, loc=(0.1, -0.5, 1.4), rot=(0, 0, 90))
        p.prism("canvas", [(0.0, 0.0), (0.0, 4.5), (-2.0, 0.2)], 0.05, loc=(0.0, -1.0, 1.4), rot=(0, 0, 90))
    else:
        p.rbox(cabin, size=(length * 0.26, length * 0.28, 1.4), loc=(0, length * 0.12, 1.65), radius=0.08)
        p.rbox("glass_day", size=(length * 0.27, 0.1, 0.5), loc=(0, length * 0.12 - length * 0.14, 1.9), radius=0.02)
        p.rbox(hull, size=(length * 0.3, length * 0.32, 0.12), loc=(0, length * 0.12, 2.4), radius=0.03)
        p.cyl("iron", r=0.05, h=2.4, seg=6, loc=(0, length * 0.22, 3.4))
        p.cyl("iron", r=0.03, h=2.6, seg=6, loc=(0, length * 0.22, 3.2), rot=(0, 70, 0))
        for k in range(3):
            p.rcyl("awning_red" if k % 2 else "hull_white", r=0.2, h=0.25, seg=10, loc=(length * 0.1 - 0.3 * k, -length * 0.15, 1.1), radius=0.03)
    p.build(col)


def port_warehouse(col):
    p = Part("Base")
    w, d, h = 16.0, 10.0, 6.0
    p.rbox("concrete", size=(w + 0.4, d + 0.4, 0.4), loc=(0, 0, 0.2), radius=0.02)
    p.rbox("hull_red", size=(w, d, h), loc=(0, 0, 0.4 + h / 2), radius=0.04)
    for k in range(9):
        x = -w / 2 + (k + 0.5) * w / 9
        p.rbox("hull_red", size=(0.12, d + 0.06, h), loc=(x, 0, 0.4 + h / 2), radius=0.01)
    for x in (-4.0, 4.0):
        p.rbox("metal_roof", size=(4.0, 0.16, 4.4), loc=(x, -d / 2 - 0.05, 2.6), radius=0.02)
        for k in range(8):
            p.rbox("iron", size=(4.0, 0.2, 0.06), loc=(x, -d / 2 - 0.06, 0.6 + k * 0.55), radius=0.005)
    gable_roof(p, w, d, 0.4 + h, 2.4, "metal_roof", gable="hull_red")
    p.rbox("sign", size=(5.0, 0.16, 1.0), loc=(0, -d / 2 - 0.1, 5.6), radius=0.03)
    for k in range(4):
        p.rbox("orange_wood", size=(1.0, 1.0, 1.0), loc=(-7.0 + (k % 2) * 1.05, -d / 2 - 1.6, 0.5 + (k // 2) * 1.0), radius=0.04)
    for k in range(5):
        p.rcyl("hull_blue", r=0.38, h=1.0, seg=14, loc=(5.0 + (k % 3) * 0.8, -d / 2 - 1.5 - (k // 3) * 0.8, 0.5), radius=0.04)
    p.build(col)


def crane(col):
    p = Part("Base")
    for x in (-2.0, 2.0):
        for y in (-2.0, 2.0):
            p.rbox("awning_red", size=(0.4, 0.4, 12.0), loc=(x, y, 6.0), radius=0.03)
    for z in (4.0, 8.0, 12.0):
        p.rbox("awning_red", size=(4.4, 4.4, 0.3), loc=(0, 0, z), radius=0.03)
    p.rbox("wall_yellow", size=(2.4, 2.4, 2.0), loc=(0, 0, 13.2), radius=0.06)
    p.rbox("glass_day", size=(2.42, 2.0, 0.8), loc=(0, 0, 13.4), radius=0.04)
    p.rbox("awning_red", size=(0.6, 18.0, 0.6), loc=(0, -6.0, 14.6), radius=0.04)
    p.rbox("concrete", size=(1.6, 2.4, 1.2), loc=(0, 3.6, 14.4), radius=0.04)
    p.cyl("iron", r=0.03, h=8.0, seg=4, loc=(0, -13.0, 10.6))
    p.rbox("iron", size=(0.8, 0.4, 0.3), loc=(0, -13.0, 6.5), radius=0.02)
    p.build(col)


def lighthouse(col):
    p = Part("Base")
    p.organic([ell((0, 0, 0.0), (6.0, 6.0, 1.6))], lambda c, n: "rock" if n.z < 0.7 else "grass_dark", voxel=0.3, ratio=0.06, smooth=1)
    for k in range(6):
        z0 = 1.0 + k * 2.4
        r0 = 2.2 - k * 0.18
        p.rcyl("awning_red" if k % 2 else "white", r=r0, h=2.4, seg=24, loc=(0, 0, z0 + 1.2), radius=0.02, r2=r0 - 0.18)
    top = 1.0 + 6 * 2.4
    p.rcyl("iron", r=1.6, h=0.3, seg=24, loc=(0, 0, top + 0.15), radius=0.04)
    for k in range(16):
        a = k * math.tau / 16
        p.cyl("iron", r=0.03, h=0.8, seg=4, loc=(math.cos(a) * 1.5, math.sin(a) * 1.5, top + 0.7))
    p.rcyl("lamp_glass", r=0.9, h=1.4, seg=12, loc=(0, 0, top + 1.0), radius=0.02)
    p.cone("awning_red", r=1.2, h=1.2, seg=16, loc=(0, 0, top + 2.3))
    p.ball("gold", r=0.2, loc=(0, 0, top + 3.0), sub=2)
    p.rbox("door_brown", size=(0.9, 0.2, 1.8), loc=(0, -2.1, 1.9), radius=0.03)
    p.build(col)


# ---- railway, helipad, countryside landmarks ------------------------------------------------------

def station(col):
    p = Part("Base")
    w, d, h = 12.0, 5.0, 4.0
    box_body(p, w, d, h, "wall_cream")
    for x in (-4.0, -2.0, 2.0, 4.0):
        window(p, x, -d / 2, 2.4, w=1.0, h=1.6, shutter="hull_red")
    door(p, 0.0, -d / 2, 0.4, w=1.4, h=2.4, colour="hull_red")
    gable_roof(p, w, d, 0.4 + h, 1.8, "roof_red", gable="wall_cream")
    p.rcyl("white", r=0.7, h=0.12, seg=20, loc=(0, -d / 2 - 0.1, 5.7), rot=(90, 0, 0), radius=0.02)
    p.rbox("black", size=(0.05, 0.06, 0.45), loc=(0, -d / 2 - 0.2, 5.85), radius=0.005)
    # the platform on the track side (+Y) with its canopy
    p.rbox("concrete", size=(w + 10.0, 4.0, 0.9), loc=(0, d / 2 + 2.0, 0.45), radius=0.03)
    p.rbox("tarmac_line", size=(w + 10.0, 0.25, 0.02), loc=(0, d / 2 + 3.7, 0.91), radius=0.005)
    for k in range(6):
        x = -w / 2 - 4 + k * ((w + 8) / 5)
        p.cyl("iron", r=0.08, h=3.0, seg=8, loc=(x, d / 2 + 1.5, 2.4))
    p.rbox("roof_red", size=(w + 9.0, 3.4, 0.16), loc=(0, d / 2 + 1.7, 3.95), rot=(-8, 0, 0), radius=0.03)
    p.rbox("sign", size=(3.0, 0.14, 0.6), loc=(0, d / 2 + 1.5, 3.3), radius=0.02)
    for k in range(2):
        p.rbox("plank", size=(1.6, 0.5, 0.06), loc=(-7 + k * 14, d / 2 + 2.6, 1.35), radius=0.01)
    p.build(col)


def train(col, cars=3):
    """A small steam locomotive with a tender and goods wagons, along +X."""
    p = Part("Base")
    x = 0.0
    # locomotive
    p.rbox("hull_red", size=(6.0, 2.2, 0.5), loc=(x, 0, 1.0), radius=0.05)
    p.rcyl("awning_green", r=1.0, h=4.0, seg=20, loc=(x + 0.8, 0, 2.2), rot=(0, 90, 0), radius=0.05)
    for k in range(3):
        p.rcyl("gold", r=1.03, h=0.12, seg=20, loc=(x - 0.6 + k * 1.3, 0, 2.2), rot=(0, 90, 0), radius=0.02)
    p.rcyl("black", r=0.32, h=1.2, seg=14, loc=(x + 2.3, 0, 3.6), radius=0.04, r2=0.45)
    p.ball("gold", r=0.4, loc=(x + 0.9, 0, 3.2), sub=2)
    p.rbox("hull_red", size=(1.9, 2.3, 2.6), loc=(x - 2.1, 0, 2.5), radius=0.06)
    p.rbox("glass_day", size=(1.92, 1.6, 0.8), loc=(x - 2.1, 0, 3.0), radius=0.04)
    p.rbox("black", size=(2.3, 2.6, 0.2), loc=(x - 2.1, 0, 3.9), radius=0.04)
    p.cone("iron", r=0.9, h=0.8, seg=4, loc=(x + 3.2, 0, 0.9), rot=(0, 90, 45))
    empty("Chimney", col, loc=(x + 2.3, 0, 4.3))
    for k in range(4):
        for s in (-1, 1):
            p.rcyl("black", r=0.55, h=0.2, seg=16, loc=(x + 2.0 - k * 1.3, s * 0.95, 0.6), rot=(90, 0, 0), radius=0.03)
            p.rcyl("awning_red", r=0.3, h=0.22, seg=12, loc=(x + 2.0 - k * 1.3, s * 0.97, 0.6), rot=(90, 0, 0), radius=0.02)
    x -= 5.6
    for c in range(cars):
        colour = ["awning_blue", "awning_red", "awning_green"][c % 3]
        p.rbox(colour, size=(4.6, 2.2, 1.6), loc=(x, 0, 1.6), radius=0.06)
        p.rbox("black", size=(4.8, 2.3, 0.3), loc=(x, 0, 0.75), radius=0.03)
        if c == 0:
            for k in range(6):
                p.rcyl("trunk", r=0.25, h=4.2, seg=10, loc=(x, -0.7 + (k % 3) * 0.7, 2.6 + (k // 3) * 0.45), rot=(0, 90, 0), radius=0.03)
        else:
            for k in range(3):
                fd.sack(p, x - 1.4 + k * 1.4, 0.0, 2.4, "canvas", 1.2)
        for k in (-1.5, 1.5):
            for s in (-1, 1):
                p.rcyl("black", r=0.45, h=0.18, seg=14, loc=(x + k, s * 0.95, 0.5), rot=(90, 0, 0), radius=0.03)
        x -= 5.2
    p.build(col)


def helipad(col):
    p = Part("Base")
    p.rcyl("concrete", r=7.0, h=0.4, seg=32, loc=(0, 0, 0.2), radius=0.06)
    p.rcyl("wall_yellow", r=6.2, h=0.02, seg=32, loc=(0, 0, 0.41), radius=0.0)
    p.rcyl("concrete", r=5.9, h=0.03, seg=32, loc=(0, 0, 0.415), radius=0.0)
    for x in (-1.6, 1.6):
        p.rbox("tarmac_line", size=(0.6, 4.6, 0.04), loc=(x, 0, 0.44), radius=0.0)
    p.rbox("tarmac_line", size=(3.2, 0.6, 0.04), loc=(0, 0, 0.44), radius=0.0)
    for k in range(8):
        a = k * math.tau / 8
        p.rcyl("lamp_glass", r=0.15, h=0.2, seg=8, loc=(math.cos(a) * 6.6, math.sin(a) * 6.6, 0.5), radius=0.03)
    # the helicopter
    els = [ell((0, 0, 2.0), (1.4, 2.2, 1.3)), cap((0, 1.6, 2.2), (0, 6.0, 2.8), 0.5, 0.18)]
    p.organic(els, lambda c, n: "glass_day" if (c.y < -1.0 and c.z > 2.0) else ("awning_red" if c.z > 1.2 else "white"), voxel=0.1, ratio=0.15, smooth=3)
    for s in (-1, 1):
        p.rcyl("iron", r=0.08, h=4.0, seg=8, loc=(s * 1.2, 0, 0.55), rot=(90, 0, 0), radius=0.02)
        p.cyl("iron", r=0.05, h=0.7, seg=6, loc=(s * 1.0, -0.8, 0.9), rot=(0, s * -20, 0))
        p.cyl("iron", r=0.05, h=0.7, seg=6, loc=(s * 1.0, 0.8, 0.9), rot=(0, s * -20, 0))
    p.rbox("awning_red", size=(0.1, 1.2, 1.0), loc=(0, 6.0, 3.3), radius=0.03)
    p.cyl("iron", r=0.12, h=0.6, seg=8, loc=(0, 0, 3.5))
    for k in range(4):
        p.rbox("iron", size=(9.0, 0.35, 0.06), loc=(0, 0, 3.85), rot=(0, 0, 45 * k + 20), radius=0.02)
    p.build(col)


def windmill(col):
    p = Part("Base")
    p.rcyl("stone", r=3.2, h=9.0, seg=16, loc=(0, 0, 4.5), radius=0.05, r2=2.3)
    p.rcyl("wood_dark", r=3.6, h=0.4, seg=16, loc=(0, 0, 3.2), radius=0.04)
    for k in range(10):
        a = k * math.tau / 10
        p.cyl("wood_dark", r=0.06, h=0.9, seg=4, loc=(math.cos(a) * 3.5, math.sin(a) * 3.5, 3.85))
    p.ball("roof_red", r=2.6, loc=(0, 0, 9.0), scale=(1, 1, 0.75), sub=3)
    door(p, 0.0, -3.0, 0.0, w=1.1, h=2.2)
    window(p, 0.0, -2.7, 6.0, w=0.7, h=0.8)
    p.build(col)
    s = Part("SpinSails")
    s.rcyl("wood_dark", r=0.4, h=0.8, seg=12, loc=(0, -2.9, 8.6), rot=(90, 0, 0), radius=0.04)
    for k in range(4):
        a = k * 90 + 15
        ca, sa = math.cos(math.radians(a)), math.sin(math.radians(a))
        s.rbox("wood_dark", size=(0.18, 0.12, 6.4), loc=(ca * 3.3, -3.3, 8.6 + sa * 3.3), rot=(0, -a + 90, 0), radius=0.02)
        s.rbox("canvas", size=(1.3, 0.06, 4.8), loc=(ca * 3.8 - sa * 0.7, -3.35, 8.6 + sa * 3.8 + ca * 0.7), rot=(0, -a + 90, 0), radius=0.01)
    s.build(col)


def silos(col):
    p = Part("Base")
    for k, x in enumerate((-2.2, 2.2)):
        p.rcyl("metal_roof", r=1.9, h=9.0, seg=24, loc=(x, 0, 4.5), radius=0.05)
        for z in range(1, 9, 2):
            p.rcyl("iron", r=1.93, h=0.12, seg=24, loc=(x, 0, z), radius=0.01)
        p.ball("metal_roof", r=1.9, loc=(x, 0, 9.0), scale=(1, 1, 0.55), sub=3)
    p.rbox("hull_red", size=(3.0, 3.0, 4.0), loc=(0, 2.4, 2.0), radius=0.05)
    p.cyl("iron", r=0.2, h=6.0, seg=10, loc=(0, 1.2, 8.0), rot=(30, 0, 0))
    p.build(col)


def water_tower(col):
    p = Part("Base")
    for x in (-1.6, 1.6):
        for y in (-1.6, 1.6):
            p.cyl("wood_dark", r=0.14, h=8.0, seg=8, loc=(x, y, 4.0))
    for z in (2.5, 5.5):
        for k in range(4):
            p.rbox("wood_dark", size=(3.4, 0.1, 0.1), loc=(0, 0, z), rot=(0, 0, k * 90), radius=0.01)
    p.rcyl("wood", r=2.4, h=3.6, seg=20, loc=(0, 0, 9.8), radius=0.06)
    for z in (8.5, 9.8, 11.1):
        p.rcyl("iron", r=2.43, h=0.12, seg=20, loc=(0, 0, z), radius=0.01)
    p.cone("roof_red", r=2.8, h=1.8, seg=20, loc=(0, 0, 12.5))
    p.build(col)


def bridge(col, length=18.0, width=7.0):
    """A stone arch bridge along X, deck at z=0."""
    p = Part("Base")
    p.rbox("stone", size=(length, width, 0.6), loc=(0, 0, -0.3), radius=0.05)
    for s in (-1, 1):
        p.rbox("stone", size=(length, 0.5, 1.0), loc=(0, s * (width / 2 - 0.25), 0.5), radius=0.06)
        for k in range(7):
            x = -length / 2 + (k + 0.5) * length / 7
            p.rbox("stone_dark", size=(0.6, 0.6, 1.3), loc=(x, s * (width / 2 - 0.25), 0.65), radius=0.05)
    for x in (-length / 2 + 2.0, length / 2 - 2.0):
        p.rbox("stone", size=(3.0, width - 0.4, 4.0), loc=(x, 0, -2.6), radius=0.08)
    p.organic([cap((-length / 2 + 2.2, 0, -1.2), (length / 2 - 2.2, 0, -1.2), 1.4)], lambda c, n: "stone", voxel=0.2, ratio=0.1, smooth=2)
    p.build(col)


def street_lamp(col):
    p = Part("Base")
    p.rcyl("iron", r=0.2, h=0.3, seg=12, loc=(0, 0, 0.15), radius=0.03, r2=0.14)
    p.rcyl("iron", r=0.07, h=4.0, seg=10, loc=(0, 0, 2.2), radius=0.01, r2=0.05)
    p.cyl("iron", r=0.04, h=1.0, seg=6, loc=(0.4, 0, 4.1), rot=(0, 70, 0))
    fd.lantern(p, 0.85, 0, 3.6)
    p.build(col)


def greenhouse(col):
    p = Part("Base")
    w, d = 8.0, 4.4
    p.rbox("stone", size=(w + 0.2, d + 0.2, 0.5), loc=(0, 0, 0.25), radius=0.03)
    p.rbox("glass_day", size=(w, d, 2.2), loc=(0, 0, 1.6), radius=0.03)
    for k in range(9):
        x = -w / 2 + k * w / 8
        p.rbox("white", size=(0.08, d + 0.04, 2.2), loc=(x, 0, 1.6), radius=0.01)
    gable_roof(p, w, d, 2.7, 1.4, "glass_day", overhang=0.05, gable="glass_day")
    for k in range(4):
        p.rbox("white", size=(0.08, d + 0.2, 0.08), loc=(-w / 2 + k * w / 3, 0, 3.5), radius=0.01)
    for k in range(6):
        p.ball("leaf_bright", r=0.4, loc=(-3.0 + k * 1.2, 0.8, 1.0), sub=2)
        p.ball("tomato_red", r=0.12, loc=(-3.0 + k * 1.2, 0.5, 1.2), sub=1)
    p.build(col)


# ---- nature in bulk --------------------------------------------------------------------------------

def forest_patch(col, seed, pines=True, count=6, radius=8.0):
    p = Part("Base")
    r = rng(seed)
    for k in range(count):
        a = r.uniform(0, math.tau)
        d = math.sqrt(r.random()) * radius
        x, y = math.cos(a) * d, math.sin(a) * d
        s = r.uniform(0.8, 1.4)
        if pines or r.random() < 0.3:
            pine_shape_at(p, x, y, s * 1.2, seed * 10 + k, detail=0.35)
        else:
            oak_shape_at(p, x, y, s, seed * 10 + k, detail=0.35)
    for k in range(4):
        a = r.uniform(0, math.tau)
        d = math.sqrt(r.random()) * radius
        fd.stone(p, math.cos(a) * d, math.sin(a) * d, -0.05, r.uniform(0.2, 0.45), seed * 7 + k)
    p.build(col)


def wildflowers(col, seed):
    p = Part("Base")
    fd.grass_field(p, 14, 3.5, seed, height=0.4)
    fd.flowers(p, 16, 3.2, seed + 1)
    p.build(col)


# ---- more of the town and the coast --------------------------------------------------------------

def fire_station(col):
    p = Part("Base")
    w, d, h = 10.0, 7.0, 5.0
    box_body(p, w, d, h, "brick")
    for x in (-2.4, 2.4):
        p.rbox("hull_red", size=(3.6, 0.16, 3.4), loc=(x, -d / 2 - 0.03, 2.1), radius=0.03)
        for k in range(5):
            p.rbox("glass_day", size=(3.2, 0.18, 0.3), loc=(x, -d / 2 - 0.04, 1.0 + k * 0.6), radius=0.01)
    p.rbox("white", size=(6.0, 0.16, 0.7), loc=(0, -d / 2 - 0.05, 4.4), radius=0.02)
    p.rbox("awning_red", size=(5.6, 0.18, 0.4), loc=(0, -d / 2 - 0.07, 4.4), radius=0.02)
    gable_roof(p, w, d, 0.4 + h, 1.8, "roof_slate", gable="brick")
    p.rbox("brick", size=(1.6, 1.6, 8.0), loc=(w / 2 - 1.0, d / 2 - 1.0, 4.0), radius=0.04)
    p.rcyl("roof_slate", r=1.3, h=1.0, seg=4, loc=(w / 2 - 1.0, d / 2 - 1.0, 8.5), rot=(0, 0, 45), radius=0.02, r2=0.02)
    # the fire engine out front
    p.rbox("hull_red", size=(1.9, 4.6, 1.5), loc=(-2.4, -d / 2 - 3.6, 1.15), radius=0.12)
    p.rbox("glass_day", size=(1.92, 1.0, 0.6), loc=(-2.4, -d / 2 - 5.4, 1.5), radius=0.06)
    p.rbox("iron_light", size=(1.2, 3.6, 0.16), loc=(-2.4, -d / 2 - 3.3, 2.0), radius=0.04)
    for y in (-5.2, -2.2):
        for xs in (-1, 1):
            p.rcyl("black", r=0.4, h=0.3, seg=14, loc=(-2.4 + xs * 0.95, -d / 2 + y, 0.4), rot=(0, 90, 0), radius=0.04)
    p.build(col)


def chapel(col):
    p = Part("Base")
    w, d, h = 6.0, 11.0, 5.0
    box_body(p, w, d, h, "stone")
    for y in (-3.0, 0.0, 3.0):
        for x in (-w / 2, w / 2):
            window(p, x, y, 3.2, w=0.8, h=2.0, face="-x" if x < 0 else "+x", glass="window_glow", frame="stone_dark")
    door(p, 0.0, -d / 2, 0.4, w=1.4, h=2.8, colour="door_brown", frame="stone_dark")
    gable_roof(p, w, d, 0.4 + h, 3.6, "roof_slate", along="y", gable="stone")
    p.rbox("stone", size=(2.4, 2.4, 9.0), loc=(0, -d / 2 - 1.0, 4.5), radius=0.04)
    p.rcyl("roof_slate", r=1.8, h=4.0, seg=4, loc=(0, -d / 2 - 1.0, 11.0), rot=(0, 0, 45), radius=0.02, r2=0.02)
    p.rcyl("white", r=0.5, h=0.1, seg=16, loc=(0, -d / 2 - 2.25, 7.5), rot=(90, 0, 0), radius=0.02)
    p.build(col)


def hotel(col):
    p = Part("Base")
    w, d, h = 12.0, 8.0, 9.0
    box_body(p, w, d, h, "wall_cream")
    for f in range(3):
        z = 1.9 + f * 3.0
        for x in (-4.5, -1.5, 1.5, 4.5):
            window(p, x, -d / 2, z, w=1.1, h=1.4, shutter="awning_blue")
        if f:
            p.rbox("white", size=(w + 0.2, 0.9, 0.12), loc=(0, -d / 2 - 0.45, z - 1.1), radius=0.02)
            for x in (-5.8, 5.8):
                p.rbox("iron", size=(0.06, 0.9, 0.9), loc=(x, -d / 2 - 0.45, z - 0.6), radius=0.01)
    p.rbox("awning_blue", size=(4.0, 2.2, 0.16), loc=(0, -d / 2 - 1.1, 3.3), radius=0.03)
    door(p, 0.0, -d / 2, 0.4, w=1.8, h=2.4, colour="glass_day")
    p.rbox("concrete", size=(w + 0.4, d + 0.4, 0.4), loc=(0, 0, 0.4 + h + 0.2), radius=0.03)
    p.rbox("sign", size=(5.0, 0.2, 1.0), loc=(0, -d / 2 - 0.1, 9.0), radius=0.03)
    p.build(col)


def sports_field(col):
    p = Part("Base")
    p.rbox("grass_light", size=(36.0, 22.0, 0.08), loc=(0, 0, 0.04), radius=0.02)
    for k in range(9):
        p.rbox("grass", size=(4.0, 22.0, 0.09), loc=(-16.0 + k * 4.0, 0, 0.045), radius=0.0) if k % 2 else None
    for x, y, sx, sy in ((0, 10.6, 35.0, 0.18), (0, -10.6, 35.0, 0.18), (-17.4, 0, 0.18, 21.0), (17.4, 0, 0.18, 21.0), (0, 0, 0.18, 21.0)):
        p.rbox("tarmac_line", size=(sx, sy, 0.02), loc=(x, y, 0.1), radius=0.0)
    p.rcyl("tarmac_line", r=3.4, h=0.02, seg=32, loc=(0, 0, 0.1), radius=0.0)
    p.rcyl("grass_light", r=3.2, h=0.03, seg=32, loc=(0, 0, 0.1), radius=0.0)
    for x in (-17.4, 17.4):
        p.cyl("white", r=0.08, h=2.4, seg=8, loc=(x, -3.6, 1.2))
        p.cyl("white", r=0.08, h=2.4, seg=8, loc=(x, 3.6, 1.2))
        p.cyl("white", r=0.08, h=7.2, seg=8, loc=(x, 0, 2.4), rot=(90, 0, 0))
    for side in (-1, 1):
        for k in range(4):
            p.rbox("awning_blue" if side < 0 else "awning_red", size=(30.0, 1.0, 0.5), loc=(0, side * (12.5 + k * 0.9), 0.25 + k * 0.5), radius=0.03)
    p.build(col)


def beach_spot(col, seed=1):
    p = Part("Base")
    r = rng(seed)
    for k in range(4):
        x, y = r.uniform(-6, 6), r.uniform(-3, 3)
        colour = r.choice(["awning_red", "awning_blue", "awning_green", "wall_yellow"])
        p.cyl("white", r=0.05, h=2.4, seg=6, loc=(x, y, 1.2))
        p.cone(colour, r=1.3, h=0.55, seg=10, loc=(x, y, 2.4))
        p.rbox(colour, size=(0.7, 1.8, 0.08), loc=(x + 1.0, y, 0.15), radius=0.02)
    p.rbox("plank", size=(2.6, 2.6, 2.4), loc=(8.0, 0, 1.6), radius=0.04)
    gable_roof(p, 2.6, 2.6, 2.8, 1.0, "awning_blue", gable="plank")
    awning(p, 2.6, 8.0, -1.3, 2.3, "awning_blue", "white", depth=0.9, stripes=6)
    for k in range(3):
        p.cyl("wood_dark", r=0.1, h=0.9, seg=6, loc=(-9.0 + k * 0.8, 1.0, 0.45))
    p.build(col)


def farmland(col, crop="wheat_gold", rows=10, length=22.0, seed=1):
    """A big decorative field in the countryside: soil rows full of a crop, edged with grass."""
    p = Part("Base")
    width = rows * 1.4
    p.rbox("tilled", size=(length, width, 0.16), loc=(0, 0, 0.0), radius=0.04)
    r = rng(seed)
    for k in range(rows):
        y = -width / 2 + 0.7 + k * 1.4
        p.rbox("tilled_light", size=(length - 0.6, 0.6, 0.2), loc=(0, y, 0.06), radius=0.06)
        n = int(length / 0.9)
        for j in range(n):
            x = -length / 2 + 0.6 + j * (length - 1.2) / max(1, n - 1)
            if crop == "wheat_gold":
                p.cone("wheat_gold", r=0.22, h=1.0 + r.uniform(-0.15, 0.15), seg=5, loc=(x, y, 0.65))
            elif crop == "sun_yellow":
                p.cyl("leaf", r=0.04, h=1.4, seg=5, loc=(x, y, 0.8))
                p.rcyl("sun_yellow", r=0.26, h=0.08, seg=10, loc=(x, y - 0.08, 1.55), rot=(70, 0, 0), radius=0.02)
                p.rcyl("sun_seed", r=0.12, h=0.1, seg=10, loc=(x, y - 0.12, 1.55), rot=(70, 0, 0), radius=0.02)
            else:
                p.ball(crop, r=0.3, loc=(x, y, 0.4), scale=(1, 1, 0.8), sub=1)
    p.build(col)


def orchard_rows(col, seed=1):
    p = Part("Base")
    r = rng(seed)
    for i in range(3):
        for j in range(5):
            x, y = -8.0 + j * 4.0, -4.0 + i * 4.0
            oak_shape_at(p, x, y, 0.75, seed * 50 + i * 5 + j, detail=0.3)
            for k in range(4):
                a = r.uniform(0, math.tau)
                p.ball("tomato_red" if seed % 2 else "onion_orange", r=0.09, loc=(x + math.cos(a) * 0.5, y + math.sin(a) * 0.5, 1.2 + r.uniform(0, 0.6)), sub=1)
    p.build(col)


ASSETS = {
    "house_cream": lambda c: house(c, "wall_cream", "roof_red", "hull_blue", seed=1),
    "house_blue": lambda c: house(c, "wall_blue", "roof_slate", "white", w=6.5, storeys=2, seed=2),
    "house_pink": lambda c: house(c, "wall_pink", "roof_red", "awning_green", w=5.5, seed=3),
    "house_mint": lambda c: house(c, "wall_mint", "roof_slate", "hull_red", w=7.0, storeys=2, seed=4),
    "house_yellow": lambda c: house(c, "wall_yellow", "roof_sky", "white", seed=5),
    "shop_bakery": lambda c: shop(c, "bakery"),
    "shop_grocer": lambda c: shop(c, "grocer"),
    "shop_fish": lambda c: shop(c, "fish"),
    "shop_tailor": lambda c: shop(c, "tailor"),
    "town_hall": town_hall,
    "school": school,
    "cafe": cafe,
    "clinic": clinic,
    "community_hall": community_hall,
    "market_stall_red": lambda c: market_stall(c, "awning_red"),
    "market_stall_green": lambda c: market_stall(c, "awning_green", ("potato_brown", "bean_green", "wheat_gold")),
    "market_stall_blue": lambda c: market_stall(c, "awning_blue", ("hull_blue", "egg", "cheese")),
    "park": park,
    "pier": pier,
    "boat_fishing": lambda c: boat(c, "hull_blue"),
    "boat_red": lambda c: boat(c, "hull_red", "hull_white"),
    "boat_sail": lambda c: boat(c, "hull_white", sail=True, length=6.0),
    "port_warehouse": port_warehouse,
    "crane": crane,
    "lighthouse": lighthouse,
    "station": station,
    "train": train,
    "helipad": helipad,
    "windmill": windmill,
    "silos": silos,
    "water_tower": water_tower,
    "bridge": bridge,
    "street_lamp": street_lamp,
    "greenhouse": greenhouse,
    "forest_pines": lambda c: forest_patch(c, 1, True),
    "forest_mixed": lambda c: forest_patch(c, 2, False),
    "forest_pines_b": lambda c: forest_patch(c, 3, True, count=8, radius=10.0),
    "wildflowers": lambda c: wildflowers(c, 9),
    "fire_station": fire_station,
    "chapel": chapel,
    "hotel": hotel,
    "sports_field": sports_field,
    "beach_spot": beach_spot,
    "farmland_wheat": lambda c: farmland(c, "wheat_gold", seed=1),
    "farmland_sunflower": lambda c: farmland(c, "sun_yellow", rows=8, seed=2),
    "farmland_cabbage": lambda c: farmland(c, "leaf_bright", rows=9, seed=3),
    "orchard_apples": lambda c: orchard_rows(c, 1),
    "orchard_oranges": lambda c: orchard_rows(c, 2),
}
