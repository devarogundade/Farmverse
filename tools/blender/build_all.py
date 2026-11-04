"""Build every Farmverse world model and export FBX + the shared palette.

    /tmp/bpyenv/bin/python build_all.py [out_dir] [--preview]

Each asset is built in a fresh scene into one collection and exported as
<out>/Models/<id>.fbx; the material atlas goes to <out>/Textures/fv_atlas.png and fv_atlas_n.png.
--preview also renders a Workbench thumbnail per model into <out>/preview/.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

import assets_build as ab  # noqa: E402
import assets_farm as af  # noqa: E402
import assets_world as aw  # noqa: E402
import assets_land as al  # noqa: E402
import assets_plants as ap  # noqa: E402
import assets_real as ar  # noqa: E402
import fvlib  # noqa: E402
from fvlib import export_fbx, new_collection, reset_scene  # noqa: E402

ASSETS = {
    "plot": al.plot_base,
    "plot_sale": al.plot_sale,
    "land_tile": al.land_tile,
    "land_sale": al.land_sale,
    "land_wild": al.land_wild,
    "field": al.field,
    "pen": al.pen,
    "facility_well": al.well,
    "villager_a": lambda c: al.villager(c, "shirt_blue", "skin", "straw", 1),
    "villager_b": lambda c: al.villager(c, "shirt_red", "skin_dark", "cap", 2),
    "villager_c": lambda c: al.villager(c, "shirt_green", "skin", "straw", 3),
    "livestock_chicken": ab.chicken,
    "livestock_cow": ab.cow,
    "tree_pine": lambda c: ap.tree(c, "tree_pine"),
    "tree_oak": lambda c: ap.tree(c, "tree_oak"),
}
for _id in ap.CROPS:
    ASSETS[_id] = (lambda i: lambda c: ap.crop(c, i))(_id)
ASSETS.update(ab.MACHINES)
ASSETS.update(af.ASSETS)
# the painted-style world replaces the first-pass land, animals, machines, buildings, trees, fences and paths
ASSETS.update(aw.ASSETS)
# sculpted animals, farmhands and trees replace the block-built ones
ASSETS.update(ar.ASSETS)
# the plot era's pieces: no longer drawn
for _gone in ("plot", "plot_sale", "facility_well", "machine_compost_bin"):
    ASSETS.pop(_gone, None)


def preview(path, col):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = scene.render.resolution_y = 512
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.75, 0.85, 0.95, 1)
    bg.inputs[1].default_value = 0.9
    scene.world = world
    sun = bpy.data.lights.new("sun", "SUN")
    sun.energy = 3.2
    sun.angle = 0.14
    sun_obj = bpy.data.objects.new("sun", sun)
    sun_obj.rotation_euler = (0.73, 0.21, 0.6)
    scene.collection.objects.link(sun_obj)
    # frame the "final" stage: hide the earlier stages so the picture is readable
    stages = {"Seeded", "Sprout", "Young", "Grown", "Ready", "Idle", "Working", "Broken"}
    meshes = [o for o in col.all_objects if o.type == "MESH"]
    roots = {}
    for o in meshes:
        root = o
        while root.parent is not None:
            root = root.parent
        roots[o] = root.name.split(".")[0]
    names = set(roots.values())
    show = next((s for s in ("Ready", "Grown", "Idle") if s in names), None)
    for o, stage in roots.items():
        o.hide_render = stage in stages and stage != show
    lo = Vector((1e9, 1e9, 1e9))
    hi = -lo
    for o in meshes:
        if o.hide_render:
            continue
        for corner in o.bound_box:
            w = o.matrix_world @ Vector(corner)
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
    centre = (lo + hi) / 2
    size = max((hi - lo).length, 0.5)
    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = size * 1.05
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    direction = Vector((0.7, -1.0, 0.85)).normalized()
    cam.location = centre + direction * size * 3
    cam.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    out = os.path.abspath(args[0] if args else os.path.join(os.path.dirname(__file__), "out"))
    want_preview = "--preview" in sys.argv
    only = os.environ.get("FV_ONLY")
    os.makedirs(os.path.join(out, "Models"), exist_ok=True)
    os.makedirs(os.path.join(out, "Textures"), exist_ok=True)
    if want_preview:
        os.makedirs(os.path.join(out, "preview"), exist_ok=True)
    fvlib.write_atlas(os.path.join(out, "Textures"))
    print("atlas written")
    for asset_id, builder in ASSETS.items():
        if only and asset_id not in only.split(","):
            continue
        reset_scene()
        col = new_collection(asset_id)
        builder(col)
        export_fbx(os.path.join(out, "Models", asset_id + ".fbx"), col)
        if want_preview:
            preview(os.path.join(out, "preview", asset_id + ".png"), col)
        tris = sum(len(o.data.polygons) for o in col.all_objects if o.type == "MESH")
        print(f"built {asset_id}: {len(col.all_objects)} objects, {tris} faces")



if __name__ == "__main__":
    main()
