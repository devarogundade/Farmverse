"""The farm's material atlas: painted-but-real surfaces generated from scratch with numpy.

One 4096 x 4096 atlas (albedo + smoothness in alpha) and a matching tangent-space
normal atlas. It is cut into 256 px cells: textured swatches (grass, soil, planks,
shingles, bricks, plaster, stone, straw, metal, foliage, bark, water, canvas) each
take one cell (the land's grass takes 2 x 2), and one cell holds the flat palette
(16 x 16 colours) for everything else. Every swatch is tinted with its palette
colour, so a model keeps the colours it was designed with and gains the surface.

A face whose colour has a swatch is mapped onto that swatch by planar projection in
its own frame (see fvlib.Part.build); the swatch's ``metres`` says how much of the
world one swatch covers, so texel density is the same everywhere.
"""
import math
import struct
import zlib

import numpy as np

ATLAS = 4096
CELL = 256
GRID = ATLAS // CELL           # 16 x 16 cells
MARGIN = 6 / CELL              # keep UVs this far inside a swatch so mipmaps don't bleed
PALETTE_CELL = (GRID - 1, GRID - 1)   # bottom-right cell holds the flat colours
PALETTE_CELLS = 16

# palette name -> (pattern, cells wide/high, metres covered, smoothness)
SURFACES = {}


def _reg(pattern, names, cells=1, metres=3.0, smooth=0.1):
    for n in names:
        SURFACES[n] = (pattern, cells, metres, smooth)


_reg("grass", ["turf", "turf_light", "turf_dark", "turf_pale", "turf_wild"], cells=2, metres=5.6, smooth=0.06)
_reg("grass", ["grass", "grass_dark", "grass_light"], metres=3.0, smooth=0.06)
_reg("soil", ["dirt", "dirt_dark", "soil", "soil_dark", "soil_dry", "path", "compost"], metres=2.4, smooth=0.04)
_reg("tilled", ["tilled", "tilled_light"], metres=2.0, smooth=0.05)
_reg("planks", ["wood", "wood_dark", "wood_light", "plank", "orange_wood", "orange_wood_dark", "door_brown",
                "fence_red", "fence_red_dark", "barn_red", "barn_red_dark", "sign"], metres=2.4, smooth=0.14)
_reg("shingles", ["roof_red", "roof_blue", "roof_brown", "roof_green", "roof_sky", "roof_sky_dark"], metres=2.4, smooth=0.22)
_reg("bricks", ["brick", "chimney_orange"], metres=1.6, smooth=0.08)
_reg("plaster", ["wall_cream", "cream", "white", "trim", "gold", "gold_dark"], metres=2.4, smooth=0.1)
_reg("cobble", ["stone", "stone_dark"], metres=2.0, smooth=0.12)
_reg("rock", ["pebble", "pebble_dark", "rock"], metres=1.2, smooth=0.12)
_reg("straw", ["straw", "hay", "feed"], metres=1.6, smooth=0.08)
_reg("metal", ["metal", "metal_dark", "iron", "iron_light", "copper"], metres=1.2, smooth=0.5)
_reg("foliage", ["canopy", "canopy_light", "leaf", "leaf_dark", "leaf_deep", "leaf_bright", "leaf_light",
                 "oak", "oak_light", "pine", "pine_light", "pine_deep", "pine_mid"], metres=1.6, smooth=0.18)
_reg("bark", ["bark", "trunk"], metres=1.2, smooth=0.06)
_reg("water", ["water", "pond"], metres=2.0, smooth=0.85)
_reg("canvas", ["canvas", "rope"], metres=0.8, smooth=0.06)
_reg("fur", ["cow", "cow_cream", "hen", "hen_cream"], metres=0.6, smooth=0.12)
_reg("plaster", ["wall_blue", "wall_pink", "wall_mint", "wall_yellow", "concrete"], metres=3.0, smooth=0.1)
_reg("planks", ["hull_blue", "hull_red", "hull_white"], metres=2.4, smooth=0.25)
_reg("shingles", ["roof_slate"], metres=2.4, smooth=0.2)
_reg("metal", ["metal_roof"], metres=2.0, smooth=0.45)


# ---- layout ---------------------------------------------------------------------------------------

