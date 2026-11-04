"""Render the UI icons from the 3D world models, so the shop shows exactly what stands on the farm.

    blender -b -P tools/blender/render_icons.py -- [models_dir] [out_dir] [--only id,id] [--size 256]
    /tmp/bpyenv/bin/python tools/blender/render_icons.py [models_dir] [out_dir] ...

models_dir  the world models (.glb from the generator, or .fbx from build_all.py);
            default Assets/Farmverse/Content/World/Models
out_dir     default Assets/Farmverse/Content/UI/Art/Rendered/Icons

One icon per model, named after the model's catalogue id (crop_wheat.png,
machine_mill.png, decor_bench.png), plus any extra views listed in
tools/blender/icons.json (another growth stage, a different angle or zoom).
Every icon is lit and framed the same way: the farm's warm sun from the upper
left, a soft sky fill, the game's three-quarter view from above, a soft contact
shadow, a thin dark outline, on a transparent background. After rendering, run
tools/ui_kit/gen_art_uss.py so the UI picks the icons up.
"""
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
DEFAULT_MODELS = os.path.join(ROOT, "Assets", "Farmverse", "Content", "World", "Models")
DEFAULT_OUT = os.path.join(ROOT, "Assets", "Farmverse", "Content", "UI", "Art", "Rendered", "Icons")
MANIFEST = os.path.join(HERE, "icons.json")

# the look every icon shares (keep in step with WorldArtSetup.DressScene)
SUN_COLOUR = (1.0, 0.96, 0.88)
SUN_ELEVATION = 48.0      # degrees above the horizon
SUN_AZIMUTH = -35.0       # degrees, from the camera's left
SUN_STRENGTH = 4.0
SKY_COLOUR = (0.62, 0.72, 0.85)
SKY_STRENGTH = 0.9
CAMERA_ELEVATION = 30.0   # the game's three-quarter view
CAMERA_YAW = 35.0
LENS_MM = 70.0            # long lens: little perspective, reads well small
MARGIN = 0.08             # empty border around the object, share of the frame
OUTLINE_PX = 3
OUTLINE_COLOUR = (0.16, 0.11, 0.07)
SHAPE_ALPHA = 0.6        # at or above: the object; below: its shadow
SHADOW_FLOOR = 0.08      # fainter than this is the catcher's veil, not shadow
GLB_YAW = 90.0           # Tripo v3.1 .glb models face Blender +X; build_all.py .fbx face -Y
STAGES = ("Seeded", "Sprout", "Young", "Grown", "Ready", "Idle", "Working", "Broken")
DEFAULT_STAGE_ORDER = ("Ready", "Grown", "Idle", "Working")


