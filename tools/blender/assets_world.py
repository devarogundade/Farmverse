"""The farm world in the painted style of the UI art (Content/UI/Art/Painted):
chunky hex land with a grass cap over brown soil, a gold-rimmed field, a pen
with round red rails, a cream cottage under a blue roof, a red gambrel barn,
round toy-like animals and orange-wood machines. Everything is modelled here
from simple solids; nothing comes from a generator or a third-party pack.

Conventions: Z is up, the front of a building faces -Y, a tile is a pointy-top
hexagon of corner radius TILE whose grass top sits at z = 0 so things stand on it.
"""
import math

from fvlib import Part, ell, empty, rng
import fvdetail as fd

TILE = 2.6
INNER = TILE * math.sqrt(3) / 2   # centre to the middle of a side
CELL = 1.0   # cells meet edge to edge: one seamless honeycomb


def hex_points(r, z=0.0, offset=30.0):
    """Corners of a pointy-top hexagon (corners at 30 + 60k degrees)."""
    return [(math.cos(math.radians(offset + 60 * k)) * r, math.sin(math.radians(offset + 60 * k)) * r, z) for k in range(6)]


def tufts(p, colour, count, radius, seed, height=0.2, avoid=0.0, z=0.0):
    r = rng(seed)
    for _ in range(count):
        a = r.uniform(0, math.tau)
        d = math.sqrt(r.uniform((avoid / radius) ** 2 if radius else 0, 1)) * radius
        h = height * r.uniform(0.7, 1.3)
        p.cone(colour, r=0.08 * r.uniform(0.8, 1.3), h=h, seg=4, loc=(math.cos(a) * d, math.sin(a) * d, z + h / 2),
               rot=(r.uniform(-14, 14), r.uniform(-14, 14), r.uniform(0, 90)))


def blossoms(p, count, radius, seed, z=0.0):
    r = rng(seed)
    for _ in range(count):
        a = r.uniform(0, math.tau)
        d = math.sqrt(r.random()) * radius
        x, y = math.cos(a) * d, math.sin(a) * d
        p.cyl("leaf_deep", r=0.015, h=0.16, seg=4, loc=(x, y, z + 0.08))
        p.ball(r.choice(["flower_pink", "flower_white", "flower_yellow", "flower_blue"]), r=0.06, loc=(x, y, z + 0.18))


# ---- land -----------------------------------------------------------------------------------------

def land_block(p, top, side="dirt", top_z=0.0, depth=0.8, drips=False):
    """The chunky tile of the land_hex icon: brown soil under a rounded grass cap whose edge
    rolls over the sides a little."""
    p.chunk(side, r=TILE * CELL * 0.985, h=depth, loc=(0, 0, top_z - 0.12 - depth / 2), radius=0.1)
    p.chunk(top, r=TILE * CELL, h=0.2, loc=(0, 0, top_z - 0.1), radius=0.03)
    if drips:
        # the grass lip: soft lobes hanging over each side, as in the painted tile
        for k in range(6):
            a = math.radians(60 * k)
            for t in (-0.55, 0.0, 0.55):
                tx, ty = -math.sin(a) * t * TILE * CELL * 0.5, math.cos(a) * t * TILE * CELL * 0.5
                x = math.cos(a) * INNER * CELL * 0.955 + tx
                y = math.sin(a) * INNER * CELL * 0.955 + ty
                p.ball(top, r=0.2, loc=(x, y, top_z - 0.22), scale=(1.0, 1.0, 0.55), sub=1)


def land_tile(col):
    """An owned tile with nothing on it: a mown lawn with grass clumps, a few flowers and a pebble."""
    p = Part("Base")
    land_block(p, "turf")
    p.build(col)
    idle = Part("Idle")
    fd.grass_field(idle, 18, TILE * 0.82, 11, height=0.24)
    fd.flowers(idle, 5, TILE * 0.7, 13)
    fd.stone(idle, 1.1, -0.9, -0.02, 0.12, 14)
    idle.build(col)


def land_sale(col):
    """Land next to the farm: paler, longer meadow a step lower; a price stake when it can be
    bought now (Ready), a gold padlock when the level's land is full (Idle)."""
    base = Part("Base")
    land_block(base, "turf_pale", top_z=-0.18)
    fd.grass_field(base, 22, TILE * 0.82, 21, z=-0.18, height=0.34, colours=("turf_light", "turf_pale", "straw"))
    fd.flowers(base, 3, TILE * 0.7, 23, z=-0.18, avoid=0.6)
    base.build(col)
    ready = Part("Ready")
    ready.rcyl("orange_wood_dark", r=0.06, h=1.0, seg=8, loc=(0, 0, 0.32), radius=0.02)
    ready.rcyl("gold", r=0.3, h=0.08, seg=24, loc=(0, -0.02, 0.92), rot=(90, 0, 0), radius=0.03)
    ready.rcyl("gold_dark", r=0.2, h=0.09, seg=24, loc=(0, -0.025, 0.92), rot=(90, 0, 0), radius=0.02)
    ready.build(col)
    locked = Part("Idle")
    locked.rbox("gold", size=(0.6, 0.3, 0.5), loc=(0, 0, 0.1), radius=0.1)
    locked.rbox("gold_dark", size=(0.1, 0.32, 0.18), loc=(0, 0, 0.1), radius=0.04)
    for x in (-0.18, 0.18):
        locked.rcyl("gold", r=0.06, h=0.32, seg=10, loc=(x, 0, 0.5), radius=0.02)
    locked.rbox("gold", size=(0.48, 0.12, 0.12), loc=(0, 0, 0.66), radius=0.05)
    locked.build(col)


def land_wild(col):
    """Land beyond what can be bought yet: low, darker rough with shrubs, tall grass and stones."""
    p = Part("Base")
    land_block(p, "turf_wild", top_z=-0.4, drips=False)
    r = rng(31)
    for k in range(1):
        a = r.uniform(0, math.tau)
        d = r.uniform(0.7, 1.4)
        x, y = math.cos(a) * d, math.sin(a) * d
        els = []
        for j in range(3):
            rad = r.uniform(0.2, 0.3)
            els.append(ell((x + r.uniform(-0.25, 0.25), y + r.uniform(-0.25, 0.25), -0.4 + rad * 0.8), (rad, rad, rad * 0.85)))
        p.organic(els, lambda c, n: "leaf_deep" if n.z < 0.3 else "canopy", voxel=0.06, ratio=0.12, smooth=4)
    fd.stone(p, -1.0, 0.8, -0.42, 0.22, 35)
    fd.grass_field(p, 9, TILE * 0.8, 33, z=-0.4, height=0.36, colours=("turf_dark", "turf_wild", "leaf_deep"))
    p.build(col)