def layout(palette_names):
    """Place every swatch in the atlas. Returns name -> (x0, y0, w, h) in cells (y from the top)."""
    taken = set([PALETTE_CELL])
    rects = {}
    for name in palette_names:
        if name not in SURFACES:
            continue
        size = SURFACES[name][1]
        placed = False
        for cy in range(0, GRID - size + 1):
            for cx in range(0, GRID - size + 1):
                cells = {(cx + i, cy + j) for i in range(size) for j in range(size)}
                if cells & taken:
                    continue
                taken |= cells
                rects[name] = (cx, cy, size, size)
                placed = True
                break
            if placed:
                break
        if not placed:
            raise RuntimeError("atlas full at " + name)
    return rects


def swatch_uv(rect, u, v):
    """A point of a swatch (u, v in 0..1, v up) as atlas UV (v up, Blender convention)."""
    cx, cy, w, h = rect
    u = MARGIN + u * (1 - 2 * MARGIN)
    v = MARGIN + v * (1 - 2 * MARGIN)
    x = (cx + u * w) / GRID
    y = 1.0 - (cy + (1 - v) * h) / GRID
    return x, y


def palette_cell_uv(index):
    cx, cy = PALETTE_CELL
    px = (index % PALETTE_CELLS + 0.5) / PALETTE_CELLS
    py = (index // PALETTE_CELLS + 0.5) / PALETTE_CELLS
    return (cx + px) / GRID, 1.0 - (cy + py) / GRID


# ---- noise ----------------------------------------------------------------------------------------

def _smooth(t):
    return t * t * (3 - 2 * t)


def vnoise(h, w, sy, sx, seed):
    """Value noise with feature size sy x sx pixels, in 0..1."""
    r = np.random.default_rng(seed)
    gh, gw = int(math.ceil(h / sy)) + 2, int(math.ceil(w / sx)) + 2
    g = r.random((gh, gw))
    y = np.arange(h) / sy
    x = np.arange(w) / sx
    y0 = y.astype(int)
    x0 = x.astype(int)
    fy = _smooth(y - y0)[:, None]
    fx = _smooth(x - x0)[None, :]
    a = g[y0][:, x0]
    b = g[y0][:, x0 + 1]
    c = g[y0 + 1][:, x0]
    d = g[y0 + 1][:, x0 + 1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fbm(h, w, s, seed, octaves=4, aspect=1.0):
    total = np.zeros((h, w))
    amp, norm = 1.0, 0.0
    for o in range(octaves):
        total += amp * vnoise(h, w, max(1.0, s), max(1.0, s * aspect), seed + o * 101)
        norm += amp
        amp *= 0.5
        s /= 2
    return total / norm


def voronoi(h, w, count, seed):
    """F1, F2 distances (pixels) and the nearest point's index, for a jittered set of points."""
    r = np.random.default_rng(seed)
    pts = r.random((count, 2)) * [h, w]
    yy, xx = np.mgrid[0:h, 0:w]
    f1 = np.full((h, w), 1e9)
    f2 = np.full((h, w), 1e9)
    idx = np.zeros((h, w), dtype=int)
    for i, (py, px) in enumerate(pts):
        d = np.hypot(yy - py, xx - px)
        closer = d < f1
        f2 = np.where(closer, f1, np.minimum(f2, d))
        idx = np.where(closer, i, idx)
        f1 = np.where(closer, d, f1)
    return f1, f2, idx


def rand_per(idx, seed):
    r = np.random.default_rng(seed)
    table = r.random(idx.max() + 1)
    return table[idx]


# ---- patterns: each returns (shade around 1.0, height 0..1) at size n x n ----------------------------

def _strokes(n, count, length, seed, angle_jitter=0.6, base_angle=-math.pi / 2):
    """Short painted strokes (grass blades, straw): returns a 0..1 map, brighter toward each tip."""
    r = np.random.default_rng(seed)
    out = np.zeros((n, n))
    ys = r.random(count) * n
    xs = r.random(count) * n
    angs = base_angle + (r.random(count) - 0.5) * 2 * angle_jitter
    lens = length * (0.6 + 0.8 * r.random(count))
    for y, x, a, l in zip(ys, xs, angs, lens):
        steps = int(l)
        for k in range(steps):
            t = k / max(1, steps - 1)
            py = int(y + math.sin(a) * k) % n
            px = int(x + math.cos(a) * k) % n
            out[py, px] = max(out[py, px], 0.35 + 0.65 * t)
    return out


def p_grass(n, seed):
    patches = fbm(n, n, n / 3, seed, 4)
    clumps = fbm(n, n, n / 14, seed + 5, 3)
    blades = _strokes(n, int(n * n / 22), n / 40, seed + 11)
    shade = 0.78 + 0.28 * patches + 0.14 * (clumps - 0.5)
    shade = shade * (0.9 + 0.22 * blades)
    shade = np.where((blades == 0) & (clumps < 0.38), shade * 0.88, shade)
    r = np.random.default_rng(seed)
    speck = r.random((n, n)) > 0.9985
    shade = np.where(speck, shade * 1.3, shade)
    height = 0.55 * blades + 0.25 * clumps + 0.2 * patches
    return shade, height


def p_soil(n, seed):
    base = fbm(n, n, n / 5, seed, 5)
    f1, f2, idx = voronoi(n, n, 140, seed + 3)
    pebble = (f1 < 3.2) & (rand_per(idx, seed) > 0.55)
    bands = 0.5 + 0.5 * np.sin(np.arange(n)[:, None] / n * math.tau * 5 + fbm(n, n, n / 4, seed + 9, 3) * 6)
    shade = 0.8 + 0.3 * base + 0.08 * (bands - 0.5)
    shade = np.where(pebble, shade * (0.85 + 0.4 * rand_per(idx, seed + 1)), shade)
    height = 0.6 * base + np.where(pebble, 0.4 * (1 - f1 / 3.2), 0)
    return shade, height


def p_tilled(n, seed):
    base = fbm(n, n, n / 6, seed, 4)
    f1, f2, idx = voronoi(n, n, 220, seed + 5)
    clod = np.clip(1 - f1 / 7, 0, 1)
    shade = 0.78 + 0.22 * base + 0.18 * clod + 0.1 * (rand_per(idx, seed) - 0.5)
    height = 0.5 * clod + 0.5 * base
    return shade, height


def p_planks(n, seed):
    board = max(10, n // 11)
    rows = np.arange(n) // board
    within = (np.arange(n) % board) / board
    r = np.random.default_rng(seed)
    per_row = r.random(rows.max() + 2)
    # board ends staggered per row
    ends = np.zeros((n, n), dtype=bool)
    xs = np.arange(n)
    for row in range(rows.max() + 1):
        length = int(n * (0.45 + 0.4 * r.random()))
        start = int(r.random() * length)
        ys = slice(row * board, min(n, (row + 1) * board))
        ends[ys, :] = ((xs - start) % length) < 2
    grain = fbm(n, n, 2.0, seed + 3, 3, aspect=24)
    rings = 0.5 + 0.5 * np.sin((np.arange(n)[None, :] / 9.0) + fbm(n, n, 12, seed + 5, 2, aspect=6) * 9)
    gap = (within[:, None] < 0.08) | ends
    shade = 0.8 + 0.22 * per_row[rows][:, None] + 0.16 * (grain - 0.5) + 0.06 * (rings - 0.5)
    shade = shade * (0.92 + 0.08 * np.sin(within * math.pi))[:, None]
    shade = np.where(gap, shade * 0.55, shade)
    height = np.where(gap, 0.0, 0.7 + 0.2 * np.sin(within * math.pi)[:, None] + 0.1 * grain)
    return shade, height


def p_shingles(n, seed):
    row_h = max(8, n // 12)
    tile_w = int(row_h * 1.1)
    y = np.arange(n)
    row = y // row_h
    t = (y % row_h) / row_h          # 0 at the top of a row, 1 at its lower (overlapping) edge
    x = np.arange(n)[None, :] + (row[:, None] % 2) * (tile_w // 2)
    col = x // tile_w
    tx = (x % tile_w) / tile_w
    idx = ((row[:, None] * 97 + col) % 4096).astype(int)
    var = rand_per(idx, seed)
    # scalloped lower edge: each tile's bottom is a half circle
    edge = 0.62 + 0.38 * np.sqrt(np.clip(1 - ((tx - 0.5) / 0.5) ** 2, 0, 1))
    below = t[:, None] > edge                  # past this tile's rounded edge: the row below shows
    side = (tx < 0.04) | (tx > 0.96)
    shade = 0.8 + 0.22 * var + 0.22 * (t[:, None] - 0.3) * (~below) + 0.06 * (fbm(n, n, 4, seed + 3, 3) - 0.5)
    shade = np.where(below, 0.62 + 0.1 * var, shade)
    shade = np.where(side & ~below, shade * 0.7, shade)
    rim = (~below) & (t[:, None] > edge - 0.08)
    shade = np.where(rim, shade * 1.08, shade)
    height = np.where(below, 0.15, np.where(side, 0.3, 0.4 + 0.6 * t[:, None]))
    return shade, height


def p_bricks(n, seed):
    bh = max(8, n // 18)
    bw = bh * 2
    y = np.arange(n)
    row = y // bh
    ty = (y % bh) / bh
    x = np.arange(n)[None, :] + (row[:, None] % 2) * (bw // 2)
    col = x // bw
    tx = (x % bw) / bw
    var = rand_per(((row[:, None] * 131 + col) % 4096).astype(int), seed)
    mortar = (ty[:, None] < 0.14) | (tx < 0.07)
    shade = 0.78 + 0.3 * var + 0.12 * (fbm(n, n, 4, seed + 1, 3) - 0.5)
    shade = np.where(mortar, 1.25, shade)
    height = np.where(mortar, 0.1, 0.8 + 0.2 * fbm(n, n, 6, seed + 2, 2))
    return shade, height


def p_plaster(n, seed):
    soft = fbm(n, n, n / 4, seed, 5)
    fine = fbm(n, n, 3, seed + 5, 2)
    shade = 0.93 + 0.1 * soft + 0.05 * (fine - 0.5)
    height = 0.6 * soft + 0.4 * fine
    return shade, height


def p_cobble(n, seed):
    f1, f2, idx = voronoi(n, n, 46, seed)
    edge = f2 - f1
    dome = np.clip(edge / 9, 0, 1)
    var = rand_per(idx, seed + 1)
    shade = 0.74 + 0.3 * var + 0.14 * dome + 0.08 * (fbm(n, n, 4, seed + 2, 3) - 0.5)
    shade = np.where(edge < 2.2, shade * 0.6, shade)
    height = np.where(edge < 2.2, 0.0, np.sqrt(dome))
    return shade, height


def p_rock(n, seed):
    base = fbm(n, n, n / 3, seed, 5)
    fine = fbm(n, n, 5, seed + 3, 3)
    f1, f2, idx = voronoi(n, n, 14, seed + 4)
    facets = rand_per(idx, seed + 2)
    seam = (f2 - f1) < 1.5
    shade = 0.82 + 0.22 * base + 0.1 * (facets - 0.5) + 0.08 * (fine - 0.5)
    shade = np.where(seam, shade * 0.8, shade)
    height = 0.5 * base + 0.3 * facets + 0.2 * fine - 0.2 * seam
    return shade, height


def p_straw(n, seed):
    a = vnoise(n, n, 1.4, 14, seed)
    b = vnoise(n, n, 1.4, 18, seed + 1)
    # strands in two directions, lighter on top
    yy, xx = np.mgrid[0:n, 0:n]
    c = vnoise(n, n, 10, 1.5, seed + 2)
    shade = 0.78 + 0.22 * np.maximum(a, b) + 0.1 * (c - 0.5) + 0.06 * (fbm(n, n, n / 4, seed + 3, 3) - 0.5)
    height = np.maximum(a, b)
    return shade, height


def p_metal(n, seed):
    brushed = vnoise(n, n, 0.8, 50, seed)
    blotch = fbm(n, n, n / 3, seed + 3, 4)
    shade = 0.86 + 0.08 * (brushed - 0.5) + 0.16 * (blotch - 0.5)
    height = 0.5 + 0.1 * brushed
    return shade, height


def p_foliage(n, seed):
    """Clusters of small leaves, lit from above: a soft bump per leaf, darker hollows between clusters."""
    f1, f2, idx = voronoi(n, n, 260, seed)
    leaf = np.clip(1 - f1 / 9, 0, 1) ** 0.7
    var = rand_per(idx, seed + 1)
    clusters = fbm(n, n, n / 5, seed + 2, 3)
    shade = 0.68 + 0.3 * leaf + 0.14 * (var - 0.5) + 0.24 * (clusters - 0.5)
    height = 0.6 * leaf + 0.4 * clusters
    return shade, height


def p_bark(n, seed):
    ridges = vnoise(n, n, 22, 2.2, seed)
    fine = fbm(n, n, 4, seed + 2, 3, aspect=0.4)
    shade = 0.74 + 0.3 * ridges + 0.1 * (fine - 0.5)
    shade = np.where(ridges < 0.18, shade * 0.6, shade)
    height = ridges * 0.8 + 0.2 * fine
    return shade, height


def p_water(n, seed):
    ripple = fbm(n, n, n / 6, seed, 4, aspect=1.6)
    shade = 0.9 + 0.2 * ripple
    height = ripple * 0.3
    return shade, height


def p_canvas(n, seed):
    yy, xx = np.mgrid[0:n, 0:n]
    weave = (np.sin(xx * 1.4) * np.sin(yy * 1.4)) * 0.5 + 0.5
    shade = 0.9 + 0.08 * weave + 0.06 * (fbm(n, n, n / 4, seed, 3) - 0.5)
    return shade, weave * 0.5


def p_fur(n, seed):
    soft = fbm(n, n, n / 3, seed, 4)
    strands = vnoise(n, n, 6, 1.2, seed + 3)
    shade = 0.95 + 0.06 * (soft - 0.5) + 0.06 * (strands - 0.5)
    return shade, 0.4 * strands + 0.6 * soft


PATTERNS = {
    "grass": p_grass, "soil": p_soil, "tilled": p_tilled, "planks": p_planks, "shingles": p_shingles,
    "bricks": p_bricks, "plaster": p_plaster, "cobble": p_cobble, "rock": p_rock, "straw": p_straw,
    "metal": p_metal, "foliage": p_foliage, "bark": p_bark, "water": p_water, "canvas": p_canvas, "fur": p_fur,
}


# ---- atlas ----------------------------------------------------------------------------------------

def _tint(rgb, shade):
    """Colour a shade map: darker shades lean warmer and more saturated, as painted shadows do."""
    rgb = np.array(rgb)[None, None, :]
    s = shade[:, :, None]
    lum = rgb.mean()
    sat = np.clip(1.0 + (1.0 - s) * 0.5, 0.8, 1.4)
    col = lum + (rgb - lum) * sat
    return np.clip(col * s, 0, 1)


def _normals(height, strength):
    gy, gx = np.gradient(height)
    nx, ny = -gx * strength, gy * strength
    nz = np.ones_like(height)
    length = np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx / length, ny / length, nz / length], axis=-1) * 0.5 + 0.5


STRENGTH = {"grass": 5, "soil": 6, "tilled": 8, "planks": 10, "shingles": 12, "bricks": 10, "plaster": 3,
            "cobble": 10, "rock": 8, "straw": 6, "metal": 2, "foliage": 7, "bark": 9, "water": 3, "canvas": 4, "fur": 3}


def build_atlas(palette):
    """palette: list of (name, rgb). Returns (albedo RGBA, normal RGB) uint8 arrays and the swatch rects."""
    names = [n for n, _ in palette]
    rects = layout(names)
    albedo = np.zeros((ATLAS, ATLAS, 4))
    normal = np.zeros((ATLAS, ATLAS, 3))
    normal[:] = (0.5, 0.5, 1.0)
    albedo[:, :, 3] = 0.12
    colours = dict(palette)
    for i, name in enumerate(names):
        if name not in rects:
            continue
        pattern, cells, metres, smooth = SURFACES[name]
        cx, cy, w, h = rects[name]
        n = CELL * w
        shade, height = PATTERNS[pattern](n, 1000 + i * 17)
        ys, xs = slice(cy * CELL, cy * CELL + n), slice(cx * CELL, cx * CELL + n)
        albedo[ys, xs, :3] = _tint(colours[name], shade)
        albedo[ys, xs, 3] = smooth
        normal[ys, xs] = _normals(height, STRENGTH[pattern])
    # the flat palette
    cx, cy = PALETTE_CELL
    step = CELL // PALETTE_CELLS
    for i, (name, rgb) in enumerate(palette):
        px, py = i % PALETTE_CELLS, i // PALETTE_CELLS
        y0, x0 = cy * CELL + py * step, cx * CELL + px * step
        albedo[y0:y0 + step, x0:x0 + step, :3] = rgb
        albedo[y0:y0 + step, x0:x0 + step, 3] = 0.6 if name in ("glass", "window_glow", "eye", "egg", "milk", "oil", "jam") else 0.14
    # linear-ish values were authored as sRGB colours already (the palette is sRGB)
    a8 = (np.clip(albedo, 0, 1) * 255 + 0.5).astype(np.uint8)
    n8 = (np.clip(normal, 0, 1) * 255 + 0.5).astype(np.uint8)
    return a8, n8, rects


def write_png(path, pixels):
    """A plain PNG writer (8-bit RGB or RGBA), rows top to bottom."""
    h, w, c = pixels.shape
    kind = {3: 2, 4: 6}[c]
    raw = b"".join(b"\x00" + pixels[y].tobytes() for y in range(h))

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, kind, 0, 0, 0)))
        f.write(chunk(b"IDAT", zlib.compress(raw, 6)))
        f.write(chunk(b"IEND", b""))
