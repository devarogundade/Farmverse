"""Farmverse model kit for Blender (bpy as a module).

Every model is built from simple solids into bmesh "parts", each face carrying a
palette colour. Faces are UV-mapped onto the centre of their colour's cell in one
shared palette texture, so the whole farm renders with a single material in Unity.
Objects are named after the stages the game's StagedVisual looks for ("Seeded",
"Sprout", "Young", "Grown", "Ready", "Idle", "Working", "Broken", "Product"),
spinning parts start with "Spin", and empties called "Chimney" mark smoke.
"""
import math
import random

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

import fvtex

PALETTE = [
    # ground
    ("grass", (0.46, 0.70, 0.27)), ("grass_dark", (0.33, 0.56, 0.20)), ("grass_light", (0.62, 0.80, 0.35)),
    ("soil", (0.47, 0.31, 0.19)), ("soil_dark", (0.34, 0.21, 0.12)), ("soil_dry", (0.66, 0.50, 0.32)),
    ("path", (0.80, 0.68, 0.48)), ("sand", (0.90, 0.82, 0.60)), ("stone", (0.66, 0.66, 0.64)),
    ("stone_dark", (0.45, 0.45, 0.46)), ("rock", (0.55, 0.53, 0.50)),
    # wood and building
    ("wood", (0.60, 0.40, 0.22)), ("wood_dark", (0.40, 0.25, 0.13)), ("wood_light", (0.80, 0.62, 0.40)),
    ("plank", (0.72, 0.52, 0.30)), ("roof_red", (0.78, 0.25, 0.20)), ("roof_blue", (0.27, 0.45, 0.70)),
    ("roof_brown", (0.50, 0.30, 0.18)), ("roof_green", (0.30, 0.55, 0.35)), ("white", (0.96, 0.95, 0.92)),
    ("cream", (0.95, 0.88, 0.72)), ("brick", (0.72, 0.36, 0.25)), ("copper", (0.80, 0.48, 0.25)),
    ("metal", (0.62, 0.65, 0.70)), ("metal_dark", (0.32, 0.34, 0.38)), ("dark", (0.18, 0.16, 0.15)),
    ("black", (0.08, 0.08, 0.08)), ("glass", (0.55, 0.78, 0.90)), ("water", (0.30, 0.60, 0.88)),
    ("hay", (0.92, 0.78, 0.38)), ("rope", (0.78, 0.66, 0.44)), ("canvas", (0.92, 0.88, 0.78)),
    # plants
    ("leaf", (0.40, 0.78, 0.20)), ("leaf_dark", (0.20, 0.56, 0.14)), ("leaf_light", (0.60, 0.88, 0.28)),
    ("sprout", (0.55, 0.90, 0.30)), ("wheat", (1.00, 0.78, 0.26)), ("wheat_dark", (0.90, 0.58, 0.14)),
    ("maize", (1.00, 0.82, 0.14)), ("husk", (0.62, 0.84, 0.30)), ("tomato", (0.95, 0.18, 0.10)),
    ("onion", (0.94, 0.58, 0.20)), ("onion_light", (1.00, 0.84, 0.56)), ("potato", (0.82, 0.54, 0.24)),
    ("bean", (0.32, 0.74, 0.18)), ("sun_petal", (1.00, 0.78, 0.06)), ("sun_seed", (0.50, 0.26, 0.08)),
    ("pine", (0.14, 0.50, 0.30)), ("pine_light", (0.22, 0.64, 0.38)), ("oak", (0.36, 0.70, 0.18)),
    ("oak_light", (0.52, 0.84, 0.24)), ("bark", (0.55, 0.32, 0.16)),
    ("flower_pink", (0.95, 0.52, 0.70)), ("flower_blue", (0.48, 0.62, 0.95)), ("flower_white", (0.98, 0.97, 0.92)),
    ("flower_yellow", (0.99, 0.88, 0.30)), ("berry", (0.75, 0.15, 0.30)),
    # animals and people
    ("hen", (1.00, 0.96, 0.86)), ("comb", (0.92, 0.18, 0.12)), ("beak", (1.00, 0.62, 0.12)),
    ("cow", (0.97, 0.96, 0.94)), ("cow_patch", (0.15, 0.14, 0.14)), ("cow_nose", (0.95, 0.70, 0.70)),
    ("horn", (0.92, 0.88, 0.76)), ("egg", (1.00, 0.95, 0.84)), ("milk", (0.99, 0.99, 0.99)),
    ("skin", (0.96, 0.76, 0.60)), ("skin_dark", (0.62, 0.42, 0.28)), ("shirt_blue", (0.30, 0.48, 0.80)),
    ("shirt_red", (0.82, 0.30, 0.28)), ("shirt_green", (0.38, 0.62, 0.36)), ("pants", (0.30, 0.32, 0.45)),
    ("straw_hat", (0.94, 0.80, 0.45)), ("hair", (0.32, 0.20, 0.12)), ("boot", (0.28, 0.18, 0.10)),
    ("compost", (0.30, 0.22, 0.12)), ("jam", (0.62, 0.10, 0.22)), ("oil", (0.92, 0.80, 0.25)),
    ("bread", (0.86, 0.60, 0.30)), ("cheese", (0.98, 0.82, 0.30)), ("flour", (0.97, 0.95, 0.90)),
    ("feed", (0.80, 0.66, 0.36)), ("sign", (0.86, 0.70, 0.45)), ("red_flag", (0.90, 0.20, 0.18)),
    # the painted look (UI icons): chunky land, gold trims, saturated roofs and wood
    ("turf", (0.50, 0.80, 0.16)), ("turf_light", (0.66, 0.88, 0.26)), ("turf_dark", (0.36, 0.64, 0.12)),
    ("turf_pale", (0.70, 0.80, 0.42)), ("turf_wild", (0.32, 0.55, 0.20)),
    ("dirt", (0.64, 0.36, 0.16)), ("dirt_dark", (0.45, 0.24, 0.10)), ("tilled", (0.36, 0.20, 0.10)), ("tilled_light", (0.50, 0.30, 0.15)),
    ("gold", (0.98, 0.70, 0.14)), ("gold_dark", (0.86, 0.50, 0.08)), ("straw", (0.99, 0.80, 0.30)),
    ("fence_red", (0.90, 0.32, 0.12)), ("fence_red_dark", (0.70, 0.20, 0.08)),
    ("orange_wood", (0.84, 0.54, 0.26)), ("orange_wood_dark", (0.60, 0.36, 0.16)),
    ("roof_sky", (0.16, 0.55, 0.92)), ("roof_sky_dark", (0.10, 0.38, 0.72)), ("barn_red", (0.84, 0.20, 0.12)), ("barn_red_dark", (0.62, 0.13, 0.08)),
    ("wall_cream", (0.99, 0.90, 0.72)), ("trim", (0.99, 0.95, 0.86)), ("window_glow", (1.00, 0.86, 0.40)), ("door_brown", (0.62, 0.30, 0.14)),
    ("chimney_orange", (0.92, 0.42, 0.18)), ("iron", (0.40, 0.42, 0.46)), ("iron_light", (0.58, 0.60, 0.64)),
    ("hen_cream", (1.00, 0.96, 0.86)), ("cow_cream", (0.99, 0.94, 0.86)), ("muzzle", (0.98, 0.62, 0.40)), ("horn_orange", (0.98, 0.66, 0.22)),
    ("eye", (0.10, 0.07, 0.06)), ("leaf_bright", (0.40, 0.80, 0.20)), ("leaf_deep", (0.18, 0.55, 0.16)),
    ("tomato_red", (0.95, 0.16, 0.10)), ("maize_gold", (1.00, 0.80, 0.12)), ("onion_orange", (0.94, 0.60, 0.20)), ("potato_brown", (0.78, 0.50, 0.22)),
    ("wheat_gold", (1.00, 0.76, 0.22)), ("sun_yellow", (1.00, 0.78, 0.06)), ("bean_green", (0.30, 0.72, 0.18)),
    ("canopy", (0.36, 0.72, 0.18)), ("canopy_light", (0.52, 0.84, 0.24)), ("pine_deep", (0.14, 0.50, 0.30)), ("pine_mid", (0.20, 0.62, 0.36)),
    ("trunk", (0.55, 0.32, 0.16)), ("pebble", (0.72, 0.72, 0.68)), ("pebble_dark", (0.54, 0.54, 0.52)), ("pond", (0.26, 0.66, 0.92)),
    # the world beyond the farm: town walls, concrete, boats, awnings
    ("wall_blue", (0.64, 0.80, 0.94)), ("wall_pink", (0.97, 0.76, 0.74)), ("wall_mint", (0.72, 0.90, 0.78)), ("wall_yellow", (0.99, 0.87, 0.54)),
    ("concrete", (0.78, 0.78, 0.76)), ("hull_blue", (0.22, 0.44, 0.72)), ("hull_red", (0.80, 0.22, 0.18)), ("hull_white", (0.95, 0.95, 0.93)),
    ("awning_red", (0.90, 0.24, 0.22)), ("awning_green", (0.30, 0.66, 0.36)), ("awning_blue", (0.26, 0.52, 0.86)), ("roof_slate", (0.40, 0.44, 0.52)),
    ("glass_day", (0.62, 0.82, 0.94)), ("metal_roof", (0.70, 0.74, 0.78)), ("tarmac_line", (0.98, 0.96, 0.90)), ("lamp_glass", (1.0, 0.95, 0.72)),
]
INDEX = {name: i for i, (name, _) in enumerate(PALETTE)}
CELLS = 16  # palette texture is 16 x 16 cells
# the material atlas: textured swatches for surfaces, flat palette cells for the rest
RECTS = fvtex.layout([n for n, _ in PALETTE])
ATLAS_PATH = None  # set by build_all once the atlas is written, for previews


