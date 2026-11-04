"""The island: a heightfield shaped by world_layout (coast, hills, river, the flat valley, town and
port), split into ground materials by height and slope, plus the sea, the surf line, the river
water and the roads, tracks and paths laid over it."""
import math

import bmesh
import bpy
from mathutils import Vector, noise

import world_layout as L

TILE_METRES = {"grass": 4.0, "sand": 4.0, "rock": 6.0, "asphalt": 4.0, "dirt": 3.0, "cobble": 3.0, "water": 10.0, "field": 4.0}


# ---- height -----------------------------------------------------------------------------------------

def _seg_dist(p, a, b):
    ax, ay = a
    bx, by = b
    px, py = p
    dx, dy = bx - ax, by - ay
    t = max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy + 1e-9)))
    cx, cy = ax + dx * t, ay + dy * t
    return math.hypot(px - cx, py - cy)


def poly_dist(p, pts):
    return min(_seg_dist(p, pts[i], pts[i + 1]) for i in range(len(pts) - 1))


def smoothstep(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def height(x, y):
    r = math.hypot(x, y)
    a = math.atan2(y, x)
    coast = L.coast_radius(a)
    # gently rolling land
    h = L.GROUND + 0.6 * noise.noise(Vector((x * 0.02, y * 0.02, 0.3))) + 0.25 * noise.noise(Vector((x * 0.07, y * 0.07, 1.7)))
    for hx, hy, hh, hr in L.HILLS:
        d = math.hypot(x - hx, y - hy) / hr
        if d < 1.6:
            bump = hh * math.exp(-d * d * 1.6) * (0.8 + 0.4 * noise.noise(Vector((x * 0.05, y * 0.05, 4.0))))
            # terraces: soft steps so the hills show cliff bands
            step = 3.0
            bump = math.floor(bump / step) * step + step * smoothstep(0.55, 1.0, (bump % step) / step)
            h += bump
    # the valley, the town and the port stand flat
    flat = 0.0
    for z in L.ZONES:
        if z["radius"]:
            zx, zy = z["at"]
            flat = max(flat, 1 - smoothstep(z["radius"], z["radius"] + 14, math.hypot(x - zx, y - zy)))
    tx, ty = L.TOWN["centre"]
    flat = max(flat, 1 - smoothstep(48, 64, max(abs(x - tx), abs(y - ty))))
    px, py = L.PORT["centre"]
    flat = max(flat, 1 - smoothstep(30, 44, math.hypot(x - px, (y - py) * 1.6)))
    for lm in (L.STATION, L.HELIPAD, L.WINDMILL, L.LIGHTHOUSE):
        lx, ly = lm["centre"]
        flat = max(flat, 1 - smoothstep(12, 20, math.hypot(x - lx, y - ly)))
    for fx, fy, fr, _, _ in L.FLAT_SPOTS:
        flat = max(flat, 1 - smoothstep(fr, fr + 10, math.hypot(x - fx, y - fy)))
    h = h * (1 - flat) + L.GROUND * flat
    # roads level their verges
    for road in L.ROADS + L.TOWN_STREETS + [L.TRACKS]:
        d = poly_dist((x, y), road)
        if d < 9:
            k = 1 - smoothstep(4.0, 9.0, d)
            h = h * (1 - k) + max(L.GROUND, min(h, L.GROUND + 1.0)) * k
    # the river cuts its bed
    d = poly_dist((x, y), L.RIVER)
    bed = L.SEA_LEVEL - 0.9
    if d < 22:
        k = 1 - smoothstep(L.RIVER_WIDTH * 0.45, 18.0, d)
        h = h * (1 - k) + min(h, bed + (d / L.RIVER_WIDTH) * 1.4) * k
    # the shore drops to the sea floor
    shore = smoothstep(coast - 26, coast + 6, r)
    h = h * (1 - shore) + (L.SEA_LEVEL - 7.0) * shore
    beach = smoothstep(coast - 40, coast - 18, r) * (1 - shore)
    h = h * (1 - beach * 0.85) + (L.SEA_LEVEL + 0.5) * beach * 0.85
    return h


# ---- meshes ----------------------------------------------------------------------------------------

def material(name, tex=None, colour=(0.5, 0.5, 0.5), rough=0.8, alpha=1.0, folder=None):
    mat = bpy.data.materials.get(name)
    if mat:
        return mat
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = rough
    if tex:
        uv = nt.nodes.new("ShaderNodeUVMap")
        mp = nt.nodes.new("ShaderNodeMapping")
        s = 1.0 / TILE_METRES[tex]
        mp.inputs["Scale"].default_value = (s, s, s)
        nt.links.new(uv.outputs["UV"], mp.inputs["Vector"])
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = bpy.data.images.load(f"{folder}/fv_tile_{tex}.png", check_existing=True)
        t.image.alpha_mode = "CHANNEL_PACKED"
        nt.links.new(mp.outputs["Vector"], t.inputs["Vector"])
        nt.links.new(t.outputs["Color"], b.inputs["Base Color"])
        tn = nt.nodes.new("ShaderNodeTexImage")
        tn.image = bpy.data.images.load(f"{folder}/fv_tile_{tex}_n.png", check_existing=True)
        tn.image.colorspace_settings.name = "Non-Color"
        nt.links.new(mp.outputs["Vector"], tn.inputs["Vector"])
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
        inv = nt.nodes.new("ShaderNodeMath")
        inv.operation = "SUBTRACT"
        inv.inputs[0].default_value = 1.0
        nt.links.new(t.outputs["Alpha"], inv.inputs[1])
        nt.links.new(inv.outputs[0], b.inputs["Roughness"])
    else:
        b.inputs["Base Color"].default_value = (*colour, 1)
    if alpha < 1.0:
        b.inputs["Alpha"].default_value = alpha
    return mat


def _uv_metres(bm, uv_layer):
    """UVs in metres: top faces from X/Y, walls from their horizontal direction and Z."""
    for f in bm.faces:
        n = f.normal
        for loop in f.loops:
            co = loop.vert.co
            if abs(n.z) > 0.6:
                loop[uv_layer].uv = (co.x, co.y)
            elif abs(n.x) > abs(n.y):
                loop[uv_layer].uv = (co.y, co.z)
            else:
                loop[uv_layer].uv = (co.x, co.z)


def link(obj, collection):
    collection.objects.link(obj)
    return obj


def _tex_nodes(nt, tex, folder, uvmap):
    mp = nt.nodes.new("ShaderNodeMapping")
    sc = 1.0 / TILE_METRES[tex]
    mp.inputs["Scale"].default_value = (sc, sc, sc)
    nt.links.new(uvmap.outputs["UV"], mp.inputs["Vector"])
    t = nt.nodes.new("ShaderNodeTexImage")
    t.image = bpy.data.images.load(f"{folder}/fv_tile_{tex}.png", check_existing=True)
    t.image.alpha_mode = "CHANNEL_PACKED"   # alpha is smoothness, not coverage
    nt.links.new(mp.outputs["Vector"], t.inputs["Vector"])
    tn = nt.nodes.new("ShaderNodeTexImage")
    tn.image = bpy.data.images.load(f"{folder}/fv_tile_{tex}_n.png", check_existing=True)
    tn.image.colorspace_settings.name = "Non-Color"
    nt.links.new(mp.outputs["Vector"], tn.inputs["Vector"])
    return t, tn


def ground_material(folder):
    """One ground material: grass, blended to sand, rock and sea floor by the vertex colour
    "Splat" (R sand, G rock, B sea floor), so borders are soft. Unity reads the same weights."""
    mat = bpy.data.materials.get("World Ground")
    if mat:
        return mat
    mat = bpy.data.materials.new("World Ground")
    mat.use_nodes = True
    nt = mat.node_tree
    b = nt.nodes["Principled BSDF"]
    uvmap = nt.nodes.new("ShaderNodeUVMap")
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = "Splat"
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(attr.outputs["Color"], sep.inputs["Color"])
    layers = [_tex_nodes(nt, t, folder, uvmap) for t in ("grass", "sand", "rock", "dirt")]
    col, nrm = layers[0][0].outputs["Color"], layers[0][1].outputs["Color"]
    for (t, tn), ch in zip(layers[1:], ("Red", "Green", "Blue")):
        mc = nt.nodes.new("ShaderNodeMix")
        mc.data_type = "RGBA"
        nt.links.new(sep.outputs[ch], mc.inputs["Factor"])
        nt.links.new(col, mc.inputs[6])
        nt.links.new(t.outputs["Color"], mc.inputs[7])
        col = mc.outputs[2]
        mn = nt.nodes.new("ShaderNodeMix")
        mn.data_type = "RGBA"
        nt.links.new(sep.outputs[ch], mn.inputs["Factor"])
        nt.links.new(nrm, mn.inputs[6])
        nt.links.new(tn.outputs["Color"], mn.inputs[7])
        nrm = mn.outputs[2]
    nt.links.new(col, b.inputs["Base Color"])
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(nrm, nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    b.inputs["Roughness"].default_value = 0.9
    return mat


def terrain(collection, folder, extent=240.0, cell=2.0):
    n = int(extent * 2 / cell)
    bm = bmesh.new()
    verts = []
    for j in range(n + 1):
        row = []
        y = -extent + j * cell
        for i in range(n + 1):
            x = -extent + i * cell
            row.append(bm.verts.new((x, y, height(x, y))))
        verts.append(row)
    for j in range(n):
        for i in range(n):
            f = bm.faces.new((verts[j][i], verts[j][i + 1], verts[j + 1][i + 1], verts[j + 1][i]))
            f.smooth = True
    bm.normal_update()
    uv = bm.loops.layers.uv.new("UVMap")
    for f in bm.faces:
        for loop in f.loops:
            loop[uv].uv = (loop.vert.co.x, loop.vert.co.y)
    splat = bm.loops.layers.color.new("Splat")
    for v in bm.verts:
        z = v.co.z
        slope = 1 - v.normal.z
        sand = 1 - smoothstep(L.SEA_LEVEL + 0.7, L.SEA_LEVEL + 1.8, z)
        rock = smoothstep(0.12, 0.3, slope) * smoothstep(L.SEA_LEVEL + 0.5, L.SEA_LEVEL + 2.5, z)
        bed = 1 - smoothstep(L.SEA_LEVEL - 3.5, L.SEA_LEVEL - 1.0, z)
        for loop in v.link_loops:
            loop[splat] = (sand, rock, bed * 0.0, 1.0)
    me = bpy.data.meshes.new("World Terrain")
    bm.to_mesh(me)
    bm.free()
    me.materials.append(ground_material(folder))
    return link(bpy.data.objects.new("World Terrain", me), collection)


def sea(collection, folder):
    mat = material("World Sea", "water", folder=folder, rough=0.08)
    nt = mat.node_tree
    b = nt.nodes["Principled BSDF"]
    b.inputs["Alpha"].default_value = 0.82
    mat.surface_render_method = "BLENDED" if hasattr(mat, "surface_render_method") else None
    bm = bmesh.new()
    s = 2400.0
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=s)
    bmesh.ops.translate(bm, vec=(0, 0, L.SEA_LEVEL), verts=bm.verts)
    uv = bm.loops.layers.uv.new("UVMap")
    _uv_metres(bm, uv)
    me = bpy.data.meshes.new("World Sea")
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mat)
    return link(bpy.data.objects.new("World Sea", me), collection)


