"""Seamless (tiling) ground textures for the world around the farm, generated from scratch.

Each texture is an albedo PNG (smoothness in alpha) and a normal PNG that repeat without
seams; meshes carry UVs in metres and each material scales them by its tile size."""
import math

import numpy as np

from fvtex import write_png


def pnoise(n, cells, seed):
    """Periodic value noise: a cells x cells lattice that wraps, sampled at n x n."""
    r = np.random.default_rng(seed)
    g = r.random((cells, cells))
    t = np.arange(n) * cells / n
    i0 = t.astype(int)
    f = t - i0
    f = f * f * (3 - 2 * f)
    i1 = (i0 + 1) % cells
    a = g[i0][:, i0]
    b = g[i0][:, i1]
    c = g[i1][:, i0]
    d = g[i1][:, i1]
    fy, fx = f[:, None], f[None, :]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def pfbm(n, cells, seed, octaves=5):
    out = np.zeros((n, n))
    amp, norm = 1.0, 0.0
    for o in range(octaves):
        out += amp * pnoise(n, cells * (2 ** o), seed + o * 31)
        norm += amp
        amp *= 0.5
    return out / norm


def pvoronoi(n, count, seed):
    """Periodic F1/F2 cellular noise."""
    r = np.random.default_rng(seed)
    pts = r.random((count, 2)) * n
    yy, xx = np.mgrid[0:n, 0:n]
    f1 = np.full((n, n), 1e9)
    f2 = np.full((n, n), 1e9)
    idx = np.zeros((n, n), dtype=int)
    for i, (py, px) in enumerate(pts):
        dy = np.abs(yy - py)
        dx = np.abs(xx - px)
        dy = np.minimum(dy, n - dy)
        dx = np.minimum(dx, n - dx)
        d = np.hypot(dy, dx)
        closer = d < f1
        f2 = np.where(closer, f1, np.minimum(f2, d))
        idx = np.where(closer, i, idx)
        f1 = np.where(closer, d, f1)
    return f1, f2, idx


def strokes(n, count, length, seed, jitter=0.7):
    r = np.random.default_rng(seed)
    out = np.zeros((n, n))
    for _ in range(count):
        y, x = r.random() * n, r.random() * n
        a = -math.pi / 2 + (r.random() - 0.5) * 2 * jitter
        ln = int(length * (0.6 + 0.8 * r.random()))
        for k in range(ln):
            t = k / max(1, ln - 1)
            py = int(y + math.sin(a) * k) % n
            px = int(x + math.cos(a) * k) % n
            out[py, px] = max(out[py, px], 0.35 + 0.65 * t)
    return out


def tint(rgb, shade):
    rgb = np.array(rgb)[None, None, :]
    s = shade[:, :, None]
    lum = rgb.mean()
    sat = np.clip(1.0 + (1.0 - s) * 0.5, 0.8, 1.4)
    return np.clip((lum + (rgb - lum) * sat) * s, 0, 1)


def normals(h, strength):
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
    nx, ny = -gx * strength, gy * strength
    nz = np.ones_like(h)
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx / ln, ny / ln, nz / ln], -1) * 0.5 + 0.5


# ---- the textures ---------------------------------------------------------------------------------

