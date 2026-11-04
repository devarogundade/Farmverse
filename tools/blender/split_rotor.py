"""Split a spinning part (windmill sails, water wheel) out of a cleaned Tripo model.

python split_rotor.py <in.glb> <out.glb> <name> <min_radius> <z_min> <band> <rotor_radius>
- candidate points: faces whose centre is further than <min_radius> from the model's vertical axis
  and above <z_min> (these are mostly the blades);
- PCA on them gives the rotor plane (normal = axle), their centre gives the hub;
- every face within <band> of that plane and within <rotor_radius> of the hub becomes the rotor.
Writes <name>_body and <name>_rotor (origin at the hub, local +Z along the axle) into one GLB.
"""
import sys, json
import bpy, bmesh
import numpy as np
from mathutils import Vector, Matrix

a = sys.argv[sys.argv.index("--") + 1:]
src, dst, name = a[0], a[1], a[2]
min_r, z_min, band, rr = map(float, a[3:7])

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
ob = [o for o in bpy.context.scene.objects if o.type == "MESH"][0]
me = ob.data
cent = np.array([p.center for p in me.polygons])
rad = np.hypot(cent[:, 0], cent[:, 1])
cand = cent[(rad > min_r) & (cent[:, 2] > z_min)]
hub = cand.mean(0)
u, s, vt = np.linalg.svd(cand - hub)
normal = vt[2]
if normal @ np.array([hub[0], hub[1], 0]) < 0:  # axle points outward from the tower
    normal = -normal
dist = (cent - hub) @ normal
inplane = np.abs(dist) < band
near = np.linalg.norm(cent - hub, axis=1) < rr
sel = inplane & near & (cent[:, 2] > z_min * 0.6)
# refine: refit plane + hub on the selection a few times (the first guess is biased to the upper blades)
for _ in range(3):
    pts = cent[sel]
    hub = pts.mean(0)
    u, s, vt = np.linalg.svd(pts - hub)
    n2 = vt[2]
    normal = n2 if n2 @ normal > 0 else -n2
    dist = (cent - hub) @ normal
    sel = (np.abs(dist) < band) & (np.linalg.norm(cent - hub, axis=1) < rr) & (cent[:, 2] > 0.3)

# island-level decision: Tripo meshes are thousands of loose pieces; a piece goes to the rotor
# when its centre lies in the rotor disc and the whole piece stays close to the plane
bm = bmesh.new()
bm.from_mesh(me)
bm.faces.ensure_lookup_table()
seen = set()
rotor_ids = []
for f in bm.faces:
    if f.index in seen:
        continue
    stack = [f]; isl = []
    seen.add(f.index)
    while stack:
        g = stack.pop(); isl.append(g.index)
        for e in g.edges:
            for h in e.link_faces:
                if h.index not in seen:
                    seen.add(h.index); stack.append(h)
    c = cent[isl]
    cc = c.mean(0)
    dd = (c - hub) @ normal
    if abs((cc - hub) @ normal) < band and np.abs(dd).max() < band * 2.5 \
            and np.linalg.norm(cc - hub) < rr and cc[2] > 1.0:
        rotor_ids.extend(isl)
bm.free()
sel_ids = set(rotor_ids)
bpy.context.view_layer.objects.active = ob
ob.select_set(True)
for v in me.vertices:
    v.select = False
for e in me.edges:
    e.select = False
for p in me.polygons:
    p.select = p.index in sel_ids
    if p.select:
        for vi in p.vertices:
            me.vertices[vi].select = True
bpy.ops.object.mode_set(mode="EDIT")
bpy.context.tool_settings.mesh_select_mode = (False, False, True)
bpy.ops.mesh.separate(type="SELECTED")
bpy.ops.object.mode_set(mode="OBJECT")
parts = [o for o in bpy.context.scene.objects if o.type == "MESH"]
body = ob
rotor = [o for o in parts if o is not ob][0]
body.name = name + "_body"
rotor.name = name + "_rotor"
# rotor origin at the hub, local Z along the axle
z = Vector(normal).normalized()
x = z.cross(Vector((0, 0, 1)))
if x.length < 1e-3:
    x = Vector((1, 0, 0))
x.normalize()
y = z.cross(x)
basis = Matrix((x, y, z)).transposed().to_4x4()
basis.translation = Vector(hub)
rotor.data.transform(basis.inverted())
rotor.matrix_world = basis
info = {"faces_rotor": len(rotor.data.polygons), "faces_body": len(body.data.polygons),
        "hub": [round(float(v), 3) for v in hub], "axle": [round(float(v), 3) for v in normal]}
bpy.ops.export_scene.gltf(filepath=dst, export_format="GLB", export_image_format="AUTO")
print("SPLIT", json.dumps(info), flush=True)