def field(col):
    """The bed a crop grows in (plot_field icon): a gold rim around dark tilled soil in ridges."""
    p = Part("Base")
    p.chunk("wood", r=TILE * 0.82, h=0.22, loc=(0, 0, 0.06), radius=0.04)
    p.chunk("tilled", r=TILE * 0.73, h=0.24, loc=(0, 0, 0.08), radius=0.05)
    rows = 6
    for i in range(rows):
        y = -INNER * 0.66 + INNER * 1.32 * (i + 0.5) / rows
        # each ridge spans the hexagon's width at this height
        # the pointy-top bed's width at this height: full across the middle, narrowing to the points
        bed = TILE * 0.71
        width = 2 * min(bed * math.sqrt(3) / 2, (bed - abs(y)) * math.sqrt(3)) - 0.25
        p.rbox("tilled_light", size=(width, 0.32, 0.12), loc=(0, y, 0.2), radius=0.06)
    p.build(col)


def pen(col):
    """A paddock (plot_pasture icon): straw ground, round red posts and rails, a hay roll and a trough."""
    p = Part("Base")
    p.chunk("straw", r=TILE * 0.82, h=0.12, loc=(0, 0, 0.03), radius=0.05)
    tufts(p, "turf_light", 8, TILE * 0.7, 61, height=0.12, z=0.08)
    posts = [(x * 0.8, y * 0.8) for x, y, _ in hex_points(TILE)]
    for i, (x, y) in enumerate(posts):
        p.rcyl("fence_red", r=0.12, h=0.8, seg=12, loc=(x, y, 0.45), radius=0.05)
        p.ball("fence_red", r=0.12, loc=(x, y, 0.85), sub=2)
        if i == 4:
            continue  # the gate side stays open toward the camera
        nx, ny = posts[(i + 1) % 6]
        mx, my = (x + nx) / 2, (y + ny) / 2
        ang = math.degrees(math.atan2(ny - y, nx - x))
        length = math.hypot(nx - x, ny - y) - 0.2
        for z in (0.36, 0.66):
            p.rcyl("fence_red_dark" if z < 0.5 else "fence_red", r=0.07, h=length, seg=10, loc=(mx, my, z), rot=(0, 90, ang), radius=0.03)
    # hay roll and a water trough
    p.rcyl("straw", r=0.38, h=0.6, seg=16, loc=(0.7, 0.75, 0.42), rot=(90, 0, 25), radius=0.08)
    p.rcyl("gold_dark", r=0.18, h=0.62, seg=16, loc=(0.7, 0.75, 0.42), rot=(90, 0, 25), radius=0.04)
    p.rbox("orange_wood", size=(1.0, 0.42, 0.3), loc=(-0.6, 1.05, 0.22), radius=0.07)
    p.rbox("pond", size=(0.84, 0.28, 0.06), loc=(-0.6, 1.05, 0.35), radius=0.03)
    p.build(col)


# ---- buildings ---------------------------------------------------------------------------------------

def farmhouse(col):
    """The cottage of fac_farmhouse_l1: gold plinth, cream walls, a soft blue roof, orange chimney,
    arched door and warm windows."""
    p = Part("Idle")
    # a fieldstone foundation and a mown yard under it
    p.rbox("stone_dark", size=(2.6, 2.0, 0.34), loc=(0, 0.15, 0.17), radius=0.06)
    p.rbox("wall_cream", size=(2.4, 1.8, 1.35), loc=(0, 0.15, 0.98), radius=0.05)
    # timber corner posts and a sill beam
    for x in (-1.19, 1.19):
        for y in (-0.74, 1.04):
            p.rbox("wood_dark", size=(0.12, 0.12, 1.36), loc=(x, y, 0.99), radius=0.02)
    p.rbox("wood_dark", size=(2.46, 1.86, 0.1), loc=(0, 0.15, 0.37), radius=0.02)
    # roof: two rounded slabs meeting at a ridge, gable ends filled in cream
    pitch, half, rise = 34.0, 1.45, 0.98
    slab = half / math.cos(math.radians(pitch))
    for side in (-1, 1):
        p.rbox("roof_sky", size=(slab, 2.25, 0.2), loc=(side * half / 2, 0.15, 1.66 + rise / 2), rot=(0, side * pitch, 0), radius=0.09)
    p.rcyl("roof_sky_dark", r=0.11, h=2.3, seg=14, loc=(0, 0.15, 1.68 + rise), rot=(90, 0, 0), radius=0.04)
    # fascia boards along the eaves and up the gables
    for side in (-1, 1):
        p.rbox("trim", size=(0.06, 2.3, 0.12), loc=(side * 1.47, 0.15, 1.58), radius=0.015)
        for y in (-0.98, 1.28):
            p.rbox("trim", size=(slab + 0.05, 0.06, 0.1), loc=(side * half / 2, y, 1.6 + rise / 2), rot=(0, side * pitch, 0), radius=0.015)
    p.prism("wall_cream", [(-1.18, 0.0), (1.18, 0.0), (0.0, 0.84)], 1.75, loc=(0, 0.15, 1.65))
    p.rbox("chimney_orange", size=(0.38, 0.38, 0.9), loc=(0.6, 0.55, 2.35), radius=0.06)
    p.rbox("chimney_orange", size=(0.48, 0.48, 0.14), loc=(0.6, 0.55, 2.82), radius=0.05)
    empty("Chimney", col, loc=(0.6, 0.55, 2.95))
    # arched door with an orange frame and a step
    p.rbox("orange_wood", size=(0.78, 0.1, 1.12), loc=(-0.45, -0.77, 0.86), radius=0.05)
    p.rcyl("orange_wood", r=0.39, h=0.1, seg=16, loc=(-0.45, -0.77, 1.42), rot=(90, 0, 0), radius=0.03)
    p.rbox("door_brown", size=(0.6, 0.1, 0.98), loc=(-0.45, -0.8, 0.86), radius=0.04)
    p.rcyl("door_brown", r=0.3, h=0.1, seg=16, loc=(-0.45, -0.8, 1.35), rot=(90, 0, 0), radius=0.02)
    p.ball("gold", r=0.05, loc=(-0.25, -0.86, 0.86))
    # stone steps, a little porch roof on brackets, a lantern and pots by the door
    p.rbox("stone", size=(0.95, 0.42, 0.14), loc=(-0.45, -1.0, 0.2), radius=0.03)
    p.rbox("stone", size=(0.85, 0.3, 0.14), loc=(-0.45, -1.15, 0.07), radius=0.03)
    p.rbox("roof_sky_dark", size=(1.15, 0.55, 0.07), loc=(-0.45, -1.0, 1.98), rot=(-18, 0, 0), radius=0.02)
    for x in (-0.95, 0.05):
        p.rbox("wood_dark", size=(0.07, 0.3, 0.07), loc=(x, -0.88, 1.84), rot=(35, 0, 0), radius=0.01)
    fd.lantern(p, 0.08, -0.84, 1.42)
    fd.potted_plant(p, -1.05, -1.1, 0.0, 61, "flower_pink")
    fd.potted_plant(p, 0.12, -1.12, 0.0, 62, "flower_yellow")
    for k, (x, y) in enumerate(((-0.45, -1.55), (-0.35, -1.95), (-0.5, -2.3))):
        p.rcyl("pebble", r=0.17, h=0.05, seg=9, loc=(x, y, 0.02), radius=0.015)
    # windows: warm glass with an orange cross and frame
    for x, y, rot in ((0.55, -0.77, 0), (-1.22, 0.2, 90), (1.22, 0.2, 90)):
        p.rbox("orange_wood", size=(0.62, 0.1, 0.6), loc=(x, y, 1.08), rot=(0, 0, rot), radius=0.05)
        p.rbox("window_glow", size=(0.48, 0.11, 0.46), loc=(x, y, 1.08), rot=(0, 0, rot), radius=0.03)
        p.rbox("orange_wood", size=(0.05, 0.12, 0.46), loc=(x, y, 1.08), rot=(0, 0, rot), radius=0.01)
        p.rbox("orange_wood", size=(0.48, 0.12, 0.05), loc=(x, y, 1.08), rot=(0, 0, rot), radius=0.01)
        # shutters either side
        for sd in (-1, 1):
            if rot == 0:
                p.rbox("roof_sky_dark", size=(0.22, 0.06, 0.62), loc=(x + sd * 0.44, y - 0.02, 1.08), radius=0.02)
            else:
                p.rbox("roof_sky_dark", size=(0.06, 0.22, 0.62), loc=(x + (0.02 if x > 0 else -0.02), y + sd * 0.44, 1.08), radius=0.02)
    # a flower box under the front window
    p.rbox("orange_wood_dark", size=(0.7, 0.2, 0.16), loc=(0.55, -0.9, 0.7), radius=0.04)
    blossoms_row = [(0.32, "flower_pink"), (0.48, "flower_yellow"), (0.64, "flower_white"), (0.8, "flower_pink")]
    for x, colour in blossoms_row:
        p.ball(colour, r=0.07, loc=(x, -0.9, 0.84))
    p.build(col)