def t_grass(n, seed):
    patches = pfbm(n, 3, seed)
    clumps = pfbm(n, 12, seed + 5, 3)
    blades = strokes(n, n * n // 26, n / 44, seed + 9)
    shade = (0.8 + 0.26 * patches + 0.12 * (clumps - 0.5)) * (0.9 + 0.2 * blades)
    rgb = tint((0.42, 0.72, 0.18), shade)
    # a warm dry tint in some patches
    dry = np.clip((pfbm(n, 2, seed + 13) - 0.55) * 4, 0, 1)[:, :, None]
    rgb = rgb * (1 - dry * 0.35) + tint((0.62, 0.72, 0.26), shade) * dry * 0.35
    return rgb, 0.06, 0.5 * blades + 0.3 * clumps + 0.2 * patches, 5


def t_sand(n, seed):
    base = pfbm(n, 4, seed)
    fine = pfbm(n, 48, seed + 3, 2)
    ripples = 0.5 + 0.5 * np.sin((np.arange(n)[:, None] / n * math.tau * 9) + pfbm(n, 3, seed + 7) * 5)
    shade = 0.9 + 0.1 * base + 0.06 * (fine - 0.5) + 0.04 * (ripples - 0.5)
    return tint((0.93, 0.83, 0.6), shade), 0.08, 0.5 * ripples + 0.5 * fine, 3


def t_rock(n, seed):
    f1, f2, idx = pvoronoi(n, 26, seed)
    r = np.random.default_rng(seed)
    facet = r.random(idx.max() + 1)[idx]
    base = pfbm(n, 4, seed + 2)
    seam = (f2 - f1) < 2.5
    strata = 0.5 + 0.5 * np.sin(np.arange(n)[:, None] / n * math.tau * 7 + base * 4)
    shade = 0.78 + 0.16 * facet + 0.12 * base + 0.06 * strata
    shade = np.where(seam, shade * 0.72, shade)
    return tint((0.62, 0.6, 0.56), shade), 0.12, 0.5 * facet + 0.3 * base - 0.3 * seam, 9


def t_asphalt(n, seed):
    base = pfbm(n, 6, seed)
    grit = np.random.default_rng(seed).random((n, n))
    shade = 0.92 + 0.08 * base + 0.08 * (grit - 0.5)
    return tint((0.36, 0.37, 0.4), shade), 0.18, 0.3 * grit + 0.2 * base, 2


def t_dirt(n, seed):
    base = pfbm(n, 5, seed)
    f1, f2, idx = pvoronoi(n, 160, seed + 3)
    r = np.random.default_rng(seed)
    pebble = (f1 < 3.0) & (r.random(idx.max() + 1)[idx] > 0.6)
    shade = 0.82 + 0.26 * base
    shade = np.where(pebble, shade * 1.15, shade)
    return tint((0.72, 0.56, 0.36), shade), 0.05, 0.6 * base + 0.4 * pebble, 5


def t_cobble(n, seed):
    f1, f2, idx = pvoronoi(n, 60, seed)
    r = np.random.default_rng(seed)
    var = r.random(idx.max() + 1)[idx]
    edge = f2 - f1
    dome = np.clip(edge / 10, 0, 1)
    shade = 0.78 + 0.22 * var + 0.12 * dome
    shade = np.where(edge < 2.5, 0.55, shade)
    return tint((0.78, 0.72, 0.64), shade), 0.15, np.where(edge < 2.5, 0, np.sqrt(dome)), 10


def t_water(n, seed):
    ripple = pfbm(n, 6, seed, 4)
    shade = 0.92 + 0.16 * ripple
    return tint((0.2, 0.62, 0.86), shade), 0.9, ripple, 6


def t_field(n, seed):
    rows = 0.5 + 0.5 * np.sin(np.arange(n)[:, None] / n * math.tau * 16)
    base = pfbm(n, 6, seed)
    shade = 0.7 + 0.3 * rows + 0.12 * (base - 0.5)
    return tint((0.5, 0.32, 0.18), shade), 0.05, rows * 0.8 + 0.2 * base, 8


TEXTURES = {
    "grass": (t_grass, 4.0), "sand": (t_sand, 4.0), "rock": (t_rock, 6.0), "asphalt": (t_asphalt, 4.0),
    "dirt": (t_dirt, 3.0), "cobble": (t_cobble, 3.0), "water": (t_water, 10.0), "field": (t_field, 4.0),
}


def write_all(folder, n=1024):
    """Write fv_tile_<name>.png and fv_tile_<name>_n.png for every ground texture."""
    out = {}
    for i, (name, (fn, metres)) in enumerate(TEXTURES.items()):
        rgb, smooth, height, strength = fn(n, 500 + i * 7)
        rgba = np.concatenate([rgb, np.full((n, n, 1), smooth)], -1)
        write_png(f"{folder}/fv_tile_{name}.png", (np.clip(rgba, 0, 1) * 255 + 0.5).astype(np.uint8))
        write_png(f"{folder}/fv_tile_{name}_n.png", (np.clip(normals(height, strength), 0, 1) * 255 + 0.5).astype(np.uint8))
        out[name] = metres
    return out
