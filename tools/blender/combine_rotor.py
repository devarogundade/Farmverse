"""Join a cleaned body GLB and a cleaned rotor GLB (same frame) into one model.
python combine_rotor.py <body.glb> <rotor.glb> <out.glb> <name>
The rotor object gets its origin at the hub (centre of the blades) and its local +Z along the axle
(the direction the blades spin around), so the game can spin it with one rotation about local Z."""
import sys, json
import bpy
import numpy as np
from mathutils import Vector, Matrix

a = sys.argv[sys.argv.index("--") + 1:]
body_p, rotor_p, out_p, name = a
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=body_p)
body = [o for o in bpy.context.scene.objects if o.type == "MESH"][0]
body.name = name + "_body"
bpy.ops.import_scene.gltf(filepath=rotor_p)
rotor = [o for o in bpy.context.scene.objects if o.type == "MESH" and o is not body][0]
rotor.name = name + "_rotor"
v = np.array([rotor.matrix_world @ x.co for x in rotor.data.vertices])
hub = v.mean(0)
_, _, vt = np.linalg.svd(v - hub, full_matrices=False)
axle = vt[2]
if axle @ np.array([hub[0], hub[1], 0.0]) < 0:  # point away from the tower
    axle = -axle
z = Vector(axle).normalized()
x = z.cross(Vector((0, 0, 1)))
if x.length < 1e-3:
    x = Vector((1, 0, 0))
x.normalize()
y = z.cross(x)
basis = Matrix((x, y, z)).transposed().to_4x4()
basis.translation = Vector(hub)
rotor.data.transform(rotor.matrix_world)
rotor.matrix_world = Matrix.Identity(4)
rotor.data.transform(basis.inverted())
rotor.matrix_world = basis
rotor.parent = body
rotor.matrix_parent_inverse = body.matrix_world.inverted()
bpy.ops.export_scene.gltf(filepath=out_p, export_format="GLB", export_image_format="AUTO")
print("COMBINED", json.dumps({"hub": [round(float(c), 3) for c in hub], "axle": [round(float(c), 3) for c in axle]}), flush=True)