def warehouse(col):
    """The red barn of fac_warehouse_l1: a gambrel roof, white trim and X-braced doors on a gold plinth."""
    p = Part("Idle")
    w, d = 2.6, 2.2
    p.rbox("stone_dark", size=(w + 0.2, d + 0.2, 0.32), loc=(0, 0.1, 0.16), radius=0.05)
    # body and roof in one gambrel profile, extruded front to back
    profile = [(-w / 2, 0.0), (w / 2, 0.0), (w / 2, 1.3), (w * 0.34, 2.0), (0.0, 2.3), (-w * 0.34, 2.0), (-w / 2, 1.3)]
    p.prism("barn_red", profile, d, loc=(0, 0.1, 0.3))
    # roof sheets: slightly proud of the walls, darker red, with white trim along the eaves
    for side in (-1, 1):
        lower_len = math.hypot(w / 2 - w * 0.34, 0.7) + 0.12
        lower_ang = math.degrees(math.atan2(0.7, w / 2 - w * 0.34))
        p.rbox("roof_red", size=(lower_len, d + 0.3, 0.14), loc=(side * (w / 2 + w * 0.34) / 2 + side * 0.06, 0.1, 0.3 + 1.65 + 0.06),
               rot=(0, side * lower_ang, 0), radius=0.05)
        upper_len = math.hypot(w * 0.34, 0.3) + 0.12
        upper_ang = math.degrees(math.atan2(0.3, w * 0.34))
        p.rbox("roof_red", size=(upper_len, d + 0.3, 0.14), loc=(side * w * 0.17, 0.1, 0.3 + 2.15 + 0.07),
               rot=(0, side * upper_ang, 0), radius=0.05)
    p.rcyl("trim", r=0.08, h=d + 0.34, seg=10, loc=(0, 0.1, 0.3 + 2.38), rot=(90, 0, 0), radius=0.03)
    front = 0.1 - d / 2 - 0.04
    # white corner and gable trim on the front
    for x in (-w / 2 + 0.06, w / 2 - 0.06):
        p.rbox("trim", size=(0.14, 0.08, 1.3), loc=(x, front, 0.95), radius=0.03)
    # big doors: white frame, red panels, white X
    p.rbox("trim", size=(1.4, 0.08, 1.24), loc=(0, front - 0.01, 0.95), radius=0.04)
    p.rbox("barn_red_dark", size=(1.2, 0.09, 1.08), loc=(0, front - 0.02, 0.93), radius=0.03)
    for s in (-1, 1):
        p.rbox("trim", size=(1.5, 0.1, 0.1), loc=(0, front - 0.04, 0.93), rot=(0, s * 42, 0), radius=0.03)
    p.rbox("trim", size=(0.06, 0.1, 1.08), loc=(0, front - 0.04, 0.93), radius=0.02)
    # hay loft window
    p.rbox("trim", size=(0.62, 0.08, 0.5), loc=(0, front - 0.01, 1.9), radius=0.04)
    p.rbox("window_glow", size=(0.46, 0.09, 0.36), loc=(0, front - 0.02, 1.9), radius=0.03)
    p.rbox("trim", size=(0.46, 0.1, 0.05), loc=(0, front - 0.03, 1.9), radius=0.01)
    p.rbox("trim", size=(0.05, 0.1, 0.36), loc=(0, front - 0.03, 1.9), radius=0.01)
    # a cupola with a weather vane on the ridge
    p.rbox("barn_red", size=(0.5, 0.5, 0.42), loc=(0, 0.1, 2.85), radius=0.03)
    p.rbox("trim", size=(0.36, 0.52, 0.24), loc=(0, 0.1, 2.86), radius=0.02)
    p.rcyl("roof_red", r=0.45, h=0.32, seg=4, loc=(0, 0.1, 3.2), rot=(0, 0, 45), radius=0.02, r2=0.02)
    p.cyl("iron", r=0.015, h=0.5, seg=6, loc=(0, 0.1, 3.55))
    p.rbox("iron", size=(0.34, 0.02, 0.12), loc=(0.06, 0.1, 3.68), radius=0.005)
    p.cone("iron", r=0.05, h=0.12, seg=4, loc=(-0.17, 0.1, 3.68), rot=(0, -90, 0))
    # crates, sacks, a hay bale and a stack of planks by the door
    p.rbox("orange_wood", size=(0.44, 0.44, 0.44), loc=(-1.05, -1.25, 0.22), radius=0.03)
    p.rbox("orange_wood", size=(0.36, 0.36, 0.36), loc=(-1.02, -1.25, 0.62), rot=(0, 0, 14), radius=0.03)
    fd.sack(p, 1.0, -1.2, 0.0, "canvas", 1.0)
    fd.sack(p, 1.32, -1.0, 0.0, "canvas", 0.85, lean=10)
    fd.hay_bale(p, 1.55, 0.6, 0.0, 0.8, rot=10)
    fd.plank_stack(p, -1.45, 0.4, 0.0, 77)
    p.build(col)


# ---- animals -----------------------------------------------------------------------------------------

