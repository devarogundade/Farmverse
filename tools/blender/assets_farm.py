"""The farm's own buildings and what players dress it with: farmhouse, warehouse,
paths, fences and decorations. Everything stands on one hex tile (TILE)."""
import math

from fvlib import Part, rng
from assets_build import gable_roof, house
from assets_land import TILE, flowers, tufts


def farmhouse(col):
    """The farmhouse on the first tile: a cottage with a porch, chimney and a flower bed."""
    p = Part("Idle")
    p.hexagon("grass", r=TILE * 0.93, h=0.06, loc=(0, 0, 0.03))
    house(p, "cream", "roof_red", 2.3, 1.7, 1.25, loc=(0, 0.2), roof_h=0.95)
    # porch: deck, two posts and a little roof over the door
    p.box("plank", size=(1.2, 0.6, 0.08), loc=(0, -0.95, 0.2))
    for x in (-0.5, 0.5):
        p.cyl("wood_dark", r=0.05, h=0.9, seg=6, loc=(x, -1.18, 0.65))
    p.box("roof_red", size=(1.3, 0.7, 0.06), loc=(0, -1.0, 1.12), rot=(-14, 0, 0))
    # chimney
    p.box("brick", size=(0.3, 0.3, 0.9), loc=(0.65, 0.5, 2.15))
    p.box("stone_dark", size=(0.36, 0.36, 0.06), loc=(0.65, 0.5, 2.62))
    # flower bed along the front
    p.box("soil", size=(0.7, 0.3, 0.12), loc=(-0.85, -0.85, 0.12))
    for i in range(4):
        p.ball(["flower_pink", "flower_yellow", "flower_white", "flower_blue"][i], r=0.07, loc=(-1.1 + i * 0.17, -0.85, 0.26), sub=1)
    tufts(p, "grass_dark", 8, TILE * 0.72, 71, avoid=1.6)
    p.build(col)


def warehouse(col):
    """The warehouse: a wide barn with big doors, crates and sacks out front."""
    p = Part("Idle")
    p.hexagon("path", r=TILE * 0.93, h=0.06, loc=(0, 0, 0.03))
    p.box("stone", size=(2.6, 1.9, 0.2), loc=(0, 0.2, 0.1))
    p.box("roof_red", size=(2.5, 1.8, 1.4), loc=(0, 0.2, 0.9))
    for x in (-1.25, 1.25):
        p.box("white", size=(0.08, 1.82, 1.42), loc=(x, 0.2, 0.9))
    gable_roof(p, "roof_brown", 2.5, 1.8, 0.9, 1.6, loc=(0, 0.2))
    # double doors with the white cross brace
    p.box("wood_dark", size=(1.1, 0.06, 1.0), loc=(0, -0.72, 0.7))
    for s in (-1, 1):
        p.box("white", size=(1.4, 0.04, 0.07), loc=(0, -0.76, 0.7), rot=(0, s * 42, 0))
    p.box("white", size=(1.2, 0.05, 0.08), loc=(0, -0.76, 1.22))
    # crates and sacks
    p.box("plank", size=(0.42, 0.42, 0.42), loc=(-1.0, -1.05, 0.27))
    p.box("plank", size=(0.36, 0.36, 0.36), loc=(-0.95, -1.05, 0.66), rot=(0, 0, 18))
    p.ball("canvas", r=0.2, loc=(1.0, -1.0, 0.24), scale=(1, 1, 1.3))
    p.ball("canvas", r=0.18, loc=(1.32, -0.9, 0.22), scale=(1, 1, 1.2))
    p.build(col)


def path_tile(col, colour, edge, stones):
    """A path drawn over a tile: a slightly smaller hexagon, with stepping stones on stone paths."""
    p = Part("Idle")
    p.hexagon(colour, r=TILE * 0.82, h=0.03, loc=(0, 0, 0.085))
    p.hexagon(edge, r=TILE * 0.86, h=0.02, loc=(0, 0, 0.075))
    if stones:
        r = rng(81)
        for _ in range(9):
            a = r.uniform(0, math.tau)
            d = math.sqrt(r.random()) * TILE * 0.7
            p.cyl("stone", r=r.uniform(0.18, 0.28), h=0.04, seg=7, loc=(math.cos(a) * d, math.sin(a) * d, 0.11))
    p.build(col)


def fence(col, rail, post):
    """One fence side, along Y and as long as a tile's edge, centred on the edge.
    The rails are the base (always shown); a gate adds its latch as the Ready stage."""
    p = Part("Base")
    half = TILE / 2
    for y in (-half, 0, half):
        p.cyl(post, r=0.06, h=0.8, seg=6, loc=(0, y, 0.4))
    for z in (0.35, 0.65):
        p.box(rail, size=(0.05, TILE, 0.08), loc=(0, 0, z))
    p.build(col)
    # a gate: the same side with an opening in the middle
    g = Part("Ready")
    g.box("metal_dark", size=(0.06, 0.06, 0.06), loc=(0, 0, 0.82))
    g.build(col)


# ---- decorations --------------------------------------------------------------------------------

def _ground(p, colour="grass"):
    p.hexagon(colour, r=TILE * 0.93, h=0.06, loc=(0, 0, 0.03))