def palette_uv(name):
    return Vector(fvtex.palette_cell_uv(INDEX[name]))


def surface_uvs(name, points, normal):
    """UVs of one face on its colour's swatch: planar in the face's own frame (u along the
    face horizontally, v up its slope; top faces use world X/Y), at the swatch's scale."""
    rect = RECTS[name]
    metres = fvtex.SURFACES[name][2]
    n = normal
    if abs(n.z) > 0.7:
        u = Vector((1, 0, 0)) if n.z > 0 else Vector((-1, 0, 0))
        v = Vector((0, 1, 0))
    else:
        u = Vector((-n.y, n.x, 0)).normalized()
        v = n.cross(u).normalized()
    cs = [(p.dot(u) / metres, p.dot(v) / metres) for p in points]
    out = []
    lo = [min(c[i] for c in cs) for i in (0, 1)]
    hi = [max(c[i] for c in cs) for i in (0, 1)]
    shift, scale = [0.0, 0.0], [1.0, 1.0]
    for i in (0, 1):
        extent = hi[i] - lo[i]
        if extent > 1.0:
            shift[i], scale[i] = lo[i], 1.0 / extent      # too big for the swatch: squeeze it in
        elif lo[i] - math.floor(lo[i]) + extent <= 1.0:
            shift[i] = math.floor(lo[i])                  # keep the world-aligned pattern
        else:
            shift[i] = lo[i]
    for c in cs:
        out.append(Vector(fvtex.swatch_uv(rect, (c[0] - shift[0]) * scale[0], (c[1] - shift[1]) * scale[1])))
    return out