def hen(p, s=1.0):
    """animal_chicken: a round cream hen with a red comb and an orange beak."""
    p.ball("hen_cream", r=0.27 * s, loc=(0, 0.04 * s, 0.33 * s), scale=(0.95, 1.15, 0.95), sub=2)
    p.ball("hen_cream", r=0.18 * s, loc=(0, -0.2 * s, 0.56 * s), sub=2)
    for k, (dy, dz, r) in enumerate(((-0.24, 0.73, 0.06), (-0.18, 0.76, 0.065), (-0.12, 0.73, 0.055))):
        p.ball("comb", r=r * s, loc=(0, dy * s, dz * s))
    p.cone("beak", r=0.05 * s, h=0.11 * s, seg=8, loc=(0, -0.4 * s, 0.55 * s), rot=(90, 0, 0))
    p.ball("comb", r=0.04 * s, loc=(0, -0.34 * s, 0.47 * s), scale=(1, 1, 1.3))
    for x in (-0.085, 0.085):
        p.ball("eye", r=0.03 * s, loc=(x * s, -0.33 * s, 0.6 * s))
        p.ball("hen_cream", r=0.13 * s, loc=(x * 2.4 * s, 0.06 * s, 0.35 * s), scale=(0.45, 1.1, 0.75), sub=2)
        p.cyl("beak", r=0.022 * s, h=0.16 * s, seg=6, loc=(x * 0.9 * s, 0.04 * s, 0.08 * s))
        p.rbox("beak", size=(0.08 * s, 0.12 * s, 0.03 * s), loc=(x * 0.9 * s, 0.0, 0.015 * s), radius=0.01)
    for k in range(3):
        p.ball("hen_cream", r=0.08 * s, loc=((k - 1) * 0.07 * s, 0.32 * s, 0.5 * s + k % 2 * 0.04 * s), scale=(0.6, 0.8, 1.4))


def chicken(col):
    grown = Part("Grown")
    hen(grown)
    grown.build(col)
    product = Part("Product")
    product.rcyl("straw", r=0.2, h=0.08, seg=12, loc=(0.42, 0.25, 0.04), radius=0.03)
    for k in range(3):
        a = math.tau * k / 3
        product.ball("egg", r=0.055, loc=(0.42 + math.cos(a) * 0.07, 0.25 + math.sin(a) * 0.07, 0.12), scale=(1, 1, 1.3), sub=2)
    product.build(col)


def cow_shape(p, s=1.0):
    """animal_cow: a cream body with black patches, an orange muzzle and short orange horns."""
    p.rbox("cow_cream", size=(0.76 * s, 1.15 * s, 0.66 * s), loc=(0, 0.05 * s, 0.72 * s), radius=0.26 * s)
    r = rng(7)
    for k in range(5):
        side = -1 if k % 2 else 1
        p.ball("eye", r=r.uniform(0.13, 0.19) * s, loc=(side * 0.34 * s, r.uniform(-0.35, 0.45) * s, r.uniform(0.65, 0.88) * s), scale=(0.3, 1.2, 0.9))
    p.ball("eye", r=0.2 * s, loc=(0.05 * s, 0.2 * s, 1.04 * s), scale=(1.2, 1.3, 0.3))
    for x in (-0.22, 0.22):
        for y in (-0.36, 0.42):
            p.rcyl("cow_cream", r=0.11 * s, h=0.4 * s, seg=10, loc=(x * s, y * s, 0.27 * s), radius=0.03)
            p.rcyl("eye", r=0.1 * s, h=0.1 * s, seg=10, loc=(x * s, y * s, 0.05 * s), radius=0.03)
    # head
    p.rbox("cow_cream", size=(0.5 * s, 0.44 * s, 0.46 * s), loc=(0, -0.64 * s, 0.98 * s), radius=0.17 * s)
    p.rbox("muzzle", size=(0.46 * s, 0.24 * s, 0.26 * s), loc=(0, -0.88 * s, 0.87 * s), radius=0.11 * s)
    for x in (-0.09, 0.09):
        p.ball("eye", r=0.03 * s, loc=(x * s, -1.0 * s, 0.9 * s), scale=(1, 0.5, 1))
        p.ball("eye", r=0.045 * s, loc=(x * 1.3 * s, -0.86 * s, 1.1 * s), sub=2)
    for x in (-1, 1):
        p.ball("eye", r=0.1 * s, loc=(x * 0.31 * s, -0.58 * s, 1.1 * s), scale=(1.4, 0.5, 0.7))
        p.cone("horn_orange", r=0.05 * s, h=0.16 * s, seg=8, loc=(x * 0.16 * s, -0.6 * s, 1.26 * s), rot=(0, x * 25, 0))
    # tail
    p.cyl("cow_cream", r=0.025 * s, h=0.45 * s, seg=6, loc=(0, 0.66 * s, 0.72 * s), rot=(20, 0, 0))
    p.ball("eye", r=0.06 * s, loc=(0, 0.74 * s, 0.5 * s), scale=(1, 1, 1.5))


def cow(col):
    grown = Part("Grown")
    cow_shape(grown)
    grown.build(col)
    product = Part("Product")
    product.rcyl("iron_light", r=0.13, h=0.36, seg=14, loc=(0.62, -0.25, 0.18), radius=0.04)
    product.rcyl("iron_light", r=0.08, h=0.12, seg=12, loc=(0.62, -0.25, 0.42), radius=0.02)
    product.rcyl("milk", r=0.07, h=0.02, seg=12, loc=(0.62, -0.25, 0.48), radius=0.005)
    product.build(col)


# ---- machines (orange wood and grey iron, like the machine icons) -------------------------------------

def crate_base(p, w, d, h, z=0.0):
    """A plank-built machine base on a fieldstone footing, with dark corner posts and iron brackets."""
    p.rbox("stone_dark", size=(w + 0.16, d + 0.16, 0.12), loc=(0, 0, z + 0.06), radius=0.03)
    p.rbox("orange_wood", size=(w, d, h - 0.1), loc=(0, 0, z + 0.1 + (h - 0.1) / 2), radius=0.03)
    for x in (-w / 2, w / 2):
        for y in (-d / 2, d / 2):
            p.rbox("orange_wood_dark", size=(0.1, 0.1, h - 0.06), loc=(x, y, z + 0.1 + (h - 0.1) / 2), radius=0.015)
            p.rbox("iron", size=(0.13, 0.13, 0.05), loc=(x, y, z + h - 0.06), radius=0.01)
    p.rbox("orange_wood_dark", size=(w + 0.02, d + 0.02, 0.05), loc=(0, 0, z + h - 0.02), radius=0.015)


def product_sack(col, x, y, colour="canvas"):
    pr = Part("Product")
    fd.sack(pr, x, y, 0.0, colour, 1.1)
    pr.build(col)