def decor_default(col):
    """Any decoration without its own model yet: a planter with a shrub."""
    p = Part("Idle")
    _ground(p)
    p.box("wood", size=(0.8, 0.8, 0.4), loc=(0, 0, 0.26))
    p.ball("leaf", r=0.45, loc=(0, 0, 0.75), sub=2)
    flowers(p, 3, 0.3, 91)
    p.build(col)


def flower_bed(col):
    p = Part("Idle")
    _ground(p)
    p.cyl("soil", r=1.1, h=0.14, seg=12, loc=(0, 0, 0.12))
    p.cyl("stone", r=1.18, h=0.1, seg=12, loc=(0, 0, 0.1))
    flowers(p, 22, 1.0, 92)
    p.build(col)


def hay_bales(col):
    p = Part("Idle")
    _ground(p)
    for x, y, z in ((-0.4, 0, 0.3), (0.4, 0, 0.3), (0, 0.05, 0.85)):
        p.cyl("hay", r=0.36, h=0.75, seg=12, loc=(x, y, z), rot=(90, 0, 0))
    p.build(col)


def bench(col):
    p = Part("Idle")
    _ground(p)
    p.box("plank", size=(1.4, 0.4, 0.07), loc=(0, 0, 0.45))
    p.box("plank", size=(1.4, 0.07, 0.4), loc=(0, 0.2, 0.7), rot=(10, 0, 0))
    for x in (-0.6, 0.6):
        p.box("wood_dark", size=(0.08, 0.36, 0.42), loc=(x, 0, 0.22))
    p.build(col)


def mailbox(col):
    p = Part("Idle")
    _ground(p)
    p.cyl("wood_dark", r=0.05, h=0.9, seg=6, loc=(0, 0, 0.45))
    p.box("roof_blue", size=(0.3, 0.5, 0.26), loc=(0, 0, 0.98))
    p.box("red_flag", size=(0.03, 0.08, 0.2), loc=(0.17, 0.1, 1.1))
    p.build(col)


def lamp_post(col):
    p = Part("Idle")
    _ground(p)
    p.cyl("metal_dark", r=0.06, h=2.0, seg=8, loc=(0, 0, 1.0))
    p.box("glass", size=(0.26, 0.26, 0.32), loc=(0, 0, 2.15))
    p.cone("metal_dark", r=0.24, h=0.18, seg=4, loc=(0, 0, 2.4))
    p.build(col)


def beehive(col):
    p = Part("Idle")
    _ground(p)
    p.box("wood_dark", size=(0.7, 0.6, 0.25), loc=(0, 0, 0.15))
    for i in range(3):
        p.box("white" if i % 2 else "cream", size=(0.6, 0.5, 0.22), loc=(0, 0, 0.38 + i * 0.22))
    p.box("roof_green", size=(0.75, 0.65, 0.06), loc=(0, 0, 1.06))
    p.build(col)


def fountain(col):
    p = Part("Idle")
    _ground(p, "path")
    p.cyl("stone", r=1.0, h=0.4, seg=14, loc=(0, 0, 0.2))
    p.cyl("water", r=0.88, h=0.04, seg=14, loc=(0, 0, 0.38))
    p.cyl("stone", r=0.14, h=0.9, seg=8, loc=(0, 0, 0.75))
    p.cyl("stone", r=0.42, h=0.12, seg=12, loc=(0, 0, 1.15))
    p.cyl("water", r=0.36, h=0.03, seg=12, loc=(0, 0, 1.22))
    p.build(col)


def signpost(col):
    """Signs: a post and a board (the player's text is drawn by the UI)."""
    p = Part("Idle")
    _ground(p)
    p.cyl("wood_dark", r=0.06, h=1.2, seg=6, loc=(0, 0, 0.6))
    p.box("sign", size=(0.9, 0.07, 0.5), loc=(0, 0, 1.15))
    p.build(col)


def seasonal_default(col):
    """Seasonal decorations without their own model: a festive planter with a bow."""
    p = Part("Idle")
    _ground(p)
    p.cyl("brick", r=0.45, h=0.4, seg=10, loc=(0, 0, 0.26))
    p.cone("pine", r=0.42, h=0.9, seg=8, loc=(0, 0, 0.9))
    p.box("red_flag", size=(0.3, 0.06, 0.16), loc=(0, -0.3, 0.45))
    p.build(col)


ASSETS = {
    "farmhouse": farmhouse,
    "facility_warehouse": warehouse,
    "path_dirt": lambda c: path_tile(c, "path", "soil_dry", False),
    "path_stone": lambda c: path_tile(c, "stone", "stone_dark", True),
    "fence_wood": lambda c: fence(c, "wood", "wood_dark"),
    "fence_white": lambda c: fence(c, "white", "white"),
    "decor_default": decor_default,
    "decor_flower_bed": flower_bed,
    "decor_hay_bales": hay_bales,
    "decor_bench": bench,
    "decor_mailbox": mailbox,
    "decor_lamp_post": lamp_post,
    "decor_beehive": beehive,
    "decor_fountain": fountain,
    "sign_default": signpost,
    "seasonal_default": seasonal_default,
}
