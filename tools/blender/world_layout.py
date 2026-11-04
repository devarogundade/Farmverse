"""The world map: one island in the ocean divided into sections - homestead, farm fields, animal
ranch, factory district, orchard, market square - each with fixed hex plots the player unlocks,
among a town, a seaport, a railway, hills, forest and a river.

Coordinates are metres, X east, Y north, Z up; the homestead's centre plot is the origin.
Plots are rectangles (square fields, paddocks, lots) laid out in a grid inside each section.
This module is the single source of truth: build_world.py models it, and it is written out
as world_layout.json for the game and the backend."""
import json
import math

TILE = 2.6
SQ3 = math.sqrt(3)

SEA_LEVEL = -2.0
GROUND = -0.2          # meadow height around the plots (a plot's top sits at 0)


def hex_xy(q, r):
    return (TILE * SQ3 * (q + r / 2.0), TILE * 1.5 * r)


def hex_dist(a, b):
    dq, dr = a[0] - b[0], a[1] - b[1]
    return max(abs(dq), abs(dr), abs(dq + dr))


def cluster(centre, count, shape=None):
    """`count` hexes around a centre hex, nearest first (ties broken by angle so the order is stable)."""
    cq, cr = centre
    out = []
    for ring in range(0, 8):
        for q in range(cq - ring, cq + ring + 1):
            for r in range(cr - ring, cr + ring + 1):
                if hex_dist((q, r), centre) != ring:
                    continue
                if shape and not shape(q - cq, r - cr):
                    continue
                out.append((q, r))
        if len(out) >= count:
            break
    cx, cy = hex_xy(cq, cr)

    def key(t):
        x, y = hex_xy(*t)
        return (hex_dist(t, centre), math.atan2(y - cy, x - cx))
    return sorted(out, key=key)[:count]


def nearest_hex(x, y):
    q = (SQ3 / 3 * x - y / 3) / TILE
    r = (2 / 3 * y) / TILE
    # cube rounding
    cx, cz = q, r
    cy = -cx - cz
    rx, ry, rz = round(cx), round(cy), round(cz)
    dx, dy, dz = abs(rx - cx), abs(ry - cy), abs(rz - cz)
    if dx > dy and dx > dz:
        rx = -ry - rz
    elif dy > dz:
        ry = -rx - rz
    else:
        rz = -rx - ry
    return (int(rx), int(rz))


# Plot sizes per kind (metres, width x depth): square fields, roomy paddocks, factory lots...
PLOT_SIZE = {"homestead": (8.0, 8.0), "crops": (6.0, 6.0), "animals": (10.0, 9.0), "machines": (11.0, 10.0),
             "trees": (7.0, 7.0), "shops": (5.0, 4.0), "fishing": (6.0, 9.0), "mining": (9.0, 8.0),
             "greenhouse": (10.0, 6.0), "zoo": (11.0, 9.0)}
PLOT_GAP = 1.5   # a path between neighbouring plots


def plot_grid(z):
    """The section's plots as a rectangular grid of (x, y, w, d), nearest to the gate first."""
    w, d = PLOT_SIZE[z["kind"]]
    n = z["plots"]
    cols = max(1, int(round(math.sqrt(n * d / w))))
    rows = int(math.ceil(n / cols))
    cx, cy = z["at"]
    out = []
    for i in range(rows):
        for j in range(cols):
            if len(out) >= n:
                break
            x = cx + (j - (cols - 1) / 2) * (w + PLOT_GAP)
            y = cy + (i - (rows - 1) / 2) * (d + PLOT_GAP)
            out.append((round(x, 2), round(y, 2), w, d))
    gx, gy = cx - cx / (math.hypot(cx, cy) or 1) * 30, cy - cy / (math.hypot(cx, cy) or 1) * 30
    out.sort(key=lambda p: math.hypot(p[0] - gx, p[1] - gy))
    return out