def args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    flags = {}
    rest = []
    i = 0
    while i < len(argv):
        a = argv[i]
        if a.startswith("--"):
            flags[a[2:]] = argv[i + 1] if i + 1 < len(argv) and not argv[i + 1].startswith("--") else True
            i += 2 if flags[a[2:]] is not True else 1
        else:
            rest.append(a)
            i += 1
    models = os.path.abspath(rest[0]) if rest else DEFAULT_MODELS
    out = os.path.abspath(rest[1]) if len(rest) > 1 else DEFAULT_OUT
    return models, out, flags


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_model(path):
    before = set(bpy.data.objects)
    if path.lower().endswith((".glb", ".gltf")):
        bpy.ops.import_scene.gltf(filepath=path)
    else:
        bpy.ops.import_scene.fbx(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


def root_name(obj):
    while obj.parent is not None:
        obj = obj.parent
    return obj.name.split(".")[0]


def choose_stage(objects, wanted):
    """Show one growth or work stage: the one asked for, else the most finished one present."""
    names = {root_name(o) for o in objects}
    show = wanted if wanted in names else next((s for s in DEFAULT_STAGE_ORDER if s in names), None)
    for o in objects:
        hidden = root_name(o) in STAGES and root_name(o) != show
        o.hide_render = hidden
        o.hide_viewport = hidden
    return show


def bounds(objects):
    lo = Vector((1e9, 1e9, 1e9))
    hi = -lo
    for o in objects:
        if o.type != "MESH" or o.hide_render:
            continue
        for corner in o.bound_box:
            w = o.matrix_world @ Vector(corner)
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
    return lo, hi


def setup_scene(size):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 96
    scene.cycles.use_denoising = True
    scene.render.film_transparent = True
    scene.render.resolution_x = scene.render.resolution_y = size
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"
    world = bpy.data.worlds.new("icon_sky")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (*SKY_COLOUR, 1.0)
    bg.inputs[1].default_value = SKY_STRENGTH
    scene.world = world
    return scene


def add_light(scene, yaw):
    sun = bpy.data.lights.new("sun", "SUN")
    sun.energy = SUN_STRENGTH
    sun.color = SUN_COLOUR
    sun.angle = math.radians(10)  # soft edges
    obj = bpy.data.objects.new("sun", sun)
    elevation = math.radians(SUN_ELEVATION)
    azimuth = math.radians(yaw + SUN_AZIMUTH)
    direction = Vector((math.sin(azimuth) * math.cos(elevation), -math.cos(azimuth) * math.cos(elevation), math.sin(elevation)))
    obj.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(obj)


def add_shadow_catcher(scene, lo, hi):
    size = max(hi.x - lo.x, hi.y - lo.y) * 2
    mesh = bpy.data.meshes.new("ground")
    h = size / 2
    mesh.from_pydata([(-h, -h, 0), (h, -h, 0), (h, h, 0), (-h, h, 0)], [], [(0, 1, 2, 3)])
    ground = bpy.data.objects.new("ground", mesh)
    ground.location = ((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z)
    ground.is_shadow_catcher = True
    scene.collection.objects.link(ground)


def add_camera(scene, lo, hi, yaw, elevation, zoom):
    centre = (lo + hi) / 2
    cam_data = bpy.data.cameras.new("cam")
    cam_data.lens = LENS_MM
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    e = math.radians(elevation)
    y = math.radians(yaw)
    direction = Vector((math.sin(y) * math.cos(e), -math.cos(y) * math.cos(e), math.sin(e)))
    cam.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
    # distance at which the projected box fills the frame minus the margin
    tan_half = math.tan(math.atan(cam_data.sensor_width / 2 / cam_data.lens)) * (1 - 2 * MARGIN)
    corners = [Vector((x, yv, z)) for x in (lo.x, hi.x) for yv in (lo.y, hi.y) for z in (lo.z, hi.z)]
    forward = -direction
    right = forward.cross(Vector((0, 0, 1))).normalized()
    up = right.cross(forward).normalized()
    # centre the frame on the projected box, then back off until every corner fits
    xs = [(c - centre).dot(right) for c in corners]
    ys = [(c - centre).dot(up) for c in corners]
    centre = centre + right * (max(xs) + min(xs)) / 2 + up * (max(ys) + min(ys)) / 2
    need = 0.0
    for c in corners:
        v = c - centre
        depth = v.dot(direction)
        lateral = max(abs(v.dot(right)), abs(v.dot(up)))
        need = max(need, depth + lateral / tan_half)
    cam.location = centre + direction * need / max(zoom, 0.1)
    cam_data.clip_start = 0.01
    cam_data.clip_end = need * 10 + 10


def outline(path):
    """A thin dark outline around the opaque shape, so icons read on any panel colour."""
    if OUTLINE_PX <= 0:
        return
    img = bpy.data.images.load(path)
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    alpha = px[..., 3]
    # the shadow catcher leaves a faint veil over its whole plane: keep only real shadow
    shadow = alpha < SHAPE_ALPHA
    alpha = np.where(shadow & (alpha < SHADOW_FLOOR), 0.0, alpha)
    px[..., 3] = alpha
    shape = (alpha >= SHAPE_ALPHA).astype(np.float32)
    grown = shape.copy()
    r = OUTLINE_PX
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            if dx * dx + dy * dy > r * r:
                continue
            np.maximum(grown, np.roll(np.roll(shape, dy, axis=0), dx, axis=1), out=grown)
    ring = np.clip(grown - shape, 0, 1)
    out = px.copy()
    for c in range(3):
        out[..., c] = np.where(ring > 0, OUTLINE_COLOUR[c], px[..., c])
    out[..., 3] = np.where(ring > 0, 1.0, alpha)
    img.pixels[:] = out.ravel()
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def jobs(models_dir, only):
    """Every model file once with defaults, then the extra views from icons.json."""
    files = {}
    for name in sorted(os.listdir(models_dir)):
        stem, ext = os.path.splitext(name)
        if ext.lower() in (".glb", ".gltf", ".fbx") and stem not in files:
            files[stem] = os.path.join(models_dir, name)
    manifest = json.load(open(MANIFEST, encoding="utf-8")) if os.path.exists(MANIFEST) else {}
    skip = set(manifest.get("skip", []))
    out = []
    for model_id, path in files.items():
        if model_id in skip:
            continue
        out.append({"icon": model_id, "path": path, **manifest.get("defaults", {}).get(model_id, {})})
    for icon, view in manifest.get("extra", {}).items():
        model = view.get("model")
        if model in files:
            out.append({"icon": icon, "path": files[model], **view})
    if only:
        wanted = set(only.split(","))
        out = [j for j in out if j["icon"] in wanted]
    return out


def render(job, out_dir, size):
    clear()
    scene = setup_scene(size)
    objects = import_model(job["path"])
    choose_stage(objects, job.get("stage"))
    lo, hi = bounds(objects)
    if lo.x > hi.x:
        print(f"skip {job['icon']}: nothing to draw")
        return False
    yaw = job.get("yaw", CAMERA_YAW) + (GLB_YAW if job["path"].lower().endswith((".glb", ".gltf")) else 0.0)
    add_light(scene, yaw)
    if job.get("shadow", True):
        add_shadow_catcher(scene, lo, hi)
    add_camera(scene, lo, hi, yaw, job.get("elevation", CAMERA_ELEVATION), job.get("zoom", 1.0))
    path = os.path.join(out_dir, job["icon"] + ".png")
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    outline(path)
    return True


def main():
    models_dir, out_dir, flags = args()
    os.makedirs(out_dir, exist_ok=True)
    size = int(flags.get("size", 256))
    done = 0
    todo = jobs(models_dir, flags.get("only"))
    for job in todo:
        if render(job, out_dir, size):
            done += 1
            print(f"icon {job['icon']}")
    print(f"rendered {done} of {len(todo)} icons → {out_dir}")


main()