def write_atlas(folder):
    """Generate the material atlas (fv_atlas.png: colour + smoothness in alpha; fv_atlas_n.png: normals)."""
    global ATLAS_PATH
    albedo, normal, _ = fvtex.build_atlas(PALETTE)
    fvtex.write_png(folder + "/fv_atlas.png", albedo)
    fvtex.write_png(folder + "/fv_atlas_n.png", normal)
    ATLAS_PATH = folder + "/fv_atlas.png"


def atlas_image():
    img = bpy.data.images.get("fv_atlas")
    if img is None and ATLAS_PATH:
        img = bpy.data.images.load(ATLAS_PATH)
        img.name = "fv_atlas"
        img.alpha_mode = "CHANNEL_PACKED"  # alpha is smoothness, not coverage
    return img


def palette_material():
    """The one material: the atlas colour, smoothness from its alpha, and the normal atlas (previews)."""
    mat = bpy.data.materials.get("fv_palette")
    if mat is not None:
        return mat
    mat = bpy.data.materials.new("fv_palette")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    img = atlas_image()
    if img is not None:
        tex = nodes.new("ShaderNodeTexImage")
        tex.image = img
        links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        inv = nodes.new("ShaderNodeMath")
        inv.operation = "SUBTRACT"
        inv.inputs[0].default_value = 1.0
        links.new(tex.outputs["Alpha"], inv.inputs[1])
        links.new(inv.outputs[0], bsdf.inputs["Roughness"])
        npath = ATLAS_PATH.replace("fv_atlas.png", "fv_atlas_n.png")
        nimg = bpy.data.images.get("fv_atlas_n") or bpy.data.images.load(npath)
        nimg.name = "fv_atlas_n"
        nimg.colorspace_settings.name = "Non-Color"
        ntex = nodes.new("ShaderNodeTexImage")
        ntex.image = nimg
        nmap = nodes.new("ShaderNodeNormalMap")
        links.new(ntex.outputs["Color"], nmap.inputs["Color"])
        links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
        nodes.active = tex
    return mat