# The sections of the world. Each has a fixed set of plots the player unlocks in order;
# what may stand on a plot depends on the section. `open` plots are free from the start.
# centre is in metres; radius is the flat ground the section stands on; icon is shown on its gate.
ZONES = [
    {"id": "homestead", "name": "Homestead", "kind": "homestead", "at": (0.0, 0.0), "plots": 7, "open": 7, "radius": 15.0, "icon": "house"},
    {"id": "farm", "name": "Farm Fields", "kind": "crops", "at": (-124.0, -44.0), "plots": 20, "open": 3, "radius": 22.0, "icon": "wheat"},
    {"id": "ranch", "name": "Animal Ranch", "kind": "animals", "at": (-36.0, 72.0), "plots": 12, "open": 1, "radius": 26.0, "icon": "cow"},
    {"id": "factories", "name": "Factory District", "kind": "machines", "at": (62.0, -58.0), "plots": 12, "open": 1, "radius": 27.0, "icon": "gear"},
    {"id": "orchard", "name": "Orchard", "kind": "trees", "at": (42.0, 66.0), "plots": 8, "open": 0, "radius": 18.0, "icon": "apple"},
    {"id": "market", "name": "Market Square", "kind": "shops", "at": (112.0, 21.0), "plots": 8, "open": 2, "radius": 0.0, "icon": "bag"},
    # later sections (new plot kinds the game does not have yet)
    {"id": "fishery", "name": "Fishery", "kind": "fishing", "at": (-48.0, -110.0), "plots": 6, "open": 0, "radius": 12.0, "icon": "fish"},
    {"id": "mine", "name": "Mine", "kind": "mining", "at": (12.0, 118.0), "plots": 6, "open": 0, "radius": 16.0, "icon": "pick"},
    {"id": "greenhouses", "name": "Greenhouse Gardens", "kind": "greenhouse", "at": (-50.0, -30.0), "plots": 8, "open": 0, "radius": 16.0, "icon": "flower"},
    {"id": "zoo", "name": "Wildlife Park", "kind": "zoo", "at": (126.0, 102.0), "plots": 8, "open": 0, "radius": 20.0, "icon": "paw"},
]
FAIRGROUND = {"centre": (68.0, 32.0)}
for _z in ZONES:
    _z["rects"] = plot_grid(_z)
    _xs = [abs(p[0] - _z["at"][0]) + p[2] / 2 for p in _z["rects"]]
    _ys = [abs(p[1] - _z["at"][1]) + p[3] / 2 for p in _z["rects"]]
    _z["extent"] = math.hypot(max(_xs), max(_ys))       # half-diagonal of the plot grid
    if _z["radius"]:
        _z["radius"] = max(_z["radius"], _z["extent"] + 9.0)


def gate_of(z, towards=(0.0, 0.0)):
    """Where a section's gate stands: on its edge, facing `towards`."""
    x, y = z["at"]
    dx, dy = towards[0] - x, towards[1] - y
    d = math.hypot(dx, dy) or 1.0
    r = max(z["radius"] - 3.0, 10.0)
    return (x + dx / d * r, y + dy / d * r)


# ---- landmarks and the land around the valley ------------------------------------------------------

ISLAND_RADIUS = 175.0
VALLEY_RADIUS = 18.0   # the homestead clearing

# the coast pulls in to the south where the seaport is and pushes out east under the town
COAST_BULGES = [  # (angle in degrees, metres added to the radius, width in degrees)
    (-90, -62, 40), (-20, 25, 50), (35, 15, 40), (150, -20, 45), (100, 10, 50), (-140, -10, 25), (-165, 22, 30),
]

TOWN = {"centre": (112.0, 18.0), "size": (70.0, 70.0)}
PORT = {"centre": (10.0, -118.0)}
STATION = {"centre": (70.0, 88.0), "angle": 18.0}
HELIPAD = {"centre": (136.0, -60.0)}
WINDMILL = {"centre": (-10.0, 38.0)}
LIGHTHOUSE = {"centre": (70.0, -112.0)}

# the river comes down from the hills in the north-west and reaches the sea in the south-west
RIVER = [(-40.0, 150.0), (-62.0, 112.0), (-78.0, 70.0), (-88.0, 30.0), (-90.0, -10.0), (-82.0, -60.0), (-78.0, -100.0), (-80.0, -128.0)]
RIVER_WIDTH = 7.0