def mill(col):
    """mach_mill: an orange crate with a cream millstone and its turning handle; a sack of flour."""
    p = Part("Idle")
    crate_base(p, 1.5, 1.5, 0.75)
    p.rcyl("iron", r=0.08, h=0.3, seg=10, loc=(0, 0, 0.85), radius=0.02)
    p.build(col)
    s = Part("SpinStone")
    s.rcyl("pebble", r=0.62, h=0.26, seg=24, loc=(0, 0, 0.92), radius=0.07)
    s.rcyl("gold", r=0.12, h=0.28, seg=12, loc=(0, 0, 0.93), radius=0.03)
    s.rcyl("orange_wood", r=0.06, h=0.5, seg=10, loc=(0.4, 0, 1.25), radius=0.02)
    s.ball("orange_wood", r=0.09, loc=(0.4, 0, 1.5), sub=2)
    s.build(col)
    product_sack(col, 0.95, -0.55, "flour")


def feed_mill(col):
    """A crate with a grey hopper on top and a side wheel that turns while it works."""
    p = Part("Idle")
    crate_base(p, 1.4, 1.2, 0.8)
    p.rcyl("iron", r=0.62, h=0.6, seg=4, loc=(0, 0, 1.15), rot=(0, 0, 45), radius=0.06, r2=0.3)
    p.rcyl("iron_light", r=0.66, h=0.1, seg=4, loc=(0, 0, 1.47), rot=(0, 0, 45), radius=0.03)
    p.rcyl("iron", r=0.06, h=0.5, seg=8, loc=(0.75, 0, 0.6), rot=(0, 90, 0), radius=0.01)
    p.build(col)
    w = Part("SpinWheel")
    w.rcyl("orange_wood", r=0.45, h=0.12, seg=20, loc=(0.82, 0, 0.6), rot=(0, 90, 0), radius=0.03)
    for k in range(4):
        w.rbox("orange_wood_dark", size=(0.13, 0.8, 0.07), loc=(0.82, 0, 0.6), rot=(45 * k, 0, 0), radius=0.02)
    w.build(col)
    product_sack(col, -0.95, -0.45, "feed")


def oven(col):
    """A red-brick bakery oven with a glowing arch and a chimney; loaves cool on a board."""
    p = Part("Idle")
    p.rbox("stone_dark", size=(1.9, 1.6, 0.25), loc=(0, 0, 0.12), radius=0.04)
    p.rbox("brick", size=(1.6, 1.3, 1.0), loc=(0, 0.05, 0.75), radius=0.14)
    p.ball("brick", r=0.8, loc=(0, 0.05, 1.2), scale=(1.0, 0.82, 0.55), sub=2)
    p.rcyl("window_glow", r=0.32, h=0.1, seg=16, loc=(0, -0.62, 0.78), rot=(90, 0, 0), radius=0.02)
    p.rbox("window_glow", size=(0.64, 0.1, 0.32), loc=(0, -0.62, 0.62), radius=0.02)
    p.rbox("gold", size=(0.84, 0.12, 0.08), loc=(0, -0.66, 0.44), radius=0.03)
    p.rcyl("chimney_orange", r=0.16, h=0.75, seg=12, loc=(0.45, 0.35, 1.7), radius=0.04)
    p.rcyl("chimney_orange", r=0.21, h=0.12, seg=12, loc=(0.45, 0.35, 2.08), radius=0.04)
    empty("Chimney", col, loc=(0.45, 0.35, 2.15))
    # a stack of split firewood beside it
    for k, (y, z) in enumerate(((-0.25, 0.1), (0.0, 0.1), (0.25, 0.1), (-0.12, 0.28), (0.12, 0.28))):
        p.rcyl("trunk", r=0.09, h=0.5, seg=7, loc=(1.05, y, z), rot=(0, 90, 0), radius=0.02)
    p.build(col)
    pr = Part("Product")
    pr.rbox("orange_wood", size=(0.7, 0.4, 0.06), loc=(-0.95, -0.55, 0.3), radius=0.02)
    for x in (-1.1, -0.85):
        pr.ball("bread", r=0.14, loc=(x, -0.55, 0.4), scale=(1.3, 0.8, 0.6), sub=2)
    pr.build(col)


def kettle(col):
    """A copper preserving kettle on a stone fire ring; steam rises while it works."""
    p = Part("Idle")
    p.rcyl("pebble", r=0.75, h=0.3, seg=16, loc=(0, 0, 0.15), radius=0.08)
    p.rcyl("dirt_dark", r=0.55, h=0.32, seg=16, loc=(0, 0, 0.16), radius=0.02)
    for k in range(3):
        a = math.tau * k / 3
        p.rcyl("iron", r=0.04, h=0.6, seg=6, loc=(math.cos(a) * 0.5, math.sin(a) * 0.5, 0.55), radius=0.01)
    for k in range(3):
        p.rcyl("trunk", r=0.06, h=0.7, seg=7, loc=(0, 0, 0.3), rot=(0, 90, 60 * k), radius=0.015)
    p.ball("window_glow", r=0.18, loc=(0, 0, 0.36), scale=(1, 1, 0.5), sub=2)
    p.ball("copper", r=0.55, loc=(0, 0, 0.85), scale=(1, 1, 0.75), sub=3)
    p.rcyl("copper", r=0.5, h=0.1, seg=20, loc=(0, 0, 1.15), radius=0.04)
    p.rcyl("jam", r=0.44, h=0.02, seg=20, loc=(0, 0, 1.19), radius=0.005)
    empty("Chimney", col, loc=(0, 0, 1.3))
    p.build(col)
    pr = Part("Product")
    for k, x in enumerate((-0.95, -0.7)):
        pr.rcyl("jam", r=0.11, h=0.2, seg=12, loc=(x, -0.6, 0.1), radius=0.03)
        pr.rcyl("canvas", r=0.12, h=0.05, seg=12, loc=(x, -0.6, 0.22), radius=0.02)
    pr.build(col)


def dairy(col):
    """A small cream dairy under a blue roof, milk churns at the door."""
    p = Part("Idle")
    p.rbox("stone_dark", size=(2.0, 1.7, 0.25), loc=(0, 0.05, 0.12), radius=0.04)
    p.rbox("wall_cream", size=(1.7, 1.4, 1.05), loc=(0, 0.1, 0.77), radius=0.08)
    for side in (-1, 1):
        p.rbox("roof_sky", size=(1.15, 1.7, 0.16), loc=(side * 0.48, 0.1, 1.55), rot=(0, side * 30, 0), radius=0.07)
    p.prism("wall_cream", [(-0.86, 0.0), (0.86, 0.0), (0.0, 0.5)], 1.35, loc=(0, 0.1, 1.28))
    p.rbox("door_brown", size=(0.5, 0.08, 0.75), loc=(0, -0.63, 0.65), radius=0.03)
    p.rbox("window_glow", size=(0.36, 0.08, 0.3), loc=(0.55, -0.63, 0.9), radius=0.03)
    p.build(col)
    pr = Part("Product")
    for x in (-0.75, -0.45):
        pr.rcyl("iron_light", r=0.13, h=0.36, seg=14, loc=(x, -0.85, 0.18), radius=0.04)
        pr.rcyl("iron_light", r=0.08, h=0.12, seg=12, loc=(x, -0.85, 0.42), radius=0.02)
    pr.build(col)