def matrix(loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    if not isinstance(scale, (tuple, list)):
        scale = (scale, scale, scale)
    return Matrix.Translation(Vector(loc)) @ Euler([math.radians(a) for a in rot]).to_matrix().to_4x4() @ Matrix.Diagonal((*scale, 1))


class Part:
    """One object being built: solids added in palette colours, then baked to a mesh.

    Faces are shaded smooth with edges sharper than ``sharp`` degrees kept hard, so
    balls, cylinders and bevelled corners read soft (the painted look) while flat
    faces stay crisp."""

    def __init__(self, name, sharp=42.0):
        self.name = name
        self.sharp = sharp
        self.bm = bmesh.new()
        # bookkeeping in our own layers: bmesh operators (bevel, create_*) overwrite element tags
        self.placed = self.bm.verts.layers.int.new("fv_placed")
        self.coloured = self.bm.faces.layers.int.new("fv_coloured")

    def _fresh(self):
        """Vertices added since the last solid was placed."""
        return [v for v in self.bm.verts if not v[self.placed]]

    def _add(self, result, colour, m):
        verts = result.get("verts") or list({v for f in result.get("faces", []) for v in f.verts})
        bmesh.ops.transform(self.bm, matrix=m, verts=verts)
        for v in verts:
            v[self.placed] = 1
        faces = {f for v in verts for f in v.link_faces}
        mi = INDEX[colour]
        for f in faces:
            if not f[self.coloured]:
                f.material_index = mi
                f[self.coloured] = 1
        return verts

    def box(self, colour, size=(1, 1, 1), loc=(0, 0, 0), rot=(0, 0, 0)):
        r = bmesh.ops.create_cube(self.bm, size=1.0)
        return self._add(r, colour, matrix(loc, rot, size))

    def rbox(self, colour, size=(1, 1, 1), loc=(0, 0, 0), rot=(0, 0, 0), radius=0.06, seg=2):
        """A box with rounded edges: the chunky, toy-like block of the painted art."""
        res = bmesh.ops.create_cube(self.bm, size=1.0)
        verts = res["verts"]
        bmesh.ops.transform(self.bm, matrix=Matrix.Diagonal((*size, 1)), verts=verts)
        edges = list({e for v in verts for e in v.link_edges})
        radius = min(radius, min(size) * 0.45)
        bmesh.ops.bevel(self.bm, geom=verts + edges, offset=radius, segments=seg, profile=0.5, affect="EDGES", clamp_overlap=True)
        return self._add({"verts": self._fresh()}, colour, matrix(loc, rot))

    def rcyl(self, colour, r=0.5, h=1.0, seg=12, loc=(0, 0, 0), rot=(0, 0, 0), radius=0.05, bseg=2, r2=None):
        """A cylinder (or prism, with few segments) whose cap rims are rounded."""
        res = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=h)
        verts = res["verts"]
        rims = [e for e in {e for v in verts for e in v.link_edges} if len(e.link_faces) == 2 and abs(e.link_faces[0].normal.z - e.link_faces[1].normal.z) > 0.5]
        radius = min(radius, h * 0.45, r * 0.45, (r if r2 is None else r2) * 0.45 if (r2 is None or r2 > 0) else radius)
        bmesh.ops.bevel(self.bm, geom=rims, offset=radius, segments=bseg, profile=0.5, affect="EDGES", clamp_overlap=True)
        return self._add({"verts": self._fresh()}, colour, matrix(loc, rot))

    def chunk(self, colour, r=1.0, h=0.4, loc=(0, 0, 0), radius=0.08):
        """A rounded hex block, pointy along Y like the game's tiles (a six-sided cone already starts at +Y)."""
        return self.rcyl(colour, r=r, h=h, seg=6, loc=loc, rot=(0, 0, 0), radius=radius)

    def cyl(self, colour, r=0.5, h=1.0, seg=10, loc=(0, 0, 0), rot=(0, 0, 0), r2=None, scale=(1, 1, 1)):
        res = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=h)
        return self._add(res, colour, matrix(loc, rot, scale))

    def cone(self, colour, r=0.5, h=1.0, seg=8, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
        return self.cyl(colour, r=r, h=h, seg=seg, loc=loc, rot=rot, r2=0.0, scale=scale)

    def ball(self, colour, r=0.5, loc=(0, 0, 0), sub=1, scale=(1, 1, 1), rot=(0, 0, 0)):
        res = bmesh.ops.create_icosphere(self.bm, subdivisions=sub, radius=r)
        return self._add(res, colour, matrix(loc, rot, scale))

    def prism(self, colour, outline, depth, loc=(0, 0, 0), rot=(0, 0, 0)):
        """A flat polygon given as (x, z) points, extruded along Y by depth (gable ends, wedges)."""
        verts = [self.bm.verts.new((x, -depth / 2, z)) for x, z in outline]
        back = [self.bm.verts.new((x, depth / 2, z)) for x, z in outline]
        faces = [self.bm.faces.new(verts[::-1]), self.bm.faces.new(back)]
        n = len(outline)
        for i in range(n):
            j = (i + 1) % n
            faces.append(self.bm.faces.new((verts[i], verts[j], back[j], back[i])))
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        return self._add({"verts": verts + back}, colour, matrix(loc, rot))

    def add_mesh(self, mesh, colour_of):
        """Append a finished mesh (e.g. from organic()), colouring each new face by
        ``colour_of(centre, normal)`` -> palette name."""
        self.bm.from_mesh(mesh)
        fresh = [v for v in self.bm.verts if not v[self.placed]]
        for v in fresh:
            v[self.placed] = 1
        faces = {f for v in fresh for f in v.link_faces}
        self.bm.normal_update()
        for f in faces:
            if not f[self.coloured]:
                f.material_index = INDEX[colour_of(f.calc_center_median(), f.normal)]
                f[self.coloured] = 1
        return fresh

    def organic(self, elements, colour_of, voxel=0.03, ratio=1.0, smooth=6):
        """A soft, sculpted shape welded from ell()/cap() solids, coloured per face."""
        mesh = organic_mesh(elements, voxel, ratio, smooth)
        out = self.add_mesh(mesh, colour_of)
        bpy.data.meshes.remove(mesh)
        return out

    def leaf(self, colour, base, heading, length, width, rise=40.0, droop=60.0, fold=0.25, segs=6, thick=0.006):
        """A real leaf blade: tapered, folded along its midrib and arching from ``rise`` degrees
        above horizontal at the base to ``droop`` degrees further down at the tip. Two-sided
        (a thin closed sheet) so it reads from every angle. heading: degrees around Z."""
        h = math.radians(heading)
        fwd = Vector((math.cos(h), math.sin(h), 0))
        side = Vector((-math.sin(h), math.cos(h), 0))
        pos = Vector(base)
        rows = []
        step = length / segs
        for i in range(segs + 1):
            t = i / segs
            w = width * math.sin(math.pi * min(1.0, t * 0.92 + 0.08)) * (1 - 0.35 * t)
            pitch = math.radians(rise - droop * t * t)
            if i:
                pos = pos + (fwd * math.cos(pitch) + Vector((0, 0, math.sin(pitch)))) * step
            up = Vector((0, 0, 1)) * math.cos(pitch) - fwd * math.sin(pitch)
            mid = pos + up * (w * fold)
            rows.append((pos - side * w, mid, pos + side * w, up))
        top, bot = [], []
        for l, m, r_, up in rows:
            top.append([self.bm.verts.new(v + up * thick) for v in (l, m, r_)])
            bot.append([self.bm.verts.new(v - up * thick) for v in (l, m, r_)])
        faces = []
        for i in range(segs):
            for j in range(2):
                faces.append(self.bm.faces.new((top[i][j], top[i][j + 1], top[i + 1][j + 1], top[i + 1][j])))
                faces.append(self.bm.faces.new((bot[i][j], bot[i + 1][j], bot[i + 1][j + 1], bot[i][j + 1])))
            faces.append(self.bm.faces.new((top[i][0], top[i + 1][0], bot[i + 1][0], bot[i][0])))
            faces.append(self.bm.faces.new((top[i][2], bot[i][2], bot[i + 1][2], top[i + 1][2])))
        verts = [v for row in top + bot for v in row]
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        return self._add({"verts": verts}, colour, Matrix.Identity(4))

    def blade(self, colour, base, heading, length, width, rise=70.0, droop=60.0):
        """A cheap grass blade: a slim three-sided spike bent once, 7 faces."""
        h = math.radians(heading)
        fwd = Vector((math.cos(h), math.sin(h), 0))
        side = Vector((-math.sin(h), math.cos(h), 0))
        b = Vector(base)
        p0 = math.radians(rise)
        p1 = math.radians(rise - droop)
        mid = b + (fwd * math.cos(p0) + Vector((0, 0, math.sin(p0)))) * length * 0.55
        tip = mid + (fwd * math.cos(p1) + Vector((0, 0, math.sin(p1)))) * length * 0.45
        ring0 = [b + side * width, b - side * width, b + fwd * width * 0.6]
        ring1 = [mid + side * width * 0.6, mid - side * width * 0.6, mid + fwd * width * 0.35]
        v0 = [self.bm.verts.new(v) for v in ring0]
        v1 = [self.bm.verts.new(v) for v in ring1]
        vt = self.bm.verts.new(tip)
        faces = [self.bm.faces.new((v0[2], v0[1], v0[0]))]
        for i in range(3):
            j = (i + 1) % 3
            faces.append(self.bm.faces.new((v0[i], v0[j], v1[j], v1[i])))
            faces.append(self.bm.faces.new((v1[i], v1[j], vt)))
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        return self._add({"verts": v0 + v1 + [vt]}, colour, Matrix.Identity(4))

    def hexagon(self, colour, r=1.0, h=0.1, loc=(0, 0, 0)):
        # pointy along Y (and so along Unity Z), matching the game's pointy-top tiles
        return self.cyl(colour, r=r, h=h, seg=6, loc=loc)

    def build(self, collection, parent=None, origin=(0, 0, 0)):
        self.bm.verts.layers.int.remove(self.placed)
        self.bm.faces.layers.int.remove(self.coloured)
        mesh = bpy.data.meshes.new(self.name)
        self.bm.normal_update()
        self.bm.to_mesh(mesh)
        self.bm.free()
        uv = mesh.uv_layers.new(name="UVMap")
        for poly in mesh.polygons:
            name = PALETTE[poly.material_index][0]
            if name in RECTS:
                pts = [mesh.vertices[mesh.loops[li].vertex_index].co for li in poly.loop_indices]
                for li, q in zip(poly.loop_indices, surface_uvs(name, pts, poly.normal)):
                    uv.data[li].uv = q
            else:
                p = palette_uv(name)
                for li in poly.loop_indices:
                    uv.data[li].uv = p
            poly.use_smooth = True
            poly.material_index = 0
        # soft where the solids are round, hard where faces meet at a corner
        mesh.set_sharp_from_angle(angle=math.radians(self.sharp))
        # one material for everything: the colour is in the UVs, so Unity draws each mesh in one call
        mesh.materials.append(palette_material())
        obj = bpy.data.objects.new(self.name, mesh)
        collection.objects.link(obj)
        if parent is not None:
            obj.parent = parent
        if origin != (0, 0, 0):
            mesh.transform(Matrix.Translation(-Vector(origin)))
            obj.location = Vector(origin)
        return obj


def ell(center, radii, rot=(0, 0, 0)):
    """An ellipsoid for organic(): centre, half axes (x, y, z), rotation in degrees."""
    return ("ell", center, radii, rot)


def cap(a, b, ra, rb=None):
    """A capsule / tapered limb for organic(): from point a (radius ra) to b (radius rb)."""
    return ("cap", a, b, ra, ra if rb is None else rb)


def organic_mesh(elements, voxel=0.03, ratio=1.0, smooth=6):
    """Weld simple solids into one soft surface: voxel remesh (the solids melt together at
    their overlaps), smoothing (rounded joins, like sculpted clay), then decimation.
    The caller owns (and removes) the returned mesh."""
    bm = bmesh.new()
    for e in elements:
        if e[0] == "ell":
            _, c, radii, rot = e
            res = bmesh.ops.create_icosphere(bm, subdivisions=3, radius=1.0)
            bmesh.ops.transform(bm, matrix=matrix(c, rot, radii), verts=res["verts"])
        else:
            _, a, b, ra, rb = e
            a, b = Vector(a), Vector(b)
            axis = b - a
            length = max(axis.length, 1e-4)
            q = axis.normalized().to_track_quat("Z", "Y")
            m = Matrix.Translation(a) @ q.to_matrix().to_4x4()
            res = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=16, radius1=ra, radius2=rb, depth=length)
            bmesh.ops.transform(bm, matrix=m @ Matrix.Translation((0, 0, length / 2)), verts=res["verts"])
            for pt, r in ((a, ra), (b, rb)):
                sp = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=r)
                bmesh.ops.translate(bm, vec=pt, verts=sp["verts"])
    mesh = bpy.data.meshes.new("fvorganic")
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new("fvorganic", mesh)
    bpy.context.scene.collection.objects.link(obj)
    rm = obj.modifiers.new("remesh", "REMESH")
    rm.mode = "VOXEL"
    rm.voxel_size = voxel
    if smooth:
        sm = obj.modifiers.new("smooth", "LAPLACIANSMOOTH")
        sm.iterations = smooth
        sm.lambda_factor = 0.6
        sm.use_volume_preserve = True
    if ratio < 1.0:
        dec = obj.modifiers.new("dec", "DECIMATE")
        dec.ratio = ratio
    dg = bpy.context.evaluated_depsgraph_get()
    out = bpy.data.meshes.new_from_object(obj.evaluated_get(dg))
    bpy.data.objects.remove(obj)
    bpy.data.meshes.remove(mesh)
    return out


def noise3(p, scale=1.0, seed=0):
    """Smooth 3D noise in -1..1 (for patches, mottling)."""
    from mathutils import noise
    return noise.noise(Vector(p) * scale + Vector((seed * 13.1, seed * 7.7, seed * 3.3)))


def empty(name, collection, loc=(0, 0, 0), parent=None):
    e = bpy.data.objects.new(name, None)
    e.location = loc
    collection.objects.link(e)
    if parent is not None:
        e.parent = parent
    return e


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def new_collection(name):
    col = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(col)
    return col


def export_fbx(path, collection):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in collection.all_objects:
        obj.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True, object_types={"MESH", "EMPTY"},
        use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False, path_mode="AUTO",
    )


def rng(seed):
    return random.Random(seed)
