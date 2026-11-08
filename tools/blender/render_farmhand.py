"""Render one Farmhand (the market's character.glb dressed in one outfit) as a transparent PNG, posed in its
idle animation and lit like render_icons.py's item icons.

    blender -b --factory-startup -P tools/blender/render_farmhand.py -- <out.png> '<outfit json>' [--size 512]

The outfit is a slot → part map as in Farmhand metadata ({"hat": "Hat_Single_008", "top": "Outwear_043", ...}).
"""

import json
import math
import os
import sys

import bpy
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CHARACTER = os.path.join(ROOT, "web", "shared", "public", "models", "character.glb")
IDLE = "Idle_Relaxed"
FULL_BODY = {"Mascot_002"}  # costumes that hide the base body (as web/shared/src/three/Character.tsx)


def args():
    argv = sys.argv[sys.argv.index("--") + 1:]
    out, outfit = argv[0], json.loads(argv[1])
    size = int(argv[argv.index("--size") + 1]) if "--size" in argv else 512
    return out, outfit, size


def main():
    out, outfit, size = args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=CHARACTER)
    shown = set(outfit.values())
    costume = outfit.get("costume")
    meshes = []
    for o in bpy.context.scene.objects:
        if o.type != "MESH":
            continue
        slot = o.get("slot")
        visible = (slot == "body" and costume not in FULL_BODY) if slot == "body" else o.name.split(".")[0] in shown
        o.hide_render = not visible
        if visible:
            meshes.append(o)

    # the idle pose, a little way in (frame 0 of some clips is the bind pose)
    rig = next((o for o in bpy.context.scene.objects if o.type == "ARMATURE"), None)
    action = next((a for a in bpy.data.actions if IDLE in a.name), None)
    if rig is not None and action is not None:
        rig.animation_data_create()
        # the importer stacks every clip as an NLA track (and makes another clip active): mute them, so the pose
        # is the idle one alone
        for track in rig.animation_data.nla_tracks:
            track.mute = True
        rig.animation_data.action = action
        # Blender 4.4+ (slotted actions): the action animates nothing until one of its slots is picked
        slots = getattr(action, "slots", None)
        if slots and len(slots) and hasattr(rig.animation_data, "action_slot"):
            rig.animation_data.action_slot = slots[0]
        bpy.context.scene.frame_set(int(action.frame_range[0]) + int(os.environ.get("FARMHAND_FRAME", "40")))
    bpy.context.view_layer.update()

    lo, hi = Vector((1e9,) * 3), Vector((-1e9,) * 3)
    deps = bpy.context.evaluated_depsgraph_get()
    for o in meshes:
        ev = o.evaluated_get(deps)
        for v in ev.to_mesh().vertices:
            w = o.matrix_world @ v.co
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
        ev.to_mesh_clear()

    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 48
    scene.render.film_transparent = True
    scene.render.resolution_x = scene.render.resolution_y = size
    world = bpy.data.worlds.new("sky")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.62, 0.72, 0.85, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
    scene.world = world

    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 4.0
    sun.data.color = (1.0, 0.96, 0.88)
    sun.rotation_euler = (math.radians(42), 0, math.radians(-35))
    scene.collection.objects.link(sun)

    # front three-quarter view, a little from above, framing the whole figure
    centre = (lo + hi) / 2
    height = max((hi - lo).z, (hi - lo).x, 0.01)
    cam_data = bpy.data.cameras.new("cam")
    cam_data.lens = 70
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    yaw, elev = math.radians(20), math.radians(float(os.environ.get("FARMHAND_ELEV", "28")))
    dist = height * 1.18 * cam_data.lens / 36.0
    forward = Vector((math.sin(yaw) * math.cos(elev), -math.cos(yaw) * math.cos(elev), math.sin(elev)))
    cam.location = centre + forward * dist
    cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()

    scene.render.filepath = os.path.abspath(out)
    bpy.ops.render.render(write_still=True)
    print("FARMHAND", out)


main()