def surf(collection):
    """A foam line along the shore: march outward from the centre at each angle to the waterline."""
    mat = material("World Foam", None, (0.95, 0.98, 1.0), rough=0.3)
    pts = []
    for k in range(720):
        a = k / 720 * math.tau
        r = 30.0
        while r < 300 and height(math.cos(a) * r, math.sin(a) * r) > L.SEA_LEVEL:
            r += 0.5
        pts.append((a, r))
    bm = bmesh.new()
    inner, outer = [], []
    for a, r in pts:
        c, s = math.cos(a), math.sin(a)
        inner.append(bm.verts.new((c * (r - 0.6), s * (r - 0.6), L.SEA_LEVEL + 0.05)))
        outer.append(bm.verts.new((c * (r + 1.6), s * (r + 1.6), L.SEA_LEVEL + 0.05)))
    for i in range(len(pts)):
        j = (i + 1) % len(pts)
        bm.faces.new((inner[i], outer[i], outer[j], inner[j]))
    me = bpy.data.meshes.new("World Surf")
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mat)
    return link(bpy.data.objects.new("World Surf", me), collection)


def ribbon(name, pts, width, z_of, mat, collection, step=1.5, lift=0.04, uv_along=True):
    """A flat strip following a polyline, resampled every `step` metres, draped over the ground."""
    dense = []
    for i in range(len(pts) - 1):
        a, b = Vector((*pts[i], 0)), Vector((*pts[i + 1], 0))
        n = max(1, int((b - a).length / step))
        for k in range(n):
            dense.append(a.lerp(b, k / n))
    dense.append(Vector((*pts[-1], 0)))
    bm = bmesh.new()
    left, right = [], []
    dist = 0.0
    uvs = []
    for i, p in enumerate(dense):
        a = dense[max(0, i - 1)]
        b = dense[min(len(dense) - 1, i + 1)]
        d = (b - a)
        d.z = 0
        d.normalize()
        side = Vector((-d.y, d.x, 0)) * (width / 2)
        if i:
            dist += (p - dense[i - 1]).length
        for target, off in ((left, side), (right, -side)):
            q = p + off
            target.append(bm.verts.new((q.x, q.y, z_of(q.x, q.y) + lift)))
        uvs.append(dist)
    faces = []
    for i in range(len(dense) - 1):
        faces.append(bm.faces.new((left[i], right[i], right[i + 1], left[i + 1])))
    uv = bm.loops.layers.uv.new("UVMap")
    for f in faces:
        for loop in f.loops:
            co = loop.vert.co
            loop[uv].uv = (co.x, co.y)
    bm.normal_update()
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mat)
    for poly in me.polygons:
        poly.use_smooth = True
    return link(bpy.data.objects.new(name, me), collection), dense