def oil_press(col):
    """mach_oil_press: a grey iron frame with a screw over a hooped barrel; the wheel on top turns."""
    p = Part("Idle")
    crate_base(p, 1.5, 1.2, 0.3)
    p.rcyl("orange_wood", r=0.4, h=0.7, seg=18, loc=(0, 0, 0.65), radius=0.05)
    for z in (0.45, 0.85):
        p.rcyl("gold", r=0.42, h=0.07, seg=18, loc=(0, 0, z), radius=0.02)
    for x in (-0.62, 0.62):
        p.rbox("iron", size=(0.2, 0.3, 1.5), loc=(x, 0, 1.0), radius=0.06)
    p.rbox("iron", size=(1.45, 0.32, 0.24), loc=(0, 0, 1.78), radius=0.08)
    p.rcyl("iron_light", r=0.07, h=0.85, seg=10, loc=(0, 0, 1.3), radius=0.02)
    p.build(col)
    w = Part("SpinWheel")
    w.rcyl("iron", r=0.4, h=0.06, seg=20, loc=(0, 0, 2.02), radius=0.02)
    for k in range(4):
        w.ball("iron_light", r=0.07, loc=(math.cos(k * math.pi / 2) * 0.42, math.sin(k * math.pi / 2) * 0.42, 2.02), sub=2)
    w.build(col)
    pr = Part("Product")
    pr.rcyl("oil", r=0.11, h=0.3, seg=12, loc=(0.95, -0.5, 0.15), radius=0.04)
    pr.rcyl("orange_wood_dark", r=0.05, h=0.1, seg=8, loc=(0.95, -0.5, 0.36), radius=0.01)
    pr.build(col)


def sawmill(col):
    """A sawing bench: a log on a wooden table and a round blade that spins while it works."""
    p = Part("Idle")
    p.rbox("orange_wood", size=(1.9, 0.9, 0.16), loc=(0, 0, 0.72), radius=0.05)
    for x in (-0.8, 0.8):
        for y in (-0.33, 0.33):
            p.rbox("orange_wood_dark", size=(0.14, 0.14, 0.68), loc=(x, y, 0.34), radius=0.03)
    p.rcyl("trunk", r=0.22, h=1.5, seg=14, loc=(-0.15, 0.1, 1.02), rot=(0, 90, 0), radius=0.04)
    p.rcyl("wood_light", r=0.18, h=1.52, seg=14, loc=(-0.15, 0.1, 1.02), rot=(0, 90, 0), radius=0.01)
    p.organic([ell((0.75, -0.55, 0.0), (0.35, 0.28, 0.1))], lambda c, n: "wood_light", voxel=0.03, ratio=0.2, smooth=3)
    fd.plank_stack(p, -0.3, -0.75, 0.0, 91)
    p.build(col)
    b = Part("SpinBlade")
    b.rcyl("iron_light", r=0.45, h=0.04, seg=24, loc=(0.75, -0.2, 0.95), rot=(90, 0, 0), radius=0.01)
    b.rcyl("iron", r=0.1, h=0.07, seg=12, loc=(0.75, -0.2, 0.95), rot=(90, 0, 0), radius=0.01)
    b.build(col)
    pr = Part("Product")
    for k in range(3):
        pr.rcyl("trunk", r=0.12, h=0.8, seg=10, loc=(-0.9 + k * 0.05, -0.75, 0.12 + k * 0.2 if k < 2 else 0.32), rot=(0, 90, 0), radius=0.03)
    pr.build(col)


# ---- trees -------------------------------------------------------------------------------------------

def pine_shape(p, s):
    p.rcyl("trunk", r=0.14 * s, h=0.7 * s, seg=10, loc=(0, 0, 0.35 * s), radius=0.03, r2=0.1 * s)
    for k, (z, r) in enumerate(((0.6, 0.95), (1.2, 0.75), (1.75, 0.52))):
        p.rcyl("pine_deep" if k % 2 == 0 else "pine_mid", r=r * s, h=0.9 * s, seg=14, loc=(0, 0, (z + 0.45) * s), radius=0.12 * s, r2=0.08 * s)
    p.ball("pine_mid", r=0.12 * s, loc=(0, 0, 2.62 * s), sub=2)


def oak_shape(p, s, seed=5):
    r = rng(seed)
    p.rcyl("trunk", r=0.18 * s, h=1.3 * s, seg=10, loc=(0, 0, 0.65 * s), radius=0.04, r2=0.13 * s)
    p.ball("canopy", r=0.85 * s, loc=(0, 0, 1.85 * s), scale=(1, 1, 0.85), sub=2)
    for k in range(5):
        a = math.tau * k / 5 + r.random()
        p.ball("canopy_light" if k % 2 else "canopy", r=0.5 * s, loc=(math.cos(a) * 0.55 * s, math.sin(a) * 0.55 * s, (1.65 + r.uniform(-0.1, 0.25)) * s), sub=2)


def tree(col, kind):
    shape = pine_shape if kind == "tree_pine" else oak_shape
    for stage, scale in (("Young", 0.45), ("Grown", 0.75), ("Ready", 1.0)):
        p = Part(stage)
        shape(p, scale)
        if stage == "Ready":
            # cut logs at the foot: ready to collect
            for k, (x, y) in enumerate(((0.55, -0.45), (0.75, -0.2))):
                p.rcyl("trunk", r=0.1, h=0.55, seg=10, loc=(x, y, 0.1), rot=(0, 90, 30 * k), radius=0.03)
        p.build(col)


# ---- fences and paths --------------------------------------------------------------------------------

def fence(col, picket, rail):
    """One fence side (fac_fence_pack): rounded pickets with pointed tops on two rails, along Y,
    as long as a tile's edge and centred on it. A gate adds its latch as the Ready stage."""
    p = Part("Base")
    n = 6
    for k in range(n):
        y = -TILE / 2 + TILE * (k + 0.5) / n
        p.rbox(picket, size=(0.1, 0.32, 0.75), loc=(0, y, 0.4), radius=0.04)
        p.rcyl(picket, r=0.17, h=0.16, seg=4, loc=(0, y, 0.84), rot=(0, 0, 45), radius=0.02, r2=0.02)
    for z in (0.3, 0.6):
        p.rbox(rail, size=(0.08, TILE, 0.12), loc=(0.07, 0, z), radius=0.04)
    p.build(col)
    g = Part("Ready")
    g.rbox("iron", size=(0.06, 0.12, 0.12), loc=(0.12, 0, 0.45), radius=0.02)
    g.build(col)