HILLS = [  # (x, y, height, radius)
    (-55.0, 125.0, 26.0, 48.0), (0.0, 140.0, 20.0, 40.0), (-125.0, 60.0, 18.0, 40.0), (55.0, 140.0, 14.0, 34.0),
]

# roads: asphalt spokes from the homestead to every section's gate, the town, the port and beyond
def _spoke(z):
    gx, gy = gate_of(z)
    d = math.hypot(gx, gy)
    return [(gx / d * 15.0, gy / d * 15.0), (gx, gy)]


ROADS = [
    [(15.0, 1.5), (60.0, 9.0), (88.0, 15.0), (112.0, 18.0)],            # homestead east to the town square
    [(112.0, 18.0), (126.0, -30.0), (96.0, -86.0), (40.0, -106.0), (10.0, -108.0)],  # town round the factories to the port
    [(112.0, 18.0), (100.0, 60.0), (78.0, 82.0)],                        # town to the station
    [(-15.0, -1.5), (-60.0, -8.0), (-90.0, -12.0), (-128.0, -8.0)],     # west over the river bridge
    [(1.0, -15.0), (6.0, -60.0), (9.0, -108.0)],                         # south to the port
    [(111.0, -58.0), (136.0, -60.0)],                                    # to the helipad
] + [_spoke(z) for z in ZONES if z["id"] in ("ranch", "factories", "orchard", "mine", "greenhouses")]
# the farm fields lie across the river, off the west road; the factories also open onto the port road
ROADS.append([gate_of(ZONES[1], (-100.0, -10.0)), (-100.0, -10.5)])
ROADS.append([gate_of(ZONES[3], (111.0, -58.0)), (111.0, -58.0)])
ROADS.append([(78.0, 82.0), (110.0, 88.0), (132.0, 92.0)])          # on to the wildlife park
ROADS.append([(8.0, -96.0), (-20.0, -100.0), (-38.0, -104.0)])                       # down to the fishery

# flat spots for the big pieces out in the country: (x, y, radius, what stands there, turn)
FLAT_SPOTS = [
    (-24.0, -70.0, 24.0, "sports_field", 0.0),
    (30.0, -82.0, 15.0, "farmland_wheat", 10.0),
    (38.0, 30.0, 14.0, "farmland_sunflower", 25.0),
    (-62.0, 18.0, 13.0, "farmland_cabbage", -20.0),
]

TOWN_STREETS = [
    [(85.0, 45.0), (140.0, 45.0)], [(85.0, -8.0), (140.0, -8.0)],
    [(88.0, 45.0), (88.0, -8.0)], [(138.0, 45.0), (138.0, -8.0)],
]
TRACKS = [(-150.0, 80.0), (-90.0, 92.0), (-30.0, 98.0), (30.0, 92.0), (70.0, 88.0), (120.0, 72.0), (160.0, 40.0)]


def coast_radius(angle):
    r = ISLAND_RADIUS
    deg = math.degrees(angle)
    for a, d, w in COAST_BULGES:
        diff = (deg - a + 180) % 360 - 180
        r += d * math.exp(-(diff / w) ** 2)
    r += 8 * math.sin(angle * 5 + 1.3) + 5 * math.sin(angle * 11 + 0.4)
    return r


def as_json():
    return {
        "tile": TILE,
        "seaLevel": SEA_LEVEL,
        "zones": [{"id": z["id"], "name": z["name"], "kind": z["kind"], "open": z["open"], "icon": z["icon"],
                   "centre": z["at"], "plotSize": PLOT_SIZE[z["kind"]],
                   "plots": [{"id": "%s.%d" % (z["id"], i + 1), "x": p[0], "y": p[1], "w": p[2], "d": p[3]} for i, p in enumerate(z["rects"])]}
                  for z in ZONES],
        "landmarks": {
            "town": TOWN["centre"], "port": PORT["centre"], "station": STATION["centre"], "helipad": HELIPAD["centre"],
            "windmill": WINDMILL["centre"], "lighthouse": LIGHTHOUSE["centre"],
        },
    }


def write(path):
    with open(path, "w") as f:
        json.dump(as_json(), f, indent=2)