def ground_z(x, y):
    return max(height(x, y), L.SEA_LEVEL + 0.4)


def roads(collection, folder):
    asphalt = material("World Asphalt", "asphalt", folder=folder)
    curb = material("World Curb", None, (0.82, 0.8, 0.76))
    line = material("World Road Line", None, (0.97, 0.95, 0.88))
    out = []
    for k, road in enumerate(L.ROADS + L.TOWN_STREETS):
        ribbon(f"Road Curb {k}", road, 7.6, ground_z, curb, collection, lift=0.06)
        obj, dense = ribbon(f"Road {k}", road, 6.4, ground_z, asphalt, collection, lift=0.12)
        # dashed centre line
        for i in range(0, len(dense) - 2, 3):
            ribbon(f"Road Dash {k}.{i}", [tuple(dense[i].xy), tuple(dense[i + 1].xy)], 0.18, ground_z, line, collection, lift=0.14)
        out.append(dense)
    return out


def river(collection, folder):
    water = material("World River", "water", folder=folder, rough=0.1)
    ribbon("World River", L.RIVER, L.RIVER_WIDTH * 1.4, lambda x, y: L.SEA_LEVEL + 0.15, water, collection, step=2.0, lift=0.0)


def tracks(collection, folder):
    ballast = material("World Ballast", "dirt", folder=folder)
    steel = material("World Rail", None, (0.42, 0.42, 0.45), rough=0.35)
    sleeper = material("World Sleeper", None, (0.36, 0.24, 0.14))
    _, dense = ribbon("Rail Bed", L.TRACKS, 3.4, ground_z, ballast, collection, step=1.0, lift=0.1)
    # sleepers every metre and two rails
    bm = bmesh.new()
    for i in range(0, len(dense) - 1):
        p, q = dense[i], dense[i + 1]
        d = (q - p).normalized()
        side = Vector((-d.y, d.x, 0))
        c = p + Vector((0, 0, ground_z(p.x, p.y) + 0.2))
        res = bmesh.ops.create_cube(bm, size=1.0)
        from mathutils import Matrix
        ang = math.atan2(d.y, d.x)
        m = Matrix.Translation(c) @ Matrix.Rotation(ang, 4, "Z") @ Matrix.Diagonal((0.25, 2.4, 0.12, 1))
        bmesh.ops.transform(bm, matrix=m, verts=res["verts"])
    me = bpy.data.meshes.new("Rail Sleepers")
    bm.to_mesh(me)
    bm.free()
    me.materials.append(sleeper)
    link(bpy.data.objects.new("Rail Sleepers", me), collection)
    for s in (-0.72, 0.72):
        pts = []
        for i in range(len(dense) - 1):
            p, q = dense[i], dense[i + 1]
            d = (q - p).normalized()
            off = Vector((-d.y, d.x, 0)) * s
            pts.append((p.x + off.x, p.y + off.y))
        ribbon(f"Rail {s}", pts, 0.12, lambda x, y: ground_z(x, y) + 0.3, steel, collection, step=1.0, lift=0.0)
    return dense