def path_tile(col, top, edge, stones):
    p = Part("Base")
    p.chunk(edge, r=TILE * 0.86, h=0.06, loc=(0, 0, 0.02), radius=0.02)
    p.chunk(top, r=TILE * 0.8, h=0.06, loc=(0, 0, 0.04), radius=0.02)
    r = rng(81)
    for _ in range(9 if stones else 5):
        a = r.uniform(0, math.tau)
        d = math.sqrt(r.random()) * TILE * 0.7
        if stones:
            p.rcyl("pebble", r=r.uniform(0.22, 0.32), h=0.06, seg=10, loc=(math.cos(a) * d, math.sin(a) * d, 0.09), radius=0.02)
        else:
            p.ball("pebble_dark", r=0.06, loc=(math.cos(a) * d, math.sin(a) * d, 0.08), scale=(1, 1, 0.5))
    p.build(col)


# ---- scenery around the farm (never tappable) ---------------------------------------------------------

def scenery_tree_round(col):
    p = Part("Base")
    oak_shape(p, 1.5, seed=17)
    p.build(col)


def scenery_tree_pine(col):
    p = Part("Base")
    pine_shape(p, 1.6)
    p.build(col)


def scenery_bush(col):
    p = Part("Base")
    for k, (x, y, r) in enumerate(((0, 0, 0.55), (0.45, 0.2, 0.4), (-0.4, 0.15, 0.42))):
        p.ball("canopy" if k % 2 == 0 else "canopy_light", r=r, loc=(x, y, r * 0.75), scale=(1, 1, 0.85), sub=2)
    p.ball("berry", r=0.07, loc=(0.2, -0.45, 0.6))
    p.ball("flower_pink", r=0.07, loc=(-0.3, -0.35, 0.5))
    p.build(col)


def scenery_rock(col):
    p = Part("Base")
    p.rbox("pebble", size=(1.1, 0.8, 0.6), loc=(0, 0, 0.25), rot=(6, -4, 20), radius=0.22)
    p.rbox("pebble_dark", size=(0.6, 0.5, 0.4), loc=(0.6, 0.35, 0.15), rot=(0, 8, -15), radius=0.15)
    p.build(col)


def scenery_flowers(col):
    p = Part("Base")
    tufts(p, "turf_dark", 10, 0.9, 41, height=0.3)
    blossoms(p, 9, 0.8, 42)
    p.build(col)


def scenery_pond(col):
    p = Part("Base")
    p.rcyl("pebble", r=1.6, h=0.12, seg=20, loc=(0, 0, 0.04), radius=0.05)
    p.rcyl("pond", r=1.4, h=0.13, seg=20, loc=(0, 0, 0.06), radius=0.03)
    for k in range(3):
        a = 1.2 + k * 0.6
        p.rcyl("leaf_bright", r=0.18, h=0.03, seg=10, loc=(math.cos(a) * 0.8, math.sin(a) * 0.8, 0.14), radius=0.01)
    p.build(col)


ASSETS = {
    "land_tile": land_tile,
    "land_sale": land_sale,
    "land_wild": land_wild,
    "field": field,
    "pen": pen,
    "farmhouse": farmhouse,
    "facility_warehouse": warehouse,
    "livestock_chicken": chicken,
    "livestock_cow": cow,
    "machine_mill": mill,
    "machine_feed_mill": feed_mill,
    "machine_oven": oven,
    "machine_kettle": kettle,
    "machine_dairy": dairy,
    "machine_oil_press": oil_press,
    "machine_sawmill": sawmill,
    "tree_pine": lambda c: tree(c, "tree_pine"),
    "tree_oak": lambda c: tree(c, "tree_oak"),
    "fence_wood": lambda c: fence(c, "orange_wood", "orange_wood_dark"),
    "fence_white": lambda c: fence(c, "trim", "wall_cream"),
    "path_dirt": lambda c: path_tile(c, "path", "dirt", False),
    "path_stone": lambda c: path_tile(c, "dirt", "dirt_dark", True),
    "scenery_tree_round": scenery_tree_round,
    "scenery_tree_pine": scenery_tree_pine,
    "scenery_bush": scenery_bush,
    "scenery_rock": scenery_rock,
    "scenery_flowers": scenery_flowers,
    "scenery_pond": scenery_pond,
}


# ---- crops (crop icons): seven chunky plants per tile, five growth stages ------------------------------

SPOTS = [(0.0, 0.0)] + [(math.cos(math.radians(60 * k)) * 1.2, math.sin(math.radians(60 * k)) * 1.2) for k in range(6)]
CROP_STAGES = ("Seeded", "Sprout", "Young", "Grown", "Ready")
GROUND = 0.2  # the field's ridge tops


def mound(p, x, y, seed):
    p.ball("tilled_light", r=0.2, loc=(x, y, GROUND), scale=(1.2, 1.2, 0.45))
    p.ball("straw", r=0.04, loc=(x + 0.05, y - 0.03, GROUND + 0.09))


def sprout(p, x, y, h=0.25):
    p.rcyl("leaf_bright", r=0.03, h=h, seg=6, loc=(x, y, GROUND + h / 2), radius=0.01)
    for side in (-1, 1):
        p.ball("leaf_bright", r=0.09, loc=(x + side * 0.08, y, GROUND + h), scale=(1.2, 0.6, 0.35), rot=(0, side * -25, 0))


def leaf_blade(p, x, y, z, angle, length, colour="leaf_bright", droop=35):
    a = math.radians(angle)
    p.rbox(colour, size=(0.1, length, 0.03), loc=(x + math.cos(a) * length * 0.4, y + math.sin(a) * length * 0.4, z),
           rot=(-droop, 0, angle - 90), radius=0.012)


def wheat_plant(p, x, y, s, ripe):
    ear = "wheat_gold" if ripe else "leaf_bright"
    stalk = "wheat_gold" if ripe else "leaf"
    r = rng(int(x * 100 + y * 10) + 3)
    for k in range(7):
        a = math.tau * k / 7
        dx, dy = math.cos(a) * 0.14 * s, math.sin(a) * 0.14 * s
        h = r.uniform(0.75, 0.95) * s
        tilt = (math.degrees(math.atan2(dy, dx)))
        p.rcyl(stalk, r=0.03, h=h, seg=6, loc=(x + dx, y + dy, GROUND + h / 2), rot=(0, 8, tilt), radius=0.01)
        p.ball(ear, r=0.07 * s, loc=(x + dx * 1.3, y + dy * 1.3, GROUND + h + 0.08 * s), scale=(0.8, 0.8, 2.0))
    if ripe:
        p.rcyl("gold_dark", r=0.17 * s, h=0.08, seg=10, loc=(x, y, GROUND + 0.35 * s), radius=0.02)


def maize_plant(p, x, y, s, ripe):
    h = 1.35 * s
    p.rcyl("leaf", r=0.06 * s, h=h, seg=8, loc=(x, y, GROUND + h / 2), radius=0.02)
    for k, z in enumerate((0.35, 0.65, 0.95)):
        leaf_blade(p, x, y, GROUND + z * s, 60 + 120 * k, 0.55 * s, "leaf_bright" if k % 2 else "leaf")
    p.cone("husk", r=0.06 * s, h=0.25 * s, seg=6, loc=(x, y, GROUND + h + 0.1 * s))
    if ripe:
        for side in (-1, 1):
            cx = x + side * 0.12 * s
            p.ball("maize_gold", r=0.12 * s, loc=(cx, y - 0.04, GROUND + 0.75 * s), scale=(0.75, 0.75, 1.6), rot=(0, side * 20, 0), sub=2)
            p.ball("husk", r=0.1 * s, loc=(cx + side * 0.03, y - 0.04, GROUND + 0.62 * s), scale=(0.7, 0.7, 1.3), rot=(0, side * 30, 0))


def tomato_plant(p, x, y, s, ripe):
    p.rcyl("orange_wood_dark", r=0.025, h=0.95 * s, seg=6, loc=(x + 0.12 * s, y + 0.1 * s, GROUND + 0.47 * s), radius=0.01)
    for k, (dx, dy, dz, r) in enumerate(((0, 0, 0.35, 0.28), (0.12, 0.08, 0.62, 0.22), (-0.1, -0.05, 0.7, 0.2))):
        p.ball("leaf" if k % 2 else "leaf_deep", r=r * s, loc=(x + dx * s, y + dy * s, GROUND + dz * s), scale=(1, 1, 0.85), sub=2)
    if ripe:
        for k, (dx, dy, dz) in enumerate(((0.2, -0.18, 0.35), (-0.22, -0.12, 0.5), (0.05, -0.25, 0.72), (-0.05, 0.22, 0.45))):
            tx, ty, tz = x + dx * s, y + dy * s, GROUND + dz * s
            p.ball("tomato_red", r=0.12 * s, loc=(tx, ty, tz), sub=2)
            p.cone("leaf_bright", r=0.06 * s, h=0.04, seg=5, loc=(tx, ty, tz + 0.12 * s))


def onion_plant(p, x, y, s, ripe):
    bulb = 0.2 * s if ripe else 0.12 * s
    p.ball("onion_orange" if ripe else "onion_light", r=bulb, loc=(x, y, GROUND + bulb * 0.6), scale=(1, 1, 0.9), sub=2)
    p.cone("onion_orange" if ripe else "onion_light", r=bulb * 0.35, h=bulb * 0.8, seg=8, loc=(x, y, GROUND + bulb * 1.4))
    for k in range(3):
        a = math.radians(120 * k + 20)
        h = 0.55 * s
        p.cone("leaf_bright" if k % 2 else "leaf", r=0.05 * s, h=h, seg=6, loc=(x + math.cos(a) * 0.06, y + math.sin(a) * 0.06, GROUND + bulb * 1.2 + h / 2),
               rot=(math.sin(a) * -18, math.cos(a) * 18, 0))


def potato_plant(p, x, y, s, ripe):
    for k, (dx, dy, dz, r) in enumerate(((0, 0, 0.3, 0.3), (0.2, 0.1, 0.22, 0.22), (-0.18, 0.12, 0.24, 0.22), (0.02, -0.2, 0.2, 0.2))):
        p.ball("leaf_bright" if k % 2 else "leaf", r=r * s, loc=(x + dx * s, y + dy * s, GROUND + dz * s), scale=(1, 1, 0.75), sub=2)
    if ripe:
        for k, (dx, dy) in enumerate(((0.3, -0.25), (-0.3, -0.2), (0.05, -0.38))):
            p.ball("potato_brown", r=0.11 * s, loc=(x + dx * s, y + dy * s, GROUND + 0.05), scale=(1.3, 1, 0.9), sub=2)
        for dx, dy in ((0.1, 0.05), (-0.12, -0.08)):
            p.ball("flower_white", r=0.05 * s, loc=(x + dx * s, y + dy * s, GROUND + 0.5 * s))


def beans_plant(p, x, y, s, ripe):
    p.rcyl("orange_wood_dark", r=0.03, h=1.25 * s, seg=6, loc=(x, y, GROUND + 0.62 * s), radius=0.01)
    for k, z in enumerate((0.3, 0.55, 0.8, 1.05)):
        a = math.radians(90 * k)
        p.ball("leaf" if k % 2 else "leaf_bright", r=0.16 * s, loc=(x + math.cos(a) * 0.12 * s, y + math.sin(a) * 0.12 * s, GROUND + z * s), scale=(1, 1, 0.7), sub=2)
    if ripe:
        for k, z in enumerate((0.4, 0.7, 0.95)):
            a = math.radians(90 * k + 45)
            p.rbox("bean_green", size=(0.07 * s, 0.07 * s, 0.32 * s), loc=(x + math.cos(a) * 0.2 * s, y + math.sin(a) * 0.2 * s, GROUND + z * s - 0.08),
                   rot=(10, 0, 0), radius=0.03)


def sunflower_plant(p, x, y, s, ripe):
    h = 1.3 * s
    p.rcyl("leaf", r=0.05 * s, h=h, seg=8, loc=(x, y, GROUND + h / 2), radius=0.02)
    for k, z in enumerate((0.4, 0.75)):
        leaf_blade(p, x, y, GROUND + z * s, 30 + 180 * k, 0.4 * s, "leaf_bright", droop=20)
    hx, hz = y - 0.08 * s, GROUND + h + 0.05 * s
    if ripe:
        p.rcyl("sun_seed", r=0.2 * s, h=0.08 * s, seg=14, loc=(x, hx, hz), rot=(70, 0, 0), radius=0.03)
        for k in range(12):
            a = math.tau * k / 12
            px = x + math.cos(a) * 0.29 * s
            pz = hz + math.sin(a) * 0.29 * s * math.cos(math.radians(20))
            py = hx - math.sin(a) * 0.29 * s * math.sin(math.radians(20)) - 0.02
            p.ball("sun_yellow", r=0.1 * s, loc=(px, py, pz), scale=(1.0, 0.35, 0.55), rot=(70, 0, math.degrees(a)))
    else:
        p.ball("leaf_bright", r=0.12 * s, loc=(x, hx, hz), scale=(1, 1, 0.8), sub=2)


CROP_PLANTS = {
    "crop_wheat": wheat_plant, "crop_maize": maize_plant, "crop_tomato": tomato_plant, "crop_onion": onion_plant,
    "crop_potato": potato_plant, "crop_beans": beans_plant, "crop_sunflower": sunflower_plant,
}


def crop(col, crop_id):
    plant = CROP_PLANTS[crop_id]
    for stage in CROP_STAGES:
        p = Part(stage)
        for k, (x, y) in enumerate(SPOTS):
            if stage == "Seeded":
                mound(p, x, y, k)
            elif stage == "Sprout":
                sprout(p, x, y)
            else:
                s = {"Young": 0.55, "Grown": 0.9, "Ready": 1.0}[stage]
                plant(p, x, y, s, stage == "Ready")
        p.build(col)


for _crop_id in CROP_PLANTS:
    ASSETS[_crop_id] = (lambda i: lambda c: crop(c, i))(_crop_id)
